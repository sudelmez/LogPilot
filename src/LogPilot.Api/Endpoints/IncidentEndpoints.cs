using LogPilot.Application.Common;
using LogPilot.Application.Incidents;
using LogPilot.Domain.Incidents;
using LogPilot.Domain.Logs;

namespace LogPilot.Api.Endpoints;

/// Resolved + ResolutionNote → not incident'a yazılır ve doğrulanmış vaka olarak hafızaya girer.
public sealed record UpdateIncidentStatusRequest(IncidentStatus Status, string? ResolutionNote = null, string? RootCause = null);

public static class IncidentEndpoints
{
    public static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/incidents").WithTags("Incidents");

        group.MapGet("/", async (IncidentStatus? status, IncidentSeverity? severity, bool? activeOnly,
                IncidentService service, CancellationToken ct, int page = 1, int pageSize = 20)
                => TypedResults.Ok(await service.SearchAsync(status, severity, activeOnly, page, pageSize, ct)))
            .Produces<PagedResult<Incident>>()
            .ProducesValidationProblem();

        group.MapGet("/{id}", async (string id, IncidentService service, CancellationToken ct) =>
                await service.GetAsync(id, ct) is { } incident ? Results.Ok(incident) : Results.NotFound())
            .Produces<Incident>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id}/logs", async (string id, IncidentService service, CancellationToken ct)
                => TypedResults.Ok(await service.GetRelatedLogsAsync(id, ct)))
            .Produces<IReadOnlyList<LogEntry>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/{id}/status", async (string id, UpdateIncidentStatusRequest request, IncidentService service, CancellationToken ct)
                => TypedResults.Ok(await service.UpdateStatusAsync(id, request.Status, request.ResolutionNote, request.RootCause, ct)))
            .Produces<Incident>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id}/summary", async (string id, IncidentService service, CancellationToken ct)
                => TypedResults.Ok(await service.RegenerateSummaryAsync(id, ct)))
            .Produces<Incident>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
