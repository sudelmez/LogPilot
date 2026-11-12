using LogPilot.Application.Notifications;
using LogPilot.Application.Quotes;
using LogPilot.Domain.Logs;
using LogPilot.Domain.Notifications;
using LogPilot.Domain.Quotes;
using LogPilot.Domain.Users;

namespace LogPilot.Application.Ai;

/// Zincirleme (multi-hop) araştırma: cevap tek tabloda değil, tablolar arasında SIRA SIRA bulunur.
///   "Poliçe SMS'i müşteriye ulaştı mı?"
///     1) quote_logs : quoteId / policyNo → teklifin sahibi (identityNo, customerNo) + poliçe zamanı
///     2) users      : identityNo → müşteri → telefon
///     3) sms_logs   : telefon (+ poliçe zamanından sonrası, POLICY_ISSUED şablonu) → mesajlar ve teslim raporları
/// Her halka TraceHop olarak kaydedilir; bir halka koparsa (kayıt yok, telefon yok) zincir orada durur ve bu da bir bulgudur.
public sealed partial class LogAnalysisTools
{
    public const string PolicySmsTemplate = "POLICY_ISSUED";
    private const string SmsApplication = "SmsGateway";
    private const string PolicyIssuedStep = "PolicyIssued";
    private const string PublishFailedCode = "NOTIFICATION_PUBLISH_FAILED";

