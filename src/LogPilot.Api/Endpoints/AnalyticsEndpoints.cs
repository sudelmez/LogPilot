using LogPilot.Application.Analytics;
using LogPilot.Domain.Analytics;

namespace LogPilot.Api.Endpoints;

/// Tüm endpoint'lerde from/to opsiyoneldir; verilmezse son 24 saat.
public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/analytics").WithTags("Analytics");

        group.MapGet("/summary", async (DateTimeOffset? from, DateTimeOffset? to, AnalyticsService s, CancellationToken ct)
                => TypedResults.Ok(await s.GetSummaryAsync(from, to, ct)))
            .Produces<AnalyticsSummary>()
            .ProducesValidationProblem();

        group.MapGet("/error-trend", async (DateTimeOffset? from, DateTimeOffset? to, int? bucketMinutes,
                AnalyticsService s, CancellationToken ct)
                => TypedResults.Ok(await s.GetErrorTrendAsync(from, to, bucketMinutes, ct)))
            .Produces<IReadOnlyList<TrendPoint>>()
            .ProducesValidationProblem();

        group.MapGet("/errors-by-service", async (DateTimeOffset? from, DateTimeOffset? to, AnalyticsService s, CancellationToken ct)
                => TypedResults.Ok(await s.GetErrorsByServiceAsync(from, to, ct)))
            .Produces<IReadOnlyList<ServiceErrorShare>>();

        group.MapGet("/top-error-codes", async (DateTimeOffset? from, DateTimeOffset? to, AnalyticsService s,
                CancellationToken ct, int limit = 10)
                => TypedResults.Ok(await s.GetTopErrorCodesAsync(from, to, limit, ct)))
            .Produces<IReadOnlyList<ErrorCodeCount>>()
            .ProducesValidationProblem();

        group.MapGet("/service-health", async (DateTimeOffset? from, DateTimeOffset? to, AnalyticsService s, CancellationToken ct)
                => TypedResults.Ok(await s.GetServiceHealthAsync(from, to, ct)))
            .Produces<IReadOnlyList<ServiceHealth>>();

        group.MapGet("/error-groups", async (DateTimeOffset? from, DateTimeOffset? to, AnalyticsService s, CancellationToken ct)
                => TypedResults.Ok(await s.GetErrorGroupsAsync(from, to, ct)))
            .Produces<IReadOnlyList<ErrorGroup>>();

        return app;
    }
}
