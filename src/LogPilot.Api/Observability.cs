using System.Diagnostics;
using System.Text.RegularExpressions;
using LogPilot.Infrastructure.Mongo;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace LogPilot.Api;

/// Her isteğe bir korelasyon kimliği verir (gelen X-Correlation-Id geçerliyse onu kullanır), log kapsamına ekler,
/// yanıt başlığına yazar ve istek özetini loglar. Başlık değeri doğrulanır: log sahteciliği (log forging) yapılamaz.
internal sealed partial class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string Header = "X-Correlation-Id";

    [GeneratedRegex(@"^[A-Za-z0-9\-_.]{1,64}$")]
    private static partial Regex SafeId();

    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[Header].ToString();
        var id = SafeId().IsMatch(supplied) ? supplied : Guid.NewGuid().ToString("N");
        context.Response.Headers[Header] = id;

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
        {
            var watch = Stopwatch.StartNew();
            await next(context);
            logger.LogInformation("{Method} {Path} -> {Status} in {Elapsed} ms",
                context.Request.Method, context.Request.Path.Value, context.Response.StatusCode, watch.ElapsedMilliseconds);
        }
    }
}

/// /health/ready: MongoDB'ye ping atar. /health/live ise hiçbir bağımlılığa bakmaz (süreç ayakta mı?).
internal sealed class MongoHealthCheck(IMongoClient client, IOptions<MongoOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            await client.GetDatabase(options.Value.DatabaseName)
                .RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: cts.Token);
            return HealthCheckResult.Healthy("MongoDB responded to ping.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("MongoDB ping failed.", ex);
        }
    }
}

internal static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
        => context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            totalDurationMs = (long)report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                durationMs = (long)e.Value.Duration.TotalMilliseconds
            })
        });
}
