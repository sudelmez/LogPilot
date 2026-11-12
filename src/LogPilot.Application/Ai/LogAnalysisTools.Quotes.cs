using LogPilot.Application.Logs;
using LogPilot.Application.Quotes;
using LogPilot.Application.Users;
using LogPilot.Domain.Logs;
using LogPilot.Domain.Quotes;
using LogPilot.Domain.Users;

namespace LogPilot.Application.Ai;

/// Sigorta teklifi araçları. Tablolar arası eşleştirme (users ↔ logs ↔ quote_logs) BURADA, backend'de yapılır:
///   • LLM serbest sorgu/JOIN yazmaz; sadece "kimin/hangi teklifin" izini istediğini söyler.
///   • Kimlik numarası (identityNo) LLM'e hiç gitmez; eşleştirme sunucu tarafında yapılır, çıktıda maskelenir.
public sealed partial class LogAnalysisTools
{
    private const int MaxJourneyEntries = 60;
    private const string QuoteEngineApplication = "QuoteEngine";
    private const string MobileQuoteOperation = "RequestQuote";

    public async Task<CustomerProfileToolResult> GetCustomerProfileAsync(CustomerProfileArgs a, ToolContext ctx, CancellationToken ct)
    {
        var userCode = Identifier(a.UserCode, "userCode")?.ToUpperInvariant();
        var customerNo = Identifier(a.CustomerNo, "customerNo")?.ToUpperInvariant();
        var quoteId = Identifier(a.QuoteId, "quoteId")?.ToUpperInvariant();
        if (userCode is null && customerNo is null && quoteId is null)
            throw new ToolArgumentException("Give userCode, customerNo or quoteId.");

        var users = RequireUsers();
        string? identity = null;
        var note = "identityNo is masked; identity matching is done by the platform.";

        if (quoteId is not null)
        {
            // Teklif numarasından müşteriye: web kanalında tek anahtar identityNo olduğu için eşleştirme burada yapılır.
            var (from, to) = Window(null, null, ctx);
            var quote = (await RequireQuotes().SearchAsync(new QuoteLogSearchRequest(from, to, QuoteId: quoteId, Limit: 1), ct)).Items.FirstOrDefault()
                        ?? throw new ToolArgumentException($"Quote {quoteId} was not found in the selected time range.");
            customerNo ??= quote.CustomerNo;
            identity = quote.IdentityNo;
            note = $"Quote {quoteId} came via the {quote.Channel} channel" +
                   (quote.Channel == QuoteChannel.Agent ? $"; userCode {quote.UserCode} on that quote is the AGENT, not the customer. " : ". ") + note;
        }

        var found = await users.FindAsync(
            userCode is null ? null : [userCode], customerNo is null ? null : [customerNo], identity is null ? null : [identity], ct);

        if (found.Count == 0) note = "No matching user/customer record was found in the user directory. " + note;
        return new CustomerProfileToolResult(found.Select(ToProfile).ToList(), note);
    }

    public async Task<LogToolResult> SearchQuoteLogsAsync(SearchQuoteLogsArgs a, ToolContext ctx, CancellationToken ct)
    {
        var quotes = RequireQuotes();
        var (from, to) = Window(a.From, a.To, ctx);
        var limit = Limit(a.Limit);

        var userCode = Identifier(a.UserCode, "userCode")?.ToUpperInvariant();
        var customerNo = Identifier(a.CustomerNo, "customerNo")?.ToUpperInvariant();

        // userCode verildiyse kişiyi dizinden çözüp TÜM anahtarlarıyla ara (mobil: userCode, acente: customerNo, web: identityNo).
        IReadOnlyCollection<string>? userCodes = null, customerNos = null, identities = null;
        var note = "";
        if (userCode is not null || customerNo is not null)
        {
            var resolved = await ResolveAsync(userCode, customerNo, ct);
            userCodes = userCode is null ? null : [userCode];
            customerNos = resolved.Select(u => u.CustomerNo).Append(customerNo).OfType<string>().Distinct().ToList();
            identities = resolved.Select(u => u.IdentityNo).OfType<string>().Distinct().ToList();
            note = resolved.Count > 0
                ? $"; customer resolved via users ({string.Join(", ", resolved.Select(u => u.UserCode))}) and matched on userCode/customerNo/identityNo"
                : "; customer not found in users, matched on the given keys only";
        }

        var page = await quotes.SearchAsync(new QuoteLogSearchRequest(from, to,
            QuoteId: Identifier(a.QuoteId, "quoteId")?.ToUpperInvariant(),
            CorrelationId: Identifier(a.CorrelationId, "correlationId"),
            UserCodes: userCodes, CustomerNos: customerNos, IdentityNos: identities,
            Product: Identifier(a.Product, "product"), Channel: ParseChannel(a.Channel),
            InsurerCode: Identifier(a.InsurerCode, "insurerCode")?.ToUpperInvariant(),
            Step: Identifier(a.Step, "step"), ErrorCode: Identifier(a.ErrorCode, "errorCode"),
            Level: ParseLevel(a.Level), Limit: limit), ct);

        var items = page.Items.Select(ToQuoteSnippet).ToList();
        return new LogToolResult(items, page.TotalCount, page.TotalCount > items.Count,
            $"{from:u}..{to:u}; quote_logs, newest first, max {limit}{note}");
    }

