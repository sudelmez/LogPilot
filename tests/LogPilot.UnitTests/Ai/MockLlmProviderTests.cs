using System.Text.Json;
using AwesomeAssertions;
using LogPilot.AI.Orchestration;
using LogPilot.AI.Providers;
using LogPilot.Application.Ai;
using static LogPilot.UnitTests.Ai.TestData;

namespace LogPilot.UnitTests.Ai;

public class MockLlmProviderTests
{
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private readonly MockLlmProvider _sut = new(new FixedTime(new DateTimeOffset(To)));
    private static readonly IReadOnlyList<LlmToolDefinition> SomeTools = [new("SearchLogs", "d", "{}")];

    private Task<LlmResponse> Ask(string question, params LlmMessage[] followUp)
        => _sut.CompleteAsync(new LlmRequest("system", [new LlmMessage(LlmRole.User, question), .. followUp], SomeTools), CancellationToken.None);

    [Fact]
    public async Task A_login_question_about_a_user_plans_a_user_lookup()
    {
        var response = await Ask("Why can't TCLI log in to the mobile application?");

        response.ToolCalls.Should().ContainSingle(c => c.Name == ToolNames.GetUserRecentErrors);
        response.ToolCalls[0].ArgumentsJson.Should().Contain("TCLI");
    }

    [Fact]
    public async Task A_correlation_id_plans_a_related_logs_lookup()
    {
        var response = await Ask("Which errors are related to correlation ID COR-20261108-001ABC?");

        response.ToolCalls.Should().Contain(c => c.Name == ToolNames.FindRelatedLogs && c.ArgumentsJson.Contains("COR-20261108-001ABC"));
    }

    [Theory]
    [InlineData("Which service has the highest error rate today?", ToolNames.GetServiceHealth)]
    [InlineData("Are multiple users affected by the same error?", ToolNames.GetErrorStatistics)]
    [InlineData("Is there an active MongoDB-related incident?", ToolNames.GetActiveIncidents)]
    [InlineData("What errors occurred in the last 15 minutes?", ToolNames.SearchLogs)]
    [InlineData("Why are proposal creation requests failing?", ToolNames.SearchLogs)]
    [InlineData("Summarize today's critical incidents.", ToolNames.GetActiveIncidents)]
    public async Task Known_question_shapes_plan_the_expected_tool(string question, string tool)
    {
        var response = await Ask(question);

        response.ToolCalls.Should().Contain(c => c.Name == tool);
    }

    [Fact]
    public async Task A_vague_question_gets_a_clarifying_question_and_no_tools()
    {
        var response = await Ask("hello?");

        response.ToolCalls.Should().BeEmpty();
        AnswerParser.Parse(response.Text)!.ClarifyingQuestion.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task After_the_user_lookup_it_follows_up_with_the_newest_error_flow()
    {
        var first = await Ask("Why can't TCLI log in?");
        var lookup = new LogToolResult([Snippet("a1", correlation: "COR-20261108-AAAAAA")], 1, false, "scope");
        var toolMessage = new LlmMessage(LlmRole.Tool, Wrap(ToolNames.GetUserRecentErrors, lookup), "call_1", ToolNames.GetUserRecentErrors);
        var assistant = new LlmMessage(LlmRole.Assistant, "", ToolCalls: first.ToolCalls);

        var second = await Ask("Why can't TCLI log in?", assistant, toolMessage);

        second.ToolCalls.Should().ContainSingle(c => c.Name == ToolNames.FindRelatedLogs && c.ArgumentsJson.Contains("COR-20261108-AAAAAA"));
    }

    [Fact]
    public async Task The_final_answer_names_the_cause_and_cites_real_log_ids()
    {
        var logs = new LogToolResult(
        [
            Snippet("a1", correlation: "COR-1"),
            Snippet("g1", service: "MobileGateway", errorCode: "LOGIN_FAILED", correlation: "COR-1", minute: 1)
        ], 2, false, "scope");
        var first = await Ask("Why can't TCLI log in?");
        var messages = new[]
        {
            new LlmMessage(LlmRole.Assistant, "", ToolCalls: first.ToolCalls),
            new LlmMessage(LlmRole.Tool, Wrap(ToolNames.GetUserRecentErrors, logs), "call_1", ToolNames.GetUserRecentErrors),
            new LlmMessage(LlmRole.Tool, Wrap(ToolNames.FindRelatedLogs, logs), "call_2", ToolNames.FindRelatedLogs)
        };

        // Araç bütçesi bitmiş gibi: araçsız çağrı → nihai cevap
        var response = await _sut.CompleteAsync(new LlmRequest("system", [new LlmMessage(LlmRole.User, "Why can't TCLI log in?"), .. messages], []), CancellationToken.None);

        var answer = AnswerParser.Parse(response.Text)!;
        answer.ProbableCauseCode.Should().Be("TOKEN_EXPIRED");   // semptom (LOGIN_FAILED) değil, kök neden
        answer.EvidenceLogIds.Should().Contain("a1");
        answer.EvidenceLogIds.Should().OnlyContain(id => id == "a1" || id == "g1");
        answer.SuggestedActions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Incident_summaries_never_claim_resolution()
    {
        var facts = new IncidentFacts("MONGO_CONNECTION_FAILED", "Critical", 12, ["A", "B", "C"], ["AuthenticationService", "UserService"], From, To);
        var request = new LlmRequest("s", [new LlmMessage(LlmRole.User, JsonSerializer.Serialize(facts, ToolJson.Options))], [], LlmPurpose.IncidentSummary);

        var response = await _sut.CompleteAsync(request, CancellationToken.None);

        response.Text.Should().Contain("MONGO_CONNECTION_FAILED").And.Contain("3 user(s)").And.Contain("does not confirm");
    }

    private static string Wrap(string tool, object result)
        => $"<untrusted_log_data tool=\"{tool}\">\n{JsonSerializer.Serialize(result, ToolJson.Options)}\n</untrusted_log_data>";
}
