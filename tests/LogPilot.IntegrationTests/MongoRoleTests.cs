using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using AwesomeAssertions;
using LogPilot.Application.Analytics;
using LogPilot.Application.Logs;
using LogPilot.Application.Quotes;
using LogPilot.Application.Purchases;
using LogPilot.Domain.Analytics;
using LogPilot.Infrastructure;
using LogPilot.Infrastructure.Mongo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace LogPilot.IntegrationTests;

/// Kimlik doğrulaması AÇIK geçici bir mongod (--auth) başlatır; rollerin VERİTABANI tarafından uygulandığını doğrular.
/// mongod PATH'te yoksa testler atlanır.
public sealed class AuthMongoFixture : IAsyncLifetime
{
    public const string Db = "logpilot_roles";
    private Process? _process;
    private string? _dataDir;

    public int Port { get; private set; }
    public bool Available { get; private set; }
    public string Root => $"mongodb://root:rootpw@127.0.0.1:{Port}/?authSource=admin";
    public string App => $"mongodb://app:apppw@127.0.0.1:{Port}/{Db}?authSource={Db}";
    public string Reader => $"mongodb://ai:aipw@127.0.0.1:{Port}/{Db}?authSource={Db}";

    public static string? MongodPath => new[] { "/opt/homebrew/bin/mongod", "/usr/local/bin/mongod", "/usr/bin/mongod" }.FirstOrDefault(File.Exists);

    public async Task InitializeAsync()
    {
        if (MongodPath is null) return;

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        _dataDir = Directory.CreateTempSubdirectory("logpilot-auth-mongo").FullName;
        _process = Process.Start(new ProcessStartInfo(MongodPath,
            $"--auth --port {Port} --bind_ip 127.0.0.1 --dbpath \"{_dataDir}\" --quiet --nounixsocket")
        { RedirectStandardOutput = true, RedirectStandardError = true });

        // localhost istisnası: hiç kullanıcı yokken ilk kullanıcı (root) yerelden oluşturulabilir.
        var bootstrap = new MongoClient($"mongodb://127.0.0.1:{Port}/?serverSelectionTimeoutMS=500");
        for (var i = 0; i < 40; i++)
        {
            try
            {
                await bootstrap.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument
                    { { "createUser", "root" }, { "pwd", "rootpw" }, { "roles", new BsonArray { "root" } } });
                break;
            }
            catch (Exception) when (i < 39) { await Task.Delay(250); }
        }

        var db = new MongoClient(Root).GetDatabase(Db);
        await db.RunCommandAsync<BsonDocument>(new BsonDocument
            { { "createUser", "app" }, { "pwd", "apppw" }, { "roles", new BsonArray { new BsonDocument { { "role", "readWrite" }, { "db", Db } } } } });
        await MongoAccessPolicy.EnsureAiReaderAsync(db, "ai", "aipw");
        Available = true;
    }

    public Task DisposeAsync()
    {
        try { _process?.Kill(entireProcessTree: true); _process?.WaitForExit(5000); } catch { /* yok say */ }
        try { if (_dataDir is not null) Directory.Delete(_dataDir, recursive: true); } catch { /* yok say */ }
        return Task.CompletedTask;
    }
}

public sealed class MongodFactAttribute : FactAttribute
{
    public MongodFactAttribute()
    {
        if (AuthMongoFixture.MongodPath is null) Skip = "mongod is not installed; the database-level role tests need it.";
    }
}

public class MongoRoleTests(AuthMongoFixture mongo) : IClassFixture<AuthMongoFixture>
{
    private IMongoDatabase As(string connection) => new MongoClient(connection).GetDatabase(AuthMongoFixture.Db);

    private static bool Unauthorized(Exception ex) => ex is MongoCommandException { Code: 13 } || ex.InnerException is MongoCommandException { Code: 13 }
                                                   || ex is MongoWriteException || ex is MongoBulkWriteException;

    [MongodFact]
    public async Task The_ai_reader_can_read_but_never_insert_update_delete_or_drop()
    {
        mongo.Available.Should().BeTrue();
        await As(mongo.App).GetCollection<BsonDocument>("logs").InsertOneAsync(new BsonDocument { { "message", "seed" }, { "timestamp", DateTime.UtcNow } });
        var logs = As(mongo.Reader).GetCollection<BsonDocument>("logs");

        (await logs.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync()).Should().NotBeEmpty("reading is allowed");

        var insert = async () => await logs.InsertOneAsync(new BsonDocument("x", 1));
        var update = async () => await logs.UpdateManyAsync(FilterDefinition<BsonDocument>.Empty, Builders<BsonDocument>.Update.Set("x", 2));
        var delete = async () => await logs.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);
        var drop = async () => await As(mongo.Reader).DropCollectionAsync("logs");
        var dropIndexes = async () => await logs.Indexes.DropAllAsync();