    public async Task<NotificationTraceToolResult> TraceNotificationDeliveryAsync(NotificationTraceArgs a, ToolContext ctx, CancellationToken ct)
    {
        var quotes = RequireQuotes();
        var sms = SmsLogs ?? throw new ToolArgumentException("The SMS log store is not available.");
        var (from, to) = Window(null, null, ctx);

        var quoteId = Identifier(a.QuoteId, "quoteId")?.ToUpperInvariant();
        var policyNo = Identifier(a.PolicyNo, "policyNo")?.ToUpperInvariant();
        var userCode = Identifier(a.UserCode, "userCode")?.ToUpperInvariant();
        var customerNo = Identifier(a.CustomerNo, "customerNo")?.ToUpperInvariant();
        var template = Identifier(a.TemplateCode, "templateCode")?.ToUpperInvariant();
        if (quoteId is null && policyNo is null && userCode is null && customerNo is null)
            throw new ToolArgumentException("Give quoteId, policyNo, userCode or customerNo.");

        var path = new List<TraceHop>();
        var findings = new List<string>();
        User? customer = null;
        List<QuoteLogEntry> quoteLogs = [];

        NotificationTraceToolResult Stop(string verdict) => new(path, customer is null ? null : ToProfile(customer),
            quoteLogs.FirstOrDefault()?.QuoteId, quoteLogs.Select(q => q.PolicyNo).FirstOrDefault(p => p is not null),
            verdict, [], findings, quoteLogs.Where(q => q.Step == PolicyIssuedStep || q.ErrorCode is not null).Select(ToQuoteSnippet).ToList(),
            Scope(from, to));

        if (quoteId is not null || policyNo is not null)
        {
            // HALKA 1: teklif tablosu → teklifin sahibinin kimlik anahtarları
            quoteLogs = await LoadQuoteAsync(quotes, from, to, quoteId, policyNo, ct);
            var key = quoteId is not null ? $"quoteId={quoteId}" : $"policyNo={policyNo}";
            if (quoteLogs.Count == 0)
            {
                path.Add(new TraceHop(1, LogSources.QuoteLogs, key, false, "No quote/policy logs in the selected time range."));
                findings.Add($"No quote or policy was found for {key}; the chain cannot continue.");
                return Stop("Unknown");
            }
            path.Add(new TraceHop(1, LogSources.QuoteLogs, key, true, DescribeQuote(quoteLogs)));

            // HALKA 2: kullanıcı dizini → telefon (kimlik no ile; yoksa müşteri no ile)
            var identity = quoteLogs.Select(q => q.IdentityNo).FirstOrDefault(i => i is not null);
            var custNo = quoteLogs.Select(q => q.CustomerNo).FirstOrDefault(c => c is not null);
            var found = Users is null ? [] : await Users.FindAsync(null, custNo is null ? null : [custNo], identity is null ? null : [identity], ct);
            customer = found.FirstOrDefault();
            path.Add(new TraceHop(2, "users", identity is not null ? $"identityNo={MaskIdentity(identity)}" : $"customerNo={custNo}",
                customer is not null, customer is null ? "No customer with this identity in the user directory."
                    : $"Customer {customer.UserCode} ({customer.CustomerNo}), phone {MaskPhone(customer.Phone) ?? "not on record"}."));
        }
        else
        {
            // HALKA 1: kullanıcı dizini (kişiyle başlandıysa sıra değişir: önce kişi, sonra teklif)
            var found = await RequireUsers().FindAsync(userCode is null ? null : [userCode], customerNo is null ? null : [customerNo], null, ct);
            customer = found.FirstOrDefault(u => u.CustomerNo is not null) ?? found.FirstOrDefault();
            path.Add(new TraceHop(1, "users", userCode is not null ? $"userCode={userCode}" : $"customerNo={customerNo}", customer is not null,
                customer is null ? "Not found in the user directory." : $"Customer {customer.UserCode} ({customer.CustomerNo ?? "no customer number"}), phone {MaskPhone(customer.Phone) ?? "not on record"}."));
            if (customer is null) { findings.Add("The person is not in the user directory, so neither their policy nor their phone can be found."); return Stop("Unknown"); }
            if (customer.CustomerNo is null)
            {
                findings.Add($"{customer.UserCode} is not a customer (roles: {string.Join(", ", customer.Roles)}). Policy SMS go to the customer, not to the agent.");
                return Stop("Unknown");
            }

            // HALKA 2: teklif tablosu → müşterinin en son poliçesi (yoksa en son teklifi)
            var keys = new QuoteLogSearchRequest(from, to, UserCodes: [customer.UserCode], CustomerNos: [customer.CustomerNo],
                IdentityNos: customer.IdentityNo is null ? null : [customer.IdentityNo], Step: PolicyIssuedStep, Limit: 1);
            var latest = (await quotes.SearchAsync(keys, ct)).Items.FirstOrDefault()
                         ?? (await quotes.SearchAsync(keys with { Step = null }, ct)).Items.FirstOrDefault();
            if (latest is not null) quoteLogs = await LoadQuoteAsync(quotes, from, to, latest.QuoteId, null, ct);
            path.Add(new TraceHop(2, LogSources.QuoteLogs, "userCode/customerNo/identityNo of the customer", quoteLogs.Count > 0,
                quoteLogs.Count > 0 ? DescribeQuote(quoteLogs) : "No quote or policy for this customer in the selected time range."));
        }

        if (customer is null)
        {
            findings.Add("The quote's identity number does not match any customer in the user directory, so the phone number is unknown and sms_logs cannot be searched (data quality issue).");
            return Stop("Unknown");
        }
        if (customer.Phone is null)
        {
            findings.Add($"Customer {customer.UserCode} has no phone number on record, so no SMS can be sent or traced.");
            return Stop("Unknown");
        }

        // HALKA 3: SMS tablosu → telefon + poliçe zamanından sonrası
        var policy = quoteLogs.FirstOrDefault(q => q.Step == PolicyIssuedStep);
        template ??= policy is not null ? PolicySmsTemplate : null;
        var since = (policy?.Timestamp ?? quoteLogs.Select(q => q.Timestamp).DefaultIfEmpty(from).Min()).AddMinutes(-1);
        var smsFrom = since > from ? since : from;
        var smsPage = await sms.SearchAsync(new SmsLogSearchRequest(smsFrom, to, Phones: [customer.Phone], TemplateCode: template,
            Limit: MaxResults, Ascending: true), ct);
        var smsLogs = smsPage.Items;

        var messages = smsLogs.GroupBy(m => m.MessageId).Select(g =>
        {
            var ordered = g.OrderBy(m => m.Timestamp).ToList();
            var last = ordered[^1];
            return new SmsMessageSummary(g.Key, last.TemplateCode, last.Status.ToString(), last.Provider,
                ordered.Count(m => m.Status is SmsStatus.SentToProvider or SmsStatus.Failed),
                ordered[0].Timestamp, last.Timestamp, ordered.LastOrDefault(m => m.ErrorCode is not null)?.ErrorCode);
        }).OrderBy(m => m.FirstAt).ToList();

        path.Add(new TraceHop(path.Count + 1, LogSources.SmsLogs,
            $"phone={MaskPhone(customer.Phone)}{(template is null ? "" : $", template={template}")}, since {smsFrom:u}",
            messages.Count > 0, messages.Count == 0 ? "No SMS was logged for this phone." :
                string.Join("; ", messages.Select(m => $"{m.MessageId} {m.TemplateCode}: {m.FinalStatus}" + (m.ErrorCode is null ? "" : $" ({m.ErrorCode})")))));

        // Karar: mesajların SON durumuna göre.
        var publishFailure = quoteLogs.FirstOrDefault(q => q.ErrorCode == PublishFailedCode);
        string verdict;
        if (messages.Any(m => m.FinalStatus == nameof(SmsStatus.Delivered)))
        {
            verdict = "Delivered";
            var d = messages.First(m => m.FinalStatus == nameof(SmsStatus.Delivered));
            findings.Add($"The {d.TemplateCode} SMS {d.MessageId} was delivered to the customer's handset at {d.LastAt:u} (provider {d.Provider}).");
        }
        else if (messages.Any(m => m.FinalStatus == nameof(SmsStatus.Undelivered)))
        {
            verdict = "NotDeliveredToHandset";
            findings.Add("The SMS was sent to the provider, but the operator's delivery report says it did not reach the handset (outside our systems: phone off, out of coverage or wrong number).");
        }
        else if (messages.Any(m => m.FinalStatus == nameof(SmsStatus.Failed)))
        {
            verdict = "FailedToSend";
            var f = messages.First(m => m.FinalStatus == nameof(SmsStatus.Failed));
            findings.Add($"The SMS {f.MessageId} could not be sent: provider {f.Provider} rejected it {f.Attempts} time(s) ({f.ErrorCode}). The customer did not receive it.");
        }
        else if (messages.Count > 0)
        {
            verdict = "Pending";
            findings.Add("The SMS is queued or sent to the provider, but no delivery report has arrived yet.");
        }
        else if (publishFailure is not null)
        {
            verdict = "NeverSent";
            findings.Add($"No SMS exists for this phone: {publishFailure.Service} failed to publish the notification event ({PublishFailedCode}) right after the policy was issued, so the SMS system was never asked to send it.");
        }
        else if (policy is null)
        {
            verdict = "NoPolicy";
            findings.Add("The quote was not turned into a policy in the selected time range, so no policy SMS was expected.");
        }
        else
        {
            verdict = "NeverSent";
            findings.Add("A policy was issued but no SMS was logged for the customer's phone, and no error explains why (check the notification pipeline).");
        }

        var evidence = quoteLogs.Where(q => q.Step == PolicyIssuedStep || q.ErrorCode is not null).Select(ToQuoteSnippet)
            .Concat(smsLogs.Select(ToSmsSnippet)).OrderBy(l => l.Timestamp).ToList();

        return new NotificationTraceToolResult(path, ToProfile(customer), quoteLogs.FirstOrDefault()?.QuoteId,
            policy?.PolicyNo, verdict, messages, findings, evidence, Scope(from, to));
    }

