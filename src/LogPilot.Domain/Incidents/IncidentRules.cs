using LogPilot.Domain.Analytics;
using LogPilot.Domain.Logs;

namespace LogPilot.Domain.Incidents;

/// Kural tabanlı incident tespiti. Saf fonksiyonlar: DB veya LLM gerektirmez, birim testi kolaydır.
public static class IncidentRules
{
    /// Bir hata kodu, pencerede en az bu kadar log ürettiyse incident açılır.
    public const int MinOccurrences = 3;

    /// Bu kodlar (birden çok kullanıcıyı etkilediğinde) doğrudan Critical sayılır.
    public static readonly IReadOnlySet<string> CriticalCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "MONGO_CONNECTION_FAILED"
    };

    /// Semptom kodları (LOGIN_FAILED vb.) ayrı incident açmaz: kök neden kodunun incident'ı zaten kapsar.
    public static bool ShouldOpen(ErrorGroup group)
        => !ErrorCodeCatalog.IsSymptom(group.ErrorCode) && group.Count >= MinOccurrences;

    public static IncidentSeverity Classify(ErrorGroup group)
    {
        var users = group.UserCodes.Count;
        var services = group.Services.Count;

        if (users >= 5 || (CriticalCodes.Contains(group.ErrorCode) && users >= 3)) return IncidentSeverity.Critical;
        if (users >= 3 || services >= 3) return IncidentSeverity.High;
        if (users >= 2 || group.Count >= 5) return IncidentSeverity.Medium;
        return IncidentSeverity.Low;
    }

    public static string Title(ErrorGroup group)
        => $"{group.ErrorCode}: {group.UserCodes.Count} user(s), {group.Services.Count} service(s) affected";
}
