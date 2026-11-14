using LogPilot.AI.Prompts;
using LogPilot.AI.Providers;
using LogPilot.AI.Security;
using LogPilot.AI.Tools;
using LogPilot.Application.Ai;
using LogPilot.Domain.Chat;
using Microsoft.Extensions.Logging;

namespace LogPilot.AI.Orchestration;

public sealed record OrchestrationResult(AssistantAnswer Answer, IReadOnlyList<ToolExecution> ToolExecutions, string Provider);

/// Soru → (LLM araç ister → backend doğrulanmış araçları çalıştırır → sonuç LLM'e döner)* → yapılandırılmış cevap → doğrulama.
public sealed partial class ChatOrchestrator(
    ILlmProvider llm,
    ToolRegistry tools,
    SensitiveDataMasker masker,
    ILogger<ChatOrchestrator> logger)
{
    public const int MaxIterations = 5;
    public const int MaxToolCalls = 8;
    private const int MaxHistoryMessages = 10;

    public async Task<OrchestrationResult> RunAsync(
        IReadOnlyList<ChatMessage> history, string userMessage, ToolContext ctx, CancellationToken ct)
    {
        // KVKK: soru modele gitmeden maskelenir (ChatService de maskeler; bu, başka çağıranlara karşı ikinci kat).
        userMessage = masker.MaskRequired(userMessage);

        // 1. savunma hattı: argo/hakaret veya açıkça konu dışı sorular LLM'e HİÇ gitmez (maliyet yok, sızma riski yok).
        var scope = ScopeGuard.Evaluate(userMessage);
        if (scope.Verdict is ScopeGuard.Verdict.Abusive or ScopeGuard.Verdict.OffTopic or ScopeGuard.Verdict.NotPermitted)
        {
            logger.LogInformation("Question refused by the scope guard: {Verdict} ({Reason}).", scope.Verdict, scope.Reason);
            return new OrchestrationResult(AnswerBuilder.Refusal(scope.Verdict, userMessage), [], "scope-guard");
        }

        // Soruyu playbook'a sınıflandır; modele bağlayıcı olmayan bir yönlendirme ipucu ver (küçük modeller için çok faydalı).
        var playbook = InvestigationPlaybooks.Classify(userMessage);
        if (playbook is not null) logger.LogInformation("Question routed to playbook {Playbook} ({Title}).", playbook.Id, playbook.Title);
        var signals = InvestigationPlaybooks.Extract(userMessage);
        var system = SystemPrompts.Chat(ctx, MaxToolCalls, playbook is null ? null : InvestigationPlaybooks.RenderHint(playbook, signals));

        // Geçmişten sadece metinler (kullanıcı soruları + asistan özetleri) taşınır; eski araç verisi taşınmaz.
        var messages = history.TakeLast(MaxHistoryMessages)
            .Select(m => new LlmMessage(m.Role == ChatRole.User ? LlmRole.User : LlmRole.Assistant, m.Content))
            .ToList();
        messages.Add(new LlmMessage(LlmRole.User, userMessage));

        var invocations = new List<ToolInvocation>();
        string? finalText = null;
        var nudged = false;

        for (var i = 0; i < MaxIterations && finalText is null; i++)
        {
            var toolsAllowed = invocations.Count < MaxToolCalls;
            var response = await llm.CompleteAsync(
                new LlmRequest(system, messages, toolsAllowed ? tools.Definitions : [], LlmPurpose.Chat), ct);

            // Küçük modeller bazen hiç araç çağırmadan tahmine dayalı cevap yazar. Netleştirme sorusu da yoksa
            // bir kez geri çevir: önce veriyi incelesin (kural 1). Araç kullanan modelleri etkilemez.
            // Soru belirli bir şey içeriyorsa (kullanıcı/hata kodu, korelasyon kimliği, servis) netleştirme sorusu
            // bahane sayılmaz: önce bakılabilecek veri vardır.
            var early = response.ToolCalls.Count == 0 ? AnswerParser.Parse(response.Text) : null;
            if (response.ToolCalls.Count == 0 && invocations.Count == 0 && toolsAllowed && !nudged
                && (IsSpecific(userMessage) || (string.IsNullOrWhiteSpace(early?.ClarifyingQuestion) && early?.OutOfScope != true && early?.NotPermitted != true)))
            {
                nudged = true;
                logger.LogInformation("The model answered without using any tool; asking it to investigate first.");
                messages.Add(new LlmMessage(LlmRole.Assistant, response.Text ?? "", ProviderState: response.ProviderState));
                messages.Add(new LlmMessage(LlmRole.User,
                    "You have not looked at any data yet. Investigate with the tools first (app/login problem of a user: GetUserRecentErrors; " +
                    "insurance quote of a user/customer/quote: TraceCustomerJourney; aggregate quote questions: GetQuoteFunnel; " +
                    "did an SMS/policy notification reach a customer: TraceNotificationDelivery; " +
                    "otherwise SearchLogs or GetErrorStatistics), then reply with the JSON object."));
                continue;
            }

            if (response.ToolCalls.Count == 0 || !toolsAllowed)
            {
                finalText = response.Text ?? "";
                break;
            }

            messages.Add(new LlmMessage(LlmRole.Assistant, response.Text ?? "", ToolCalls: response.ToolCalls,
                ProviderState: response.ProviderState));

            foreach (var call in response.ToolCalls.Take(MaxToolCalls - invocations.Count))
            {
                // Uydurma kimlik: araç ÇALIŞTIRILMAZ, model hatayı görüp düzeltir (bütçeden düşer, sonsuz döngü olmaz).
                var known = string.Join("\n", messages.Where(m => m.Role != LlmRole.Assistant).Select(m => m.Content));
                var invented = ArgumentGrounding.Check(call.ArgumentsJson, known, signals.Describe());
                if (invented is not null) logger.LogWarning("Tool call {Tool} rejected: {Reason}", call.Name, invented);
                var invocation = invented is not null ? ToolRegistry.Rejected(call, invented) : await tools.InvokeAsync(call, ctx, ct);
                invocations.Add(invocation);
                logger.LogInformation("Tool {Tool} finished in {Ms} ms: {Count} result(s), error: {Error}",
                    invocation.ToolName, invocation.DurationMs, invocation.ResultCount, invocation.Error ?? "none");

                messages.Add(new LlmMessage(LlmRole.Tool,
                    invocation.Error is null ? invocation.WrappedResult : $"Tool error: {invocation.Error}",
                    call.Id, call.Name));
            }
        }

        // Araç/iterasyon bütçesi bitti ama nihai cevap yok: araçsız, "şimdi cevap ver" diye zorla.
        finalText ??= (await llm.CompleteAsync(
            new LlmRequest(system + "\nThe tool budget is exhausted. Answer now with the data you already have.", messages, [], LlmPurpose.Chat), ct)).Text;

        var parsed = AnswerParser.Parse(finalText);
        if (parsed is null)
        {
            logger.LogWarning("The model did not return a valid JSON answer; retrying once.");
            messages.Add(new LlmMessage(LlmRole.Assistant, finalText ?? ""));
            messages.Add(new LlmMessage(LlmRole.User, "Your previous reply was not a valid JSON object in the required schema. Reply with ONLY that JSON object."));
            var retry = await llm.CompleteAsync(new LlmRequest(system, messages, [], LlmPurpose.Chat), ct);
            parsed = AnswerParser.Parse(retry.Text);
        }

        parsed ??= new LlmAnswer
        {
            Summary = "I could not produce a structured answer. Please rephrase the question.",
            DataLimitations = ["The language model did not return an answer in the required format."]
        };

        if (parsed.NotPermitted == true)
        {
            logger.LogInformation("The model recognised a data modification request; refused (read-only role).");
            return new OrchestrationResult(AnswerBuilder.Refusal(ScopeGuard.Verdict.NotPermitted, userMessage), [], llm.Name);
        }

        // 2. savunma hattı: model soruyu görev alanı dışı bulduysa (ve veri incelemediyse) standart ret.
        if (parsed.OutOfScope == true && invocations.Count == 0 && !IsSpecific(userMessage))
        {
            logger.LogInformation("Question refused by the model as out of scope.");
            return new OrchestrationResult(AnswerBuilder.Refusal(ScopeGuard.Verdict.OffTopic, userMessage), [], llm.Name);
        }

        if (!string.IsNullOrWhiteSpace(parsed.Confidence))
            logger.LogDebug("Model-reported confidence '{Confidence}' ignored; confidence is computed from evidence.", parsed.Confidence);

        var answer = AnswerBuilder.Build(parsed, invocations, ctx, masker);
        var executions = invocations.Select(i => new ToolExecution
        {
            Name = i.ToolName,
            ArgumentsJson = i.ArgumentsJson,
            DurationMs = i.DurationMs,
            ResultCount = i.ResultCount,
            Truncated = i.Truncated,
            FlaggedContent = i.InjectionDetections,
            Error = i.Error,
            Path = i.Path?.Select(h => new InvestigationHop { Source = h.Source, LookedUpBy = h.LookedUpBy, Found = h.Found, Result = h.Result }).ToList() ?? []
        }).ToList();

        return new OrchestrationResult(answer, executions, llm.Name);
    }

    /// Soruda incelenebilecek somut bir tanımlayıcı var mı: BOB/TCLI gibi kullanıcı kodu, TOKEN_EXPIRED gibi hata kodu,
    /// COR-/TRX-/TKL-/CUS- kimliği veya AuthenticationService gibi servis adı.
    public static bool IsSpecific(string question) => SpecificToken().IsMatch(question);

    [System.Text.RegularExpressions.GeneratedRegex(@"\b(?:[A-Z][A-Z0-9_]{2,}|(?:COR|TRX|TKL|CUS|POL)-[A-Za-z0-9-]+|[A-Z][a-z]+(?:Service|Gateway))\b")]
    private static partial System.Text.RegularExpressions.Regex SpecificToken();
}
