using AwesomeAssertions;
using LogPilot.Domain.Analytics;

namespace LogPilot.UnitTests.Analytics;

public class AnalyticsDomainTests
{
    [Theory]
    [InlineData(0, 0, 0.0)]
    [InlineData(5, 0, 0.0)]
    [InlineData(10, 100, 0.1)]
    public void Ratio_is_safe_for_zero_denominator(long n, long d, double expected)
        => Rates.Ratio(n, d).Should().BeApproximately(expected, 1e-9);

    [Theory]
    [InlineData(4, 1.0, ServiceHealthStatus.Unknown)]      // örnek çok az: yargı yok
    [InlineData(100, 0.00, ServiceHealthStatus.Healthy)]
    [InlineData(100, 0.049, ServiceHealthStatus.Healthy)]
    [InlineData(100, 0.05, ServiceHealthStatus.Degraded)]
    [InlineData(100, 0.199, ServiceHealthStatus.Degraded)]
    [InlineData(100, 0.20, ServiceHealthStatus.Unhealthy)]
    public void Service_health_thresholds(long total, double ratio, ServiceHealthStatus expected)
        => ServiceHealthClassifier.Classify(total, ratio).Should().Be(expected);

    [Theory]
    [InlineData(60, 1)]          // 1 saat → 1 dk kova (60 nokta)
    [InlineData(6 * 60, 5)]      // 6 saat → 5 dk (72 nokta)
    [InlineData(24 * 60, 15)]    // 24 saat → 15 dk (96 nokta)
    [InlineData(7 * 24 * 60, 120)]
    public void Choose_picks_the_smallest_bucket_within_target_points(int rangeMinutes, int expectedBucket)
        => TrendBuckets.Choose(TimeSpan.FromMinutes(rangeMinutes)).Should().Be(expectedBucket);

    [Fact]
    public void Floor_aligns_to_the_bucket_start()
    {
        var t = new DateTime(2026, 11, 8, 12, 37, 45, DateTimeKind.Utc);

        TrendBuckets.Floor(t, 15).Should().Be(new DateTime(2026, 11, 8, 12, 30, 0, DateTimeKind.Utc));
        TrendBuckets.Floor(t, 60).Should().Be(new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc));
        TrendBuckets.Floor(t, 1440).Should().Be(new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void IsValid_rejects_unknown_or_too_fine_buckets()
    {
        TrendBuckets.IsValid(7, TimeSpan.FromHours(1)).Should().BeFalse();
        TrendBuckets.IsValid(1, TimeSpan.FromDays(7)).Should().BeFalse();   // 10080 nokta
        TrendBuckets.IsValid(15, TimeSpan.FromDays(1)).Should().BeTrue();
    }
}