    public async Task<QuoteFunnelToolResult> GetQuoteFunnelAsync(QuoteFunnelArgs a, ToolContext ctx, CancellationToken ct)
    {
        var quotes = RequireQuotes();
        var (from, to) = Window(null, null, ctx);
        var funnel = await quotes.GetFunnelAsync(from, to,
            Identifier(a.Product, "product"), ParseChannel(a.Channel), Identifier(a.InsurerCode, "insurerCode")?.ToUpperInvariant(), ct);

        var errors = funnel.Errors.Take(8).ToList();
        var samples = await quotes.GetByIdsAsync(errors.SelectMany(e => e.SampleLogIds).Distinct().ToList(), ct);

        return new QuoteFunnelToolResult(
            funnel.QuotesRequested, funnel.QuotesIssued, funnel.QuotesFailed, funnel.QuotesWithPartialOffers,
            funnel.RequestsByChannel, funnel.Steps, funnel.Insurers, errors,
            samples.Select(ToQuoteSnippet).ToList(),
            $"{from:u}..{to:u}; quote_logs only. 'failed' = quotes that ended without an offer; " +
            "'partial' = issued but at least one insurer failed to price");
    }

    public async Task<CustomerJourneyToolResult> TraceCustomerJourneyAsync(CustomerJourneyArgs a, ToolContext ctx, CancellationToken ct)
    {
        var quotes = RequireQuotes();
        var (from, to) = Window(null, null, ctx);
        var limit = Math.Clamp(a.Limit ?? 40, 10, MaxJourneyEntries);
        var userCode = Identifier(a.UserCode, "userCode")?.ToUpperInvariant();
        var customerNo = Identifier(a.CustomerNo, "customerNo")?.ToUpperInvariant();
        var quoteId = Identifier(a.QuoteId, "quoteId")?.ToUpperInvariant();
        if (userCode is null && customerNo is null && quoteId is null)
            throw new ToolArgumentException("Give userCode, customerNo or quoteId.");

        // 1) Müşteriyi çöz. Teklif numarasıyla gelindiyse teklifin kimlik anahtarlarından müşteriye git.
        string? identityFromQuote = null;
        if (quoteId is not null)
        {
            var seed = (await quotes.SearchAsync(new QuoteLogSearchRequest(from, to, QuoteId: quoteId, Limit: 1), ct)).Items.FirstOrDefault()
                       ?? throw new ToolArgumentException($"Quote {quoteId} was not found in the selected time range.");
            customerNo ??= seed.CustomerNo;
            identityFromQuote = seed.IdentityNo;
            if (seed.Channel == QuoteChannel.Mobile) userCode ??= seed.UserCode;
        }

        var resolved = await ResolveAsync(userCode, customerNo, ct, identityFromQuote);
        var customer = resolved.FirstOrDefault(u => u.CustomerNo is not null) ?? resolved.FirstOrDefault();
        var mobileUser = customer?.UserCode ?? userCode;
        var isCustomer = customer is null || customer.CustomerNo is not null;

        // 2) Teklif logları: müşterinin TÜM anahtarlarıyla (kanal fark etmez).
        var customerNos = resolved.Select(u => u.CustomerNo).Append(customerNo).OfType<string>().Distinct().ToList();
        var identities = resolved.Select(u => u.IdentityNo).Append(identityFromQuote).OfType<string>().Distinct().ToList();
        var quotePage = await quotes.SearchAsync(new QuoteLogSearchRequest(from, to,
            UserCodes: isCustomer && mobileUser is not null ? [mobileUser] : null,
            CustomerNos: customerNos, IdentityNos: identities, Limit: MaxResults, Ascending: false), ct);
        var quoteLogs = quotePage.Items;

        // 3) Uygulama logları: müşterinin mobil hesabı varsa (acente kodu ile değil).
        IReadOnlyList<LogEntry> appLogs = [];
        long appTotal = 0;
        if (isCustomer && mobileUser is not null)
        {
            var page = await AppLogs.SearchAsync(new LogSearchRequest(
                From: new DateTimeOffset(from, TimeSpan.Zero), To: new DateTimeOffset(to, TimeSpan.Zero),
                UserCode: mobileUser, Page: 1, PageSize: MaxResults), ct);
            appLogs = page.Items;
            appTotal = page.TotalCount;
        }

        // 4) Eşleştir: correlationId (mobil ↔ teklif) ve quoteId.
        var (links, findings) = Correlate(appLogs, quoteLogs);
        if (customer is null) findings.Insert(0, "The customer was not found in the user directory; only the given keys were searched.");
        else if (!isCustomer) findings.Insert(0, $"{customer.UserCode} is not a customer (roles: {string.Join(", ", customer.Roles)}); quotes they made as an agent are found with SearchQuoteLogs.");

        // Zaman çizelgesi: sorunlu kayıtlar + her akışın son adımı öncelikli, sonra en yeniler.
        var important = appLogs.Where(l => l.Level >= LogSeverity.Warning || l.Operation == MobileQuoteOperation).Select(ToSnippet)
            .Concat(quoteLogs.Where(q => q.Level >= LogSeverity.Warning || q.Outcome is not null).Select(ToQuoteSnippet));
        var rest = appLogs.Select(ToSnippet).Concat(quoteLogs.Select(ToQuoteSnippet));
        var timeline = important.Concat(rest).DistinctBy(l => l.Id).Take(limit).OrderBy(l => l.Timestamp).ToList();

        var truncated = appTotal + quotePage.TotalCount > timeline.Count;
        return new CustomerJourneyToolResult(customer is null ? null : ToProfile(customer), links, findings, timeline,
            appTotal, quotePage.TotalCount, truncated,
            $"{from:u}..{to:u}; app_logs (by userCode) + quote_logs (by userCode/customerNo/identityNo), linked by correlationId and quoteId");
    }

