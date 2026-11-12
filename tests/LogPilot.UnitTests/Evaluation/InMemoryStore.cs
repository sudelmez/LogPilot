using LogPilot.Application.Analytics;
using LogPilot.Application.Common;
using LogPilot.Application.Incidents;
using LogPilot.Application.Logs;
using LogPilot.Application.Quotes;
using LogPilot.Application.Notifications;
using LogPilot.Domain.Notifications;
using LogPilot.Application.Users;
using LogPilot.Domain.Analytics;
using LogPilot.Domain.Incidents;
using LogPilot.Domain.Logs;
using LogPilot.Domain.Quotes;
using LogPilot.Domain.Users;

namespace LogPilot.UnitTests.Evaluation;

/// Değerlendirme testleri için bellek içi veri deposu. Üretim kodunun KULLANDIĞI arayüzleri uygular; böylece gerçek
/// araçlar ve analitik servisler (AnalyticsService, LogAnalysisTools) MongoDB olmadan uçtan uca çalıştırılabilir.
internal sealed class InMemoryStore : ILogRepository, ILogAnalyticsRepository, IIncidentRepository, IQuoteLogRepository, IUserRepository, ISmsLogRepository
{
    public List<SmsLogEntry> SmsLogs { get; } = [];

    public void AddSms(IEnumerable<SmsLogEntry> entries)
    {
        foreach (var e in entries)
            SmsLogs.Add(new SmsLogEntry
            {
                Id = $"{_next++:x24}", Timestamp = e.Timestamp, Level = e.Level, Service = e.Service, MessageId = e.MessageId,
                Phone = e.Phone, TemplateCode = e.TemplateCode, Status = e.Status, Provider = e.Provider, ErrorCode = e.ErrorCode,
                Message = e.Message, DurationMs = e.DurationMs, Metadata = new Dictionary<string, string>(e.Metadata)
            });
    }

    // ---------------- ISmsLogRepository ----------------
    Task<PagedResult<SmsLogEntry>> ISmsLogReader.SearchAsync(SmsLogSearchRequest r, CancellationToken ct)
    {
        var q = SmsLogs.Where(l => l.Timestamp >= r.From && l.Timestamp < r.To);
        if (r.Phones is { Count: > 0 }) q = q.Where(l => r.Phones.Contains(l.Phone));
        if (r.MessageId is not null) q = q.Where(l => l.MessageId == r.MessageId);
        if (r.TemplateCode is not null) q = q.Where(l => l.TemplateCode == r.TemplateCode);
        if (r.Status is { } st) q = q.Where(l => l.Status == st);
        if (r.ErrorCode is not null) q = q.Where(l => l.ErrorCode == r.ErrorCode);
        if (r.MinLevel is { } lv) q = q.Where(l => l.Level >= lv);
        var all = q.ToList();
        var page = (r.Ascending ? all.OrderBy(l => l.Timestamp) : all.OrderByDescending(l => l.Timestamp)).Take(r.Limit).ToList();
        return Task.FromResult(new PagedResult<SmsLogEntry>(page, all.Count, 1, r.Limit));
    }

    Task<SmsLogEntry?> ISmsLogReader.GetByIdAsync(string id, CancellationToken ct) => Task.FromResult(SmsLogs.FirstOrDefault(l => l.Id == id));

    public Task AddManyAsync(IReadOnlyCollection<SmsLogEntry> entries, CancellationToken ct)
    {
        AddSms(entries);
        return Task.CompletedTask;
    }

    Task<long> ISmsLogReader.GetEstimatedCountAsync(CancellationToken ct) => Task.FromResult((long)SmsLogs.Count);

    public List<QuoteLogEntry> QuoteLogs { get; } = [];
    public List<User> Users { get; } = [];

    public void AddQuotes(IEnumerable<QuoteLogEntry> entries)
    {
        foreach (var e in entries)
            QuoteLogs.Add(new QuoteLogEntry
            {
                Id = $"{_next++:x24}", Timestamp = e.Timestamp, Level = e.Level, Service = e.Service, QuoteId = e.QuoteId,
                Step = e.Step, Channel = e.Channel, Product = e.Product, UserCode = e.UserCode, CustomerNo = e.CustomerNo,
                IdentityNo = e.IdentityNo, CorrelationId = e.CorrelationId, InsurerCode = e.InsurerCode, PolicyNo = e.PolicyNo,
                PremiumAmount = e.PremiumAmount, Outcome = e.Outcome, ErrorCode = e.ErrorCode, ExceptionType = e.ExceptionType,
                Message = e.Message, DurationMs = e.DurationMs, Metadata = new Dictionary<string, string>(e.Metadata)
            });
    }

