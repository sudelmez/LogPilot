using LogPilot.Application.Common;
using LogPilot.Application.Scenarios;
using LogPilot.Domain.Scenarios;

namespace LogPilot.Api.Endpoints;

public static class ScenarioEndpoints
{
    public static IEndpointRouteBuilder MapScenarioEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/scenarios").WithTags("Scenarios");

        group.MapGet("/", async (ScenarioService service, CancellationToken ct)
                => TypedResults.Ok(await service.ListAsync(ct)))
            .Produces<IReadOnlyList<ScenarioSummary>>();

        // "/executions" sabit segment olduğu için "/{id}" rotasından önceliklidir.
        group.MapGet("/executions", async (string? scenarioId, ScenarioService service, CancellationToken ct,
                int page = 1, int pageSize = 20)
                => TypedResults.Ok(await service.GetExecutionsAsync(scenarioId, page, pageSize, ct)))
            .Produces<PagedResult<ScenarioExecution>>()
            .ProducesValidationProblem();

        group.MapGet("/executions/{id}", async (string id, ScenarioService service, CancellationToken ct) =>
                await service.GetExecutionAsync(id, ct) is { } execution ? Results.Ok(execution) : Results.NotFound())
            .Produces<ScenarioExecution>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id}", async (string id, ScenarioService service, CancellationToken ct) =>
                await service.GetAsync(id, ct) is { } scenario ? Results.Ok(scenario) : Results.NotFound())
            .Produces<Scenario>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Log üretimi burada yapılmaz: Pending kayıt oluşturulur, LogGenerator işler.
        group.MapPost("/{id}/trigger", async (string id, ScenarioService service, CancellationToken ct) =>
            {
                var execution = await service.TriggerAsync(id, ct);
                return Results.Accepted($"/api/scenarios/executions/{execution.Id}", execution);
            })
            .Produces<ScenarioExecution>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Canlı ayarlar (sıklık, olasılık, ağırlık, aktif/pasif). Generator scheduler'ı her tick'te okur.
        group.MapPut("/{id}/settings", async (string id, ScenarioSettingsRequest request, ScenarioService service, CancellationToken ct)
                => TypedResults.Ok(await service.UpdateSettingsAsync(id, request, ct)))
            .Produces<ScenarioSummary>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
