using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using LogPilot.AI;
using LogPilot.AI.Providers;
using LogPilot.AI.Providers.Claude;
using Anthropic.Models.Beta.Messages;

namespace LogPilot.UnitTests.Ai;

/// Claude'a giden istek gövdesinin biçimi (ağ çağrısı yok).
public class ClaudeProviderTests
{
    private static readonly ClaudeOptions Options = new() { ApiKey = "test-key" };

    private static readonly LlmToolDefinition[] Tools =
    [
        new("SearchLogs", "Search logs.",
            """{"type":"object","additionalProperties":false,"properties":{"userCode":{"type":"string"},"limit":{"type":"integer","minimum":1,"maximum":50}}}"""),
        new("GetIncidentDetails", "Incident details.",
            """{"type":"object","additionalProperties":false,"required":["incidentId"],"properties":{"incidentId":{"type":"string"}}}""")
    ];

    private static JsonNode Body(LlmRequest request, ClaudeOptions? options = null)
    {
        var parameters = ClaudeLlmProvider.BuildParameters(request, options ?? Options);
        // SDK'nın ağa göndereceği gövde (snake_case alan adlarıyla).
        return JsonNode.Parse(JsonSerializer.Serialize(parameters.RawBodyData))!;
    }

    [Fact]
    public void Request_uses_configured_model_effort_and_tools()
    {
        var body = Body(new LlmRequest("system rules", [new LlmMessage(LlmRole.User, "Why can't TCLI log in?")], Tools));

        body["model"]!.GetValue<string>().Should().Be("claude-opus-5-5");
        body["output_config"]!["effort"]!.GetValue<string>().Should().Be("medium");
        body["system"]![0]!["text"]!.GetValue<string>().Should().Be("system rules");

        var tools = body["tools"]!.AsArray();
        tools.Select(t => t!["name"]!.GetValue<string>()).Should().Equal("SearchLogs", "GetIncidentDetails");
        tools[1]!["input_schema"]!["required"]![0]!.GetValue<string>().Should().Be("incidentId");
        tools[0]!["input_schema"]!["properties"]!["limit"]!["maximum"]!.GetValue<int>().Should().Be(50);
    }

    [Fact]
    public void Only_the_last_tool_and_the_conversation_are_cache_marked()
    {
        var body = Body(new LlmRequest("s", [new LlmMessage(LlmRole.User, "q")], Tools));

        body["tools"]![0]!["cache_control"].Should().BeNull();
        body["tools"]![1]!["cache_control"]!["type"]!.GetValue<string>().Should().Be("ephemeral");
        body["cache_control"]!["type"]!.GetValue<string>().Should().Be("ephemeral");
    }

    [Fact]
    public void Refusal_fallback_is_on_by_default_and_can_be_disabled()
    {
        var request = new LlmRequest("s", [new LlmMessage(LlmRole.User, "q")], []);

        Body(request)["fallbacks"]!.GetValue<string>().Should().Be("default");
        Body(request, new ClaudeOptions { ApiKey = "k", UseRefusalFallback = false })["fallbacks"].Should().BeNull();
    }

    [Fact]
    public void No_tools_field_when_no_tools_are_allowed()
    {
        var body = Body(new LlmRequest("s", [new LlmMessage(LlmRole.User, "q")], []));
        body["tools"].Should().BeNull();
    }

    [Fact]
    public void Incident_summary_uses_summary_effort_and_a_small_token_limit()
    {
        var body = Body(new LlmRequest("s", [new LlmMessage(LlmRole.User, "facts")], [], LlmPurpose.IncidentSummary));

        body["output_config"]!["effort"]!.GetValue<string>().Should().Be("low");
        body["max_tokens"]!.GetValue<int>().Should().Be(1024);
    }

    [Fact]
    public void Consecutive_tool_results_are_sent_in_one_user_message_after_the_tool_calls()
    {
        var calls = new List<LlmToolCall>
        {
            new("toolu_1", "SearchLogs", """{"userCode":"TCLI"}"""),
            new("toolu_2", "GetIncidentDetails", """{"incidentId":"abc"}""")
        };
        var messages = new List<LlmMessage>
        {
            new(LlmRole.User, "Why can't TCLI log in?"),
            new(LlmRole.Assistant, "", ToolCalls: calls),
            new(LlmRole.Tool, "<untrusted_log_data>[]</untrusted_log_data>", "toolu_1", "SearchLogs"),
            new(LlmRole.Tool, "Tool error: incident not found", "toolu_2", "GetIncidentDetails"),
        };

        var sent = Body(new LlmRequest("s", messages, Tools))["messages"]!.AsArray();

        sent.Should().HaveCount(3);
        sent[1]!["role"]!.GetValue<string>().Should().Be("assistant");
        var toolUses = sent[1]!["content"]!.AsArray();
        toolUses.Select(b => b!["type"]!.GetValue<string>()).Should().Equal("tool_use", "tool_use");
        toolUses[0]!["input"]!["userCode"]!.GetValue<string>().Should().Be("TCLI");

        sent[2]!["role"]!.GetValue<string>().Should().Be("user");
        var results = sent[2]!["content"]!.AsArray();
        results.Select(b => b!["tool_use_id"]!.GetValue<string>()).Should().Equal("toolu_1", "toolu_2");
        results[0]!["is_error"]!.GetValue<bool>().Should().BeFalse();
        results[1]!["is_error"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void Claude_turn_state_is_echoed_unchanged_including_thinking_blocks()
    {
        var state = new ClaudeTurnState(
        [
            new BetaThinkingBlockParam { Thinking = "", Signature = "sig-123" },
            new BetaToolUseBlockParam { ID = "toolu_9", Name = "SearchLogs", Input = ClaudeMessageMapper.ParseArguments("""{"limit":5}""") }
        ]);
        var messages = new List<LlmMessage>
        {
            new(LlmRole.User, "q"),
            new(LlmRole.Assistant, "", ToolCalls: [new("toolu_9", "SearchLogs", """{"limit":5}""")], ProviderState: state),
            new(LlmRole.Tool, "[]", "toolu_9", "SearchLogs"),
        };

        var assistant = Body(new LlmRequest("s", messages, Tools))["messages"]![1]!["content"]!.AsArray();

        assistant[0]!["type"]!.GetValue<string>().Should().Be("thinking");
        assistant[0]!["signature"]!.GetValue<string>().Should().Be("sig-123");
        assistant[1]!["id"]!.GetValue<string>().Should().Be("toolu_9");
    }

    [Fact]
    public void Chat_history_text_turns_map_to_plain_messages()
    {
        var sent = Body(new LlmRequest("s",
        [
            new LlmMessage(LlmRole.User, "first question"),
            new LlmMessage(LlmRole.Assistant, "first answer summary"),
            new LlmMessage(LlmRole.User, "follow-up")
        ], []))["messages"]!.AsArray();

        sent.Select(m => m!["role"]!.GetValue<string>()).Should().Equal("user", "assistant", "user");
        sent[1]!["content"]!.GetValue<string>().Should().Be("first answer summary");
    }

    [Theory]
    [InlineData("medium", true)]
    [InlineData("xhigh", true)]
    [InlineData("extreme", false)]
    public void Options_validation_checks_effort(string effort, bool valid)
    {
        new ClaudeOptions { ApiKey = "k", ChatEffort = effort }.IsValid().Should().Be(valid);
    }

    [Fact]
    public void Options_are_invalid_without_an_api_key()
    {
        var previous = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
            new ClaudeOptions().IsValid().Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", previous);
        }
    }
}
