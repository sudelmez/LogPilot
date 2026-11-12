using System.Text.RegularExpressions;
using System.Text.Json;
using LogPilot.AI.Prompts;
using LogPilot.AI.Providers;
using LogPilot.AI.Security;
using LogPilot.Application.Ai;
using LogPilot.Application.Incidents;
using Microsoft.Extensions.Logging;

namespace LogPilot.AI.Incidents;

/// Incident özeti: LLM'e sadece YAPILANDIRILMIŞ olgular gider (serbest log metni yok), çıktı maskelenir ve
/// kanıtsız "çözüldü" iddialarından arındırılır.
public sealed partial class LlmIncidentSummarizer(
    ILlmProvider llm,
    PromptInjectionGuard guard,
    SensitiveDataMasker masker,
    ILogger<LlmIncidentSummarizer> logger) : IIncidentSummarizer
{
    private const int MaxLength = 800;

    // NOT: AnswerBuilder'daki "çözüldü" koruması ile aynı kalıplar (ileride ortak bir sınıfa taşınabilir).
    [GeneratedRegex(@"\b(resolved|fixed|is now working|back to normal|has recovered|recovered|no longer (occurring|failing|happening))\b", RegexOptions.IgnoreCase)]
    private static partial Regex ResolvedClaim();

    [GeneratedRegex(@"\b(not|cannot|can't|unable|no evidence|does not|doesn't|n't|unless|until)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Negation();

    public async Task<string?> SummarizeAsync(IncidentSummaryInput input, CancellationToken ct)
    {
        var facts = new IncidentFacts(
            Clean(input.ErrorCode), input.Severity, input.LogCount,
            input.UserCodes.Take(20).Select(Clean).ToList(), input.Services.Take(20).Select(Clean).ToList(),
            input.FirstSeen, input.LastSeen);

        var content = $"<untrusted_log_data tool=\"IncidentFacts\">\n{JsonSerializer.Serialize(facts, ToolJson.Options)}\n</untrusted_log_data>";

        var response = await llm.CompleteAsync(
            new LlmRequest(SystemPrompts.IncidentSummary(), [new LlmMessage(LlmRole.User, content)], [], LlmPurpose.IncidentSummary), ct);

        var text = response.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return null;

        var kept = Regex.Split(text, @"(?<=[.!?])\s+")
            .Where(s => !(ResolvedClaim().IsMatch(s) && !Negation().IsMatch(s)))
            .ToList();
        if (kept.Count < Regex.Split(text, @"(?<=[.!?])\s+").Length)
            logger.LogWarning("Removed an unsupported 'resolved' claim from an incident summary.");

        var cleaned = masker.MaskRequired(string.Join(" ", kept));
        return cleaned.Length <= MaxLength ? cleaned : cleaned[..(MaxLength - 1)] + "…";
    }

    private string Clean(string value) => guard.Sanitize(value).Text;
}
