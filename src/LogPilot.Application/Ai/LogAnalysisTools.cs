using System.Text.RegularExpressions;
using LogPilot.Application.Analytics;
using LogPilot.Application.Incidents;
using LogPilot.Application.Knowledge;
using LogPilot.Application.Logs;
using LogPilot.Application.Notifications;
using LogPilot.Application.Quotes;
using LogPilot.Application.Users;
using LogPilot.Domain.Incidents;
using LogPilot.Domain.Logs;

namespace LogPilot.Application.Ai;

/// ROL: salt-okunur analist. Bağımlılıkların HEPSİ okuma arayüzleridir (I*Reader); yazma/silme metodu yoktur.
/// Bu, ArchitectureTests ile denetlenir; DI'da bu arayüzler read-only MongoDB hesabına bağlanır.
public sealed partial class LogAnalysisTools(
    ILogReader logs,
    AnalyticsService analytics,
    IIncidentReader incidents,
    SensitiveDataMasker masker,
    IKnowledgeReader? knowledge = null,
    IQuoteLogReader? quotes = null,
    IUserReader? users = null,
    ISmsLogReader? sms = null) : ILogAnalysisTools
{
    // Teklif araçları (LogAnalysisTools.Quotes.cs) bu erişimcileri kullanır.
    private ILogReader AppLogs => logs;
    private IQuoteLogReader? Quotes => quotes;
    private IUserReader? Users => users;
    private SensitiveDataMasker Masker => masker;
    private ISmsLogReader? SmsLogs => sms;

    public const int MaxKnowledgeResults = 5;
    public const int MaxResults = 50;
    public const int DefaultResults = 20;
    private const int SamplesPerGroup = 3;
    private const int MaxIncidentSamples = 10;

    private static readonly Regex IdentifierPattern = new(@"^[A-Za-z0-9_.\-]{1,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ObjectIdPattern = new(@"^[0-9a-fA-F]{24}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<LogToolResult> SearchLogsAsync(SearchLogsArgs a, ToolContext ctx, CancellationToken ct)
    {
        var (from, to) = Window(a.From, a.To, ctx);
        var limit = Limit(a.Limit);

        var page = await logs.SearchAsync(new LogSearchRequest(
            From: new DateTimeOffset(from, TimeSpan.Zero),
            To: new DateTimeOffset(to, TimeSpan.Zero),
            Level: ParseLevel(a.Level),
            Application: Identifier(a.Application, "application"),
            Service: Identifier(a.Service, "service"),
            UserCode: Identifier(a.UserCode, "userCode"),
            ErrorCode: Identifier(a.ErrorCode, "errorCode"),
            CorrelationId: Identifier(a.CorrelationId, "correlationId"),
            TransactionId: Identifier(a.TransactionId, "transactionId"),
            Page: 1, PageSize: limit), ct);

        return ToLogResult(page.Items, page.TotalCount, from, to, $"newest first, max {limit}");
    }

    public async Task<LogToolResult> GetUserRecentErrorsAsync(UserRecentErrorsArgs a, ToolContext ctx, CancellationToken ct)
    {
        var user = Identifier(a.UserCode, "userCode") ?? throw new ToolArgumentException("userCode is required.");
        var (from, to) = Window(null, null, ctx);
        var limit = Limit(a.Limit);

        // Kullanıcının son loglarını çek; uyarı/hata olanları + en yeni başarıyı döndür.
        // Başarı logu, "hatadan sonra düzeldi mi?" sorusunu cevaplamak için gerekli (güven hesabını etkiler).
        var page = await logs.SearchAsync(new LogSearchRequest(
            From: new DateTimeOffset(from, TimeSpan.Zero), To: new DateTimeOffset(to, TimeSpan.Zero),
            UserCode: user, Page: 1, PageSize: MaxResults), ct);

        var problems = page.Items.Where(l => l.Level >= LogSeverity.Warning).Take(limit).ToList();
        var latestSuccess = page.Items.FirstOrDefault(l => l.Outcome == OperationOutcome.Success);
        var selected = (latestSuccess is null ? problems : problems.Append(latestSuccess))
            .DistinctBy(l => l.Id).OrderByDescending(l => l.Timestamp).ToList();

        return ToLogResult(selected, page.TotalCount, from, to, $"user {user}: latest warnings/errors plus latest success, max {limit}");
    }

    public async Task<ServiceHealthToolResult> GetServiceHealthAsync(ServiceHealthArgs a, ToolContext ctx, CancellationToken ct)
    {
        var service = Identifier(a.Service, "service");
        var all = await analytics.GetServiceHealthAsync(Utc(ctx.From), Utc(ctx.To), ct);

        var items = all.Where(h => service is null || string.Equals(h.Service, service, StringComparison.OrdinalIgnoreCase)).ToList();
        return new ServiceHealthToolResult(items, $"{ctx.From:u}..{ctx.To:u}; ratio = error logs / all logs");
    }

    public async Task<ErrorStatisticsToolResult> GetErrorStatisticsAsync(ErrorStatisticsArgs a, ToolContext ctx, CancellationToken ct)
    {
        var limit = Math.Clamp(a.Limit ?? 10, 1, 20);
        var groups = (await analytics.GetErrorGroupsAsync(Utc(ctx.From), Utc(ctx.To), ct)).Take(limit).ToList();

        // Her grup için birkaç örnek log: kanıt olarak gösterilebilsin ve ID'leri doğrulanabilsin.
        var sampleIds = groups.SelectMany(g => g.RecentLogIds.Take(SamplesPerGroup)).ToList();
        var samples = (await logs.GetByIdsAsync(sampleIds, ct)).ToDictionary(l => l.Id!);

        var items = groups.Select(g => new ErrorGroupSummary(
            g.ErrorCode, g.Count, g.UserCodes.Count, g.UserCodes.Take(10).ToList(), g.Services, g.FirstSeen, g.LastSeen,
            g.RecentLogIds.Take(SamplesPerGroup).Where(samples.ContainsKey).Select(id => ToSnippet(samples[id])).ToList()))
            .ToList();

        return new ErrorStatisticsToolResult(items, $"{ctx.From:u}..{ctx.To:u}; Error/Critical logs grouped by error code");
    }

    public async Task<LogToolResult> FindRelatedLogsAsync(RelatedLogsArgs a, ToolContext ctx, CancellationToken ct)
    {
        var correlationId = Identifier(a.CorrelationId, "correlationId");
        var transactionId = Identifier(a.TransactionId, "transactionId");
        var quoteId = Identifier(a.QuoteId, "quoteId")?.ToUpperInvariant();
        var (from, to) = Window(null, null, ctx);

        if (correlationId is null && transactionId is null && quoteId is null && !string.IsNullOrWhiteSpace(a.LogId))
        {
            if (!ObjectIdPattern.IsMatch(a.LogId)) throw new ToolArgumentException("logId is not a valid log id.");
            var seed = await logs.GetByIdAsync(a.LogId, ct);
            if (seed is not null)
            {
                correlationId = seed.CorrelationId;
                transactionId = seed.TransactionId;
            }
            else if (Quotes is not null && await Quotes.GetByIdAsync(a.LogId, ct) is { } quoteSeed)
            {
                quoteId = quoteSeed.QuoteId;
            }
            else throw new ToolArgumentException("The log was not found.");
        }

        if (correlationId is null && transactionId is null && quoteId is null)
            throw new ToolArgumentException("Provide correlationId, transactionId, quoteId or a logId that has one.");

        // Tablolar arası: teklif numarasıyla gelindiyse önce teklif loglarını al, mobil kanaldaysa correlationId'yi
        // oradan öğrenip uygulama loglarını da getir. correlationId ile gelindiyse aynı akışın teklif loglarını da ekle.
        var quoteLogs = new List<LogSnippet>();
        long quoteTotal = 0;
        if (Quotes is not null && (quoteId is not null || correlationId is not null))
        {
            var page = await Quotes.SearchAsync(new QuoteLogSearchRequest(from, to,
                QuoteId: quoteId, CorrelationId: quoteId is null ? correlationId : null, Limit: MaxResults, Ascending: true), ct);
            quoteLogs.AddRange(page.Items.Select(ToQuoteSnippet));
            quoteTotal = page.TotalCount;
            correlationId ??= page.Items.Select(q => q.CorrelationId).FirstOrDefault(c => c is not null);
        }

        var appLogs = new List<LogSnippet>();
        long appTotal = 0;
        if (correlationId is not null || transactionId is not null)
        {
            var page = await logs.SearchAsync(new LogSearchRequest(
                From: new DateTimeOffset(from, TimeSpan.Zero), To: new DateTimeOffset(to, TimeSpan.Zero),
                CorrelationId: correlationId, TransactionId: transactionId,
                Page: 1, PageSize: MaxResults, Ascending: true), ct);
            appLogs.AddRange(page.Items.Select(ToSnippet));
            appTotal = page.TotalCount;
        }

        var merged = appLogs.Concat(quoteLogs).OrderBy(l => l.Timestamp).ToList();
        var total = appTotal + quoteTotal;
        return new LogToolResult(merged, total, total > merged.Count,
            $"{from:u}..{to:u}; oldest first, app_logs + quote_logs of the same flow, max {MaxResults} each");
    }

    /// Doğrulanmış geçmiş vakalar. En az bir filtre gerekir (tüm hafızayı dökmek yok).
    public async Task<KnowledgeToolResult> SearchKnowledgeAsync(SearchKnowledgeArgs a, CancellationToken ct)
    {
        var errorCode = Identifier(a.ErrorCode, "errorCode");
        var service = Identifier(a.Service, "service");
        var text = string.IsNullOrWhiteSpace(a.Text) ? null : a.Text.Trim();
        if (text?.Length > 100) throw new ToolArgumentException("text must be at most 100 characters.");
        if (errorCode is null && service is null && text is null)
            throw new ToolArgumentException("Give at least one of errorCode, service or text.");
        var limit = Math.Clamp(a.Limit ?? MaxKnowledgeResults, 1, MaxKnowledgeResults);

        if (knowledge is null) return new KnowledgeToolResult([], "The knowledge base is not available.");

        var page = await knowledge.SearchAsync(new KnowledgeQuery(
            errorCode is null ? null : [errorCode], service is null ? null : [service], text, 1, limit), ct);

        var cases = page.Items.Select(c => new KnowledgeSnippet(
            c.Id!, masker.MaskRequired(c.Title), c.ErrorCodes, c.Services, masker.Mask(c.Symptom), masker.Mask(c.RootCause),
            masker.MaskRequired(c.Resolution), c.Source.ToString(), c.UpdatedAt)).ToList();

        return new KnowledgeToolResult(cases,
            "Past cases verified by people. Use them as hints only; current logs remain the only evidence.");
    }

    public async Task<IncidentListToolResult> GetActiveIncidentsAsync(ActiveIncidentsArgs a, ToolContext ctx, CancellationToken ct)
    {
        var limit = Math.Clamp(a.Limit ?? 10, 1, 20);
        var active = await incidents.GetActiveAsync(limit, ct);

        var items = active.Where(i => i.LastSeenAt >= ctx.From).Select(ToIncidentItem).ToList();
        return new IncidentListToolResult(items, $"unresolved incidents last seen since {ctx.From:u}");
    }

    public async Task<IncidentDetailsToolResult> GetIncidentDetailsAsync(IncidentDetailsArgs a, ToolContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(a.IncidentId) || !ObjectIdPattern.IsMatch(a.IncidentId))
            throw new ToolArgumentException("incidentId is not a valid incident id.");

        var incident = await incidents.GetByIdAsync(a.IncidentId, ct) ?? throw new ToolArgumentException("The incident was not found.");
        var samples = await logs.GetByIdsAsync(incident.RelatedLogIds.Take(MaxIncidentSamples).ToList(), ct);

        return new IncidentDetailsToolResult(
            ToIncidentItem(incident),
            incident.AffectedUserCodes.Take(20).ToList(),
            samples.Select(ToSnippet).ToList());
    }

    // ---------------- yardımcılar ----------------

    private LogToolResult ToLogResult(IReadOnlyList<LogEntry> items, long total, DateTime from, DateTime to, string note)
        => new(items.Select(ToSnippet).ToList(), total, total > items.Count, $"{from:u}..{to:u}; {note}");

    private LogSnippet ToSnippet(LogEntry l) => new(
        l.Id ?? "", l.Timestamp, l.Level.ToString(), l.Application, l.Service,
        l.UserCode, l.CorrelationId, l.TransactionId, l.Operation, l.Outcome?.ToString(),
        l.ErrorCode, masker.Mask(l.ExceptionType), masker.MaskRequired(l.Message), l.DurationMs,
        l.Metadata.ToDictionary(kv => kv.Key, kv => masker.MaskRequired(kv.Value)));

    private IncidentToolItem ToIncidentItem(Incident i) => new(
        i.Id ?? "", masker.MaskRequired(i.Title), i.Severity.ToString(), i.Status.ToString(), i.ErrorCode,
        i.AffectedServices, i.AffectedUserCodes.Count, i.LogCount, i.FirstSeenAt, i.LastSeenAt, masker.Mask(i.AiSummary));

    private static (DateTime From, DateTime To) Window(DateTime? from, DateTime? to, ToolContext ctx)
    {
        var f = from is { } x ? Max(ToUtc(x), ctx.From) : ctx.From;
        var t = to is { } y ? Min(ToUtc(y), ctx.To) : ctx.To;
        if (f >= t) throw new ToolArgumentException("The requested time range does not overlap the selected time range.");
        return (f, t);
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static DateTimeOffset Utc(DateTime value) => new(ToUtc(value), TimeSpan.Zero);
    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private static int Limit(int? requested) => Math.Clamp(requested ?? DefaultResults, 1, MaxResults);

    private static string? Identifier(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (!IdentifierPattern.IsMatch(trimmed))
            throw new ToolArgumentException($"'{name}' contains unsupported characters or is too long.");
        return trimmed;
    }

    private static LogSeverity? ParseLevel(string? level)
    {
        if (string.IsNullOrWhiteSpace(level)) return null;
        if (Enum.TryParse<LogSeverity>(level.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)) return parsed;
        throw new ToolArgumentException("'level' must be one of: Debug, Information, Warning, Error, Critical.");
    }
}
