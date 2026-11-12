using LogPilot.Application.Analytics;
using LogPilot.Domain.Analytics;
using LogPilot.Domain.Incidents;
using Microsoft.Extensions.Logging;

namespace LogPilot.Application.Incidents;

public sealed record DetectionResult(int Created, int Updated);

/// Periyodik çalışır: son N dakikadaki hata gruplarına bakar; yeni incident açar veya var olanı günceller.
/// Hiçbir zaman otomatik "Resolved" yapmaz: çözüldüğünü kanıtlayan bir veri yoktur, bunu kullanıcı işaretler.
public sealed class IncidentDetectionService(
    ILogAnalyticsRepository analytics,
    IIncidentRepository incidents,
    IIncidentSummarizer summarizer,
    ILogger<IncidentDetectionService> logger)
{
    /// autoSummarize=false: incident kayıtları güncellenir ama AI özeti üretilmez (yerel model/maliyet). Elle "Regenerate" yine çalışır.
    public async Task<DetectionResult> DetectAsync(DateTime nowUtc, int windowMinutes, CancellationToken ct, bool autoSummarize = true)
    {
        var window = new TimeRange(nowUtc.AddMinutes(-windowMinutes), nowUtc);
        var groups = await analytics.GetErrorGroupsAsync(window, null, ct);

        var created = 0;
        var updated = 0;

        foreach (var group in groups)
        {
            var existing = await incidents.FindActiveByErrorCodeAsync(group.ErrorCode, ct);

            if (existing is null)
            {
                if (!IncidentRules.ShouldOpen(group)) continue;

                var incident = await incidents.InsertAsync(new Incident
                {
                    Title = IncidentRules.Title(group),
                    ErrorCode = group.ErrorCode,
                    Severity = IncidentRules.Classify(group),
                    Status = IncidentStatus.Open,
                    AffectedServices = group.Services.ToList(),
                    AffectedUserCodes = group.UserCodes.ToList(),
                    RelatedLogIds = group.RecentLogIds.ToList(),
                    LogCount = group.Count,
                    FirstSeenAt = group.FirstSeen,
                    LastSeenAt = group.LastSeen,
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc
                }, ct);

                if (autoSummarize) await TrySummarizeAsync(incident, nowUtc, ct);
                created++;
                logger.LogInformation("Incident opened for {ErrorCode}: {Severity}, {Count} log(s), {Users} user(s).",
                    incident.ErrorCode, incident.Severity, incident.LogCount, incident.AffectedUserCodes.Count);
                continue;
            }

            // Açık incident'ı tüm ömrü boyunca birleşik görünümle güncelle (pencere sadece son N dakikadır).
            var lifetime = (await analytics.GetErrorGroupsAsync(new TimeRange(existing.FirstSeenAt, nowUtc.AddSeconds(1)), group.ErrorCode, ct))
                .FirstOrDefault() ?? group;

            if (existing.LogCount == lifetime.Count && existing.LastSeenAt == lifetime.LastSeen) continue;

            existing.LogCount = lifetime.Count;
            existing.LastSeenAt = lifetime.LastSeen;
            existing.AffectedUserCodes = existing.AffectedUserCodes.Union(lifetime.UserCodes, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            existing.AffectedServices = existing.AffectedServices.Union(lifetime.Services, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            existing.RelatedLogIds = lifetime.RecentLogIds.ToList();
            existing.Severity = (IncidentSeverity)Math.Max((int)existing.Severity, (int)IncidentRules.Classify(lifetime));   // otomatik düşürme yok
            existing.Title = IncidentRules.Title(lifetime);
            existing.UpdatedAt = nowUtc;

            await incidents.ReplaceAsync(existing, ct);

            // Olay büyüdüyse (log sayısı ikiye katlandıysa) özeti yenile.
            if (existing.AiSummary is null || existing.LogCount >= existing.SummaryLogCount * 2)
                if (autoSummarize) await TrySummarizeAsync(existing, nowUtc, ct);

            updated++;
        }

        return new DetectionResult(created, updated);
    }

    private async Task TrySummarizeAsync(Incident incident, DateTime nowUtc, CancellationToken ct)
    {
        try
        {
            var summary = await summarizer.SummarizeAsync(new IncidentSummaryInput(
                incident.ErrorCode, incident.Severity.ToString(), incident.LogCount,
                incident.AffectedUserCodes, incident.AffectedServices, incident.FirstSeenAt, incident.LastSeenAt), ct);
            if (string.IsNullOrWhiteSpace(summary)) return;

            incident.AiSummary = summary;
            incident.SummaryGeneratedAt = nowUtc;
            incident.SummaryLogCount = incident.LogCount;
            await incidents.ReplaceAsync(incident, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Özet üretilemese de incident kaydı kalır.
            logger.LogWarning(ex, "Could not generate an AI summary for incident {ErrorCode}.", incident.ErrorCode);
        }
    }
}
