using AwesomeAssertions;
using LogPilot.Application.Ai;
using LogPilot.Application.Analytics;
using LogPilot.Application.Common;
using LogPilot.Application.Incidents;
using LogPilot.Application.Logs;
using LogPilot.Domain.Logs;
using Moq;
using static LogPilot.UnitTests.Ai.TestData;

namespace LogPilot.UnitTests.Ai;

public class LogAnalysisToolsTests
{
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private readonly Mock<ILogRepository> _logs = new();
    private readonly Mock<IIncidentRepository> _incidents = new();
    private readonly LogAnalysisTools _sut;
    private LogSearchRequest? _captured;

    public LogAnalysisToolsTests()
    {
        _logs.Setup(r => r.SearchAsync(It.IsAny<LogSearchRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LogSearchRequest, CancellationToken>((r, _) => _captured = r)
            .ReturnsAsync(new PagedResult<LogEntry>([], 0, 1, 50));

        var analytics = new AnalyticsService(new Mock<ILogAnalyticsRepository>().Object, new FixedTime(new DateTimeOffset(To)));
        _sut = new LogAnalysisTools(_logs.Object, analytics, _incidents.Object, new SensitiveDataMasker());
    }

    [Fact]
    public async Task Limit_is_clamped_to_the_maximum()
    {
        await _sut.SearchLogsAsync(new SearchLogsArgs(Limit: 500), Context, CancellationToken.None);

        _captured!.PageSize.Should().Be(LogAnalysisTools.MaxResults);
    }

    [Fact]
    public async Task Default_limit_applies_when_none_is_given()
    {
        await _sut.SearchLogsAsync(new SearchLogsArgs(), Context, CancellationToken.None);

        _captured!.PageSize.Should().Be(LogAnalysisTools.DefaultResults);
    }

    [Fact]
    public async Task The_search_window_is_clamped_to_the_selected_range()
    {
        await _sut.SearchLogsAsync(new SearchLogsArgs(From: From.AddDays(-30), To: To.AddDays(5)), Context, CancellationToken.None);

        _captured!.From!.Value.UtcDateTime.Should().Be(From);
        _captured.To!.Value.UtcDateTime.Should().Be(To);
    }

    [Fact]
    public async Task A_range_outside_the_selected_range_is_rejected()
    {
        var act = () => _sut.SearchLogsAsync(new SearchLogsArgs(From: To.AddHours(1), To: To.AddHours(2)), Context, CancellationToken.None);

        await act.Should().ThrowAsync<ToolArgumentException>();
    }

    [Theory]
    [InlineData("TCLI' || 1==1")]
    [InlineData("a;drop")]
    [InlineData("{\"$ne\":null}")]
    public async Task Identifiers_with_unsupported_characters_are_rejected(string value)
    {
        var act = () => _sut.SearchLogsAsync(new SearchLogsArgs(UserCode: value), Context, CancellationToken.None);

        await act.Should().ThrowAsync<ToolArgumentException>();
        _logs.Verify(r => r.SearchAsync(It.IsAny<LogSearchRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unknown_levels_are_rejected()
    {
        var act = () => _sut.SearchLogsAsync(new SearchLogsArgs(Level: "Fatalish"), Context, CancellationToken.None);

        await act.Should().ThrowAsync<ToolArgumentException>();
    }

    [Fact]
    public async Task User_lookup_requires_a_user_code()
    {
        var act = () => _sut.GetUserRecentErrorsAsync(new UserRecentErrorsArgs(), Context, CancellationToken.None);

        await act.Should().ThrowAsync<ToolArgumentException>();
    }

    [Fact]
    public async Task Related_logs_need_some_identifier()
    {
        var act = () => _sut.FindRelatedLogsAsync(new RelatedLogsArgs(), Context, CancellationToken.None);

        await act.Should().ThrowAsync<ToolArgumentException>();
    }

    [Fact]
    public async Task Incident_details_reject_malformed_ids()
    {
        var act = () => _sut.GetIncidentDetailsAsync(new IncidentDetailsArgs("not-an-id"), Context, CancellationToken.None);

        await act.Should().ThrowAsync<ToolArgumentException>();
    }

    [Fact]
    public async Task Log_content_is_masked_before_it_leaves_the_tool()
    {
        var entry = new LogEntry
        {
            Id = "507f1f77bcf86cd799439011", Timestamp = From.AddHours(1), Level = LogSeverity.Error, Application = "MobileApp",
            Service = "AuthenticationService", Message = "Failed for jane@example.com with password=abc123",
            Metadata = { ["note"] = "token=xyz" }
        };
        _logs.Setup(r => r.SearchAsync(It.IsAny<LogSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<LogEntry>([entry], 1, 1, 20));

        var result = await _sut.SearchLogsAsync(new SearchLogsArgs(), Context, CancellationToken.None);

        var snippet = result.Logs.Single();
        snippet.Message.Should().NotContain("jane@example.com").And.NotContain("abc123");
        snippet.Metadata["note"].Should().NotContain("xyz");
    }

    [Fact]
    public async Task User_lookup_returns_warnings_plus_the_latest_success()
    {
        LogEntry Entry(string id, LogSeverity level, int minutes, OperationOutcome? outcome = null) => new()
        {
            Id = id, Timestamp = From.AddHours(2).AddMinutes(minutes), Level = level, Application = "MobileApp",
            Service = "AuthenticationService", UserCode = "TCLI", Message = "m", Outcome = outcome
        };
        _logs.Setup(r => r.SearchAsync(It.IsAny<LogSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<LogEntry>(
            [
                Entry("3", LogSeverity.Information, 3, OperationOutcome.Success),
                Entry("2", LogSeverity.Error, 2),
                Entry("1", LogSeverity.Information, 1)
            ], 3, 1, 50));

        var result = await _sut.GetUserRecentErrorsAsync(new UserRecentErrorsArgs("TCLI"), Context, CancellationToken.None);

        result.Logs.Select(l => l.Id).Should().BeEquivalentTo(["3", "2"]);   // plain info log "1" dışarıda
    }
}
