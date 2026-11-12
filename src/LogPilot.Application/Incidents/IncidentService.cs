using LogPilot.Application.Common;
using LogPilot.Application.Knowledge;
using LogPilot.Application.Logs;
using LogPilot.Domain.Incidents;
using LogPilot.Domain.Logs;

namespace LogPilot.Application.Incidents;

public sealed class IncidentService(
    IIncidentRepository incidents,
    ILogRepository logs,
    IIncidentSummarizer summarizer,
    TimeProvider time,
    KnowledgeService? knowledge = null)
{
    public const int MaxPageSize = 100;

    public Task<PagedResult<Incident>> SearchAsync(
        IncidentStatus? status, IncidentSeverity? severity, bool? activeOnly, int page, int pageSize, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1) errors["Page"] = ["Page must be at least 1."];
        if (pageSize is < 1 or > MaxPageSize) errors["PageSize"] = [$"PageSize must be between 1 and {MaxPageSize}."];
        if (errors.Count > 0) throw new RequestValidationException(errors);

        return incidents.SearchAsync(status, severity, activeOnly ?? false, page, pageSize, ct);
    }

    public Task<Incident?> GetAsync(string id, CancellationToken ct) => incidents.GetByIdAsync(id, ct);

    public async Task<IReadOnlyList<LogEntry>> GetRelatedLogsAsync(string id, CancellationToken ct)
    {
        var incident = await incidents.GetByIdAsync(id, ct) ?? throw new NotFoundException($"Incident '{id}' was not found.");
        return await logs.GetByIdsAsync(incident.RelatedLogIds, ct);
    }

    public Task<Incident> UpdateStatusAsync(string id, IncidentStatus status, CancellationToken ct)
        => UpdateStatusAsync(id, status, null, null, ct);

    /// Resolved + çözüm notu: not incident'a yazılır ve doğrulanmış vaka olarak hafızaya girer.
    public async Task<Incident> UpdateStatusAsync(
        string id, IncidentStatus status, string? resolutionNote, string? rootCause, CancellationToken ct)
    {
        if (!Enum.IsDefined(status))
            throw new RequestValidationException(new Dictionary<string, string[]> { ["Status"] = ["Status must be Open, Investigating or Resolved."] });
        if (!string.IsNullOrWhiteSpace(resolutionNote) && status != IncidentStatus.Resolved)
            throw new RequestValidationException(new Dictionary<string, string[]> { ["ResolutionNote"] = ["A resolution note can only be given when resolving."] });

        if (!await incidents.UpdateStatusAsync(id, status, time.GetUtcNow().UtcDateTime, ct))
            throw new NotFoundException($"Incident '{id}' was not found.");

        var incident = await incidents.GetByIdAsync(id, ct) ?? throw new NotFoundException($"Incident '{id}' was not found.");

        if (status == IncidentStatus.Resolved && !string.IsNullOrWhiteSpace(resolutionNote) && knowledge is not null)
        {
            var knowledgeCase = await knowledge.RecordIncidentResolutionAsync(incident, resolutionNote, rootCause, ct);
            incident.ResolutionNote = knowledgeCase?.Resolution;   // maskelenmiş hali
            await incidents.ReplaceAsync(incident, ct);
        }

        return incident;
    }

    public async Task<Incident> RegenerateSummaryAsync(string id, CancellationToken ct)
    {
        var incident = await incidents.GetByIdAsync(id, ct) ?? throw new NotFoundException($"Incident '{id}' was not found.");

        var summary = await summarizer.SummarizeAsync(new IncidentSummaryInput(
            incident.ErrorCode, incident.Severity.ToString(), incident.LogCount,
            incident.AffectedUserCodes, incident.AffectedServices, incident.FirstSeenAt, incident.LastSeenAt), ct);

        if (string.IsNullOrWhiteSpace(summary))
            throw new ConflictException("An AI summary could not be generated right now.");

        incident.AiSummary = summary;
        incident.SummaryGeneratedAt = time.GetUtcNow().UtcDateTime;
        incident.SummaryLogCount = incident.LogCount;
        await incidents.ReplaceAsync(incident, ct);
        return incident;
    }
}
