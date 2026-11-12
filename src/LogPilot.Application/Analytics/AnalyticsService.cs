using LogPilot.Application.Common;
using LogPilot.Domain.Analytics;

namespace LogPilot.Application.Analytics;

public sealed class AnalyticsService(ILogAnalyticsRepository repository, TimeProvider time)
{
    public static readonly TimeSpan DefaultRange = TimeSpan.FromHours(24);
    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(31);
    public const int MaxTopLimit = 50;

    public async Task<AnalyticsSummary> GetSummaryAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var range = ResolveRange(from, to);
        var c = await repository.GetCountsAsync(range, ct);

        return new AnalyticsSummary(
            range.From, range.To, c.Total, c.Errors, c.Warnings,
            Rates.Ratio(c.Errors, c.Total),
            new RequestStats(c.Completed, c.Failed, Rates.Ratio(c.Failed, c.Completed)));
    }

    public async Task<IReadOnlyList<TrendPoint>> GetErrorTrendAsync(
        DateTimeOffset? from, DateTimeOffset? to, int? bucketMinutes, CancellationToken ct)
    {
        var range = ResolveRange(from, to);
        var span = range.To - range.From;
        var bucket = bucketMinutes ?? TrendBuckets.Choose(span);

        if (!TrendBuckets.IsValid(bucket, span))
            throw new RequestValidationException(new Dictionary<string, string[]>
            {
                ["BucketMinutes"] = [$"BucketMinutes must be one of [{string.Join(", ", TrendBuckets.AllowedMinutes)}] and produce at most {TrendBuckets.MaxPoints} points."]
            });

        var raw = (await repository.GetTrendAsync(range, bucket, ct)).ToDictionary(p => p.BucketStart);

        // Boş kovaları sıfırla doldur: grafik sürekli bir zaman ekseni çizsin.
        var points = new List<TrendPoint>();
        for (var t = TrendBuckets.Floor(range.From, bucket); t < range.To; t = t.AddMinutes(bucket))
            points.Add(raw.TryGetValue(t, out var p) ? p : new TrendPoint(t, 0, 0, 0));

        return points;
    }

    public async Task<IReadOnlyList<ServiceErrorShare>> GetErrorsByServiceAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var stats = await repository.GetServiceStatsAsync(ResolveRange(from, to), ct);
        var totalErrors = stats.Sum(s => s.Errors);

        return stats
            .Where(s => s.Errors > 0)
            .Select(s => new ServiceErrorShare(
                s.Service, s.Errors, s.Total, Rates.Ratio(s.Errors, s.Total), Rates.Ratio(s.Errors, totalErrors)))
            .OrderByDescending(s => s.Errors)
            .ThenBy(s => s.Service, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyList<ErrorCodeCount>> GetTopErrorCodesAsync(
        DateTimeOffset? from, DateTimeOffset? to, int limit, CancellationToken ct)
    {
        if (limit is < 1 or > MaxTopLimit)
            throw new RequestValidationException(new Dictionary<string, string[]>
            {
                ["Limit"] = [$"Limit must be between 1 and {MaxTopLimit}."]
            });

        return await repository.GetTopErrorCodesAsync(ResolveRange(from, to), limit, ct);
    }

    public async Task<IReadOnlyList<ServiceHealth>> GetServiceHealthAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var stats = await repository.GetServiceStatsAsync(ResolveRange(from, to), ct);

        return stats
            .Select(s =>
            {
                var ratio = Rates.Ratio(s.Errors, s.Total);
                return new ServiceHealth(
                    s.Service, s.Total, s.Errors, s.Warnings, ratio,
                    s.AvgDurationMs, s.MaxDurationMs, s.LastErrorAt,
                    ServiceHealthClassifier.Classify(s.Total, ratio));
            })
            .OrderByDescending(h => h.LogErrorRatio)
            .ThenBy(h => h.Service, StringComparer.Ordinal)
            .ToList();
    }

    public Task<IReadOnlyList<ErrorGroup>> GetErrorGroupsAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
        => repository.GetErrorGroupsAsync(ResolveRange(from, to), null, ct);

    /// from/to verilmezse son 24 saat. Aralık ters veya 31 günden uzunsa reddedilir.
    internal TimeRange ResolveRange(DateTimeOffset? from, DateTimeOffset? to)
    {
        var end = (to ?? time.GetUtcNow()).UtcDateTime;
        var start = from?.UtcDateTime ?? end - DefaultRange;

        var errors = new Dictionary<string, string[]>();
        if (start >= end) errors["From"] = ["From must be earlier than To."];
        else if (end - start > MaxRange) errors["From"] = [$"The time range cannot exceed {MaxRange.TotalDays:0} days."];

        if (errors.Count > 0) throw new RequestValidationException(errors);
        return new TimeRange(start, end);
    }
}
