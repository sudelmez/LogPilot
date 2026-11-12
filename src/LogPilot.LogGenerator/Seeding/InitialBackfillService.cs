using LogPilot.Application.Logs;
using LogPilot.Application.Notifications;
using LogPilot.Application.Quotes;
using Microsoft.Extensions.Options;

namespace LogPilot.LogGenerator.Seeding;

/// İlk kurulumda (ör. Docker'da boş veritabanı) dashboard'un dolu açılması için geçmiş log üretir.
/// Yalnızca `logs` (veya yeni eklenen `quote_logs`) koleksiyonu boşsa çalışır; yeniden başlatmalarda veri çoğalmaz.
/// CatalogSyncService'ten SONRA kaydedilmelidir: senaryolar ve kullanıcılar önce DB'de olmalı.
internal sealed class InitialBackfillService(
    BackfillRunner backfill,
    ILogRepository logs,
    IQuoteLogRepository quotes,
    ISmsLogRepository sms,
    IOptions<GeneratorOptions> options,
    ILogger<InitialBackfillService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        var minutes = options.Value.InitialBackfillMinutes;
        if (minutes <= 0) return;

        var hasLogs = await logs.GetEstimatedCountAsync(ct) > 0;
        var hasQuotes = await quotes.GetEstimatedCountAsync(ct) > 0 && await sms.GetEstimatedCountAsync(ct) > 0;

        if (hasLogs && hasQuotes)
        {
            logger.LogInformation("Initial backfill skipped: logs already exist.");
            return;
        }

        // Uygulama logları var ama teklif tablosu yeni eklendiyse: sadece teklif senaryolarını geriye doldur.
        if (hasLogs) logger.LogInformation("quote_logs/sms_logs is empty; backfilling quote and SMS scenarios only.");
        await backfill.RunAsync(minutes, ct, quoteOnly: hasLogs);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