    public async Task<LogToolResult> SearchSmsLogsAsync(SearchSmsLogsArgs a, ToolContext ctx, CancellationToken ct)
    {
        var sms = SmsLogs ?? throw new ToolArgumentException("The SMS log store is not available.");
        var (from, to) = Window(null, null, ctx);
        var limit = Limit(a.Limit);
        var userCode = Identifier(a.UserCode, "userCode")?.ToUpperInvariant();
        var customerNo = Identifier(a.CustomerNo, "customerNo")?.ToUpperInvariant();
        var quoteId = Identifier(a.QuoteId, "quoteId")?.ToUpperInvariant();

        // Telefon numarası LLM'den ALINMAZ: kişi/teklif → dizin → telefon çözümü burada yapılır.
        List<string>? phones = null;
        var note = "all phones";
        if (userCode is not null || customerNo is not null || quoteId is not null)
        {
            string? identity = null;
            if (quoteId is not null)
            {
                var q = (await RequireQuotes().SearchAsync(new QuoteLogSearchRequest(from, to, QuoteId: quoteId, Limit: 1), ct)).Items.FirstOrDefault();
                identity = q?.IdentityNo;
                customerNo ??= q?.CustomerNo;
            }
            var people = await RequireUsers().FindAsync(userCode is null ? null : [userCode], customerNo is null ? null : [customerNo],
                identity is null ? null : [identity], ct);
            phones = people.Select(p => p.Phone).OfType<string>().Distinct().ToList();
            if (phones.Count == 0)
                return new LogToolResult([], 0, false, $"{from:u}..{to:u}; sms_logs: no phone number could be resolved for the given person/quote via users");
            note = $"phone(s) of {string.Join(", ", people.Select(p => p.UserCode))} resolved via users: {string.Join(", ", phones.Select(MaskPhone))}";
        }

        var page = await sms.SearchAsync(new SmsLogSearchRequest(from, to, Phones: phones,
            TemplateCode: Identifier(a.TemplateCode, "templateCode")?.ToUpperInvariant(), Status: ParseSmsStatus(a.Status),
            ErrorCode: Identifier(a.ErrorCode, "errorCode"), MinLevel: a.OnlyProblems == true ? LogSeverity.Warning : null, Limit: limit), ct);

        var items = page.Items.Select(ToSmsSnippet).ToList();
        return new LogToolResult(items, page.TotalCount, page.TotalCount > items.Count, $"{from:u}..{to:u}; sms_logs, newest first, max {limit}; {note}");
    }

