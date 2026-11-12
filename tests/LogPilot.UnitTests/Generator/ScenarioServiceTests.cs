using AwesomeAssertions;
using LogPilot.Application.Common;
using LogPilot.Application.Scenarios;
using LogPilot.Domain.Scenarios;
using Moq;

namespace LogPilot.UnitTests.Generator;

public class ScenarioServiceTests
{
    private readonly Mock<IScenarioRepository> _scenarios = new();
    private readonly Mock<IScenarioExecutionRepository> _executions = new();
    private readonly ScenarioService _sut;

    public ScenarioServiceTests() => _sut = new ScenarioService(_scenarios.Object, _executions.Object, TimeProvider.System);

    private static Scenario Existing(string id) => new()
    {
        Id = id, Name = id, Kind = ScenarioKind.Incident,
        Users = new UserSelection(), Settings = new ScenarioSettings(), Steps = []
    };

    [Fact]
    public async Task Trigger_unknown_scenario_throws_not_found()
    {
        _scenarios.Setup(r => r.GetByIdAsync("nope", It.IsAny<CancellationToken>())).ReturnsAsync((Scenario?)null);

        var act = () => _sut.TriggerAsync("nope", CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Trigger_throws_conflict_when_an_execution_is_already_active()
    {
        _scenarios.Setup(r => r.GetByIdAsync("token-expired", It.IsAny<CancellationToken>())).ReturnsAsync(Existing("token-expired"));
        _executions.Setup(r => r.HasActiveAsync("token-expired", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var act = () => _sut.TriggerAsync("token-expired", CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        _executions.Verify(r => r.CreateAsync(It.IsAny<string>(), It.IsAny<ExecutionTrigger>(), It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Trigger_creates_a_manual_execution()
    {
        _scenarios.Setup(r => r.GetByIdAsync("token-expired", It.IsAny<CancellationToken>())).ReturnsAsync(Existing("token-expired"));
        _executions.Setup(r => r.HasActiveAsync("token-expired", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _executions
            .Setup(r => r.CreateAsync("token-expired", ExecutionTrigger.Manual, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScenarioExecution
            {
                Id = "abc", ScenarioId = "token-expired", TriggeredBy = ExecutionTrigger.Manual,
                Status = ExecutionStatus.Pending, RequestedAt = DateTime.UtcNow
            });

        var execution = await _sut.TriggerAsync("token-expired", CancellationToken.None);

        execution.Status.Should().Be(ExecutionStatus.Pending);
        execution.TriggeredBy.Should().Be(ExecutionTrigger.Manual);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, ScenarioService.MaxPageSize + 1)]
    public async Task GetExecutions_rejects_invalid_paging(int page, int pageSize)
    {
        var act = () => _sut.GetExecutionsAsync(null, page, pageSize, CancellationToken.None);

        await act.Should().ThrowAsync<RequestValidationException>();
    }
}
