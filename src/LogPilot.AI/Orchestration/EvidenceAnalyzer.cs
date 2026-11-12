using LogPilot.Application.Ai;
using LogPilot.Domain.Ai;
using LogPilot.Domain.Logs;

namespace LogPilot.AI.Orchestration;

/// Toplanan kanıttan, güven hesabının girdilerini çıkarır (LLM'e güvenmeden).
public static class EvidenceAnalyzer
{
    public static ConfidenceInput BuildInput(
        string causeCode, IReadOnlyList<LogSnippet> evidenceLogs, IReadOnlyList<LogSnippet> allLogs, bool truncated)
    {
        var errorLogs = evidenceLogs.Where(l => l.ErrorCode is not null).ToList();
        var causeLogs = errorLogs.Where(l => string.Equals(l.ErrorCode, causeCode, StringComparison.OrdinalIgnoreCase)).ToList();

        // Pay, semptom olmayan hata kodları içinde hesaplanır (LOGIN_FAILED gibi yankılar payı sulandırmasın).
        var comparable = ErrorCodeCatalog.IsSymptom(causeCode)
            ? errorLogs
            : errorLogs.Where(l => !ErrorCodeCatalog.IsSymptom(l.ErrorCode)).ToList();
        var share = comparable.Count == 0 ? 0 : (double)causeLogs.Count / comparable.Count;

        // Zincir: neden loglarının correlationId'leri, retrieve edilen tüm loglarda ≥2 serviste iz bırakmış mı?
        var flows = causeLogs.Select(l => l.CorrelationId).OfType<string>().ToHashSet();
        var chain = allLogs.Where(l => l.CorrelationId is not null && flows.Contains(l.CorrelationId))
            .GroupBy(l => l.CorrelationId).Any(g => g.Select(l => l.Service).Distinct().Count() >= 2);

        var users = causeLogs.Select(l => l.UserCode).OfType<string>().Distinct().ToList();

        // Düzelme: etkilenen bir kullanıcıda, o kullanıcının son neden logundan SONRA başarı var mı?
        var laterSuccess = users.Any(user =>
        {
            var lastCause = causeLogs.Where(l => l.UserCode == user).Max(l => l.Timestamp);
            return allLogs.Any(l => l.UserCode == user && l.Outcome == "Success" && l.Timestamp > lastCause);
        });

        return new ConfidenceInput(
            CauseEvidenceCount: causeLogs.Count,
            CauseIsSpecific: !ErrorCodeCatalog.IsSymptom(causeCode),
            HasCrossServiceChain: chain,
            DistinctUsersWithCause: users.Count,
            CauseShareOfErrors: share,
            HasLaterSuccess: laterSuccess,
            ResultsTruncated: truncated);
    }
}
