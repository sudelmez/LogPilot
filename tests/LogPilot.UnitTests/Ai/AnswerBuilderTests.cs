using AwesomeAssertions;
using LogPilot.AI.Orchestration;
using LogPilot.Application.Ai;
using LogPilot.Domain.Ai;
using static LogPilot.UnitTests.Ai.TestData;

namespace LogPilot.UnitTests.Ai;

public class AnswerBuilderTests
{
    private readonly SensitiveDataMasker _masker = new();

    private LogPilot.Domain.Chat.AssistantAnswer Build(LlmAnswer llm, params LogPilot.AI.Tools.ToolInvocation[] invocations)
        => AnswerBuilder.Build(llm, invocations, Context, _masker);

    [Fact]
    public void Fabricated_evidence_ids_are_removed()
    {
        var invocation = Invocation(Snippet("aaa111"));
        var llm = new LlmAnswer { Summary = "TCLI failed.", EvidenceLogIds = ["aaa111", "fabricated999"] };

        var answer = Build(llm, invocation);

        answer.EvidenceLogIds.Should().Equal("aaa111");
        answer.DataLimitations.Should().Contain(l => l.Contains("could not be verified"));
    }

    [Fact]
    public void A_cause_not_supported_by_cited_logs_is_dropped()
    {
        var invocation = Invocation(Snippet("aaa111", errorCode: "TOKEN_EXPIRED"));
        var llm = new LlmAnswer
        {
            Summary = "Something failed.", ProbableCause = "MongoDB is down", ProbableCauseCode = "MONGO_CONNECTION_FAILED",
            EvidenceLogIds = ["aaa111"]
        };

        var answer = Build(llm, invocation);

        answer.ProbableCause.Should().BeNull();
        answer.Confidence.Should().NotBe(ConfidenceLevel.High);
        answer.DataLimitations.Should().Contain(l => l.Contains("not supported by any cited log"));
    }

    [Fact]
    public void Confidence_comes_from_evidence_not_from_the_model()
    {
        var invocation = Invocation(
            Snippet("a1", service: "AuthenticationService", correlation: "COR-1"),
            Snippet("a2", level: "Error", service: "MobileGateway", errorCode: "LOGIN_FAILED", correlation: "COR-1", minute: 1));
        var llm = new LlmAnswer
        {
            Summary = "Token expired.", ProbableCause = "Token expired", ProbableCauseCode = "TOKEN_EXPIRED",
            EvidenceLogIds = ["a1", "a2"], Confidence = "High"      // model "High" diyor
        };

        var answer = Build(llm, invocation);

        answer.Confidence.Should().Be(ConfidenceLevel.Medium);      // tek kullanıcı, tek olay
        answer.ConfidenceScore.Should().Be(65);
        answer.ConfidenceFactors.Should().NotBeEmpty();
    }

    [Fact]
    public void Corroborated_evidence_reaches_high_confidence()
    {
        var invocation = Invocation(
            Snippet("a1", user: "TCLI", correlation: "COR-1"), Snippet("g1", service: "MobileGateway", user: "TCLI", correlation: "COR-1", errorCode: "LOGIN_FAILED"),
            Snippet("a2", user: "ALEX", correlation: "COR-2", minute: 1), Snippet("a3", user: "BOB", correlation: "COR-3", minute: 2));
        var llm = new LlmAnswer
        {
            Summary = "Token expired for several users.", ProbableCause = "Token expired", ProbableCauseCode = "TOKEN_EXPIRED",
            EvidenceLogIds = ["a1", "g1", "a2", "a3"]
        };

        var answer = Build(llm, invocation);

        answer.Confidence.Should().Be(ConfidenceLevel.High);
    }

