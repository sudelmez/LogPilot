using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using LogPilot.AI;
using LogPilot.AI.Providers;
using LogPilot.AI.Providers.Ollama;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LogPilot.UnitTests.Ai;

/// Ollama /api/chat istek/cevap biçimi ve hata eşlemesi (gerçek Ollama gerekmez).
public class OllamaProviderTests
{
    private static readonly OllamaOptions Options = new();

    private static readonly LlmToolDefinition[] Tools =
    [
        new("SearchLogs", "Search logs.",
            """{"type":"object","additionalProperties":false,"properties":{"userCode":{"type":"string"}}}""")
    ];

    [Fact]
    public void Request_contains_system_prompt_model_context_and_tools()
    {
        var body = OllamaLlmProvider.BuildRequest(
            new LlmRequest("rules", [new LlmMessage(LlmRole.User, "Why can't TCLI log in?")], Tools), Options);

        body["model"]!.GetValue<string>().Should().Be("qwen3:8b");
        body["stream"]!.GetValue<bool>().Should().BeFalse();
        body["think"]!.GetValue<bool>().Should().BeFalse();
        body["options"]!["num_ctx"]!.GetValue<int>().Should().Be(16384);

        var messages = body["messages"]!.AsArray();
        messages[0]!["role"]!.GetValue<string>().Should().Be("system");
        messages[0]!["content"]!.GetValue<string>().Should().Be("rules");
        messages[1]!["role"]!.GetValue<string>().Should().Be("user");

        var tool = body["tools"]![0]!;
        tool["type"]!.GetValue<string>().Should().Be("function");
        tool["function"]!["name"]!.GetValue<string>().Should().Be("SearchLogs");
        tool["function"]!["parameters"]!["properties"]!["userCode"]!["type"]!.GetValue<string>().Should().Be("string");
        body["format"].Should().BeNull();
    }

    [Fact]
    public void Final_chat_turn_without_tools_asks_for_json()
    {
        var body = OllamaLlmProvider.BuildRequest(new LlmRequest("s", [new LlmMessage(LlmRole.User, "q")], []), Options);
        body["format"]!.GetValue<string>().Should().Be("json");
        body["tools"].Should().BeNull();
    }

    [Fact]
    public void Incident_summary_is_plain_text()
    {
        var body = OllamaLlmProvider.BuildRequest(
            new LlmRequest("s", [new LlmMessage(LlmRole.User, "facts")], [], LlmPurpose.IncidentSummary), Options);
        body["format"].Should().BeNull();
    }

    [Fact]
    public void Tool_calls_and_results_are_sent_back_in_ollama_format()
    {
        var messages = new List<LlmMessage>
        {
            new(LlmRole.User, "q"),
            new(LlmRole.Assistant, "", ToolCalls: [new("call_1", "SearchLogs", """{"userCode":"TCLI"}""")]),
            new(LlmRole.Tool, "<untrusted_log_data>[]</untrusted_log_data>", "call_1", "SearchLogs"),
        };

        var sent = OllamaLlmProvider.BuildRequest(new LlmRequest("s", messages, Tools), Options)["messages"]!.AsArray();

        sent[2]!["tool_calls"]![0]!["function"]!["name"]!.GetValue<string>().Should().Be("SearchLogs");
        sent[2]!["tool_calls"]![0]!["function"]!["arguments"]!["userCode"]!.GetValue<string>().Should().Be("TCLI");
        sent[3]!["role"]!.GetValue<string>().Should().Be("tool");
        sent[3]!["tool_name"]!.GetValue<string>().Should().Be("SearchLogs");
    }

    [Fact]
    public void Response_tool_calls_get_ids_and_json_arguments()
    {
        var json = JsonNode.Parse("""
            {"message":{"role":"assistant","content":"","tool_calls":[
              {"function":{"name":"SearchLogs","arguments":{"userCode":"TCLI","limit":5}}},
              {"function":{"name":"GetServiceHealth","arguments":"{\"service\":\"AuthenticationService\"}"}}
            ]},"done":true}
            """)!;
        var n = 0;

        var response = OllamaLlmProvider.ParseResponse(json, () => $"call_{++n}");

        response.Text.Should().BeNull();
        response.ToolCalls.Select(c => c.Id).Should().Equal("call_1", "call_2");
        JsonNode.Parse(response.ToolCalls[0].ArgumentsJson)!["limit"]!.GetValue<int>().Should().Be(5);
        JsonNode.Parse(response.ToolCalls[1].ArgumentsJson)!["service"]!.GetValue<string>().Should().Be("AuthenticationService");
    }

    [Fact]
    public void Think_blocks_are_removed_from_the_answer_text()
    {
        var json = JsonNode.Parse("""{"message":{"role":"assistant","content":"<think>internal notes</think>\n{\"summary\":\"ok\"}"}}""")!;
        OllamaLlmProvider.ParseResponse(json, () => "x").Text.Should().Be("""{"summary":"ok"}""");
    }

    [Fact]
    public async Task Successful_call_returns_the_model_answer()
    {
        var handler = new FakeHandler(HttpStatusCode.OK,
            """{"message":{"role":"assistant","content":"{\"summary\":\"done\"}"},"done":true,"prompt_eval_count":120,"eval_count":30}""");
        using var provider = Provider(handler);

        var response = await provider.CompleteAsync(new LlmRequest("s", [new LlmMessage(LlmRole.User, "q")], []), CancellationToken.None);

        response.Text.Should().Be("""{"summary":"done"}""");
        handler.LastRequestUri!.AbsolutePath.Should().Be("/api/chat");
        provider.Name.Should().Be("ollama:qwen3:8b");
    }

    [Fact]
    public async Task Missing_model_tells_the_user_how_to_install_it()
    {
        using var provider = Provider(new FakeHandler(HttpStatusCode.NotFound, """{"error":"model \"qwen3:8b\" not found, try pulling it first"}"""));

        var act = () => provider.CompleteAsync(new LlmRequest("s", [new LlmMessage(LlmRole.User, "q")], []), CancellationToken.None);

        (await act.Should().ThrowAsync<LlmUnavailableException>()).WithMessage("*ollama pull qwen3:8b*");
    }

    [Fact]
    public async Task Unreachable_ollama_tells_the_user_to_start_it()
    {
        using var provider = Provider(new FakeHandler(new HttpRequestException("Connection refused")));

        var act = () => provider.CompleteAsync(new LlmRequest("s", [new LlmMessage(LlmRole.User, "q")], []), CancellationToken.None);

        (await act.Should().ThrowAsync<LlmUnavailableException>()).WithMessage("*ollama serve*");
    }

    [Theory]
    [InlineData("http://localhost:11434", true)]
    [InlineData("localhost:11434", false)]
    [InlineData("ftp://host", false)]
    public void Options_validation_checks_the_url(string url, bool valid)
    {
        new OllamaOptions { BaseUrl = url }.IsValid().Should().Be(valid);
    }

    private static OllamaLlmProvider Provider(HttpMessageHandler handler) =>
        new(Microsoft.Extensions.Options.Options.Create(new AiOptions { Provider = "Ollama" }),
            NullLogger<OllamaLlmProvider>.Instance, handler);

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode status;
        private readonly string body = "";
        private readonly Exception? error;

        public FakeHandler(HttpStatusCode status, string body) { this.status = status; this.body = body; }
        public FakeHandler(Exception error) => this.error = error;

        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequestUri = request.RequestUri;
            if (error is not null) throw error;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
