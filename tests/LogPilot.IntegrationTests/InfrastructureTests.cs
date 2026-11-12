using AwesomeAssertions;
using LogPilot.Domain.Logs;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace LogPilot.IntegrationTests;

public class InfrastructureTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private IMongoDatabase Database()
    {
        _ = factory.CreateClient();   // host'u başlatır (indeks oluşturucu çalışır)
        return factory.Services.GetRequiredService<IMongoClient>().GetDatabase(factory.DatabaseName);
    }

    [MongoFact]
    public async Task A_changed_retention_updates_the_existing_ttl_index_in_place()
    {
        var db = Database();
        var logs = db.GetCollection<BsonDocument>("logs");

        // Eski kurulum: 7 günlük TTL.
        await db.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "collMod", "logs" },
            { "index", new BsonDocument { { "name", "ix_timestamp_ttl" }, { "expireAfterSeconds", 7 * 24 * 3600 } } }
        });

        // Açılıştaki indeks oluşturucuyu yeniden çalıştır (yeni ayar: 24 saat). Çakışma hatası vermemeli.
        var initializer = factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .Single(s => s.GetType().Name == "MongoIndexInitializer");
        await initializer.StartAsync(CancellationToken.None);

        var ttl = (await logs.Indexes.List().ToListAsync()).Single(i => i["name"].AsString == "ix_timestamp_ttl");
        ttl["expireAfterSeconds"].ToInt64().Should().Be(24 * 3600);
    }

    [MongoFact]
    public async Task Startup_creates_the_designed_indexes()
    {
        var indexes = await Database().GetCollection<BsonDocument>("logs").Indexes.List().ToListAsync();
        var names = indexes.Select(i => i["name"].AsString).ToList();

        names.Should().Contain(["ix_timestamp_ttl", "ix_user_timestamp", "ix_service_timestamp", "ix_errorcode_timestamp", "ix_correlation", "ix_transaction"]);
        indexes.Single(i => i["name"].AsString == "ix_timestamp_ttl").Contains("expireAfterSeconds").Should().BeTrue("old logs expire automatically");
        indexes.Single(i => i["name"].AsString == "ix_errorcode_timestamp").Contains("partialFilterExpression").Should().BeTrue();

        var executions = await Database().GetCollection<BsonDocument>("scenario_executions").Indexes.List().ToListAsync();
        executions.Select(i => i["name"].AsString).Should().Contain(["ix_requestedat_ttl", "ix_status_requestedat", "ix_scenario_requestedat"]);
    }

    [MongoFact]
    public async Task The_user_query_pattern_uses_its_index()
    {
        await Seed.AddAsync(factory.Services,
            Enumerable.Range(0, 20).Select(i => Seed.Entry(DateTime.UtcNow.AddMinutes(-i), LogSeverity.Information, user: i % 2 == 0 ? "TCLI" : "ALEX")).ToArray());

        var explain = await Database().RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "explain", new BsonDocument { { "find", "logs" }, { "filter", new BsonDocument("userCode", "TCLI") }, { "sort", new BsonDocument("timestamp", -1) } } },
            { "verbosity", "queryPlanner" }
        });

        explain.ToJson().Should().Contain("IXSCAN").And.Contain("ix_user_timestamp");
    }

    [MongoFact]
    public async Task Health_endpoints_report_liveness_and_mongodb_readiness()
    {
        var client = factory.CreateClient();

        (await client.GetAsync("/health/live")).IsSuccessStatusCode.Should().BeTrue();

        var ready = await client.GetJsonAsync("/health/ready");
        ready.GetProperty("status").GetString().Should().Be("Healthy");
        ready.GetProperty("checks")[0].GetProperty("name").GetString().Should().Be("mongodb");

        (await client.GetJsonAsync("/health")).GetProperty("status").GetString().Should().Be("Healthy");
    }
}
