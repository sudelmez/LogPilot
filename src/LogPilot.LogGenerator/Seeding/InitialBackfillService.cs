using LogPilot.Application.Logs;
using LogPilot.Application.Notifications;
using LogPilot.Application.Purchases;
using LogPilot.Application.Quotes;
using LogPilot.Domain.Scenarios;
using Microsoft.Extensions.Options;

namespace LogPilot.LogGenerator.Seeding;

/// İlk kurulumda (ör. Docker'da boş veritabanı) dashboard'un dolu açılması için geçmiş log üretir.
/// Yalnızca BOŞ olan tablo grupları doldurulur; yeniden başlatmalarda veri çoğalmaz. Böylece sonradan eklenen bir log
/// tablosu (ör. satın alma tabloları) mevcut kurulumda da veriyle açılır, eski tablolar çoğalmaz.
/// CatalogSyncService'ten SONRA kaydedilmelidir: senaryolar ve kullanıcılar önce DB'de olmalı.
internal sealed class InitialBackfillService(
    BackfillRunner backfill,
    ILogRepository logs,
    IQuoteLogRepository quotes,
    ISmsLogRepository sms,
    IPurchaseLogRepository purchases,
    IOptions<GeneratorOptions> options,
    ILogger<InitialBackfillService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        var minutes = options.Value.InitialBackfillMinutes;
        if (minutes <= 0) return;

        if (await logs.GetEstimatedCountAsync(ct) == 0)
        {
            await backfill.RunAsync(minutes, ct);   // boş veritabanı: her şey
            return;
        }

        var quotesEmpty = await quotes.GetEstimatedCountAsync(ct) == 0 || await sms.GetEstimatedCountAsync(ct) == 0;
        var purchasesEmpty = await purchases.GetEstimatedCountAsync(ct) == 0;
        if (!quotesEmpty && !purchasesEmpty)
        {
            logger.LogInformation("Initial backfill skipped: logs already exist.");
            return;
        }

        static bool IsQuote(Scenario s) => s.Steps.Any(st => st.Target != StepTarget.AppLog);
        static bool IsPurchase(Scenario s) => s.Purchase is not null;
        logger.LogInformation("Backfilling empty log groups only (quote/SMS: {Quotes}, purchase: {Purchases}).", quotesEmpty, purchasesEmpty);
        await backfill.RunAsync(minutes, ct, s => (quotesEmpty && IsQuote(s)) || (purchasesEmpty && IsPurchase(s)));
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
