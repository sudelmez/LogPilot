namespace LogPilot.Domain.Scenarios;

public enum ExecutionStatus { Pending, Running, Completed, Failed }
public enum ExecutionTrigger { Schedule, Manual }

/// Bir senaryo çalıştırmasının kaydı. Aynı zamanda API → Generator komut kuyruğudur (Status = Pending).
public sealed class ScenarioExecution
{
    public string? Id { get; init; }
    public required string ScenarioId { get; init; }
    public required ExecutionTrigger TriggeredBy { get; init; }
    public ExecutionStatus Status { get; init; }
    public required DateTime RequestedAt { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
    public int? Seed { get; init; }
    public int GeneratedLogCount { get; init; }
    public List<string> CorrelationIds { get; init; } = [];
    public string? Error { get; init; }
}
