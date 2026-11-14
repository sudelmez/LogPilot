using LogPilot.Application.Logs;
using LogPilot.Application.Notifications;
using LogPilot.Application.Purchases;
using LogPilot.Application.Quotes;
using LogPilot.Application.Users;
using LogPilot.Domain.Scenarios;

namespace LogPilot.LogGenerator.Engine;

/// Motoru çalıştırıp sonucu MongoDB'ye yazan ince katman (I/O burada).
public sealed class ScenarioRunner(
    ScenarioEngine engine, IUserRepository users, ILogRepository logs, IQuoteLogRepository quotes, ISmsLogRepository sms,
    IPurchaseLogRepository purchases, TimeProvider time)
{
    public async Task<ScenarioRun> RunAsync(Scenario scenario, CancellationToken ct)
    {
        var allUsers = await users.GetAllAsync(ct);

        // Seed tanımlıysa tekrarlanabilir, değilse rastgele; hangisi olursa olsun execution kaydına yazılır.
        var seed = scenario.Seed ?? Random.Shared.Next();
        var run = engine.Generate(scenario, allUsers, seed, time.GetUtcNow().UtcDateTime);

        // Üç ayrı sistem, üç ayrı tablo: uygulama, teklif motoru, SMS ağ geçidi.
        await logs.AddManyAsync(run.Logs, ct);
        await quotes.AddManyAsync(run.Quotes, ct);
        await sms.AddManyAsync(run.Sms, ct);
        await purchases.AddManyAsync(run.ProposalSteps, run.Winsure, run.Payments, ct);
        return run;
    }
}