    // ---------------- eşleştirme ----------------

    /// Platformun hesapladığı deterministik bağlantılar ve bulgular. LLM bunları yorumlar ama kendisi JOIN yapmaz.
    private static (List<JourneyLink> Links, List<string> Findings) Correlate(IReadOnlyList<LogEntry> appLogs, IReadOnlyList<QuoteLogEntry> quoteLogs)
    {
        var links = new List<JourneyLink>();
        var findings = new List<string>();
        var quotesByCorrelation = quoteLogs.Where(q => q.CorrelationId is not null).GroupBy(q => q.CorrelationId!).ToDictionary(g => g.Key, g => g.ToList());

        // Mobil teklif istekleri
        foreach (var flow in appLogs.Where(l => l.Operation == MobileQuoteOperation && l.CorrelationId is not null).GroupBy(l => l.CorrelationId!))
        {
            var mobileFinal = flow.Where(l => l.Outcome is not null).OrderByDescending(l => l.Timestamp).FirstOrDefault();
            var mobileError = flow.Where(l => l.ErrorCode is not null).OrderBy(l => ErrorCodeCatalog.IsSymptom(l.ErrorCode)).FirstOrDefault();

            if (quotesByCorrelation.TryGetValue(flow.Key, out var q))
            {
                var root = RootError(q);
                var final = q.Where(x => x.Outcome is not null && x.Step != QuoteFunnelCalculator.PricingStep).OrderByDescending(x => x.Timestamp).FirstOrDefault();
                links.Add(new JourneyLink(flow.Key, q[0].QuoteId, "Mobile", mobileFinal?.Outcome?.ToString(), final?.Outcome?.ToString(), root?.ErrorCode));
                if (root is not null)
                    findings.Add($"Mobile quote request {flow.Key} → quote {q[0].QuoteId}: {root.ErrorCode} in {root.Service} at step {root.Step}" +
                                 (mobileError is not null ? $" (the app only logged {mobileError.ErrorCode})." : "."));
            }
            else
            {
                links.Add(new JourneyLink(flow.Key, null, "Mobile", mobileFinal?.Outcome?.ToString(), null, mobileError?.ErrorCode));
                findings.Add($"Mobile quote request {flow.Key} never reached QuoteEngine (no quote_logs with this correlationId)" +
                             (mobileError is not null ? $"; the app logged {mobileError.ErrorCode} in {mobileError.Service}." : "."));
            }
        }

        // Mobil izi olmayan teklifler (web / acente)
        foreach (var quote in quoteLogs.Where(q => q.CorrelationId is null || !appLogs.Any(l => l.CorrelationId == q.CorrelationId)).GroupBy(q => q.QuoteId))
        {
            var first = quote.First();
            var root = RootError(quote.ToList());
            var final = quote.Where(x => x.Outcome is not null && x.Step != QuoteFunnelCalculator.PricingStep).OrderByDescending(x => x.Timestamp).FirstOrDefault();
            links.Add(new JourneyLink(first.CorrelationId, quote.Key, first.Channel.ToString(), null, final?.Outcome?.ToString(), root?.ErrorCode));
            var who = first.Channel switch
            {
                QuoteChannel.Agent => $"by agent {first.UserCode}",
                QuoteChannel.Web => "anonymously on the web (matched by identity number)",
                _ => "via mobile"
            };
            findings.Add($"Quote {quote.Key} was requested {who}; " +
                         (root is null ? $"result: {final?.Outcome?.ToString() ?? "unknown"}." : $"{root.ErrorCode} in {root.Service} at step {root.Step}."));
        }

        // Eksik sigortacı teklifleri
        foreach (var quote in quoteLogs.GroupBy(q => q.QuoteId))
        {
            var missing = quote.Where(q => q.Step == QuoteFunnelCalculator.PricingStep && q.ErrorCode is not null).Select(q => q.InsurerCode).OfType<string>().Distinct().ToList();
            if (missing.Count > 0 && quote.Any(q => q.Step == QuoteFunnelCalculator.IssuedStep))
                findings.Add($"Quote {quote.Key} was issued without an offer from {string.Join(", ", missing)} (insurer pricing failed).");
        }

        if (links.Count == 0) findings.Add("No quote activity was found for this customer in the selected time range.");
        return (links, findings);
    }

