using AwesomeAssertions;
using LogPilot.Application.Common;
using LogPilot.Application.Scenarios;
using LogPilot.Domain.Scenarios;
using Moq;

namespace LogPilot.UnitTests.Generator;

public class ScenarioSettingsTests
{
    private readonly Mock<IScenarioRepository> _scenarios = new();
    private readonly ScenarioService _sut;

    public ScenarioSettingsTests()
        => _sut = new ScenarioService(_scenarios.Object, new Mock<IScenarioExecutionRepository>().Object, TimeProvider.System);

    private static Scenario Existing() => new()
    {
        Id = "token-expired", Name = "Token expired", Kind = ScenarioKind.Incident,
        Users = new UserSelection(), Settings = new ScenarioSettings { IntervalSeconds = 60 }, Steps = []
    };

    [Theory]
    [InlineData(-1, 0.5, 1)]
    [InlineData(ScenarioService.MaxIntervalSeconds + 1, 0.5, 1)]
    [InlineData(60, -0.1, 1)]
    [InlineData(60, 1.1, 1)]
    [InlineData(60, double.NaN, 1)]
    [InlineData(60, 0.5, -1)]
    [InlineData(60, 0.5, ScenarioService.MaxWeight + 1)]
    public async Task Update_rejects_out_of_range_values(int interval, double probability, int weight)
    {
        var act = () => _sut.UpdateSettingsAsync("token-expired", new ScenarioSettingsRequest(true, interval, probability, weight), CancellationToken.None);

        await act.Should().ThrowAsync<RequestValidationException>();
        _scenarios.Verify(r => r.UpdateSettingsAsync(It.IsAny<string>(), It.IsAny<ScenarioSettings>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_throws_not_found_for_unknown_scenario()
    {
        _scenarios.Setup(r => r.UpdateSettingsAsync("nope", It.IsAny<ScenarioSettings>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var act = () => _sut.UpdateSettingsAsync("nope", new ScenarioSettingsRequest(true, 60, 0.5, 1), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Update_persists_new_settings_and_returns_the_summary()
    {
        _scenarios.Setup(r => r.UpdateSettingsAsync("token-expired", It.IsAny<ScenarioSettings>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _scenarios.Setup(r => r.GetByIdAsync("token-expired", It.IsAny<CancellationToken>())).ReturnsAsync(Existing());

        var summary = await _sut.UpdateSettingsAsync("token-expired", new ScenarioSettingsRequest(false, 300, 0.25, 1), CancellationToken.None);

        summary.Id.Should().Be("token-expired");
        _scenarios.Verify(r => r.UpdateSettingsAsync("token-expired",
            It.Is<ScenarioSettings>(s => !s.Enabled && s.IntervalSeconds == 300 && s.Probability == 0.25),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
