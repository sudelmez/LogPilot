using LogPilot.AI.Orchestration;
using LogPilot.AI.Providers;
using LogPilot.Application.Ai;
using LogPilot.Application.Chat;
using LogPilot.Application.Common;
using LogPilot.Application.Knowledge;
using LogPilot.Domain.Chat;
using Microsoft.Extensions.Logging;

namespace LogPilot.AI.Chat;

public sealed class ChatService(
    IChatRepository chats,
    ChatOrchestrator orchestrator,
    TimeProvider time,
    ILogger<ChatService> logger,
    SensitiveDataMasker masker,
    KnowledgeService? knowledge = null)
{
    public const int MaxMessageLength = 2000;
    public const string DefaultTitle = "New conversation";
    private static readonly TimeSpan DefaultRange = TimeSpan.FromHours(24);
    private static readonly TimeSpan MaxRange = TimeSpan.FromDays(7);

    public Task<ChatSession> CreateSessionAsync(string? userName, CancellationToken ct)
    {
        var name = string.IsNullOrWhiteSpace(userName) ? "demo" : userName.Trim();
        if (name.Length > 64) throw Invalid(nameof(userName), "UserName must be at most 64 characters.");

        return chats.CreateSessionAsync(name, DefaultTitle, Now(), ct);
    }

    public Task<IReadOnlyList<ChatSession>> ListSessionsAsync(CancellationToken ct) => chats.ListSessionsAsync(50, ct);

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string sessionId, CancellationToken ct)
    {
        _ = await chats.GetSessionAsync(sessionId, ct) ?? throw new NotFoundException($"Chat session '{sessionId}' was not found.");
        return await chats.GetMessagesAsync(sessionId, 200, ct);
    }

    public async Task<ChatMessage> SendMessageAsync(
        string sessionId, string? content, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(content)) throw Invalid("Content", "Content is required.");
        if (content.Length > MaxMessageLength) throw Invalid("Content", $"Content must be at most {MaxMessageLength} characters.");

        var end = (to ?? time.GetUtcNow()).UtcDateTime;
        var start = from?.UtcDateTime ?? end - DefaultRange;
        if (start >= end) throw Invalid("From", "From must be earlier than To.");
        if (end - start > MaxRange) throw Invalid("From", $"The time range cannot exceed {MaxRange.TotalDays:0} days.");

        var session = await chats.GetSessionAsync(sessionId, ct) ?? throw new NotFoundException($"Chat session '{sessionId}' was not found.");
        var history = await chats.GetMessagesAsync(sessionId, 20, ct);
        // KVKK: kullanıcının yazdığı kişisel veri (telefon, kimlik no, e-posta, plaka, IBAN) ne veritabanına
        // ne de modele açık gider. Kimlikler (COR-/TKL-/POL-…) maskeleyiciden etkilenmez.
        var text = masker.MaskRequired(content.Trim());

        // Soruda açık bir süre varsa ("son 15 dakika", "last 2 hours", "bugün") arayüzdeki seçimin yerine o geçer:
        // daha özel olan niyettir. Aralığın sonu arayüzdeki "to" (genelde şimdi) olarak kalır.
        string? rangeSource = null;
        if (QuestionTimeRange.TryParse(text, end, time.LocalTimeZone) is { } asked)
        {
            (start, end, rangeSource) = (asked.From, asked.To, asked.Label);
            logger.LogInformation("Time range taken from the question ({Label}): {From:u} – {To:u}.", asked.Label, start, end);
        }

        await chats.AddMessageAsync(new ChatMessage
        {
            SessionId = sessionId, Role = ChatRole.User, Content = text,
            RangeFrom = start, RangeTo = end, CreatedAt = Now()
        }, ct);

        ChatMessage reply;
        try
        {
            var result = await orchestrator.RunAsync(history, text, new ToolContext(start, end, rangeSource), ct);
            await AttachRelatedCasesAsync(result.Answer, ct);
            reply = new ChatMessage
            {
                SessionId = sessionId, Role = ChatRole.Assistant, Content = result.Answer.Summary,
                Answer = result.Answer, ToolExecutions = result.ToolExecutions.ToList(),
                Provider = result.Provider, CreatedAt = Now()
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sağlayıcı/araç hatası kullanıcıya 500 olarak yansımasın; konuşma devam edebilsin.
            logger.LogError(ex, "The assistant failed to answer in session {SessionId}.", sessionId);
            // Model servisi hatası (anahtar, kota, ağ): mesajı kullanıcıya uygun ve sırsızdır, nedeni göster.
            var summary = ex is LlmUnavailableException unavailable
                ? unavailable.Message
                : "The assistant could not complete the investigation. Please try again.";
            var limitation = ex is LlmUnavailableException
                ? "The AI service did not respond, so the logs were not analysed; nothing was concluded."
                : "An internal error occurred while analysing the logs; nothing was concluded.";
            reply = new ChatMessage
            {
                SessionId = sessionId, Role = ChatRole.Assistant,
                Content = summary,
                Answer = new AssistantAnswer
                {
                    Summary = summary,
                    Confidence = LogPilot.Domain.Ai.ConfidenceLevel.Insufficient,
                    DataLimitations = [limitation]
                },
                CreatedAt = Now()
            };
        }

        var stored = await chats.AddMessageAsync(reply, ct);
        var title = session.Title == DefaultTitle ? Shorten(text, 60) : null;
        await chats.TouchSessionAsync(sessionId, title, Now(), ct);
        return stored;
    }

    /// Cevabın DOĞRULANMIŞ kanıtındaki hata kodlarıyla geçmiş vakaları bulur (modelden bağımsız: Mock dahil her sağlayıcıda).
    /// Vakalar kanıt değildir; güven puanına dokunulmaz. Hafıza hatası cevabı bozmaz.
    private async Task AttachRelatedCasesAsync(AssistantAnswer answer, CancellationToken ct)
    {
        if (knowledge is null) return;
        var codes = answer.Evidence.Select(e => e.ErrorCode).OfType<string>().ToList();
        if (codes.Count == 0) return;

        try
        {
            var related = await knowledge.FindRelatedAsync(codes, answer.AffectedServices, KnowledgeService.RelatedLimit, ct);
            answer.RelatedCases.AddRange(related.Select(c => new RelatedCase
            {
                Id = c.Id!, Title = c.Title, Resolution = c.Resolution, RootCause = c.RootCause,
                ErrorCodes = c.ErrorCodes, Services = c.Services, CreatedAt = c.CreatedAt
            }));
            await knowledge.MarkSuggestedAsync(related.Select(c => c.Id!).ToList(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not look up related past cases; the answer is returned without them.");
        }
    }

    private DateTime Now() => time.GetUtcNow().UtcDateTime;

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    private static RequestValidationException Invalid(string field, string message)
        => new(new Dictionary<string, string[]> { [field] = [message] });
}
