using AwesomeAssertions;
using LogPilot.Domain.Logs;
using LogPilot.Domain.Scenarios;
using LogPilot.Domain.Users;
using LogPilot.LogGenerator.Engine;

namespace LogPilot.UnitTests.Generator;

public class ScenarioEngineTests
{
    private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);
    private readonly ScenarioEngine _sut = new();

    private static User U(string code, UserStatus status = UserStatus.Active)
        => new() { UserCode = code, UserId = $"USR-{code}", Status = status };

    private static ScenarioStep Step(int offset, LogSeverity level = LogSeverity.Information,
        double probability = 1.0, string message = "Hello {userCode}", string? errorCode = null)
        => new()
        {
            OffsetMs = offset, Application = "MobileApp", Service = "AuthenticationService", Level = level,
            Operation = "Login", Message = message, ErrorCode = errorCode, Probability = probability
        };

    private static Scenario Build(IEnumerable<ScenarioStep> steps, int min = 1, int max = 1, int spacingMs = 1000,
        UserStatus status = UserStatus.Active, List<string>? userCodes = null)
        => new()
        {
            Id = "test", Name = "Test", Kind = ScenarioKind.Incident,
            Users = new UserSelection
            {
                Status = status, MinCount = min, MaxCount = max, SpacingMs = spacingMs, UserCodes = userCodes ?? []
            },
            Settings = new ScenarioSettings(),
            Steps = steps.ToList()
        };

    private static readonly List<User> Users =
        [U("AAA"), U("BBB"), U("CCC"), U("DDD"), U("OLD1", UserStatus.Inactive)];

    [Fact]
    public void Same_seed_produces_identical_logs()
    {
        var scenario = Build([Step(0), Step(100)], min: 2, max: 3);

        var first = _sut.Generate(scenario, Users, seed: 42, Now);
        var second = _sut.Generate(scenario, Users, seed: 42, Now);

        first.Logs.Select(l => (l.Timestamp, l.UserCode, l.CorrelationId, l.TransactionId, l.Message))
            .Should().Equal(second.Logs.Select(l => (l.Timestamp, l.UserCode, l.CorrelationId, l.TransactionId, l.Message)));
    }

    [Fact]
    public void Each_user_flow_has_its_own_correlation_and_transaction_id()
    {
        var scenario = Build([Step(0), Step(50), Step(100)], min: 3, max: 3);

        var run = _sut.Generate(scenario, Users, seed: 1, Now);

        var byUser = run.Logs.GroupBy(l => l.UserCode).ToList();
        byUser.Should().HaveCount(3);
        byUser.Should().OnlyContain(g => g.Select(l => l.CorrelationId).Distinct().Count() == 1);
        byUser.Should().OnlyContain(g => g.Select(l => l.TransactionId).Distinct().Count() == 1);
        run.CorrelationIds.Should().HaveCount(3).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void No_timestamp_is_in_the_future()
    {
        var scenario = Build([Step(0), Step(8000)], min: 4, max: 4, spacingMs: 3000);

        var run = _sut.Generate(scenario, Users, seed: 7, Now);

        run.Logs.Should().OnlyContain(l => l.Timestamp <= Now);
        run.Logs.Max(l => l.Timestamp).Should().Be(Now);   // akış tam "şimdi"de biter
    }

    [Fact]
    public void Only_users_with_the_requested_status_are_selected()
    {
        var scenario = Build([Step(0)], min: 1, max: 1, status: UserStatus.Inactive);

        var run = _sut.Generate(scenario, Users, seed: 3, Now);

        run.Logs.Should().OnlyContain(l => l.UserCode == "OLD1");
    }

    [Fact]
    public void UserCodes_restrict_the_pool()
    {
        var scenario = Build([Step(0)], min: 1, max: 1, userCodes: ["BBB"]);

        for (var seed = 0; seed < 10; seed++)
            _sut.Generate(scenario, Users, seed, Now).Logs.Should().OnlyContain(l => l.UserCode == "BBB");
    }

    [Fact]
    public void Selected_user_count_stays_within_bounds()
    {
        var scenario = Build([Step(0)], min: 2, max: 3);

        for (var seed = 0; seed < 20; seed++)
        {
            var count = _sut.Generate(scenario, Users, seed, Now).CorrelationIds.Count;
            count.Should().BeInRange(2, 3);
        }
    }

    [Fact]
    public void Placeholders_are_rendered()
    {
        var scenario = Build([Step(0, message: "Login failed for {userCode} ({correlationId})")]);

        var log = _sut.Generate(scenario, Users, seed: 5, Now).Logs.Single();

        log.Message.Should().Contain(log.UserCode!).And.Contain(log.CorrelationId!).And.NotContain("{");
    }

    [Fact]
    public void Zero_probability_step_is_omitted()
    {
        var scenario = Build([Step(0, message: "always"), Step(10, probability: 0.0, message: "never")]);

        var run = _sut.Generate(scenario, Users, seed: 9, Now);

        run.Logs.Should().OnlyContain(l => l.Message == "always");
    }

    [Fact]
    public void Throws_when_no_user_matches()
    {
        var scenario = Build([Step(0)], userCodes: ["NOPE"]);

        var act = () => _sut.Generate(scenario, Users, seed: 1, Now);

        act.Should().Throw<InvalidOperationException>();
    }
}