    // ---------------- IQuoteLogRepository ----------------
    private IEnumerable<QuoteLogEntry> Filter(QuoteLogSearchRequest r)
    {
        var q = QuoteLogs.Where(l => l.Timestamp >= r.From && l.Timestamp < r.To);
        if (r.QuoteId is not null) q = q.Where(l => l.QuoteId == r.QuoteId);
        if (r.PolicyNo is not null) q = q.Where(l => l.PolicyNo == r.PolicyNo);
        if (r.CorrelationId is not null) q = q.Where(l => l.CorrelationId == r.CorrelationId);
        if (r.CorrelationIds is { Count: > 0 }) q = q.Where(l => l.CorrelationId is not null && r.CorrelationIds.Contains(l.CorrelationId));
        if (r.Product is not null) q = q.Where(l => l.Product == r.Product);
        if (r.Channel is { } c) q = q.Where(l => l.Channel == c);
        if (r.InsurerCode is not null) q = q.Where(l => l.InsurerCode == r.InsurerCode);
        if (r.Step is not null) q = q.Where(l => l.Step == r.Step);
        if (r.ErrorCode is not null) q = q.Where(l => l.ErrorCode == r.ErrorCode);
        if (r.Level is { } level) q = q.Where(l => l.Level == level);
        if (r.UserCodes is { Count: > 0 } || r.CustomerNos is { Count: > 0 } || r.IdentityNos is { Count: > 0 })
            q = q.Where(l => (l.UserCode is not null && r.UserCodes?.Contains(l.UserCode) == true)
                             || (l.CustomerNo is not null && r.CustomerNos?.Contains(l.CustomerNo) == true)
                             || (l.IdentityNo is not null && r.IdentityNos?.Contains(l.IdentityNo) == true));
        return q;
    }

    Task<PagedResult<QuoteLogEntry>> IQuoteLogReader.SearchAsync(QuoteLogSearchRequest r, CancellationToken ct)
    {
        var all = Filter(r).ToList();
        var sorted = (r.Ascending ? all.OrderBy(l => l.Timestamp) : all.OrderByDescending(l => l.Timestamp)).Take(r.Limit).ToList();
        return Task.FromResult(new PagedResult<QuoteLogEntry>(sorted, all.Count, 1, r.Limit));
    }

    Task<QuoteLogEntry?> IQuoteLogReader.GetByIdAsync(string id, CancellationToken ct) => Task.FromResult(QuoteLogs.FirstOrDefault(l => l.Id == id));

    Task<IReadOnlyList<QuoteLogEntry>> IQuoteLogReader.GetByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<QuoteLogEntry>>(QuoteLogs.Where(l => ids.Contains(l.Id!)).OrderBy(l => l.Timestamp).ToList());

    public Task AddManyAsync(IReadOnlyCollection<QuoteLogEntry> entries, CancellationToken ct)
    {
        AddQuotes(entries);
        return Task.CompletedTask;
    }

    Task<long> IQuoteLogReader.GetEstimatedCountAsync(CancellationToken ct) => Task.FromResult((long)QuoteLogs.Count);

    public Task<QuoteFunnel> GetFunnelAsync(DateTime from, DateTime to, string? product, QuoteChannel? channel, string? insurerCode, CancellationToken ct)
    {
        var logs = Filter(new QuoteLogSearchRequest(from, to, Product: product, Channel: channel)).ToList();
        if (insurerCode is not null)
        {
            var ids = logs.Where(l => l.InsurerCode == insurerCode).Select(l => l.QuoteId).ToHashSet();
            logs = logs.Where(l => ids.Contains(l.QuoteId)).ToList();
        }
        return Task.FromResult(QuoteFunnelCalculator.Calculate(logs));
    }

    // ---------------- IUserRepository ----------------
    public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<User>>(Users);

