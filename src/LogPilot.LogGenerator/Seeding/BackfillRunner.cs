using LogPilot.Application.Logs;
using LogPilot.Application.Notifications;
using LogPilot.Application.Quotes;
using LogPilot.Application.Scenarios;
using LogPilot.Application.Users;
using LogPilot.Domain.Logs;
using LogPilot.Domain.Notifications;
using LogPilot.Domain.Quotes;
using LogPilot.Domain.Scenarios;
using LogPilot.LogGenerator.Engine;
using Microsoft.Extensions.Options;

namespace LogPilot.LogGenerator.Seeding;

/// Geçmişe dönük log üretir: dashboard ve "son 15 dakika" gibi sorular ilk günden anlamlı olsun diye.
/// Aynı motoru kullanır; sadece "şimdi" yerine geçmiş zaman damgaları verir.
public sealed class BackfillRunner(
    ScenarioEngine engine,
    IScenarioRepository scenarios,
    IUserRepository users,
    ILogRepository logs,
    IQuoteLogRepository quotes,
    ISmsLogRepository sms,
    TimeProvider time,
    IOptions<GeneratorOptions> options,
    ILogger<BackfillRunner> logger)
{
    private const int BatchSize = 2000;
    private const double IncidentChance = 0.7;   // her incident senaryosunun pencerede görünme olasılığı

    /// quoteOnly: sadece teklif adımı olan senaryolar (uygulama logları dolu ama `quote_logs` boşsa — eski kurulumlar).
    public async Task RunAsync(int minutes, CancellationToken ct, bool quoteOnly = false)
    {
        if (minutes <= 0) return;

        var all = (await scenarios.GetAllAsync(ct))
            .Where(s => !quoteOnly || s.Steps.Any(st => st.Target != StepTarget.AppLog)).ToList();
        var normal = all.Where(s => s.Kind == ScenarioKind.Normal && s.Settings.Enabled && s.Settings.Weight > 0).ToList();
        var incidents = all.Where(s => s.Kind == ScenarioKind.Incident).ToList();
        var userList = await users.GetAllAsync(ct);

        var traffic = options.Value.NormalTraffic;
        var rng = new Random();
        var end = time.GetUtcNow().UtcDateTime;
        var start = end.AddMinutes(-minutes);

        var batch = new List<LogEntry>();
        var quoteBatch = new List<QuoteLogEntry>();
        var smsBatch = new List<SmsLogEntry>();
        var total = 0;

        void Add(ScenarioRun run)
        {
            batch.AddRange(run.Logs);
            quoteBatch.AddRange(run.Quotes);
            smsBatch.AddRange(run.Sms);
        }

        async Task FlushAsync()
        {
            if (batch.Count > 0) await logs.AddManyAsync(batch, ct);
            if (quoteBatch.Count > 0) await quotes.AddManyAsync(quoteBatch, ct);
            if (smsBatch.Count > 0) await sms.AddManyAsync(smsBatch, ct);
            total += batch.Count + quoteBatch.Count + smsBatch.Count;
            batch.Clear();
            quoteBatch.Clear();
            smsBatch.Clear();
        }

        if (normal.Count > 0)
        {
            for (var t = start; t < end; t = t.AddSeconds(rng.Next(traffic.MinIntervalSeconds, traffic.MaxIntervalSeconds + 1)))
            {
                var flows = rng.Next(traffic.MinFlowsPerTick, traffic.MaxFlowsPerTick + 1);
                for (var i = 0; i < flows; i++)
                {
                    var scenario = WeightedPicker.Pick(normal, rng);
                    Add(engine.Generate(scenario, userList, rng.Next(), t));
                }

                if (batch.Count + quoteBatch.Count >= BatchSize) await FlushAsync();
            }
        }

        foreach (var scenario in incidents)
        {
            if (rng.NextDouble() > IncidentChance) continue;

            var at = start.AddSeconds(60 + rng.NextDouble() * Math.Max(1, minutes * 60 - 120));
            if (at > end) at = end;

            Add(engine.Generate(scenario, userList, rng.Next(), at));
        }

        await FlushAsync();
        logger.LogInformation("Backfill finished: {Count} logs generated for the last {Minutes} minutes.", total, minutes);
    }
}
