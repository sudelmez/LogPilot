using System.Net;
using AwesomeAssertions;
using LogPilot.Application.Scenarios;
using LogPilot.Domain.Scenarios;
using Microsoft.Extensions.DependencyInjection;

namespace LogPilot.IntegrationTests;

public class ScenarioApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task SyncCatalogAsync()
    {
        var repository = factory.Services.GetRequiredService<IScenarioRepository>();
        foreach (var scenario in Seed.LoadScenarios()) await repository.SyncDefinitionAsync(scenario, CancellationToken.None);
    }

    [MongoFact]
    public async Task Trigger_queues_a_pending_execution_and_rejects_duplicates()
    {
        await SyncCatalogAsync();
        var client = factory.CreateClient();

        var list = await client.GetJsonAsync("/api/scenarios");
        list.GetArrayLength().Should().BeGreaterThanOrEqualTo(13);

        var first = await client.SendJsonAsync(HttpMethod.Post, "/api/scenarios/forbidden-access/trigger");
        first.Status.Should().Be(HttpStatusCode.Accepted);
        first.Body.GetProperty("status").GetString().Should().Be("Pending");
        first.Body.GetProperty("triggeredBy").GetString().Should().Be("Manual");

        (await client.SendJsonAsync(HttpMethod.Post, "/api/scenarios/forbidden-access/trigger")).Status.Should().Be(HttpStatusCode.Conflict);
        (await client.SendJsonAsync(HttpMethod.Post, "/api/scenarios/no-such-scenario/trigger")).Status.Should().Be(HttpStatusCode.NotFound);

        var executions = await client.GetJsonAsync("/api/scenarios/executions?scenarioId=forbidden-access");
        executions.GetProperty("totalCount").GetInt64().Should().Be(1);
    }

    [MongoFact]
    public async Task A_pending_execution_is_claimed_exactly_once()
    {
        await SyncCatalogAsync();
        var executions = factory.Services.GetRequiredService<IScenarioExecutionRepository>();
        await executions.CreateAsync("token-expired", ExecutionTrigger.Schedule, DateTime.UtcNow, CancellationToken.None);

        var claims = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => executions.ClaimNextPendingAsync(DateTime.UtcNow, CancellationToken.None)));

        var claimed = claims.Where(c => c is not null).Select(c => c!).ToList();

        // Atomik sahiplenme: hiçbir kayıt iki kez alınamaz ve kendi kaydımız tam bir kez alınır.
        // (Aynı sınıftaki başka bir test de Pending kayıt bırakmış olabilir; o da birer kez alınır.)
        claimed.Select(c => c.Id).Should().OnlyHaveUniqueItems("two workers must never claim the same execution");
        claimed.Count(c => c.ScenarioId == "token-expired").Should().Be(1);
        claimed.Should().OnlyContain(c => c.Status == ExecutionStatus.Running);
    }

    [MongoFact]
    public async Task Settings_can_be_updated_and_are_validated()
    {
        await SyncCatalogAsync();
        var client = factory.CreateClient();

        var ok = await client.SendJsonAsync(HttpMethod.Put, "/api/scenarios/mongo-outage/settings",
            new { enabled = false, intervalSeconds = 1800, probability = 0.1, weight = 1 });
        ok.Status.Should().Be(HttpStatusCode.OK);
        ok.Body.GetProperty("settings").GetProperty("enabled").GetBoolean().Should().BeFalse();

        var persisted = await client.GetJsonAsync("/api/scenarios/mongo-outage");
        persisted.GetProperty("settings").GetProperty("intervalSeconds").GetInt32().Should().Be(1800);

        (await client.SendJsonAsync(HttpMethod.Put, "/api/scenarios/mongo-outage/settings",
            new { enabled = true, intervalSeconds = 60, probability = 2.0, weight = 1 })).Status.Should().Be(HttpStatusCode.BadRequest);
        (await client.SendJsonAsync(HttpMethod.Put, "/api/scenarios/nope/settings",
            new { enabled = true, intervalSeconds = 60, probability = 0.5, weight = 1 })).Status.Should().Be(HttpStatusCode.NotFound);
    }

    [MongoFact]
    public async Task Syncing_the_catalog_again_keeps_the_live_settings()
    {
        await SyncCatalogAsync();
        var client = factory.CreateClient();
        await client.SendJsonAsync(HttpMethod.Put, "/api/scenarios/token-expired/settings",
            new { enabled = true, intervalSeconds = 999, probability = 0.33, weight = 1 });

        await SyncCatalogAsync();   // generator yeniden başlamış gibi

        var scenario = await client.GetJsonAsync("/api/scenarios/token-expired");
        scenario.GetProperty("settings").GetProperty("intervalSeconds").GetInt32().Should().Be(999);
    }
}
