using AwesomeAssertions;
using LogPilot.Domain.Scenarios;
using LogPilot.LogGenerator.Config;
using LogPilot.LogGenerator.Engine;

namespace LogPilot.UnitTests.Generator;

/// Gerçek scenarios/*.json ve seed/users.json dosyalarını doğrular: bir typo yüzünden generator açılışta patlamasın.
public class ScenarioFilesTests
{
    private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void All_scenario_files_are_valid()
    {
        var scenarios = SeedFileLoader.LoadScenarios(RepoPaths.FindDirectory("scenarios"));

        scenarios.Should().NotBeEmpty();
        scenarios.Select(s => s.Id).Should().OnlyHaveUniqueItems();
        scenarios.Count(s => s.Kind == ScenarioKind.Incident).Should().BeGreaterThanOrEqualTo(8);
        scenarios.Count(s => s.Kind == ScenarioKind.Normal).Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Every_scenario_generates_logs_with_the_seed_users()
    {
        var scenarios = SeedFileLoader.LoadScenarios(RepoPaths.FindDirectory("scenarios"));
        var users = SeedFileLoader.LoadUsers(Path.Combine(RepoPaths.FindDirectory("seed"), "users.json"));
        var engine = new ScenarioEngine();

        foreach (var scenario in scenarios)
        {
            var run = engine.Generate(scenario, users, seed: 1, Now);

            run.TotalCount.Should().BePositive($"scenario '{scenario.Id}' should produce logs");
            run.Logs.Should().OnlyContain(l => l.Timestamp <= Now);
            run.Quotes.Should().OnlyContain(q => q.Timestamp <= Now);
            if (scenario.Steps.Any(s => s.Target == StepTarget.Quote))
                run.Quotes.Should().NotBeEmpty($"quote scenario '{scenario.Id}' should write quote_logs");
        }
    }
}
