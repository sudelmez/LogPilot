using LogPilot.Application.Chat;
using LogPilot.Domain.Chat;
using MongoDB.Bson;
using MongoDB.Driver;

namespace LogPilot.Infrastructure.Mongo;

internal sealed class MongoChatRepository(MongoContext context) : IChatRepository
{
    public async Task<ChatSession> CreateSessionAsync(string userName, string title, DateTime nowUtc, CancellationToken ct)
    {
        var session = new ChatSession
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserName = userName, Title = title, CreatedAt = nowUtc, UpdatedAt = nowUtc
        };

        using var timeout = context.CreateTimeout(ct);
        await context.ChatSessions.InsertOneAsync(session, cancellationToken: timeout.Token);
        return session;
    }

    public async Task<ChatSession?> GetSessionAsync(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out _)) return null;
        using var timeout = context.CreateTimeout(ct);
        return await context.ChatSessions.Find(x => x.Id == id).FirstOrDefaultAsync(timeout.Token);
    }

    public async Task<IReadOnlyList<ChatSession>> ListSessionsAsync(int limit, CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        return await context.ChatSessions.Find(FilterDefinition<ChatSession>.Empty)
            .SortByDescending(x => x.UpdatedAt).Limit(limit).ToListAsync(timeout.Token);
    }

    public async Task TouchSessionAsync(string id, string? newTitle, DateTime nowUtc, CancellationToken ct)
    {
        var u = Builders<ChatSession>.Update;
        var update = u.Set(x => x.UpdatedAt, nowUtc);
        if (newTitle is not null) update = update.Set(x => x.Title, newTitle);

        using var timeout = context.CreateTimeout(ct);
        await context.ChatSessions.UpdateOneAsync(x => x.Id == id, update, cancellationToken: timeout.Token);
    }

    public async Task<ChatMessage> AddMessageAsync(ChatMessage message, CancellationToken ct)
    {
        var stored = new ChatMessage
        {
            Id = ObjectId.GenerateNewId().ToString(),
            SessionId = message.SessionId, Role = message.Role, Content = message.Content, Answer = message.Answer,
            ToolExecutions = message.ToolExecutions, RangeFrom = message.RangeFrom, RangeTo = message.RangeTo,
            Provider = message.Provider, CreatedAt = message.CreatedAt, Feedback = message.Feedback
        };

        using var timeout = context.CreateTimeout(ct);
        await context.ChatMessages.InsertOneAsync(stored, cancellationToken: timeout.Token);
        return stored;
    }

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string sessionId, int limit, CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        var latest = await context.ChatMessages.Find(x => x.SessionId == sessionId)
            .SortByDescending(x => x.CreatedAt).Limit(limit).ToListAsync(timeout.Token);
        latest.Reverse();
        return latest;
    }

    public async Task<ChatMessage?> GetMessageAsync(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out _)) return null;
        using var timeout = context.CreateTimeout(ct);
        return await context.ChatMessages.Find(x => x.Id == id).FirstOrDefaultAsync(timeout.Token);
    }

    public async Task SetFeedbackAsync(string messageId, MessageFeedback feedback, CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        await context.ChatMessages.UpdateOneAsync(x => x.Id == messageId,
            Builders<ChatMessage>.Update.Set(x => x.Feedback, feedback), cancellationToken: timeout.Token);
    }
}
