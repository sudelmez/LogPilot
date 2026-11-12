using System.Text.RegularExpressions;
using LogPilot.AI.Tools;
using LogPilot.Application.Ai;
using LogPilot.Domain.Ai;
using LogPilot.Domain.Chat;

namespace LogPilot.AI.Orchestration;

/// LLM cevabını DOĞRULAYIP kullanıcıya gidecek AssistantAnswer'a çevirir. Halüsinasyona karşı ana savunma burasıdır:
///   • Kanıt ID'leri gerçekten araç sonuçlarında var mı? Yoksa atılır.
///   • Olası neden, atıf yapılan loglardan biriyle desteklenmiyorsa kaldırılır.
///   • Güven puanı LLM'den değil kanıttan hesaplanır.
///   • Kanıtsız "çözüldü" iddiaları temizlenir.
///   • Servis listesi sadece araçların gördüğü servislerle sınırlanır.
public static partial class AnswerBuilder
{
    private const int MaxEvidenceItems = 8;
    private const int MaxActions = 6;
    private const int MaxLimitations = 8;

    [GeneratedRegex(@"\b(resolved|fixed|is now working|back to normal|has recovered|recovered|no longer (occurring|failing|happening))\b", RegexOptions.IgnoreCase)]
    private static partial Regex ResolvedClaim();