    public Task UpsertManyAsync(IReadOnlyCollection<User> users, CancellationToken ct)
    {
        Users.RemoveAll(u => users.Any(x => x.UserCode == u.UserCode));
        Users.AddRange(users);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<User>> FindAsync(IReadOnlyCollection<string>? userCodes, IReadOnlyCollection<string>? customerNos,
        IReadOnlyCollection<string>? identityNos, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<User>>(Users.Where(u =>
            userCodes?.Contains(u.UserCode) == true
            || (u.CustomerNo is not null && customerNos?.Contains(u.CustomerNo) == true)
            || (u.IdentityNo is not null && identityNos?.Contains(u.IdentityNo) == true)).ToList());

    private int _next = 1;

    public List<LogEntry> Logs { get; } = [];
    public List<Incident> Incidents { get; } = [];

    public void Add(IEnumerable<LogEntry> entries)
    {
        foreach (var e in entries) Logs.Add(Copy(e, $"{_next++:x24}"));
    }

    private static LogEntry Copy(LogEntry e, string id) => new()
    {
        Id = id, Timestamp = e.Timestamp, Level = e.Level, Application = e.Application, Service = e.Service,
        Environment = e.Environment, UserCode = e.UserCode, UserId = e.UserId, CorrelationId = e.CorrelationId,
        TransactionId = e.TransactionId, Operation = e.Operation, Outcome = e.Outcome, ErrorCode = e.ErrorCode,
        Message = e.Message, ExceptionType = e.ExceptionType, DurationMs = e.DurationMs,
        Metadata = new Dictionary<string, string>(e.Metadata)
    };

    private static bool IsError(LogEntry l) => l.Level >= LogSeverity.Error;
    private IEnumerable<LogEntry> In(TimeRange r) => Logs.Where(l => l.Timestamp >= r.From && l.Timestamp < r.To);

    // ---------------- ILogRepository ----------------
    public Task<PagedResult<LogEntry>> SearchAsync(LogSearchRequest r, CancellationToken ct)
    {
        IEnumerable<LogEntry> q = Logs;
        if (r.From is { } from) q = q.Where(l => l.Timestamp >= from.UtcDateTime);
        if (r.To is { } to) q = q.Where(l => l.Timestamp < to.UtcDateTime);
        if (r.Level is { } level) q = q.Where(l => l.Level == level);
        if (r.Application is not null) q = q.Where(l => l.Application == r.Application);
        if (r.Service is not null) q = q.Where(l => l.Service == r.Service);
        if (r.UserCode is not null) q = q.Where(l => l.UserCode == r.UserCode);
        if (r.ErrorCode is not null) q = q.Where(l => l.ErrorCode == r.ErrorCode);
        if (r.CorrelationId is not null) q = q.Where(l => l.CorrelationId == r.CorrelationId);
        if (r.TransactionId is not null) q = q.Where(l => l.TransactionId == r.TransactionId);

        var list = (r.Ascending ? q.OrderBy(l => l.Timestamp) : q.OrderByDescending(l => l.Timestamp)).ToList();
        var page = list.Skip((r.Page - 1) * r.PageSize).Take(r.PageSize).ToList();
        return Task.FromResult(new PagedResult<LogEntry>(page, list.Count, r.Page, r.PageSize));
    }

    public Task<LogEntry?> GetByIdAsync(string id, CancellationToken ct) => Task.FromResult(Logs.FirstOrDefault(l => l.Id == id));

    public Task<IReadOnlyList<LogEntry>> GetByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<LogEntry>>(Logs.Where(l => ids.Contains(l.Id!)).OrderBy(l => l.Timestamp).ToList());

    public Task AddManyAsync(IReadOnlyCollection<LogEntry> entries, CancellationToken ct)
    {
        Add(entries);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LogEntry>> GetRecentErrorsAsync(DateTime? sinceUtc, int limit, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<LogEntry>>(Logs.Where(IsError).Where(l => sinceUtc is null || l.Timestamp >= sinceUtc)
            .OrderByDescending(l => l.Timestamp).Take(limit).ToList());

    public Task<long> GetEstimatedCountAsync(CancellationToken ct) => Task.FromResult((long)Logs.Count);

    // ---------------- ILogAnalyticsRepository ----------------
    public Task<LogCounts> GetCountsAsync(TimeRange range, CancellationToken ct)
    {
        var logs = In(range).ToList();
        return Task.FromResult(new LogCounts(
            logs.Count, logs.Count(IsError), logs.Count(l => l.Level == LogSeverity.Warning),
            logs.Count(l => l.Outcome is not null), logs.Count(l => l.Outcome == OperationOutcome.Failure)));
    }

    public Task<IReadOnlyList<TrendPoint>> GetTrendAsync(TimeRange range, int bucketMinutes, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<TrendPoint>>(In(range)
            .GroupBy(l => TrendBuckets.Floor(l.Timestamp, bucketMinutes))
            .OrderBy(g => g.Key)
            .Select(g => new TrendPoint(g.Key, g.Count(), g.Count(IsError), g.Count(l => l.Level == LogSeverity.Warning)))
            .ToList());

    public Task<IReadOnlyList<ServiceStats>> GetServiceStatsAsync(TimeRange range, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ServiceStats>>(In(range).GroupBy(l => l.Service).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var durations = g.Where(l => l.DurationMs is not null).Select(l => l.DurationMs!.Value).ToList();
                var errors = g.Where(IsError).ToList();
                return new ServiceStats(g.Key, g.Count(), errors.Count, g.Count(l => l.Level == LogSeverity.Warning),
                    durations.Count == 0 ? null : durations.Average(), durations.Count == 0 ? null : durations.Max(),
                    errors.Count == 0 ? null : errors.Max(l => l.Timestamp));
            }).ToList());

    public Task<IReadOnlyList<ErrorCodeCount>> GetTopErrorCodesAsync(TimeRange range, int limit, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ErrorCodeCount>>(In(range).Where(l => l.ErrorCode is not null)
            .GroupBy(l => l.ErrorCode!).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Take(limit)
            .Select(g => new ErrorCodeCount(g.Key, g.Count(), g.Select(l => l.UserCode).OfType<string>().Distinct().Count(), g.Max(l => l.Timestamp)))
            .ToList());

    public Task<IReadOnlyList<ErrorGroup>> GetErrorGroupsAsync(TimeRange range, string? errorCode, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ErrorGroup>>(In(range).Where(IsError).Where(l => l.ErrorCode is not null)
            .Where(l => errorCode is null || l.ErrorCode == errorCode)
            .GroupBy(l => l.ErrorCode!).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new ErrorGroup(
                g.Key, g.Count(),
                g.Select(l => l.UserCode).OfType<string>().Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList(),
                g.Select(l => l.Service).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList(),
                g.Min(l => l.Timestamp), g.Max(l => l.Timestamp),
                g.OrderByDescending(l => l.Timestamp).Take(50).Select(l => l.Id!).ToList()))
            .ToList());

    // ---------------- IIncidentRepository ----------------
    Task<Incident?> IIncidentReader.GetByIdAsync(string id, CancellationToken ct) => Task.FromResult(Incidents.FirstOrDefault(i => i.Id == id));

    public Task<PagedResult<Incident>> SearchAsync(IncidentStatus? status, IncidentSeverity? severity, bool activeOnly, int page, int pageSize, CancellationToken ct)
    {
        var q = Incidents.Where(i => (status is null || i.Status == status) && (severity is null || i.Severity == severity)
                                     && (!activeOnly || i.Status != IncidentStatus.Resolved))
            .OrderByDescending(i => i.LastSeenAt).ToList();
        return Task.FromResult(new PagedResult<Incident>(q.Skip((page - 1) * pageSize).Take(pageSize).ToList(), q.Count, page, pageSize));
    }

    public Task<IReadOnlyList<Incident>> GetActiveAsync(int limit, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Incident>>(Incidents.Where(i => i.Status != IncidentStatus.Resolved).OrderByDescending(i => i.LastSeenAt).Take(limit).ToList());

    public Task<long> CountActiveAsync(CancellationToken ct) => Task.FromResult((long)Incidents.Count(i => i.Status != IncidentStatus.Resolved));

    public Task<Incident?> FindActiveByErrorCodeAsync(string errorCode, CancellationToken ct)
        => Task.FromResult(Incidents.Where(i => i.ErrorCode == errorCode && i.Status != IncidentStatus.Resolved).OrderByDescending(i => i.LastSeenAt).FirstOrDefault());

    public Task<Incident> InsertAsync(Incident incident, CancellationToken ct)
    {
        Incidents.Add(incident);
        return Task.FromResult(incident);
    }

    public Task ReplaceAsync(Incident incident, CancellationToken ct) => Task.CompletedTask;

    public Task<bool> UpdateStatusAsync(string id, IncidentStatus status, DateTime nowUtc, CancellationToken ct)
    {
        var incident = Incidents.FirstOrDefault(i => i.Id == id);
        if (incident is null) return Task.FromResult(false);
        incident.Status = status;
        incident.UpdatedAt = nowUtc;
        return Task.FromResult(true);
    }
}
