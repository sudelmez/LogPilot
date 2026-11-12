using LogPilot.Application.Common;
using LogPilot.Domain.Scenarios;

namespace LogPilot.Application.Scenarios;

public interface IScenarioExecutionRepository
{
    Task<ScenarioExecution> CreateAsync(string scenarioId, ExecutionTrigger trigger, DateTime requestedAt, CancellationToken ct);
    Task<ScenarioExecution?> GetByIdAsync(string id, CancellationToken ct);
    Task<PagedResult<ScenarioExecution>> SearchAsync(string? scenarioId, int page, int pageSize, CancellationToken ct);

    /// Aynı senaryo için Pending veya Running kayıt var mı?
    Task<bool> HasActiveAsync(string scenarioId, CancellationToken ct);

    /// En eski Pending kaydı atomik olarak Running yapıp döndürür (birden fazla worker olsa da çift işlenmez).
    Task<ScenarioExecution?> ClaimNextPendingAsync(DateTime startedAt, CancellationToken ct);

    Task CompleteAsync(string id, DateTime finishedAt, int logCount, int seed, IReadOnlyList<string> correlationIds, CancellationToken ct);
    Task FailAsync(string id, DateTime finishedAt, string error, CancellationToken ct);

    /// Worker çöktüğünde Running'de kalmış kayıtları Failed yapar.
    Task<long> FailStaleRunningAsync(DateTime now, CancellationToken ct);
}
