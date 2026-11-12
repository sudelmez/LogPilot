using LogPilot.Application.Common;
using LogPilot.Application.Quotes;
using LogPilot.Domain.Quotes;
using MongoDB.Bson;
using MongoDB.Driver;

namespace LogPilot.Infrastructure.Mongo;

internal sealed class MongoQuoteLogRepository(MongoContext context) : IQuoteLogRepository
{
    /// Huni hesabında okunacak azami log: zaman penceresi ve TTL bunu zaten sınırlar, bu bir emniyet kemeri.
    private const int MaxFunnelLogs = 20_000;

    public async Task<PagedResult<QuoteLogEntry>> SearchAsync(QuoteLogSearchRequest r, CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        var filter = BuildFilter(r);
        var sort = r.Ascending
            ? Builders<QuoteLogEntry>.Sort.Ascending(x => x.Timestamp)
            : Builders<QuoteLogEntry>.Sort.Descending(x => x.Timestamp);

        var itemsTask = context.QuoteLogs.Find(filter).Sort(sort).Limit(r.Limit).ToListAsync(timeout.Token);
        var countTask = context.QuoteLogs.CountDocumentsAsync(filter, cancellationToken: timeout.Token);
        await Task.WhenAll(itemsTask, countTask);

        return new PagedResult<QuoteLogEntry>(await itemsTask, await countTask, 1, r.Limit);
    }

    public async Task<QuoteLogEntry?> GetByIdAsync(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out _)) return null;
        using var timeout = context.CreateTimeout(ct);
        return await context.QuoteLogs.Find(x => x.Id == id).FirstOrDefaultAsync(timeout.Token);
    }

    public async Task<IReadOnlyList<QuoteLogEntry>> GetByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        var valid = ids.Where(i => ObjectId.TryParse(i, out _)).Distinct().ToList();
        if (valid.Count == 0) return [];

        using var timeout = context.CreateTimeout(ct);
        return await context.QuoteLogs.Find(Builders<QuoteLogEntry>.Filter.In(x => x.Id, valid))
            .SortBy(x => x.Timestamp).ToListAsync(timeout.Token);
    }

    public async Task AddManyAsync(IReadOnlyCollection<QuoteLogEntry> entries, CancellationToken ct)
    {
        if (entries.Count == 0) return;
        using var timeout = context.CreateTimeout(ct);
        await context.QuoteLogs.InsertManyAsync(entries, cancellationToken: timeout.Token);
    }

    public async Task<long> GetEstimatedCountAsync(CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        return await context.QuoteLogs.EstimatedDocumentCountAsync(cancellationToken: timeout.Token);
    }

    public async Task<QuoteFunnel> GetFunnelAsync(DateTime from, DateTime to, string? product, QuoteChannel? channel,
        string? insurerCode, CancellationToken ct)
    {
        var f = Builders<QuoteLogEntry>.Filter;
        var filter = f.Gte(x => x.Timestamp, from) & f.Lt(x => x.Timestamp, to);
        if (product is not null) filter &= f.Eq(x => x.Product, product);
        if (channel is { } c) filter &= f.Eq(x => x.Channel, c);

        using var timeout = context.CreateTimeout(ct);
        if (insurerCode is not null)
        {
            // Sigortacı filtresi: o sigortacıya fiyat sorulan tekliflerin TAMAMI (huni bütün kalsın).
            var quoteIds = await context.QuoteLogs.Distinct(x => x.QuoteId, filter & f.Eq(x => x.InsurerCode, insurerCode))
                .ToListAsync(timeout.Token);
            filter &= f.In(x => x.QuoteId, quoteIds);
        }

        var logs = await context.QuoteLogs.Find(filter).Limit(MaxFunnelLogs).ToListAsync(timeout.Token);
        return QuoteFunnelCalculator.Calculate(logs);
    }

    private static FilterDefinition<QuoteLogEntry> BuildFilter(QuoteLogSearchRequest r)
    {
        var f = Builders<QuoteLogEntry>.Filter;
        var filter = f.Gte(x => x.Timestamp, r.From) & f.Lt(x => x.Timestamp, r.To);

        if (r.QuoteId is not null) filter &= f.Eq(x => x.QuoteId, r.QuoteId);
        if (r.PolicyNo is not null) filter &= f.Eq(x => x.PolicyNo, r.PolicyNo);
        if (r.CorrelationId is not null) filter &= f.Eq(x => x.CorrelationId, r.CorrelationId);
        if (r.CorrelationIds is { Count: > 0 }) filter &= f.In(x => x.CorrelationId, r.CorrelationIds);
        if (r.Product is not null) filter &= f.Eq(x => x.Product, r.Product);
        if (r.Channel is { } channel) filter &= f.Eq(x => x.Channel, channel);
        if (r.InsurerCode is not null) filter &= f.Eq(x => x.InsurerCode, r.InsurerCode);
        if (r.Step is not null) filter &= f.Eq(x => x.Step, r.Step);
        if (r.ErrorCode is not null) filter &= f.Eq(x => x.ErrorCode, r.ErrorCode);
        if (r.Level is { } level) filter &= f.Eq(x => x.Level, level);

        // Kimlik anahtarları VEYA ile: aynı müşterinin farklı kanallardan gelen teklifleri birlikte.
        var identity = new List<FilterDefinition<QuoteLogEntry>>();
        if (r.UserCodes is { Count: > 0 }) identity.Add(f.In(x => x.UserCode, r.UserCodes));
        if (r.CustomerNos is { Count: > 0 }) identity.Add(f.In(x => x.CustomerNo, r.CustomerNos));
        if (r.IdentityNos is { Count: > 0 }) identity.Add(f.In(x => x.IdentityNo, r.IdentityNos));
        if (identity.Count > 0) filter &= f.Or(identity);

        return filter;
    }
}
