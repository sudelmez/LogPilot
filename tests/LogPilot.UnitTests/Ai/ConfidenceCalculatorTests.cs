using AwesomeAssertions;
using LogPilot.Domain.Ai;

namespace LogPilot.UnitTests.Ai;

public class ConfidenceCalculatorTests
{
    private static ConfidenceInput Input(
        int count = 1, bool specific = true, bool chain = true, int users = 1, double share = 1.0,
        bool laterSuccess = false, bool truncated = false)
        => new(count, specific, chain, users, share, laterSuccess, truncated);

    [Fact]
    public void No_evidence_means_insufficient()
    {
        var result = ConfidenceCalculator.Assess(Input(count: 0));

        result.Level.Should().Be(ConfidenceLevel.Insufficient);
        result.Score.Should().Be(0);
    }

    [Fact]
    public void A_single_specific_occurrence_is_only_medium()
    {
        var result = ConfidenceCalculator.Assess(Input());   // 5 + 30 + 20 + 10 = 65

        result.Level.Should().Be(ConfidenceLevel.Medium);
        result.Score.Should().Be(65);
    }

    [Fact]
    public void Corroboration_by_several_users_makes_it_high()
    {
        var result = ConfidenceCalculator.Assess(Input(count: 3, users: 3));   // 15 + 30 + 20 + 10 + 10 = 85

        result.Level.Should().Be(ConfidenceLevel.High);
        result.Score.Should().Be(85);
    }

    [Fact]
    public void A_generic_symptom_code_never_reaches_high()
    {
        var result = ConfidenceCalculator.Assess(Input(count: 10, specific: false, users: 5));

        result.Level.Should().NotBe(ConfidenceLevel.High);
    }

    [Fact]
    public void A_later_success_lowers_the_score()
    {
        var without = ConfidenceCalculator.Assess(Input(count: 3, users: 3));
        var with = ConfidenceCalculator.Assess(Input(count: 3, users: 3, laterSuccess: true));

        (without.Score - with.Score).Should().Be(25);
        with.Factors.Should().Contain(f => f.Contains("later success"));
    }

    [Fact]
    public void Mixed_evidence_and_truncation_are_penalised()
    {
        var clean = ConfidenceCalculator.Assess(Input(count: 3, share: 0.9));
        var mixed = ConfidenceCalculator.Assess(Input(count: 3, share: 0.4, truncated: true));

        mixed.Score.Should().BeLessThan(clean.Score);
    }

    [Fact]
    public void Score_is_at_least_one_when_any_evidence_exists()
    {
        var result = ConfidenceCalculator.Assess(Input(count: 1, specific: false, chain: false, share: 0.1, laterSuccess: true, truncated: true));

        result.Score.Should().Be(1);
        result.Level.Should().Be(ConfidenceLevel.Low);
    }

    [Fact]
    public void Every_assessment_explains_itself()
    {
        ConfidenceCalculator.Assess(Input(count: 3, users: 2)).Factors.Should().NotBeEmpty();
    }
}
