using LogPilot.Application.Scenarios;
using LogPilot.Domain.Scenarios;
using Microsoft.Extensions.Options;

namespace LogPilot.LogGenerator.Workers;

/// Incident senaryolarını zamanlar: aralık dolunca olasılık zarı atar, tutarsa Pending execution yazar.
/// Elle tetikleme ile AYNI yolu kullanır; log üretimini ExecutionProcessorWorker yapar.
internal sealed class ScenarioSchedulerWorker(
    IScenarioRepository scenarios,
    IScenarioExecutionRepository executions,
    TimeProvider time,
    IOptions<GeneratorOptions> options,
    ILogger<ScenarioSchedulerWorker> logger) : BackgroundService
{
    private readonly Dictionary<string, DateTime> _nextRun = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.Scheduler.TickSeconds));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await TickAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Scheduler tick failed; will retry on the next tick.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // kapanış
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var multiplier = options.Value.Scheduler.IntervalMultiplier;

        // Ayarlar her tick'te DB'den okunur: Compass'tan yapılan değişiklik restart gerektirmez.
        foreach (var scenario in await scenarios.GetAllAsync(ct))
        {
            var s = scenario.Settings;
            if (scenario.Kind != ScenarioKind.Incident || !s.Enabled || s.IntervalSeconds <= 0)
            {
                _nextRun.Remove(scenario.Id);
                continue;
            }

            if (!_nextRun.TryGetValue(scenario.Id, out var due))
            {
                // İlk görüşte, senaryolar aynı anda patlamasın diye aralığın rastgele bir kesrinde planla.
                _nextRun[scenario.Id] = now.AddSeconds(s.IntervalSeconds * multiplier * Random.Shared.NextDouble());
                continue;
            }

            if (now < due) continue;
            _nextRun[scenario.Id] = now.AddSeconds(s.IntervalSeconds * multiplier);

            if (Random.Shared.NextDouble() > s.Probability)
            {
                logger.LogDebug("Scenario {ScenarioId} skipped this round (probability {Probability}).", scenario.Id, s.Probability);
                continue;
            }

            if (await executions.HasActiveAsync(scenario.Id, ct)) continue;

            var execution = await executions.CreateAsync(scenario.Id, ExecutionTrigger.Schedule, now, ct);
            logger.LogInformation("Scheduled scenario {ScenarioId} (execution {ExecutionId}).", scenario.Id, execution.Id);
        }
    }
}
