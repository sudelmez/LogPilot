using LogPilot.Application.Scenarios;
using LogPilot.Domain.Scenarios;
using MongoDB.Driver;

namespace LogPilot.Infrastructure.Mongo;

internal sealed class MongoScenarioRepository(MongoContext context) : IScenarioRepository
{
    public async Task<IReadOnlyList<Scenario>> GetAllAsync(CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        return await context.Scenarios.Find(FilterDefinition<Scenario>.Empty)
            .SortBy(x => x.Id)
            .ToListAsync(timeout.Token);
    }

    public async Task<Scenario?> GetByIdAsync(string id, CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        return await context.Scenarios.Find(x => x.Id == id).FirstOrDefaultAsync(timeout.Token);
    }

    public async Task SyncDefinitionAsync(Scenario definition, CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);

        var existing = await context.Scenarios.Find(x => x.Id == definition.Id).FirstOrDefaultAsync(timeout.Token);
        if (existing is not null)
            definition.Settings = existing.Settings;   // canlı ayarlar dosyayla ezilmez

        await context.Scenarios.ReplaceOneAsync(
            x => x.Id == definition.Id, definition, new ReplaceOptions { IsUpsert = true }, timeout.Token);
    }

    public async Task<bool> UpdateSettingsAsync(string id, ScenarioSettings settings, CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        var result = await context.Scenarios.UpdateOneAsync(
            x => x.Id == id,
            Builders<Scenario>.Update.Set(x => x.Settings, settings),
            cancellationToken: timeout.Token);

        return result.MatchedCount > 0;
    }
}
