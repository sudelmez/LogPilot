using LogPilot.Domain.Chat;

namespace LogPilot.Application.Chat;

public interface IChatRepository
{
    Task<ChatSession> CreateSessionAsync(string userName, string title, DateTime nowUtc, CancellationToken ct);
    Task<ChatSession?> GetSessionAsync(string id, CancellationToken ct);
    Task<IReadOnlyList<ChatSession>> ListSessionsAsync(int limit, CancellationToken ct);
    Task TouchSessionAsync(string id, string? newTitle, DateTime nowUtc, CancellationToken ct);

    Task<ChatMessage> AddMessageAsync(ChatMessage message, CancellationToken ct);

    /// Son `limit` mesaj, kronolojik sırada.
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string sessionId, int limit, CancellationToken ct);

    Task<ChatMessage?> GetMessageAsync(string id, CancellationToken ct);
    Task SetFeedbackAsync(string messageId, MessageFeedback feedback, CancellationToken ct);
}
