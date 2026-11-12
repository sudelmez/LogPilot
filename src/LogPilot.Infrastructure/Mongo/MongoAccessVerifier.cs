using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace LogPilot.Infrastructure.Mongo;

/// API açılışında AI okuyucu hesabının GERÇEKTEN salt-okunur olduğunu MongoDB'ye sorarak doğrular (veri değiştirmez).
/// Mongo:RequireReadOnlyAiAccess=true ise yazma yetkisi görüldüğünde uygulama AÇILMAZ (fail closed).
internal sealed class MongoAccessVerifier(
    [FromKeyedServices(DependencyInjection.AiReaderKey)] IMongoClient readerClient,
    IOptions<MongoOptions> options,
    ILogger<MongoAccessVerifier> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        var o = options.Value;
        AccessReport report;
        try { report = await MongoAccessPolicy.InspectAsync(readerClient, ct); }
        catch (Exception ex) when (!o.RequireReadOnlyAiAccess)
        {
            logger.LogWarning(ex, "Could not inspect the AI reader's MongoDB privileges.");
            return;
        }

        if (!report.IsAuthenticated)
        {
            const string message = "MongoDB authentication is not enabled for the AI reader: read-only access is enforced in code " +
                                   "(reader interfaces) but NOT by the database. Use docker-compose.secure.yml in production.";
            if (o.RequireReadOnlyAiAccess) throw new InvalidOperationException(message);
            logger.LogWarning(message);
            return;
        }

        if (report.WriteActions.Count > 0)
        {
            var message = $"The AI reader account ({string.Join(", ", report.Users)}) has write privileges: {string.Join(", ", report.WriteActions)}. " +
                          "It must only have 'find' on the analysed collections.";
            if (o.RequireReadOnlyAiAccess) throw new InvalidOperationException(message);
            logger.LogCritical(message);
            return;
        }

        logger.LogInformation("AI tools use the read-only MongoDB account {Users} (actions: {Actions}).",
            string.Join(", ", report.Users), string.Join(", ", report.Actions));
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