    [GeneratedRegex(@"\b(not|cannot|can't|unable|no evidence|unconfirmed|n't|unless|until)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Negation();

    public static AssistantAnswer Build(LlmAnswer llm, IReadOnlyList<ToolInvocation> invocations, ToolContext ctx, SensitiveDataMasker masker)
    {
        var allLogs = invocations.SelectMany(i => i.Logs).DistinctBy(l => l.Id).ToList();
        var known = allLogs.ToDictionary(l => l.Id);
        var limitations = new List<string>();
        foreach (var text in llm.DataLimitations ?? []) AddText(limitations, text);

        // 1) Kanıt ID doğrulaması
        var requested = (llm.EvidenceLogIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct().ToList();
        var verified = requested.Where(known.ContainsKey).ToList();
        if (verified.Count < requested.Count)
            limitations.Add($"{requested.Count - verified.Count} evidence reference(s) proposed by the model could not be verified against the retrieved logs and were removed.");
        var evidenceLogs = verified.Select(id => known[id]).OrderBy(l => l.Timestamp).ToList();

        // 2) Olası neden doğrulaması
        var cause = string.IsNullOrWhiteSpace(llm.ProbableCause) ? null : llm.ProbableCause.Trim();
        var causeCode = string.IsNullOrWhiteSpace(llm.ProbableCauseCode) ? null : llm.ProbableCauseCode.Trim();
        if (cause is not null)
        {
            var supported = causeCode is not null && evidenceLogs.Any(l => string.Equals(l.ErrorCode, causeCode, StringComparison.OrdinalIgnoreCase));
            if (!supported)
            {
                limitations.Add("The model proposed a probable cause that is not supported by any cited log; it was removed.");
                cause = null;
                causeCode = null;
            }
        }

        // 3) Kanıt kartları (backend, doğrulanmış loglardan üretir)
        var evidence = evidenceLogs.Where(l => l.ErrorCode is not null)
            .GroupBy(l => (l.ErrorCode, l.Service))
            .Select(g => new EvidenceItem
            {
                ErrorCode = g.Key.ErrorCode,
                Service = g.Key.Service,
                Count = g.Count(),
                FirstSeen = g.Min(l => l.Timestamp),
                LastSeen = g.Max(l => l.Timestamp),
                LogIds = g.OrderByDescending(l => l.Timestamp).Take(10).Select(l => l.Id).ToList(),
                UserCodes = g.Select(l => l.UserCode).OfType<string>().Distinct().Take(10).ToList(),
                CorrelationIds = g.Select(l => l.CorrelationId).OfType<string>().Distinct().Take(3).ToList()
            })
            .OrderByDescending(e => e.Count).ThenBy(e => e.ErrorCode, StringComparer.Ordinal)
            .Take(MaxEvidenceItems).ToList();

        // 4) Güven (LLM'in "confidence" alanı yok sayılır)
        ConfidenceLevel level;
        var score = 0;
        var factors = new List<string>();
        var hasLaterSuccess = false;
        var hasData = invocations.Any(i => i.Error is null && i.ResultCount > 0);

        if (cause is not null && causeCode is not null)
        {
            var input = EvidenceAnalyzer.BuildInput(causeCode, evidenceLogs, allLogs, invocations.Any(i => i.Truncated));
            var assessment = ConfidenceCalculator.Assess(input);
            level = assessment.Level;
            score = assessment.Score;
            factors = assessment.Factors.ToList();
            hasLaterSuccess = input.HasLaterSuccess;
        }
        else if (!string.IsNullOrWhiteSpace(llm.ClarifyingQuestion)) level = ConfidenceLevel.NotApplicable;
        else if (!hasData) { level = ConfidenceLevel.Insufficient; factors.Add("No data was retrieved for this question."); }
        else level = ConfidenceLevel.NotApplicable;

        // 5) Servisler: sadece araçların gerçekten gördüğü servisler
        var knownServices = invocations.SelectMany(i => i.Services).Concat(allLogs.Select(l => l.Service)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var services = (llm.AffectedServices ?? []).Where(s => knownServices.Contains(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (services.Count == 0 && cause is not null)
            services = evidenceLogs.Where(l => l.ErrorCode is not null).Select(l => l.Service).Distinct().ToList();

        // 6) "Çözüldü" iddiası: açık bir sonraki başarı yoksa kaldır
        var summary = string.IsNullOrWhiteSpace(llm.Summary) ? "I could not produce a summary from the retrieved data." : llm.Summary.Trim();
        var strippedClaim = false;
        if (!hasLaterSuccess)
        {
            summary = StripResolvedClaims(summary, ref strippedClaim);
            if (cause is not null) cause = StripResolvedClaims(cause, ref strippedClaim);
        }
        if (strippedClaim) limitations.Add("The logs do not confirm that the issue is resolved; unsupported claims of resolution were removed.");

        // 7) Platformun eklediği sınırlılıklar
        var detections = invocations.Sum(i => i.InjectionDetections);
        if (detections > 0)
            limitations.Add("Some log content looked like instructions to an AI and was removed before analysis. Log text is treated as untrusted data.");
        if (invocations.Any(i => i.Truncated))
            limitations.Add("Some results were truncated to the tool limits, so the evidence may be incomplete.");
        foreach (var failed in invocations.Where(i => i.Error is not null))
            limitations.Add($"The {failed.ToolName} tool could not run: {failed.Error}");
        if (!hasData && string.IsNullOrWhiteSpace(llm.ClarifyingQuestion))
            limitations.Add("No matching logs, statistics or incidents were found in the selected time range.");
        limitations.Add(ctx.RangeSource is { Length: > 0 } source
            ? $"Time range analysed: {ctx.From:yyyy-MM-dd HH:mm} – {ctx.To:yyyy-MM-dd HH:mm} UTC (taken from the question: \"{source}\")."
            : $"Time range analysed: {ctx.From:yyyy-MM-dd HH:mm} – {ctx.To:yyyy-MM-dd HH:mm} UTC.");

        return new AssistantAnswer
        {
            Summary = masker.MaskRequired(summary),
            ProbableCause = masker.Mask(cause),
            Confidence = level,
            ConfidenceScore = score,
            ConfidenceFactors = factors,
            Evidence = evidence,
            EvidenceLogIds = verified,
            AffectedServices = services,
            SuggestedActions = Clean(llm.SuggestedActions, MaxActions, masker),
            DataLimitations = Clean(limitations, MaxLimitations, masker),
            ClarifyingQuestion = masker.Mask(string.IsNullOrWhiteSpace(llm.ClarifyingQuestion) ? null : llm.ClarifyingQuestion.Trim())
        };
    }

    /// Görev alanı dışı soru için sabit, kibar ret. Model ne yazmış olursa olsun metni platform belirler (tutarlı ve güvenli).
    public static AssistantAnswer Refusal(Security.ScopeGuard.Verdict verdict, string question) => new()
    {
        Summary = Security.ScopeGuard.RefusalMessage(verdict, question),
        Confidence = ConfidenceLevel.NotApplicable,
        OutOfScope = verdict.ToString()
    };

    private static string StripResolvedClaims(string text, ref bool stripped)
    {
        var sentences = Regex.Split(text, @"(?<=[.!?])\s+");
        var kept = new List<string>();
        foreach (var sentence in sentences)
        {
            if (ResolvedClaim().IsMatch(sentence) && !Negation().IsMatch(sentence)) { stripped = true; continue; }
            kept.Add(sentence);
        }

        return kept.Count == 0 ? "The logs do not confirm whether the issue is resolved." : string.Join(" ", kept);
    }

    private static List<string> Clean(IEnumerable<string>? items, int max, SensitiveDataMasker masker)
        => (items ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => masker.MaskRequired(s.Trim()))
            .Distinct().Take(max).ToList();

    private static void AddText(List<string> list, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text)) list.Add(text.Trim());
    }
}