    /// Bir teklifteki kök hata: semptom (QUOTE_FAILED) değil, ondan önceki gerçek neden.
    private static QuoteLogEntry? RootError(IReadOnlyList<QuoteLogEntry> quote)
        => quote.Where(q => q.ErrorCode is not null && !ErrorCodeCatalog.IsSymptom(q.ErrorCode)).OrderBy(q => q.Timestamp).FirstOrDefault()
           ?? quote.Where(q => q.ErrorCode is not null).OrderBy(q => q.Timestamp).FirstOrDefault();

    private async Task<IReadOnlyList<User>> ResolveAsync(string? userCode, string? customerNo, CancellationToken ct, string? identity = null)
    {
        if (Users is null) return [];
        return await Users.FindAsync(
            userCode is null ? null : [userCode], customerNo is null ? null : [customerNo], identity is null ? null : [identity], ct);
    }

    private IQuoteLogReader RequireQuotes() => Quotes ?? throw new ToolArgumentException("The quote log store is not available.");
    private IUserReader RequireUsers() => Users ?? throw new ToolArgumentException("The user directory is not available.");

    private static CustomerProfile ToProfile(User u) => new(
        u.UserCode, u.UserId, u.CustomerNo, u.Status.ToString(), u.Roles, MaskIdentity(u.IdentityNo),
        u.Roles.Contains("Customer") && u.Status == UserStatus.Active, MaskPhone(u.Phone));

    /// "+905551014729" → "+90*******4729": teyit için yeterli iz, kimliği açığa çıkarmaz (maskeleyici kalıplarına da takılmaz).
    public static string? MaskPhone(string? phone)
        => string.IsNullOrEmpty(phone) || phone.Length < 8 ? (phone is null ? null : "***")
            : phone[..3] + new string('*', phone.Length - 7) + phone[^4..];

    /// 11 haneli kimlik no → "*********95". Maskeleyici uzun sayıları zaten siler; bu, eşleşmeyi doğrulamaya yetecek kadar iz bırakır.
    public static string? MaskIdentity(string? identity)
        => string.IsNullOrEmpty(identity) ? null : new string('*', Math.Max(0, identity.Length - 2)) + identity[^Math.Min(2, identity.Length)..];

    private LogSnippet ToQuoteSnippet(QuoteLogEntry q)
    {
        var metadata = new Dictionary<string, string>
        {
            ["quoteId"] = q.QuoteId,
            ["channel"] = q.Channel.ToString(),
            ["product"] = q.Product
        };
        if (q.CustomerNo is not null) metadata["customerNo"] = q.CustomerNo;
        if (MaskIdentity(q.IdentityNo) is { } id) metadata["identityNo"] = id;
        if (q.InsurerCode is not null) metadata["insurerCode"] = q.InsurerCode;
        if (q.PremiumAmount is { } p) metadata["premiumTry"] = p.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        foreach (var (k, v) in q.Metadata) metadata.TryAdd(k, Masker.MaskRequired(v));

        return new LogSnippet(
            q.Id ?? "", q.Timestamp, q.Level.ToString(), QuoteEngineApplication, q.Service,
            q.UserCode, q.CorrelationId, null, q.Step, q.Outcome?.ToString(),
            q.ErrorCode, Masker.Mask(q.ExceptionType), Masker.MaskRequired(q.Message), q.DurationMs,
            metadata, LogSources.QuoteLogs);
    }

    private static QuoteChannel? ParseChannel(string? channel)
    {
        if (string.IsNullOrWhiteSpace(channel)) return null;
        if (Enum.TryParse<QuoteChannel>(channel.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)) return parsed;
        throw new ToolArgumentException("'channel' must be one of: Mobile, Web, Agent.");
    }
}
