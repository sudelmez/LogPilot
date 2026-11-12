using LogPilot.Application.Incidents;
using Microsoft.Extensions.Options;

namespace LogPilot.Api;

public sealed class IncidentOptions
{
    public const string SectionName = "Incidents";

    public bool Enabled { get; set; } = true;
    public int DetectionIntervalSeconds { get; set; } = 30;
    public int WindowMinutes { get; set; } = 15;

    /// Yeni/büyüyen incident için AI özetini otomatik üret. Kapalıyken özet yalnızca arayüzdeki "Regenerate" ile üretilir
    /// (yerel modelde bilgisayarı, Claude'da bakiyeyi boşuna meşgul etmemek için).
    public bool AutoSummarize { get; set; } = true;
}

/// API host'unda çalışan arka plan işi: incident tespiti iş mantığıdır, log üretimiyle ilgisi yoktur.
internal sealed class IncidentDetectorWorker(
    IServiceScopeFactory scopes,
    TimeProvider time,
    IOptions<IncidentOptions> options,
    ILogger<IncidentDetectorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Incident detection is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, settings.DetectionIntervalSeconds)));

        try
        {
            do
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var detection = scope.ServiceProvider.GetRequiredService<IncidentDetectionService>();
                    var result = await detection.DetectAsync(
                        time.GetUtcNow().UtcDateTime, settings.WindowMinutes, stoppingToken, settings.AutoSummarize);

                    if (result.Created + result.Updated > 0)
                        logger.LogInformation("Incident detection: {Created} created, {Updated} updated.", result.Created, result.Updated);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Incident detection failed; will retry on the next tick.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // kapanış
        }
    }
}
