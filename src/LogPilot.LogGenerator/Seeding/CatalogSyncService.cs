using LogPilot.Application.Scenarios;
using LogPilot.Application.Users;
using LogPilot.LogGenerator.Config;
using Microsoft.Extensions.Options;

namespace LogPilot.LogGenerator.Seeding;

/// Açılışta: senaryo dosyalarını ve kullanıcıları DB'ye senkronlar, yarım kalmış çalıştırmaları kapatır.
/// Geçersiz dosya varsa uygulama bilerek açılmaz (fail-fast).
internal sealed class CatalogSyncService(
    IScenarioRepository scenarios,
    IUserRepository users,
    IScenarioExecutionRepository executions,
    TimeProvider time,
    IOptions<GeneratorOptions> options,
    ILogger<CatalogSyncService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        var o = options.Value;
        var scenarioDir = RepoPaths.FindDirectory("scenarios", o.ScenariosPath);
        var seedDir = RepoPaths.FindDirectory("seed", o.SeedPath);

        var definitions = SeedFileLoader.LoadScenarios(scenarioDir);
        foreach (var definition in definitions)
            await scenarios.SyncDefinitionAsync(definition, ct);

        var userList = SeedFileLoader.LoadUsers(Path.Combine(seedDir, "users.json"));
        await users.UpsertManyAsync(userList, ct);

        var interrupted = await executions.FailStaleRunningAsync(time.GetUtcNow().UtcDateTime, ct);

        logger.LogInformation(
            "Catalog synced from {Directory}: {ScenarioCount} scenarios, {UserCount} users, {Interrupted} interrupted executions marked failed.",
            scenarioDir, definitions.Count, userList.Count, interrupted);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
