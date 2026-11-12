using System.Text.Json;
using LogPilot.AI.Providers;

namespace LogPilot.AI.Orchestration;

/// LLM'in döndürdüğü HAM cevap. Güvenilmez kabul edilir: AnswerBuilder doğrulamadan kullanıcıya gitmez.
public sealed class LlmAnswer
{
    public string? Summary { get; set; }
    public string? ProbableCause { get; set; }
    public string? ProbableCauseCode { get; set; }
    public List<string>? EvidenceLogIds { get; set; }
    public List<string>? AffectedServices { get; set; }
    public List<string>? SuggestedActions { get; set; }
    public List<string>? DataLimitations { get; set; }
    public string? ClarifyingQuestion { get; set; }
    public string? Confidence { get; set; }     // YOK SAYILIR: güven backend'de hesaplanır
    public bool? OutOfScope { get; set; }       // model soruyu görev alanı dışı buldu (ikinci savunma hattı)
    public bool? NotPermitted { get; set; }     // model veri değiştirme talebi gördü (rolü dışında)
}

public static class AnswerParser
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// Kod çiti (```json) veya önsöz olsa bile ilk '{' ile son '}' arasını okur. Başarısızsa null.
    public static LlmAnswer? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        try { return JsonSerializer.Deserialize<LlmAnswer>(text[start..(end + 1)], Options); }
        catch (JsonException) { return null; }
    }
}
