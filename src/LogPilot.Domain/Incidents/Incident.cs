namespace LogPilot.Domain.Incidents;

public enum IncidentStatus { Open, Investigating, Resolved }
public enum IncidentSeverity { Low, Medium, High, Critical }

/// Aynı hata kodunu paylaşan hata loglarının gruplanmış hali. Tespit kural tabanlıdır (Faz 6).
public sealed class Incident
{
    public string? Id { get; init; }
    public required string Title { get; set; }
    public required string ErrorCode { get; init; }
    public required IncidentSeverity Severity { get; set; }
    public required IncidentStatus Status { get; set; }
    public List<string> AffectedServices { get; set; } = [];
    public List<string> AffectedUserCodes { get; set; } = [];
    public List<string> RelatedLogIds { get; set; } = [];        // en yeni ≤ 50 log
    public long LogCount { get; set; }
    public required DateTime FirstSeenAt { get; init; }
    public required DateTime LastSeenAt { get; set; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolutionNote { get; set; }                  // çözülürken yazılan not (vaka hafızasına da girer)
    public string? AiSummary { get; set; }
    public DateTime? SummaryGeneratedAt { get; set; }
    public long SummaryLogCount { get; set; }                    // özet üretildiğindeki log sayısı (ne zaman yenileneceğini bilmek için)
}
