namespace LogPilot.Application.Incidents;

/// Incident için yapay zekâ özeti. Uygulaması AI projesindedir; Application LLM'den habersiz kalır.
public sealed record IncidentSummaryInput(
    string ErrorCode, string Severity, long LogCount,
    IReadOnlyList<string> UserCodes, IReadOnlyList<string> Services, DateTime FirstSeen, DateTime LastSeen);

public interface IIncidentSummarizer
{
    /// Özet üretilemezse null döner (tespit akışı bundan etkilenmez).
    Task<string?> SummarizeAsync(IncidentSummaryInput input, CancellationToken ct);
}
