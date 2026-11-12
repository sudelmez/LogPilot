using Microsoft.Extensions.DependencyInjection.Extensions;
using LogPilot.Infrastructure;
using LogPilot.LogGenerator;
using LogPilot.LogGenerator.Engine;
using LogPilot.LogGenerator.Seeding;
using LogPilot.LogGenerator.Workers;

// Kullanım:
//   dotnet run --project src/LogPilot.LogGenerator                                   (sürekli çalışır)
//   dotnet run --project src/LogPilot.LogGenerator -- --seed-only --backfill-minutes 60   (tek seferlik)
var seedOnly = args.Contains("--seed-only");
var backfillMinutes = ReadInt(args, "--backfill-minutes", 0);

// ContentRoot'u açıkça derleme dizinine sabitliyoruz: appsettings.json çalışma dizininden bağımsız bulunur.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    ContentRootPath = AppContext.BaseDirectory
});

if (builder.Configuration.GetValue<bool>("Logging:Json"))
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(o =>
    {
        o.IncludeScopes = true;
        o.UseUtcTimestamp = true;
        o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
    });
}

// AddApplication() çağrılmaz: Incident servisleri AI katmanındaki IIncidentSummarizer'a bağlı ve generator
// AI'ı yüklemez (Development'ta DI doğrulaması açılışta çöker). Generator yalnızca saati ve repository'leri kullanır.
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddInfrastructure(builder.Configuration);   // Mongo + indeksler (hosted service)

builder.Services.AddOptions<GeneratorOptions>()
    .Bind(builder.Configuration.GetSection(GeneratorOptions.SectionName))
    .Validate(o => o.IsValid(), "Generator options are invalid (check intervals, flow counts and limits).")
    .ValidateOnStart();

builder.Services.AddSingleton<ScenarioEngine>();
builder.Services.AddSingleton<ScenarioRunner>();
builder.Services.AddSingleton<BackfillRunner>();

// Başlatma sırası = kayıt sırası: indeksler (Infrastructure) → katalog senkronu → worker'lar.
builder.Services.AddHostedService<CatalogSyncService>();

if (!seedOnly)
{
    builder.Services.AddHostedService<InitialBackfillService>();   // boş DB'de geçmiş log (katalogdan sonra)
    builder.Services.AddHostedService<NormalTrafficWorker>();
    builder.Services.AddHostedService<ScenarioSchedulerWorker>();
    builder.Services.AddHostedService<ExecutionProcessorWorker>();
}

using var host = builder.Build();

if (seedOnly)
{
    await host.StartAsync();
    await host.Services.GetRequiredService<BackfillRunner>().RunAsync(backfillMinutes, CancellationToken.None);
    await host.StopAsync();
    return;
}

await host.RunAsync();

static int ReadInt(string[] args, string name, int fallback)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] == name && i + 1 < args.Length && int.TryParse(args[i + 1], out var spaced)) return spaced;
        if (args[i].StartsWith(name + "=", StringComparison.Ordinal)
            && int.TryParse(args[i][(name.Length + 1)..], out var joined)) return joined;
    }
    return fallback;
}
