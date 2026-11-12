using LogPilot.AI.Tools;
using LogPilot.Application.Ai;

namespace LogPilot.UnitTests.Ai;

internal static class TestData
{
    public static readonly DateTime From = new(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime To = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);
    public static readonly ToolContext Context = new(From, To);

    public static LogSnippet Snippet(
        string id, string level = "Error", string service = "AuthenticationService", string? user = "TCLI",
        string? correlation = "COR-20261108-AAAAAA", string? errorCode = "TOKEN_EXPIRED", string? outcome = null,
        int minute = 0, string message = "Access token expired.")
        => new(id, From.AddHours(6).AddMinutes(minute), level, "MobileApp", service, user, correlation, "TRX-100000",
            "Login", outcome, errorCode, null, message, null, new Dictionary<string, string>());

    public static ToolInvocation Invocation(params LogSnippet[] logs)
        => new("SearchLogs", "{}", "", logs, logs.Select(l => l.Service).Distinct().ToList(), logs.Length, false, 0, null, 1);
}