    [Fact]
    public void Unsupported_resolved_claims_are_removed()
    {
        var invocation = Invocation(Snippet("a1"));
        var llm = new LlmAnswer
        {
            Summary = "TCLI hit TOKEN_EXPIRED. The issue is resolved now.",
            ProbableCause = "Token expired. It has been fixed.", ProbableCauseCode = "TOKEN_EXPIRED", EvidenceLogIds = ["a1"]
        };

        var answer = Build(llm, invocation);

        answer.Summary.Should().Contain("TOKEN_EXPIRED").And.NotContain("resolved");
        answer.ProbableCause.Should().NotContain("fixed");
        answer.DataLimitations.Should().Contain(l => l.Contains("do not confirm that the issue is resolved"));
    }

    [Fact]
    public void Resolution_is_allowed_when_a_later_success_exists()
    {
        var invocation = Invocation(
            Snippet("a1", minute: 0),
            Snippet("s1", level: "Information", errorCode: null, outcome: "Success", minute: 5, message: "User TCLI logged in successfully."));
        var llm = new LlmAnswer
        {
            Summary = "TCLI hit TOKEN_EXPIRED, then logged in successfully. The problem appears resolved.",
            ProbableCause = "Token expired", ProbableCauseCode = "TOKEN_EXPIRED", EvidenceLogIds = ["a1"]
        };

        var answer = Build(llm, invocation);

        answer.Summary.Should().Contain("resolved");
        answer.ConfidenceFactors.Should().Contain(f => f.Contains("later success"));
    }

    [Fact]
    public void Negated_statements_about_resolution_are_kept()
    {
        var invocation = Invocation(Snippet("a1"));
        var llm = new LlmAnswer
        {
            Summary = "The logs cannot confirm that the issue is resolved.",
            ProbableCause = "Token expired", ProbableCauseCode = "TOKEN_EXPIRED", EvidenceLogIds = ["a1"]
        };

        Build(llm, invocation).Summary.Should().Contain("cannot confirm");
    }

    [Fact]
    public void Affected_services_are_limited_to_services_the_tools_actually_saw()
    {
        var invocation = Invocation(Snippet("a1", service: "AuthenticationService"));
        var llm = new LlmAnswer { Summary = "x", AffectedServices = ["AuthenticationService", "ImaginaryService"] };

        Build(llm, invocation).AffectedServices.Should().Equal("AuthenticationService");
    }

    [Fact]
    public void No_data_yields_insufficient_confidence_and_says_so()
    {
        var answer = Build(new LlmAnswer { Summary = "Nothing found." }, Invocation());

        answer.Confidence.Should().Be(ConfidenceLevel.Insufficient);
        answer.DataLimitations.Should().Contain(l => l.Contains("No matching logs"));
    }

    [Fact]
    public void A_clarifying_question_is_passed_through_without_confidence()
    {
        var answer = Build(new LlmAnswer { Summary = "Need more detail.", ClarifyingQuestion = "Which user?" });

        answer.ClarifyingQuestion.Should().Be("Which user?");
        answer.Confidence.Should().Be(ConfidenceLevel.NotApplicable);
    }

    [Fact]
    public void Secrets_in_the_answer_are_masked()
    {
        var answer = Build(new LlmAnswer { Summary = "Mail admin@example.com with password=secret123", SuggestedActions = ["Email ops@example.com"] }, Invocation(Snippet("a1")));

        answer.Summary.Should().NotContain("admin@example.com").And.NotContain("secret123");
        answer.SuggestedActions.Should().OnlyContain(a => !a.Contains("@example.com"));
    }

    [Fact]
    public void Flagged_log_content_is_reported_as_a_limitation()
    {
        var flagged = Invocation(Snippet("a1")) with { InjectionDetections = 2 };

        var answer = Build(new LlmAnswer { Summary = "x" }, flagged);

        answer.DataLimitations.Should().Contain(l => l.Contains("looked like instructions"));
    }

    [Fact]
    public void The_analysed_time_range_is_always_reported()
        => Build(new LlmAnswer { Summary = "x" }).DataLimitations.Should().Contain(l => l.StartsWith("Time range analysed"));
}
