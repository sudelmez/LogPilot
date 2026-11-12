using LogPilot.AI.Chat;
using LogPilot.Application.Knowledge;
using LogPilot.Domain.Chat;

namespace LogPilot.Api.Endpoints;

public sealed record CreateSessionRequest(string? UserName);

public sealed record SendMessageRequest(string? Content, DateTimeOffset? From, DateTimeOffset? To);

/// Helpful + Resolution → çözüm doğrulanmış vaka olarak hafızaya girer.
public sealed record FeedbackRequest(bool Helpful, string? Resolution, string? RootCause);

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/chat").WithTags("Chat");

        group.MapPost("/sessions", async (CreateSessionRequest? request, ChatService chat, CancellationToken ct) =>
            {
                var session = await chat.CreateSessionAsync(request?.UserName, ct);
                return Results.Created($"/api/chat/sessions/{session.Id}/messages", session);
            })
            .Produces<ChatSession>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapGet("/sessions", async (ChatService chat, CancellationToken ct)
                => TypedResults.Ok(await chat.ListSessionsAsync(ct)))
            .Produces<IReadOnlyList<ChatSession>>();

        group.MapGet("/sessions/{id}/messages", async (string id, ChatService chat, CancellationToken ct)
                => TypedResults.Ok(await chat.GetMessagesAsync(id, ct)))
            .Produces<IReadOnlyList<ChatMessage>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Kullanıcı mesajını işler, asistan cevabını (kanıt referansları + araç kayıtlarıyla) döndürür.
        group.MapPost("/sessions/{id}/messages", async (string id, SendMessageRequest request, ChatService chat, CancellationToken ct)
                => TypedResults.Ok(await chat.SendMessageAsync(id, request.Content, request.From, request.To, ct)))
            .Produces<ChatMessage>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Asistan cevabına geri bildirim (👍/👎). 👍 + "gerçek çözüm" notu vaka hafızasına girer.
        group.MapPost("/messages/{id}/feedback", async (string id, FeedbackRequest request, KnowledgeService knowledge, CancellationToken ct)
                => TypedResults.Ok(await knowledge.SubmitFeedbackAsync(id, new FeedbackInput(request.Helpful, request.Resolution, request.RootCause), ct)))
            .Produces<ChatMessage>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
