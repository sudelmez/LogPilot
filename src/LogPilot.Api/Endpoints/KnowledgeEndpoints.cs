using LogPilot.Application.Common;
using LogPilot.Application.Knowledge;
using LogPilot.Domain.Knowledge;

namespace LogPilot.Api.Endpoints;

public sealed record KnowledgeCaseRequest(
    string? Title, string? Resolution, string? RootCause, string? Symptom,
    IReadOnlyList<string>? ErrorCodes, IReadOnlyList<string>? Services);

/// Doğrulanmış vaka hafızası: insanların onayladığı geçmiş çözümler. Asistan bunları ipucu olarak kullanır.
public static class KnowledgeEndpoints
{
    public static IEndpointRouteBuilder MapKnowledgeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/knowledge").WithTags("Knowledge");

        group.MapGet("/", async (string? errorCode, string? service, string? q, KnowledgeService knowledge,
                CancellationToken ct, int page = 1, int pageSize = 20)
                => TypedResults.Ok(await knowledge.SearchAsync(errorCode, service, q, page, pageSize, ct)))
            .Produces<PagedResult<KnowledgeCase>>()
            .ProducesValidationProblem();

        group.MapGet("/{id}", async (string id, KnowledgeService knowledge, CancellationToken ct) =>
                await knowledge.GetAsync(id, ct) is { } found ? Results.Ok(found) : Results.NotFound())
            .Produces<KnowledgeCase>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", async (KnowledgeCaseRequest request, KnowledgeService knowledge, CancellationToken ct) =>
            {
                var created = await knowledge.CreateAsync(ToInput(request), ct);
                return Results.Created($"/api/knowledge/{created.Id}", created);
            })
            .Produces<KnowledgeCase>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapPut("/{id}", async (string id, KnowledgeCaseRequest request, KnowledgeService knowledge, CancellationToken ct)
                => TypedResults.Ok(await knowledge.UpdateAsync(id, ToInput(request), ct)))
            .Produces<KnowledgeCase>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id}", async (string id, KnowledgeService knowledge, CancellationToken ct) =>
            {
                await knowledge.DeleteAsync(id, ct);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static KnowledgeCaseInput ToInput(KnowledgeCaseRequest r)
        => new(r.Title, r.Resolution, r.RootCause, r.Symptom, r.ErrorCodes, r.Services);
}
