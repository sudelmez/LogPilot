using AwesomeAssertions;
using LogPilot.Application.Analytics;
using LogPilot.Application.Common;
using LogPilot.Domain.Analytics;
using Moq;

namespace LogPilot.UnitTests.Analytics;

public class AnalyticsServiceTests
{
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<ILogAnalyticsRepository> _repo = new();
    private readonly AnalyticsService _sut;

    public AnalyticsServiceTests() => _sut = new AnalyticsService(_repo.Object, new FixedTime(Now));

    [Fact]
    public async Task Summary_separates_log_error_ratio_from_request_failure_rate()
    {
        _repo.Setup(r => r.GetCountsAsync(It.IsAny<TimeRange>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogCounts(Total: 100, Errors: 10, Warnings: 5, Completed: 40, Failed: 8));

        var summary = await _sut.GetSummaryAsync(null, null, CancellationToken.None);

        summary.LogErrorRatio.Should().BeApproximately(0.10, 1e-9);          // 10 / 100 log
        summary.Requests.FailureRate.Should().BeApproximately(0.20, 1e-9);   // 8 / 40 tamamlanan istek
        summary.To.Should().Be(Now.UtcDateTime);
        summary.From.Should().Be(Now.UtcDateTime.AddHours(-24));
    }

    [Fact]
    public async Task Summary_is_zero_safe_when_there_is_no_data()
    {
        _repo.Setup(r => r.GetCountsAsync(It.IsAny<TimeRange>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogCounts(0, 0, 0, 0, 0));

        var summary = await _sut.GetSummaryAsync(null, null, CancellationToken.None);

        summary.LogErrorRatio.Should().Be(0);
        summary.Requests.FailureRate.Should().Be(0);
    }

    [Fact]
    public async Task Trend_fills_empty_buckets_with_zeros()
    {
        var from = Now.AddHours(-1);
        _repo.Setup(r => r.GetTrendAsync(It.IsAny<TimeRange>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TrendPoint(from.UtcDateTime.AddMinutes(10), 7, 2, 1)]);

        var points = await _sut.GetErrorTrendAsync(from, Now, null, CancellationToken.None);

        points.Should().HaveCount(60);
        points.Count(p => p.Total == 0).Should().Be(59);
        points[10].Should().Be(new TrendPoint(from.UtcDateTime.AddMinutes(10), 7, 2, 1));
        points.Select(p => p.BucketStart).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Trend_rejects_unsupported_bucket()
    {
        var act = () => _sut.GetErrorTrendAsync(null, null, 7, CancellationToken.None);
        await act.Should().ThrowAsync<RequestValidationException>();
    }

    [Fact]
    public async Task Range_must_be_ordered_and_at_most_31_days()
    {
        var reversed = () => _sut.GetSummaryAsync(Now, Now.AddHours(-1), CancellationToken.None);
        var tooLong = () => _sut.GetSummaryAsync(Now.AddDays(-32), Now, CancellationToken.None);

        await reversed.Should().ThrowAsync<RequestValidationException>();
        await tooLong.Should().ThrowAsync<RequestValidationException>();
    }

    [Fact]
    public async Task Service_health_is_sorted_by_error_ratio_and_classified()
    {
        _repo.Setup(r => r.GetServiceStatsAsync(It.IsAny<TimeRange>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ServiceStats("AuthenticationService", 100, 2, 0, 120, 900, null),
                new ServiceStats("ProposalService", 100, 30, 5, 300, 5000, null),
                new ServiceStats("TinyService", 2, 2, 0, null, null, null)
            ]);

        var health = await _sut.GetServiceHealthAsync(null, null, CancellationToken.None);

        health.Select(h => h.Service).Should().ContainInOrder("TinyService", "ProposalService", "AuthenticationService");
        health.Single(h => h.Service == "ProposalService").Status.Should().Be(ServiceHealthStatus.Unhealthy);
        health.Single(h => h.Service == "AuthenticationService").Status.Should().Be(ServiceHealthStatus.Healthy);
        health.Single(h => h.Service == "TinyService").Status.Should().Be(ServiceHealthStatus.Unknown);
    }

    [Fact]
    public async Task Errors_by_service_reports_share_of_all_errors()
    {
        _repo.Setup(r => r.GetServiceStatsAsync(It.IsAny<TimeRange>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ServiceStats("A", 100, 30, 0, null, null, null),
                new ServiceStats("B", 100, 10, 0, null, null, null),
                new ServiceStats("C", 100, 0, 0, null, null, null)
            ]);

        var result = await _sut.GetErrorsByServiceAsync(null, null, CancellationToken.None);

        result.Should().HaveCount(2);   // hatasız servis listelenmez
        result[0].Service.Should().Be("A");
        result[0].ShareOfAllErrors.Should().BeApproximately(0.75, 1e-9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(AnalyticsService.MaxTopLimit + 1)]
    public async Task Top_error_codes_rejects_invalid_limit(int limit)
    {
        var act = () => _sut.GetTopErrorCodesAsync(null, null, limit, CancellationToken.None);
        await act.Should().ThrowAsync<RequestValidationException>();
    }
}