        foreach (var write in new[] { insert, update, delete, drop, dropIndexes })
            (await write.Should().ThrowAsync<Exception>()).Which.Should().Match<Exception>(e => Unauthorized(e));

        (await As(mongo.App).GetCollection<BsonDocument>("logs").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty))
            .Should().BeGreaterThan(0, "nothing was deleted");
    }

    [MongodFact]
    public async Task The_ai_reader_cannot_even_read_collections_outside_its_role()
    {
        await As(mongo.App).GetCollection<BsonDocument>("chat_messages").InsertOneAsync(new BsonDocument("content", "private chat"));

        var read = async () => await As(mongo.Reader).GetCollection<BsonDocument>("chat_messages").Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();

        (await read.Should().ThrowAsync<MongoCommandException>()).Which.Code.Should().Be(13);
    }

    [MongodFact]
    public async Task Privilege_inspection_tells_the_reader_from_the_app_account()
    {
        var reader = await MongoAccessPolicy.InspectAsync(new MongoClient(mongo.Reader));
        var app = await MongoAccessPolicy.InspectAsync(new MongoClient(mongo.App));

        reader.IsReadOnly.Should().BeTrue();
        reader.Actions.Should().Equal("find");
        app.IsReadOnly.Should().BeFalse();
        app.WriteActions.Should().Contain(["insert", "update", "remove"]);
    }

    [MongodFact]
    public async Task Everything_the_ai_tools_need_works_with_the_read_only_account()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mongo:ConnectionString"] = mongo.App,
            ["Mongo:ReadOnlyConnectionString"] = mongo.Reader,
            ["Mongo:DatabaseName"] = AuthMongoFixture.Db
        }).Build();
        await using var sp = new ServiceCollection().AddLogging().AddInfrastructure(config).BuildServiceProvider();

        // Yazma uygulama hesabıyla (generator gibi), okuma AI hesabıyla.
        await sp.GetRequiredService<ILogRepository>().AddManyAsync([Seed.Entry(DateTime.UtcNow.AddMinutes(-1), Domain.Logs.LogSeverity.Error, user: "BOB", errorCode: "TOKEN_EXPIRED")], CancellationToken.None);
        var now = DateTime.UtcNow;

        var reader = sp.GetRequiredService<ILogReader>();
        (await reader.SearchAsync(new LogSearchRequest(UserCode: "BOB"), CancellationToken.None)).TotalCount.Should().Be(1);   // find + count (aggregate)
        (await reader.GetEstimatedCountAsync(CancellationToken.None)).Should().BeGreaterThan(0);                                 // count
        (await sp.GetRequiredService<ILogAnalyticsRepository>().GetErrorGroupsAsync(new TimeRange(now.AddHours(-1), now.AddMinutes(1)), null, CancellationToken.None))
            .Should().Contain(g => g.ErrorCode == "TOKEN_EXPIRED");                                                           // aggregate
        await sp.GetRequiredService<IQuoteLogReader>().GetFunnelAsync(now.AddHours(-1), now, null, null, "INS_NRD", CancellationToken.None);   // distinct
        var purchases = sp.GetRequiredService<IPurchaseLogReader>();
        await purchases.SearchProposalStepsAsync(new ProposalStepSearch(now.AddHours(-1), now, ResponseContains: "POL-2026"), CancellationToken.None);   // regex in JSON
        await purchases.SearchPaymentsAsync(new PaymentSearch(now.AddHours(-1), now, ClientReferenceCodes: ["PAY-00000000"]), CancellationToken.None);
        await purchases.GetWindowAsync(now.AddHours(-1), now, CancellationToken.None);

        // Okuma arayüzünün arkasındaki nesne yazmaya ZORLANSA bile veritabanı reddeder (kod hatasına karşı ikinci kat).
        var forced = async () => await ((ILogRepository)reader).AddManyAsync([Seed.Entry(now, Domain.Logs.LogSeverity.Information)], CancellationToken.None);
        (await forced.Should().ThrowAsync<Exception>()).Which.Should().Match<Exception>(e => Unauthorized(e));
    }
}