    // ---------------- yardımcılar ----------------

    private static async Task<List<QuoteLogEntry>> LoadQuoteAsync(IQuoteLogReader quotes, DateTime from, DateTime to,
        string? quoteId, string? policyNo, CancellationToken ct)
    {
        if (quoteId is null && policyNo is not null)
            quoteId = (await quotes.SearchAsync(new QuoteLogSearchRequest(from, to, PolicyNo: policyNo, Limit: 1), ct)).Items.FirstOrDefault()?.QuoteId;
        if (quoteId is null) return [];
        return (await quotes.SearchAsync(new QuoteLogSearchRequest(from, to, QuoteId: quoteId, Limit: MaxResults, Ascending: true), ct)).Items.ToList();
    }

    private static string DescribeQuote(IReadOnlyList<QuoteLogEntry> logs)
    {
        var first = logs[0];
        var policy = logs.FirstOrDefault(q => q.Step == PolicyIssuedStep);
        return $"Quote {first.QuoteId} ({first.Product}, {first.Channel} channel)" +
               (policy is null ? ", no policy issued" : $", policy {policy.PolicyNo} issued at {policy.Timestamp:u}") +
               $"; owner identityNo {MaskIdentity(first.IdentityNo) ?? "-"}, customerNo {first.CustomerNo ?? "-"}.";
    }

    private static string Scope(DateTime from, DateTime to)
        => $"{from:u}..{to:u}; chained lookup quote_logs → users → sms_logs (the SMS system only knows phone numbers)";

    private LogSnippet ToSmsSnippet(SmsLogEntry m)
    {
        var metadata = new Dictionary<string, string>
        {
            ["messageId"] = m.MessageId,
            ["phone"] = MaskPhone(m.Phone) ?? "",
            ["status"] = m.Status.ToString()
        };
        if (m.Provider is not null) metadata["provider"] = m.Provider;
        foreach (var (k, v) in m.Metadata) metadata.TryAdd(k, Masker.MaskRequired(v));

        var outcome = m.Status switch
        {
            SmsStatus.Delivered => "Success",
            SmsStatus.Failed or SmsStatus.Undelivered => "Failure",
            _ => null
        };
        return new LogSnippet(m.Id ?? "", m.Timestamp, m.Level.ToString(), SmsApplication, m.Service, null, null, null,
            m.TemplateCode, outcome, m.ErrorCode, null, Masker.MaskRequired(m.Message), m.DurationMs, metadata, LogSources.SmsLogs);
    }

    private static SmsStatus? ParseSmsStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;
        if (Enum.TryParse<SmsStatus>(status.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)) return parsed;
        throw new ToolArgumentException("'status' must be one of: Queued, SentToProvider, Delivered, Failed, Undelivered.");
    }
}
