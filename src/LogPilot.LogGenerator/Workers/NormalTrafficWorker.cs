using LogPilot.Application.Logs;
using LogPilot.Application.Scenarios;
using LogPilot.Domain.Scenarios;
using LogPilot.LogGenerator.Engine;
using Microsoft.Extensions.Options;

namespace LogPilot.LogGenerator.Workers;

/// Arka plan trafiği: 10-30 sn aralıkla (ayarlanabilir) birkaç normal akış üretir.
internal sealed class NormalTrafficWorker(
    IScenarioRepository scenarios,
    ILogRepository logs,
    ScenarioRunner runner,
    IOptions<GeneratorOptions> options,
    ILogger<NormalTrafficWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value.NormalTraffic;
        if (!settings.Enabled)
        {
            logger.LogInformation("Normal traffic generation is disabled.");
            return;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await TickAsync(settings, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Normal traffic tick failed; will retry on the next tick.");
                }

                var delay = TimeSpan.FromSeconds(Random.Shared.Next(settings.MinIntervalSeconds, settings.MaxIntervalSeconds + 1));
                await Task.Delay(delay, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // kapanış
        }
    }

    private async Task TickAsync(NormalTrafficOptions settings, CancellationToken ct)
    {
        var current = await logs.GetEstimatedCountAsync(ct);
        if (current >= settings.MaxLogCount)
        {
            logger.LogWarning("Log cap reached ({Count} >= {Max}); normal traffic paused until TTL frees space.",
                current, settings.MaxLogCount);
            return;
        }

        var normal = (await scenarios.GetAllAsync(ct))
            .Where(s => s.Kind == ScenarioKind.Normal && s.Settings.Enabled && s.Settings.Weight > 0)
            .ToList();
        if (normal.Count == 0) return;

        var flows = Random.Shared.Next(settings.MinFlowsPerTick, settings.MaxFlowsPerTick + 1);
        var generated = 0;
        for (var i = 0; i < flows; i++)
        {
            var scenario = WeightedPicker.Pick(normal, Random.Shared);
            generated += (await runner.RunAsync(scenario, ct)).TotalCount;
        }

        logger.LogDebug("Normal traffic tick: {Flows} flows, {Logs} logs.", flows, generated);
    }
}
