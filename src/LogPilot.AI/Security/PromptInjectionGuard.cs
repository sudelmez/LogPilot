using System.Text.RegularExpressions;

namespace LogPilot.AI.Security;

public sealed record GuardResult(string Text, int Detections);

/// Log içeriği güvenilmezdir. Talimat gibi görünen kalıpları LLM'e gitmeden temizler ve sayar.
/// Bu YALNIZCA ikinci savunma hattıdır; asıl koruma mimaridir: araçlar salt-okunur, çıktı doğrulanır,
/// kanıt ID'leri ve güven puanı backend'de hesaplanır.
public sealed class PromptInjectionGuard
{
    public const string Replacement = "[REMOVED: instruction-like text]";

    private static readonly Regex[] Patterns = new[]
    {
        @"ignore\s+(all\s+|any\s+)?(the\s+)?(previous|prior|above|earlier)\s+(instructions?|prompts?|rules?)",
        @"disregard\s+(all\s+|the\s+|any\s+)?(previous|prior|above|system|earlier)",
        @"(forget|override)\s+(everything|all|your|the)\s+(instructions?|rules?|above|previous)",
        @"you\s+are\s+now\s+(a|an|the)\b",
        @"(reveal|print|show|repeat|leak)\s+(the\s+|your\s+)?(system\s+)?(prompt|instructions)",
        @"\[\s*(system|inst)\s*\]|<\s*/?\s*(system|assistant|tool_call|untrusted_log_data)\s*>|<\|im_(start|end)\|>",
        @"(mark|say|state|report|declare|conclude|tell\s+the\s+user)\b[^.\n]{0,40}\b(resolved|fixed|no\s+issues?)",
        @"(call|invoke|run|execute)\s+(the\s+)?(tool|function|command|query|sql|code|script)\b",
        @"do\s+not\s+(tell|mention|reveal|inform)",
        @"new\s+instructions?\s*:"
    }.Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)).ToArray();

    public GuardResult Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return new GuardResult(text ?? "", 0);

        var result = text;
        var detections = 0;
        foreach (var pattern in Patterns)
        {
            result = pattern.Replace(result, _ =>
            {
                detections++;
                return Replacement;
            });
        }

        return new GuardResult(result, detections);
    }
}
