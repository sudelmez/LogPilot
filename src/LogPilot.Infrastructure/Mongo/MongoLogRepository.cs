using LogPilot.Application.Common;
using LogPilot.Application.Logs;
using LogPilot.Domain.Logs;
using MongoDB.Bson;
using MongoDB.Driver;

namespace LogPilot.Infrastructure.Mongo;

internal sealed class MongoLogRepository(MongoContext context) : ILogRepository
{
    public async Task<PagedResult<LogEntry>> SearchAsync(LogSearchRequest request, CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        var token = timeout.Token;

        var filters = BuildFilters(request);
        var hasFilter = filters.Count > 0;
        var filter = hasFilter ? Builders<LogEntry>.Filter.And(filters) : FilterDefinition<LogEntry>.Empty;

        var sort = request.Ascending
            ? Builders<LogEntry>.Sort.Ascending(x => x.Timestamp)
            : Builders<LogEntry>.Sort.Descending(x => x.Timestamp);

        var itemsTask = context.Logs.Find(filter)
            .Sort(sort)
            .Skip((request.Page - 1) * request.PageSize)
            .Limit(request.PageSize)
            .ToListAsync(token);

        // Filtre yoksa tüm koleksiyonu saymak yerine metadata'dan tahmini sayı al.
        var countTask = hasFilter
            ? context.Logs.CountDocumentsAsync(filter, cancellationToken: token)
            : context.Logs.EstimatedDocumentCountAsync(cancellationToken: token);

        await Task.WhenAll(itemsTask, countTask);   // iki sorgu paralel çalışır

        return new PagedResult<LogEntry>(await itemsTask, await countTask, request.Page, request.PageSize);
    }

    public async Task<LogEntry?> GetByIdAsync(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out _)) return null;   // geçersiz id = bulunamadı (404)

        using var timeout = context.CreateTimeout(ct);
        return await context.Logs.Find(x => x.Id == id).FirstOrDefaultAsync(timeout.Token);
    }

    public async Task<IReadOnlyList<LogEntry>> GetByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        var valid = ids.Where(i => ObjectId.TryParse(i, out _)).Distinct().ToList();
        if (valid.Count == 0) return [];

        using var timeout = context.CreateTimeout(ct);
        return await context.Logs.Find(Builders<LogEntry>.Filter.In(x => x.Id, valid))
            .SortBy(x => x.Timestamp)
            .ToListAsync(timeout.Token);
    }

    public async Task AddManyAsync(IReadOnlyCollection<LogEntry> entries, CancellationToken ct)
    {
        if (entries.Count == 0) return;

        using var timeout = context.CreateTimeout(ct);
        await context.Logs.InsertManyAsync(entries, cancellationToken: timeout.Token);
    }

    public async Task<IReadOnlyList<LogEntry>> GetRecentErrorsAsync(DateTime? sinceUtc, int limit, CancellationToken ct)
    {
        var f = Builders<LogEntry>.Filter;
        var filter = f.In(x => x.Level, new[] { LogSeverity.Error, LogSeverity.Critical });
        if (sinceUtc is { } since) filter &= f.Gte(x => x.Timestamp, since);

        using var timeout = context.CreateTimeout(ct);
        return await context.Logs.Find(filter)
            .SortByDescending(x => x.Timestamp)
            .Limit(limit)
            .ToListAsync(timeout.Token);
    }

    public async Task<long> GetEstimatedCountAsync(CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        return await context.Logs.EstimatedDocumentCountAsync(cancellationToken: timeout.Token);
    }

    private static List<FilterDefinition<LogEntry>> BuildFilters(LogSearchRequest r)
    {
        var f = Builders<LogEntry>.Filter;
        var filters = new List<FilterDefinition<LogEntry>>();

        if (r.From is { } from) filters.Add(f.Gte(x => x.Timestamp, from.UtcDateTime));
        if (r.To is { } to) filters.Add(f.Lt(x => x.Timestamp, to.UtcDateTime));
        if (r.Level is { } level) filters.Add(f.Eq(x => x.Level, level));
        if (r.Application is not null) filters.Add(f.Eq(x => x.Application, r.Application));
        if (r.Service is not null) filters.Add(f.Eq(x => x.Service, r.Service));
        if (r.UserCode is not null) filters.Add(f.Eq(x => x.UserCode, r.UserCode));
        if (r.ErrorCode is not null) filters.Add(f.Eq(x => x.ErrorCode, r.ErrorCode));
        if (r.CorrelationId is not null) filters.Add(f.Eq(x => x.CorrelationId, r.CorrelationId));
        if (r.TransactionId is not null) filters.Add(f.Eq(x => x.TransactionId, r.TransactionId));

        return filters;
    }
}
