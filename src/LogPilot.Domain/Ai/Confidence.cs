namespace LogPilot.Domain.Ai;

public enum ConfidenceLevel
{
    NotApplicable,   // kök neden iddiası yok (ör. "hangi servisin hata oranı yüksek?" gibi olgusal cevap)
    Insufficient,    // kanıt yok
    Low,
    Medium,
    High
}

/// Güven puanının girdileri: LLM'den DEĞİL, toplanan kanıttan hesaplanır.
public sealed record ConfidenceInput(
    int CauseEvidenceCount,          // olası nedenin hata koduyla eşleşen log sayısı
    bool CauseIsSpecific,            // semptom değil, açıklayıcı bir kod mu?
    bool HasCrossServiceChain,       // aynı correlationId zinciri ≥ 2 serviste iz bırakmış mı?
    int DistinctUsersWithCause,
    double CauseShareOfErrors,       // kanıttaki (semptom olmayan) hata loglarında bu kodun payı (0..1)
    bool HasLaterSuccess,            // etkilenen kullanıcıda hatadan SONRA başarı var mı? (geçici/düzelmiş olabilir)
    bool ResultsTruncated);          // araç sonuçları kesildi mi? (kanıt eksik olabilir)

public sealed record ConfidenceAssessment(ConfidenceLevel Level, int Score, IReadOnlyList<string> Factors);

/// Tanımlı, açıklanabilir, deterministik güven hesabı. Toplam 0–95; kanıt varsa en az 1.
/// High için tek bir log yetmez: birden çok log + birden çok kullanıcı gibi doğrulama gerekir.
public static class ConfidenceCalculator
{
    public const int HighThreshold = 70;
    public const int MediumThreshold = 40;

    public static ConfidenceAssessment Assess(ConfidenceInput input)
    {
        if (input.CauseEvidenceCount <= 0)
            return new ConfidenceAssessment(ConfidenceLevel.Insufficient, 0, ["No log evidence supports a probable cause."]);

        var score = 0;
        var factors = new List<string>();

        void Add(int points, string reason)
        {
            score += points;
            factors.Add($"{(points >= 0 ? "+" : "")}{points}: {reason}");
        }

        // Kanıt miktarı
        var count = input.CauseEvidenceCount;
        Add(count >= 5 ? 25 : count >= 2 ? 15 : 5, $"{count} supporting log(s)");

        // Kanıt kalitesi
        if (input.CauseIsSpecific) Add(30, "a specific error code explains the failure");
        else factors.Add("+0: only a generic symptom code was found, not a root cause");

        if (input.HasCrossServiceChain) Add(20, "one correlation chain spans multiple services");

        // Tutarlılık
        if (input.CauseShareOfErrors >= 0.8) Add(10, "evidence is consistent (one error code dominates)");
        else if (input.CauseShareOfErrors < 0.5) Add(-15, "evidence is mixed (several competing error codes)");

        if (input.DistinctUsersWithCause >= 2) Add(10, $"corroborated by {input.DistinctUsersWithCause} different users");

        // Çelişki / eksiklik
        if (input.HasLaterSuccess) Add(-25, "a later success for an affected user suggests the issue may be transient or recovered");
        if (input.ResultsTruncated) Add(-5, "tool results were truncated, so evidence may be incomplete");

        score = Math.Clamp(score, 1, 100);
        var level = score >= HighThreshold ? ConfidenceLevel.High
                  : score >= MediumThreshold ? ConfidenceLevel.Medium
                  : ConfidenceLevel.Low;

        return new ConfidenceAssessment(level, score, factors);
    }
}
