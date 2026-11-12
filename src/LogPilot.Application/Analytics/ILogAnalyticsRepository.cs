using LogPilot.Domain.Analytics;

namespace LogPilot.Application.Analytics;

public interface ILogAnalyticsRepository
{
    Task<LogCounts> GetCountsAsync(TimeRange range, CancellationToken ct);
    Task<IReadOnlyList<TrendPoint>> GetTrendAsync(TimeRange range, int bucketMinutes, CancellationToken ct);
    Task<IReadOnlyList<ServiceStats>> GetServiceStatsAsync(TimeRange range, CancellationToken ct);
    Task<IReadOnlyList<ErrorCodeCount>> GetTopErrorCodesAsync(TimeRange range, int limit, CancellationToken ct);

    /// Error/Critical logları hata koduna göre gruplar; errorCode verilirse sadece o kod.
    Task<IReadOnlyList<ErrorGroup>> GetErrorGroupsAsync(TimeRange range, string? errorCode, CancellationToken ct);
}
