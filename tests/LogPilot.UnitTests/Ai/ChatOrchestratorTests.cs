using AwesomeAssertions;
using LogPilot.AI.Orchestration;
using LogPilot.AI.Providers;
using LogPilot.AI.Security;
using LogPilot.AI.Tools;
using LogPilot.Application.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LogPilot.UnitTests.Ai;

public class ChatOrchestratorTests
{
    private static readonly ToolContext Context = new(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow);

    private static (ChatOrchestrator Sut, ScriptedProvider Llm, Mock<ILogAnalysisTools> Tools) Create(params LlmResponse[] script)
    {
        var tools = new Mock<ILogAnalysisTools>();
        tools.Setup(t => t.GetUserRecentErrorsAsync(It.IsAny<UserRecentErrorsArgs>(), It.IsAny<ToolContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogToolResult([], 0, false, "scope"));
        var llm = new ScriptedProvider(script);
        var sut = new ChatOrchestrator(llm, new ToolRegistry(tools.Object, new PromptInjectionGuard()), new SensitiveDataMasker(),
            NullLogger<ChatOrchestrator>.Instance);
        return (sut, llm, tools);
    }

    [Fact]
    public async Task A_guess_without_any_tool_call_is_sent_back_once_to_investigate_first()
    {
        var (sut, llm, tools) = Create(
            LlmResponse.FromText("""{"summary":"BOB probably had a system problem."}"""),
            new LlmResponse(null, [new LlmToolCall("c1", ToolNames.GetUserRecentErrors, """{"userCode":"BOB"}""")]),
            LlmResponse.FromText("""{"summary":"No errors were found for BOB in this range."}"""));

        var result = await sut.RunAsync([], "Why can't BOB log in?", Context, CancellationToken.None);

        result.Answer.Summary.Should().Be("No errors were found for BOB in this range.");
        tools.Verify(t => t.GetUserRecentErrorsAsync(It.IsAny<UserRecentErrorsArgs>(), It.IsAny<ToolContext>(), It.IsAny<CancellationToken>()), Times.Once);
        llm.Requests[1].Messages.Should().Contain(m => m.Role == LlmRole.User && m.Content.Contains("Investigate with the tools first"));
    }

    [Fact]
    public async Task A_clarifying_question_is_not_sent_back()
    {
        var (sut, llm, _) = Create(
            LlmResponse.FromText("""{"summary":"Which user do you mean?","clarifyingQuestion":"Which user do you mean?"}"""));

        var result = await sut.RunAsync([], "Why does it fail?", Context, CancellationToken.None);

        result.Answer.ClarifyingQuestion.Should().Be("Which user do you mean?");
        llm.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_clarifying_question_is_sent_back_when_the_question_names_something_to_look_up()
    {
        var (sut, llm, tools) = Create(
            LlmResponse.FromText("""{"summary":"Unclear.","clarifyingQuestion":"Where did BOB try to log in?"}"""),
            new LlmResponse(null, [new LlmToolCall("c1", ToolNames.GetUserRecentErrors, """{"userCode":"BOB"}""")]),
            LlmResponse.FromText("""{"summary":"No errors were found for BOB."}"""));

        var result = await sut.RunAsync([], "Son 15 dakikada BOB neden giriş yapamadı?", Context, CancellationToken.None);

        result.Answer.Summary.Should().Be("No errors were found for BOB.");
        llm.Requests.Should().HaveCount(3);
    }

    [Theory]
    [InlineData("Son 15 dakikada BOB neden giriş yapamadı?", true)]
    [InlineData("What does TOKEN_EXPIRED mean here?", true)]
    [InlineData("Which errors are related to COR-20261108-001ABC?", true)]
    [InlineData("Is AuthenticationService healthy?", true)]
    [InlineData("Can you help me?", false)]
    [InlineData("Why does it fail?", false)]
    public void Specific_questions_are_recognised(string question, bool specific)
    {
        ChatOrchestrator.IsSpecific(question).Should().Be(specific);
    }

    [Fact]
    public async Task The_model_is_only_sent_back_once()
    {
        var (sut, llm, _) = Create(
            LlmResponse.FromText("""{"summary":"guess 1"}"""),
            LlmResponse.FromText("""{"summary":"guess 2"}"""));

        var result = await sut.RunAsync([], "Why can't BOB log in?", Context, CancellationToken.None);

        result.Answer.Summary.Should().Be("guess 2");
        llm.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task The_question_is_routed_to_a_playbook_and_the_hint_reaches_the_model()
    {
        var (sut, llm, _) = Create(LlmResponse.FromText("""{"summary":"ok","clarifyingQuestion":"Which customer?"}"""));

        await sut.RunAsync([], "ELIF'e poliçe SMS'i gitti mi?", Context, CancellationToken.None);

        llm.Requests[0].SystemPrompt.Should().Contain("ROUTING HINT").And.Contain("P6").And.Contain(ToolNames.TraceNotificationDelivery);
    }

    [Fact]
    public async Task An_off_topic_question_is_refused_before_the_model_is_called()
    {
        var (sut, llm, _) = Create(LlmResponse.FromText("""{"summary":"Kasko is a type of insurance..."}"""));

        var result = await sut.RunAsync([], "Kasko nedir?", Context, CancellationToken.None);

        llm.Requests.Should().BeEmpty("the scope guard answers without spending a model call");
        result.Answer.OutOfScope.Should().Be("OffTopic");
        result.Answer.Summary.Should().StartWith("Üzgünüm, bu konuda size yardımcı olamıyorum");
        result.ToolExecutions.Should().BeEmpty();
        result.Provider.Should().Be("scope-guard");
    }

    [Fact]
    public async Task Abusive_language_is_refused_even_with_a_log_question_inside()
    {
        var (sut, llm, _) = Create(LlmResponse.FromText("{}"));

        var result = await sut.RunAsync([], "amk BOB yine giriş yapamıyor", Context, CancellationToken.None);

        llm.Requests.Should().BeEmpty();
        result.Answer.OutOfScope.Should().Be("Abusive");
    }

    [Fact]
    public async Task When_the_model_flags_out_of_scope_the_standard_refusal_is_returned_without_a_nudge()
    {
        var (sut, llm, _) = Create(LlmResponse.FromText("""{"summary":"Sure! The best insurer is...","outOfScope":true}"""));

        var result = await sut.RunAsync([], "Bunu bir düşünür müsün?", Context, CancellationToken.None);

        llm.Requests.Should().HaveCount(1, "an out-of-scope verdict is not sent back to 'investigate first'");
        result.Answer.OutOfScope.Should().Be("OffTopic");
        result.Answer.Summary.Should().NotContain("best insurer", "the platform, not the model, writes the refusal text");
    }

    [Fact]
    public async Task A_delete_request_is_refused_by_the_read_only_role_without_calling_the_model()
    {
        var (sut, llm, _) = Create(LlmResponse.FromText("{}"));

        var result = await sut.RunAsync([], "BOB'un loglarını sil", Context, CancellationToken.None);

        llm.Requests.Should().BeEmpty();
        result.Answer.OutOfScope.Should().Be("NotPermitted");
    }

    [Fact]
    public async Task When_the_model_flags_a_modification_request_it_is_refused()
    {
        var (sut, _, _) = Create(LlmResponse.FromText("""{"summary":"Done, I removed it.","notPermitted":true}"""));

        var result = await sut.RunAsync([], "Şu kaydı bir hallet bakalım", Context, CancellationToken.None);

        result.Answer.OutOfScope.Should().Be("NotPermitted");
        result.Answer.Summary.Should().NotContain("removed", "the model may never claim it changed data");
    }

    [Fact]
    public async Task Personal_data_in_the_question_never_reaches_the_model()
    {
        var (sut, llm, _) = Create(LlmResponse.FromText("""{"summary":"ok","clarifyingQuestion":"Which user?"}"""));

        await sut.RunAsync([], "+905551014729 numaralı müşteri ve 99001007919 kimlikli kişi neden teklif alamıyor?", Context, CancellationToken.None);

        var sent = string.Join("\n", llm.Requests[0].Messages.Select(m => m.Content));
        sent.Should().NotContain("5551014729").And.NotContain("99001007919").And.Contain("[MASKED_PHONE]");
        llm.Requests[0].SystemPrompt.Should().Contain("READ-ONLY analyst");
    }

    [Fact]
    public async Task An_invented_identifier_is_rejected_and_the_model_corrects_itself()
    {
        var (sut, llm, tools) = Create(
            new LlmResponse(null, [new LlmToolCall("c1", ToolNames.GetUserRecentErrors, """{"userCode":"ZORRO"}""")]),
            new LlmResponse(null, [new LlmToolCall("c2", ToolNames.GetUserRecentErrors, """{"userCode":"BOB"}""")]),
            LlmResponse.FromText("""{"summary":"No errors were found for BOB."}"""));

        var result = await sut.RunAsync([], "Why can't BOB log in?", Context, CancellationToken.None);

        // Uydurma değerle araç hiç çalıştırılmadı; sorudaki değerle çalıştırıldı.
        tools.Verify(t => t.GetUserRecentErrorsAsync(It.Is<UserRecentErrorsArgs>(a => a.UserCode == "ZORRO"), It.IsAny<ToolContext>(), It.IsAny<CancellationToken>()), Times.Never);
        tools.Verify(t => t.GetUserRecentErrorsAsync(It.Is<UserRecentErrorsArgs>(a => a.UserCode == "BOB"), It.IsAny<ToolContext>(), It.IsAny<CancellationToken>()), Times.Once);
        llm.Requests[1].Messages.Should().Contain(m => m.Role == LlmRole.Tool && m.Content.Contains("do not invent identifiers") && m.Content.Contains("userCode=BOB"));
        result.ToolExecutions[0].Error.Should().Contain("ZORRO");
    }

    [Fact]
    public async Task Identifiers_found_in_earlier_tool_results_may_be_used()
    {
        var (sut, _, tools) = Create(
            new LlmResponse(null, [new LlmToolCall("c1", ToolNames.GetUserRecentErrors, """{"userCode":"BOB"}""")]),
            new LlmResponse(null, [new LlmToolCall("c2", ToolNames.FindRelatedLogs, """{"correlationId":"COR-20261108-ABC123"}""")]),
            LlmResponse.FromText("""{"summary":"done"}"""));
        tools.Setup(t => t.GetUserRecentErrorsAsync(It.IsAny<UserRecentErrorsArgs>(), It.IsAny<ToolContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogToolResult([TestLog("COR-20261108-ABC123")], 1, false, "scope"));
        tools.Setup(t => t.FindRelatedLogsAsync(It.IsAny<RelatedLogsArgs>(), It.IsAny<ToolContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogToolResult([], 0, false, "scope"));

        await sut.RunAsync([], "Why can't BOB log in?", Context, CancellationToken.None);

        tools.Verify(t => t.FindRelatedLogsAsync(It.Is<RelatedLogsArgs>(a => a.CorrelationId == "COR-20261108-ABC123"), It.IsAny<ToolContext>(), It.IsAny<CancellationToken>()), Times.Once,
            "a correlation id returned by a previous tool is grounded");
    }

    private static LogSnippet TestLog(string correlationId) => new("0123456789abcdef01234567", DateTime.UtcNow, "Error", "MobileApp",
        "AuthenticationService", "BOB", correlationId, null, "Login", null, "TOKEN_EXPIRED", null, "Access token expired.", null,
        new Dictionary<string, string>());

    [Fact]
    public async Task The_routing_hint_lists_the_identifiers_found_in_the_question()
    {
        var (sut, llm, _) = Create(LlmResponse.FromText("""{"summary":"ok","clarifyingQuestion":"?"}"""));

        await sut.RunAsync([], "BOB'a poliçe SMS'i ulaştı mı?", Context, CancellationToken.None);

        llm.Requests[0].SystemPrompt.Should().Contain("Identifiers in the question: userCode=BOB");
    }

    private sealed class ScriptedProvider(IReadOnlyList<LlmResponse> script) : ILlmProvider
    {
        public List<LlmRequest> Requests { get; } = [];
        public string Name => "scripted";

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(script[Math.Min(Requests.Count - 1, script.Count - 1)]);
        }
    }
}
