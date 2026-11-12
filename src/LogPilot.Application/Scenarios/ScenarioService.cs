using LogPilot.Application.Common;
using LogPilot.Domain.Scenarios;

namespace LogPilot.Application.Scenarios;

public sealed record ScenarioSummary(
    string Id, string Name, string Description, ScenarioKind Kind, ScenarioSettings Settings, int StepCount);

public sealed record ScenarioSettingsRequest(bool Enabled, int IntervalSeconds, double Probability, int Weight);

public sealed class ScenarioService(
    IScenarioRepository scenarios,
    IScenarioExecutionRepository executions,
    TimeProvider time)
{
    public const int MaxPageSize = 100;
    public const int MaxIntervalSeconds = 86_400;
    public const int MaxWeight = 1000;

    public async Task<IReadOnlyList<ScenarioSummary>> ListAsync(CancellationToken ct)
        => (await scenarios.GetAllAsync(ct)).Select(ToSummary).ToList();

    public Task<Scenario?> GetAsync(string id, CancellationToken ct)
        => scenarios.GetByIdAsync(id, ct);

    /// Çalıştırmayı Pending olarak kuyruğa yazar; asıl log üretimini LogGenerator yapar.
    public async Task<ScenarioExecution> TriggerAsync(string id, CancellationToken ct)
    {
        _ = await scenarios.GetByIdAsync(id, ct)
            ?? throw new NotFoundException($"Scenario '{id}' was not found.");

        if (await executions.HasActiveAsync(id, ct))
            throw new ConflictException($"Scenario '{id}' already has a pending or running execution.");

        return await executions.CreateAsync(id, ExecutionTrigger.Manual, time.GetUtcNow().UtcDateTime, ct);
    }

    public async Task<ScenarioSummary> UpdateSettingsAsync(string id, ScenarioSettingsRequest request, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.IntervalSeconds is < 0 or > MaxIntervalSeconds)
            errors[nameof(request.IntervalSeconds)] = [$"IntervalSeconds must be between 0 and {MaxIntervalSeconds}."];
        if (double.IsNaN(request.Probability) || request.Probability is < 0 or > 1)
            errors[nameof(request.Probability)] = ["Probability must be between 0 and 1."];
        if (request.Weight is < 0 or > MaxWeight)
            errors[nameof(request.Weight)] = [$"Weight must be between 0 and {MaxWeight}."];
        if (errors.Count > 0) throw new RequestValidationException(errors);

        var settings = new ScenarioSettings
        {
            Enabled = request.Enabled,
            IntervalSeconds = request.IntervalSeconds,
            Probability = request.Probability,
            Weight = request.Weight
        };

        if (!await scenarios.UpdateSettingsAsync(id, settings, ct))
            throw new NotFoundException($"Scenario '{id}' was not found.");

        var updated = await scenarios.GetByIdAsync(id, ct)
            ?? throw new NotFoundException($"Scenario '{id}' was not found.");
        return ToSummary(updated);
    }

    public Task<ScenarioExecution?> GetExecutionAsync(string id, CancellationToken ct)
        => executions.GetByIdAsync(id, ct);

    public Task<PagedResult<ScenarioExecution>> GetExecutionsAsync(string? scenarioId, int page, int pageSize, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1) errors["Page"] = ["Page must be at least 1."];
        if (pageSize is < 1 or > MaxPageSize) errors["PageSize"] = [$"PageSize must be between 1 and {MaxPageSize}."];
        if (errors.Count > 0) throw new RequestValidationException(errors);

        var id = string.IsNullOrWhiteSpace(scenarioId) ? null : scenarioId.Trim();
        return executions.SearchAsync(id, page, pageSize, ct);
    }

    private static ScenarioSummary ToSummary(Scenario s)
        => new(s.Id, s.Name, s.Description, s.Kind, s.Settings, s.Steps.Count);
}
