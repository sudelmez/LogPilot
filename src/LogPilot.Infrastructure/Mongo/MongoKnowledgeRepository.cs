using System.Text.RegularExpressions;
using LogPilot.Application.Common;
using LogPilot.Application.Knowledge;
using LogPilot.Domain.Knowledge;
using MongoDB.Bson;
using MongoDB.Driver;

namespace LogPilot.Infrastructure.Mongo;

internal sealed class MongoKnowledgeRepository(MongoContext context) : IKnowledgeRepository
{
    public async Task<KnowledgeCase> InsertAsync(KnowledgeCase knowledgeCase, CancellationToken ct)
    {
        var stored = new KnowledgeCase
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Title = knowledgeCase.Title, ErrorCodes = knowledgeCase.ErrorCodes, Services = knowledgeCase.Services,
            Symptom = knowledgeCase.Symptom, RootCause = knowledgeCase.RootCause, Resolution = knowledgeCase.Resolution,
            Source = knowledgeCase.Source, SourceId = knowledgeCase.SourceId,
            CreatedAt = knowledgeCase.CreatedAt, UpdatedAt = knowledgeCase.UpdatedAt,
            TimesSuggested = knowledgeCase.TimesSuggested, LastSuggestedAt = knowledgeCase.LastSuggestedAt
        };

        using var timeout = context.CreateTimeout(ct);
        await context.Knowledge.InsertOneAsync(stored, cancellationToken: timeout.Token);
        return stored;
    }

    public async Task<KnowledgeCase?> GetByIdAsync(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out _)) return null;
        using var timeout = context.CreateTimeout(ct);
        return await context.Knowledge.Find(x => x.Id == id).FirstOrDefaultAsync(timeout.Token);
    }

    public async Task<KnowledgeCase?> FindBySourceAsync(KnowledgeSource source, string sourceId, CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        return await context.Knowledge.Find(x => x.Source == source && x.SourceId == sourceId).FirstOrDefaultAsync(timeout.Token);
    }

    public async Task ReplaceAsync(KnowledgeCase knowledgeCase, CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        await context.Knowledge.ReplaceOneAsync(x => x.Id == knowledgeCase.Id, knowledgeCase, cancellationToken: timeout.Token);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out _)) return false;
        using var timeout = context.CreateTimeout(ct);
        var result = await context.Knowledge.DeleteOneAsync(x => x.Id == id, timeout.Token);
        return result.DeletedCount > 0;
    }

    public async Task<PagedResult<KnowledgeCase>> SearchAsync(KnowledgeQuery query, CancellationToken ct)
    {
        var f = Builders<KnowledgeCase>.Filter;
        var filters = new List<FilterDefinition<KnowledgeCase>>();

        // Hata kodları BÜYÜK harfle saklanır: tam eşleşme ix_errorcodes indeksini kullanır.
        // Servis: büyük/küçük harf duyarsız tam eşleşme (dizi alanında herhangi bir eleman).
        if (query.ErrorCodes is { Count: > 0 } codes)
            filters.Add(f.AnyIn(x => x.ErrorCodes, codes.Select(c => c.ToUpperInvariant())));
        if (query.Services is { Count: > 0 } services)
            filters.Add(f.Or(services.Select(s => f.Regex(x => x.Services, ExactIgnoreCase(s)))));

        // Serbest metin: başlık, çözüm, kök neden ve belirtide geçen (kaçışlı, regex enjeksiyonu yok).
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var rx = new BsonRegularExpression(Regex.Escape(query.Text.Trim()), "i");
            filters.Add(f.Or(f.Regex(x => x.Title, rx), f.Regex(x => x.Resolution, rx),
                f.Regex(x => x.RootCause, rx), f.Regex(x => x.Symptom, rx)));
        }

        var filter = filters.Count == 0 ? f.Empty : f.And(filters);
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 100);

        using var timeout = context.CreateTimeout(ct);
        var total = await context.Knowledge.CountDocumentsAsync(filter, cancellationToken: timeout.Token);
        var items = await context.Knowledge.Find(filter).SortByDescending(x => x.UpdatedAt)
            .Skip((page - 1) * size).Limit(size).ToListAsync(timeout.Token);

        return new PagedResult<KnowledgeCase>(items, total, page, size);
    }

    public async Task MarkSuggestedAsync(IReadOnlyCollection<string> ids, DateTime nowUtc, CancellationToken ct)
    {
        var valid = ids.Where(id => ObjectId.TryParse(id, out _)).ToList();
        if (valid.Count == 0) return;

        var update = Builders<KnowledgeCase>.Update.Inc(x => x.TimesSuggested, 1).Set(x => x.LastSuggestedAt, nowUtc);
        using var timeout = context.CreateTimeout(ct);
        await context.Knowledge.UpdateManyAsync(Builders<KnowledgeCase>.Filter.In(x => x.Id, valid), update, cancellationToken: timeout.Token);
    }

    private static BsonRegularExpression ExactIgnoreCase(string value) => new($"^{Regex.Escape(value)}$", "i");
}
