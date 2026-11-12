using LogPilot.Application.Scenarios;
using LogPilot.Domain.Scenarios;
using LogPilot.LogGenerator.Engine;
using Microsoft.Extensions.Options;

namespace LogPilot.LogGenerator.Workers;

/// Pending execution kuyruğunu (MongoDB) yoklar; kaydı atomik olarak sahiplenir, senaryoyu çalıştırır, sonucu yazar.
internal sealed class ExecutionProcessorWorker(
    IScenarioRepository scenarios,
    IScenarioExecutionRepository executions,
    ScenarioRunner runner,
    TimeProvider time,
    IOptions<GeneratorOptions> options,
    ILogger<ExecutionProcessorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var poll = TimeSpan.FromMilliseconds(options.Value.Processor.PollIntervalMilliseconds);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var processed = false;
                try
                {
                    var claimed = await executions.ClaimNextPendingAsync(Now(), stoppingToken);
                    if (claimed is not null)
                    {
                        await ProcessAsync(claimed, stoppingToken);
                        processed = true;
                    }
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Execution processing loop failed; will retry.");
                }

                if (!processed) await Task.Delay(poll, stoppingToken);   // kuyruk doluyken beklemeden devam
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // kapanış: Running kalan kayıt bir sonraki açılışta CatalogSyncService tarafından Failed yapılır
        }
    }

    private async Task ProcessAsync(ScenarioExecution execution, CancellationToken ct)
    {
        var id = execution.Id!;
        var scenario = await scenarios.GetByIdAsync(execution.ScenarioId, ct);
        if (scenario is null)
        {
            await executions.FailAsync(id, Now(), $"Scenario '{execution.ScenarioId}' not found.", ct);
            logger.LogWarning("Execution {ExecutionId} failed: scenario {ScenarioId} not found.", id, execution.ScenarioId);
            return;
        }

        try
        {
            var run = await runner.RunAsync(scenario, ct);
            await executions.CompleteAsync(id, Now(), run.TotalCount, run.Seed, run.CorrelationIds, ct);

            logger.LogInformation(
                "Scenario {ScenarioId} ({Trigger}) completed: {LogCount} app logs + {QuoteCount} quote logs, {FlowCount} user flows, seed {Seed}.",
                scenario.Id, execution.TriggeredBy, run.Logs.Count, run.Quotes.Count, run.CorrelationIds.Count, run.Seed);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            await executions.FailAsync(id, Now(), ex.Message, CancellationToken.None);
            logger.LogError(ex, "Scenario {ScenarioId} execution {ExecutionId} failed.", scenario.Id, id);
        }
    }

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
