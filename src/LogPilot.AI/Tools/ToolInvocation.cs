using LogPilot.Application.Ai;

namespace LogPilot.AI.Tools;

/// Bir araç çağrısının sonucu: LLM'e giden metin + backend'in doğrulama için sakladığı yapılandırılmış veri.
public sealed record ToolInvocation(
    string ToolName,
    string ArgumentsJson,
    string WrappedResult,
    IReadOnlyList<LogSnippet> Logs,
    IReadOnlyList<string> Services,
    int ResultCount,
    bool Truncated,
    int InjectionDetections,
    string? Error,
    long DurationMs);
