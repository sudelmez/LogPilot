using LogPilot.Application.Common;
using LogPilot.Application.Incidents;
using LogPilot.Domain.Incidents;
using MongoDB.Bson;
using MongoDB.Driver;

namespace LogPilot.Infrastructure.Mongo;

internal sealed class MongoIncidentRepository(MongoContext context) : IIncidentRepository
{
    private static readonly FilterDefinitionBuilder<Incident> F = Builders<Incident>.Filter;

    public async Task<PagedResult<Incident>> SearchAsync(
        IncidentStatus? status, IncidentSeverity? severity, bool activeOnly, int page, int pageSize, CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        var token = timeout.Token;

        var filters = new List<FilterDefinition<Incident>>();
        if (status is { } s) filters.Add(F.Eq(x => x.Status, s));
        if (severity is { } v) filters.Add(F.Eq(x => x.Severity, v));
        if (activeOnly) filters.Add(F.Ne(x => x.Status, IncidentStatus.Resolved));
        var filter = filters.Count == 0 ? FilterDefinition<Incident>.Empty : F.And(filters);

        var itemsTask = context.Incidents.Find(filter)
            .SortByDescending(x => x.LastSeenAt)
            .Skip((page - 1) * pageSize).Limit(pageSize)
            .ToListAsync(token);
        var countTask = context.Incidents.CountDocumentsAsync(filter, cancellationToken: token);
        await Task.WhenAll(itemsTask, countTask);

        return new PagedResult<Incident>(await itemsTask, await countTask, page, pageSize);
    }

    public async Task<Incident?> GetByIdAsync(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out _)) return null;
        using var timeout = context.CreateTimeout(ct);
        return await context.Incidents.Find(x => x.Id == id).FirstOrDefaultAsync(timeout.Token);
    }

    public async Task<IReadOnlyList<Incident>> GetActiveAsync(int limit, CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        return await context.Incidents.Find(F.Ne(x => x.Status, IncidentStatus.Resolved))
            .SortByDescending(x => x.LastSeenAt).Limit(limit).ToListAsync(timeout.Token);
    }

    public async Task<long> CountActiveAsync(CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        return await context.Incidents.CountDocumentsAsync(F.Ne(x => x.Status, IncidentStatus.Resolved), cancellationToken: timeout.Token);
    }

    public async Task<Incident?> FindActiveByErrorCodeAsync(string errorCode, CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        var filter = F.Eq(x => x.ErrorCode, errorCode) & F.Ne(x => x.Status, IncidentStatus.Resolved);
        return await context.Incidents.Find(filter).SortByDescending(x => x.LastSeenAt).FirstOrDefaultAsync(timeout.Token);
    }

    public async Task<Incident> InsertAsync(Incident incident, CancellationToken ct)
    {
        // Id'yi biz üretiriz (generator'a bağımlı kalmayalım): API cevabında hemen kullanılabilir.
        var withId = new Incident
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Title = incident.Title, ErrorCode = incident.ErrorCode, Severity = incident.Severity, Status = incident.Status,
            AffectedServices = incident.AffectedServices, AffectedUserCodes = incident.AffectedUserCodes,
            RelatedLogIds = incident.RelatedLogIds, LogCount = incident.LogCount,
            FirstSeenAt = incident.FirstSeenAt, LastSeenAt = incident.LastSeenAt,
            CreatedAt = incident.CreatedAt, UpdatedAt = incident.UpdatedAt, ResolvedAt = incident.ResolvedAt,
            AiSummary = incident.AiSummary, SummaryGeneratedAt = incident.SummaryGeneratedAt, SummaryLogCount = incident.SummaryLogCount
        };

        using var timeout = context.CreateTimeout(ct);
        await context.Incidents.InsertOneAsync(withId, cancellationToken: timeout.Token);
        return withId;
    }

    public async Task ReplaceAsync(Incident incident, CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        await context.Incidents.ReplaceOneAsync(x => x.Id == incident.Id, incident, cancellationToken: timeout.Token);
    }

    public async Task<bool> UpdateStatusAsync(string id, IncidentStatus status, DateTime nowUtc, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out _)) return false;

        var u = Builders<Incident>.Update;
        var update = u.Set(x => x.Status, status).Set(x => x.UpdatedAt, nowUtc);
        update = status == IncidentStatus.Resolved ? update.Set(x => x.ResolvedAt, nowUtc) : update.Unset(x => x.ResolvedAt);

        using var timeout = context.CreateTimeout(ct);
        var result = await context.Incidents.UpdateOneAsync(x => x.Id == id, update, cancellationToken: timeout.Token);
        return result.MatchedCount > 0;
    }
}
