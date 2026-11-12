using LogPilot.Application.Common;
using LogPilot.Application.Notifications;
using LogPilot.Domain.Notifications;
using MongoDB.Bson;
using MongoDB.Driver;

namespace LogPilot.Infrastructure.Mongo;

internal sealed class MongoSmsLogRepository(MongoContext context) : ISmsLogRepository
{
    public async Task<PagedResult<SmsLogEntry>> SearchAsync(SmsLogSearchRequest r, CancellationToken ct)
    {
        var f = Builders<SmsLogEntry>.Filter;
        var filter = f.Gte(x => x.Timestamp, r.From) & f.Lt(x => x.Timestamp, r.To);
        if (r.Phones is { Count: > 0 }) filter &= f.In(x => x.Phone, r.Phones);
        if (r.MessageId is not null) filter &= f.Eq(x => x.MessageId, r.MessageId);
        if (r.TemplateCode is not null) filter &= f.Eq(x => x.TemplateCode, r.TemplateCode);
        if (r.Status is { } status) filter &= f.Eq(x => x.Status, status);
        if (r.ErrorCode is not null) filter &= f.Eq(x => x.ErrorCode, r.ErrorCode);
        if (r.MinLevel is { } level) filter &= f.Gte(x => x.Level, level);

        var sort = r.Ascending ? Builders<SmsLogEntry>.Sort.Ascending(x => x.Timestamp) : Builders<SmsLogEntry>.Sort.Descending(x => x.Timestamp);
        using var timeout = context.CreateTimeout(ct);
        var itemsTask = context.SmsLogs.Find(filter).Sort(sort).Limit(r.Limit).ToListAsync(timeout.Token);
        var countTask = context.SmsLogs.CountDocumentsAsync(filter, cancellationToken: timeout.Token);
        await Task.WhenAll(itemsTask, countTask);
        return new PagedResult<SmsLogEntry>(await itemsTask, await countTask, 1, r.Limit);
    }

    public async Task<SmsLogEntry?> GetByIdAsync(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out _)) return null;
        using var timeout = context.CreateTimeout(ct);
        return await context.SmsLogs.Find(x => x.Id == id).FirstOrDefaultAsync(timeout.Token);
    }

    public async Task AddManyAsync(IReadOnlyCollection<SmsLogEntry> entries, CancellationToken ct)
    {
        if (entries.Count == 0) return;
        using var timeout = context.CreateTimeout(ct);
        await context.SmsLogs.InsertManyAsync(entries, cancellationToken: timeout.Token);
    }

    public async Task<long> GetEstimatedCountAsync(CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        return await context.SmsLogs.EstimatedDocumentCountAsync(cancellationToken: timeout.Token);
    }
}
