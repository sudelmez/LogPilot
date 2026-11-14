using System.Text.Json;

namespace LogPilot.AI.Security;

/// Uydurma kimliklere karşı koruma. Küçük modeller bazen soruda olmayan bir teklif/poliçe/korelasyon numarası uydurur
/// ("TKL-1234567890") ve boş sonuçla "kayıt yok" der. Kimlik türündeki her argüman, ya kullanıcının sorusunda/geçmişinde
/// ya da önceki bir araç sonucunda GEÇMİŞ olmalıdır; değilse araç çalıştırılmaz ve modele düzeltme fırsatı verilir.
public static class ArgumentGrounding
{
    /// Değeri dışarıdan gelmesi gereken argümanlar (filtre değerleri — errorCode, product, level… — bu listede değil).
    private static readonly HashSet<string> IdentifierArguments = new(StringComparer.OrdinalIgnoreCase)
    {
        "userCode", "customerNo", "quoteId", "policyNo", "correlationId", "transactionId", "logId", "incidentId",
        "proposalNo", "clientReferenceCode"
    };

    /// null: argümanlar temellendirilmiş. Aksi halde modele gidecek açıklayıcı hata.
    public static string? Check(string? argumentsJson, string knownText, string? questionIdentifiers)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson)) return null;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(argumentsJson); }
        catch (JsonException) { return null; }   // biçim hatası ToolRegistry'de raporlanır

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (!IdentifierArguments.Contains(p.Name) || p.Value.ValueKind != JsonValueKind.String) continue;
                var value = p.Value.GetString()?.Trim();
                if (string.IsNullOrEmpty(value) || knownText.Contains(value, StringComparison.OrdinalIgnoreCase)) continue;

                return $"'{p.Name}' = '{value}' does not appear in the user's question or in any earlier tool result: do not invent identifiers. " +
                       (questionIdentifiers is null
                           ? "Use only values you were given, or call a tool that can find them."
                           : $"Identifiers in the question: {questionIdentifiers}. Use exactly these.");
            }
        }
        return null;
    }
}
