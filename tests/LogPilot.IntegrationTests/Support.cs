using System.Net.Http.Json;
using System.Text.Json;
using LogPilot.Application.Logs;
using LogPilot.Domain.Logs;
using LogPilot.Domain.Scenarios;
using LogPilot.Domain.Users;
using LogPilot.LogGenerator.Config;
using LogPilot.LogGenerator.Engine;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace LogPilot.IntegrationTests;

/// Gerçek bir MongoDB var mı? Yoksa entegrasyon testleri ATLANIR (CI'sız yerel geliştirmede testler kırılmasın).
public static class MongoAvailability
{
    public static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("LOGPILOT_TEST_MONGO") ?? "mongodb://localhost:27017";

    private static readonly Lazy<bool> Reachable = new(Check);
    public static bool IsAvailable => Reachable.Value;

    private static bool Check()
    {
        try
        {
            var settings = MongoClientSettings.FromConnectionString(ConnectionString);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(2);
            new MongoClient(settings).GetDatabase("admin").RunCommand<BsonDocument>(new BsonDocument("ping", 1));
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class MongoFactAttribute : FactAttribute
{
    public MongoFactAttribute()
    {
        if (!MongoAvailability.IsAvailable)
            Skip = $"MongoDB is not reachable at {MongoAvailability.ConnectionString} (start mongod or set LOGPILOT_TEST_MONGO).";
    }
}

/// Gerçek API'yi (TestServer) geçici bir veritabanıyla ayağa kaldırır; test bitince veritabanını siler.
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public string DatabaseName { get; } = $"logpilot_it_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Mongo:ConnectionString", MongoAvailability.ConnectionString);
        builder.UseSetting("Mongo:DatabaseName", DatabaseName);
        builder.UseSetting("Incidents:Enabled", "false");   // testler tespiti kendileri tetikler
        builder.UseSetting("Ai:Provider", "Mock");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && MongoAvailability.IsAvailable)
        {
            try { new MongoClient(MongoAvailability.ConnectionString).DropDatabase(DatabaseName); }
            catch { /* temizlik başarısızsa testi bozma */ }
        }
    }
}

internal static class Seed
{
    private static readonly IReadOnlyList<Scenario> Scenarios = SeedFileLoader.LoadScenarios(RepoPaths.FindDirectory("scenarios"));
    private static readonly IReadOnlyList<User> Users = SeedFileLoader.LoadUsers(Path.Combine(RepoPaths.FindDirectory("seed"), "users.json"));

    public static IReadOnlyList<Scenario> LoadScenarios() => Scenarios;

    public static Task AddAsync(IServiceProvider services, params LogEntry[] logs)
        => services.GetRequiredService<ILogRepository>().AddManyAsync(logs, CancellationToken.None);

    /// Gerçek senaryo dosyasından deterministik loglar üretip MongoDB'ye yazar.
    public static async Task RunScenarioAsync(IServiceProvider services, string id, DateTime at, int seed, params string[] onlyUsers)
    {
        var scenario = Scenarios.Single(s => s.Id == id);
        if (onlyUsers.Length > 0)
        {
            scenario = new Scenario
            {
                Id = scenario.Id, Name = scenario.Name, Kind = scenario.Kind, Seed = scenario.Seed,
                Users = new UserSelection { Status = scenario.Users.Status, Role = scenario.Users.Role, UserCodes = onlyUsers.ToList(), MinCount = 1, MaxCount = 1, SpacingMs = scenario.Users.SpacingMs },
                Settings = scenario.Settings, Steps = scenario.Steps, Quote = scenario.Quote, Purchase = scenario.Purchase
            };
        }

        var run = new ScenarioEngine().Generate(scenario, Users, seed, at);
        await services.GetRequiredService<ILogRepository>().AddManyAsync(run.Logs, CancellationToken.None);
        await services.GetRequiredService<LogPilot.Application.Quotes.IQuoteLogRepository>().AddManyAsync(run.Quotes, CancellationToken.None);
        await services.GetRequiredService<LogPilot.Application.Notifications.ISmsLogRepository>().AddManyAsync(run.Sms, CancellationToken.None);
        await services.GetRequiredService<LogPilot.Application.Purchases.IPurchaseLogRepository>().AddManyAsync(run.ProposalSteps, run.Winsure, run.Payments, CancellationToken.None);
    }

    public static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    public static LogEntry Entry(
        DateTime timestamp, LogSeverity level, string service = "AuthenticationService", string? user = null,
        string? errorCode = null, OperationOutcome? outcome = null, int? durationMs = null, string? correlationId = null)
        => new()
        {
            Timestamp = timestamp, Level = level, Application = "TestApp", Service = service, UserCode = user,
            ErrorCode = errorCode, Outcome = outcome, DurationMs = durationMs, CorrelationId = correlationId, Message = "test message"
        };
}

internal static class HttpExtensions
{
    public static async Task<JsonElement> GetJsonAsync(this HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static async Task<(System.Net.HttpStatusCode Status, JsonElement Body)> SendJsonAsync(
        this HttpClient client, HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);

        var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        var json = string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
        return (response.StatusCode, json);
    }
}
