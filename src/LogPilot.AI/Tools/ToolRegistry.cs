using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using LogPilot.AI.Providers;
using LogPilot.AI.Security;
using LogPilot.Application.Ai;

namespace LogPilot.AI.Tools;

/// LLM'in araç adı + JSON argümanını, doğrulanmış ILogAnalysisTools çağrılarına çevirir.
/// Sonuçlar enjeksiyon temizliğinden geçirilip <untrusted_log_data> içinde LLM'e verilir.
public sealed class ToolRegistry(ILogAnalysisTools tools, PromptInjectionGuard guard)
{
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(10);

    public IReadOnlyList<LlmToolDefinition> Definitions { get; } =
    [
        new(ToolNames.SearchLogs, "Search APP logs (collection `logs`: mobile app, MobileGateway, Authentication, NotificationApp OTP flow, ProposalApp). Does NOT contain quote engine or SMS gateway delivery logs. Newest first, max 50.",
            """{"type":"object","additionalProperties":false,"properties":{"service":{"type":"string"},"application":{"type":"string"},"userCode":{"type":"string"},"errorCode":{"type":"string"},"level":{"type":"string","enum":["Debug","Information","Warning","Error","Critical"]},"correlationId":{"type":"string"},"transactionId":{"type":"string"},"from":{"type":"string","format":"date-time"},"to":{"type":"string","format":"date-time"},"limit":{"type":"integer","minimum":1,"maximum":50}}}"""),
        new(ToolNames.GetUserRecentErrors, "APP logs only: a user's recent warnings/errors plus their latest success (to see whether they recovered).",
            """{"type":"object","additionalProperties":false,"required":["userCode"],"properties":{"userCode":{"type":"string"},"limit":{"type":"integer","minimum":1,"maximum":50}}}"""),
        new(ToolNames.GetServiceHealth, "Per-service log counts, error ratio and health status for the selected range.",
            """{"type":"object","additionalProperties":false,"properties":{"service":{"type":"string"}}}"""),
        new(ToolNames.GetErrorStatistics, "Error/Critical logs grouped by error code with affected users and services.",
            """{"type":"object","additionalProperties":false,"properties":{"limit":{"type":"integer","minimum":1,"maximum":20}}}"""),
        new(ToolNames.FindRelatedLogs, "All logs of one flow across BOTH collections (app logs + quote_logs), by correlationId, transactionId, quoteId (TKL-...) or a seed logId.",
            """{"type":"object","additionalProperties":false,"properties":{"correlationId":{"type":"string"},"transactionId":{"type":"string"},"quoteId":{"type":"string"},"logId":{"type":"string"}}}"""),
        new(ToolNames.GetActiveIncidents, "Unresolved incidents seen in the selected range.",
            """{"type":"object","additionalProperties":false,"properties":{"limit":{"type":"integer","minimum":1,"maximum":20}}}"""),
        new(ToolNames.GetIncidentDetails, "Details and sample logs of one incident.",
            """{"type":"object","additionalProperties":false,"required":["incidentId"],"properties":{"incidentId":{"type":"string"}}}"""),
        new(ToolNames.SearchKnowledgeBase, "Past cases that people verified as solved (root cause + what fixed it). Search by the error code you found. Hints only, not evidence.",
            """{"type":"object","additionalProperties":false,"properties":{"errorCode":{"type":"string"},"service":{"type":"string"},"text":{"type":"string","maxLength":100},"limit":{"type":"integer","minimum":1,"maximum":5}}}"""),
        new(ToolNames.TraceCustomerJourney, "Cross-collection trace of ONE customer: resolves the user directory, then merges their app logs and ALL their quote_logs (mobile, web by identity number, agent by customerNo) into one timeline, links flows by correlationId/quoteId and returns platform-computed findings (e.g. 'never reached QuoteEngine', root error per quote). Start here for any quote question about a specific user/customer/quote.",
            """{"type":"object","additionalProperties":false,"properties":{"userCode":{"type":"string"},"customerNo":{"type":"string"},"quoteId":{"type":"string"},"limit":{"type":"integer","minimum":10,"maximum":60}}}"""),
        new(ToolNames.SearchQuoteLogs, "Search insurance quote engine logs (collection `quote_logs`). userCode/customerNo are resolved through the user directory and matched on all customer keys. Newest first, max 50.",
            """{"type":"object","additionalProperties":false,"properties":{"quoteId":{"type":"string"},"userCode":{"type":"string"},"customerNo":{"type":"string"},"product":{"type":"string","enum":["Kasko","Trafik"]},"channel":{"type":"string","enum":["Mobile","Web","Agent"]},"insurerCode":{"type":"string"},"step":{"type":"string","enum":["QuoteRequested","CustomerVerification","VehicleLookup","RiskScoring","InsurerPricing","QuoteIssued","QuoteFailed"]},"errorCode":{"type":"string"},"level":{"type":"string","enum":["Debug","Information","Warning","Error","Critical"]},"correlationId":{"type":"string"},"from":{"type":"string","format":"date-time"},"to":{"type":"string","format":"date-time"},"limit":{"type":"integer","minimum":1,"maximum":50}}}"""),
        new(ToolNames.GetQuoteFunnel, "Aggregate quote statistics from quote_logs: requested/issued/failed/partial counts, per-step failures, per-insurer pricing success and latency, top error codes with sample logs. Use for 'how many / which insurer / which step' questions.",
            """{"type":"object","additionalProperties":false,"properties":{"product":{"type":"string","enum":["Kasko","Trafik"]},"channel":{"type":"string","enum":["Mobile","Web","Agent"]},"insurerCode":{"type":"string"}}}"""),
        new(ToolNames.TraceNotificationDelivery, "Multi-hop: did a notification SMS (default: the POLICY_ISSUED SMS) reach the customer? Follows quote_logs (quoteId/policyNo → owner's identityNo) → users (identityNo → phone) → sms_logs (phone → messages and delivery reports). Returns the path it followed hop by hop, a verdict (Delivered | NotDeliveredToHandset | FailedToSend | Pending | NeverSent | NoPolicy | Unknown), findings and evidence. Start with a quoteId, policyNo, userCode or customerNo.",
            """{"type":"object","additionalProperties":false,"properties":{"quoteId":{"type":"string"},"policyNo":{"type":"string"},"userCode":{"type":"string"},"customerNo":{"type":"string"},"templateCode":{"type":"string","enum":["POLICY_ISSUED","QUOTE_READY","OTP"]}}}"""),
        new(ToolNames.SearchSmsLogs, "Search SMS gateway logs (collection `sms_logs`). The SMS system only knows phone numbers: give userCode, customerNo or quoteId and the platform resolves the phone through users. Without a person it searches all phones (use onlyProblems=true for failures).",
            """{"type":"object","additionalProperties":false,"properties":{"userCode":{"type":"string"},"customerNo":{"type":"string"},"quoteId":{"type":"string"},"templateCode":{"type":"string","enum":["POLICY_ISSUED","QUOTE_READY","OTP"]},"status":{"type":"string","enum":["Queued","SentToProvider","Delivered","Failed","Undelivered"]},"errorCode":{"type":"string"},"onlyProblems":{"type":"boolean"},"limit":{"type":"integer","minimum":1,"maximum":50}}}"""),
        new(ToolNames.GetCustomerProfile, "Look up the user directory (collection `users`) by userCode, customerNo or quoteId: returns userCode, customerNo, status, roles and MASKED identity number and phone. Use it to map between the keys of the log collections.",
            """{"type":"object","additionalProperties":false,"properties":{"userCode":{"type":"string"},"customerNo":{"type":"string"},"quoteId":{"type":"string"}}}""")
    ];

    public async Task<ToolInvocation> InvokeAsync(LlmToolCall call, ToolContext ctx, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var json = string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ToolTimeout);
            var token = cts.Token;

            switch (call.Name)
            {
                case ToolNames.SearchLogs:
                {
                    var r = await tools.SearchLogsAsync(Parse<SearchLogsArgs>(json), ctx, token);
                    return Logs(call, watch, r);
                }
                case ToolNames.GetUserRecentErrors:
                {
                    var r = await tools.GetUserRecentErrorsAsync(Parse<UserRecentErrorsArgs>(json), ctx, token);
                    return Logs(call, watch, r);
                }
                case ToolNames.FindRelatedLogs:
                {
                    var r = await tools.FindRelatedLogsAsync(Parse<RelatedLogsArgs>(json), ctx, token);
                    return Logs(call, watch, r);
                }
                case ToolNames.GetServiceHealth:
                {
                    var r = await tools.GetServiceHealthAsync(Parse<ServiceHealthArgs>(json), ctx, token);
                    return Build(call, watch, r, [], r.Services.Select(s => s.Service), r.Services.Count, false);
                }
                case ToolNames.GetErrorStatistics:
                {
                    var r = await tools.GetErrorStatisticsAsync(Parse<ErrorStatisticsArgs>(json), ctx, token);
                    return Build(call, watch, r, r.Groups.SelectMany(g => g.SampleLogs).ToList(),
                        r.Groups.SelectMany(g => g.Services), r.Groups.Count, false);
                }
                case ToolNames.GetActiveIncidents:
                {
                    var r = await tools.GetActiveIncidentsAsync(Parse<ActiveIncidentsArgs>(json), ctx, token);
                    return Build(call, watch, r, [], r.Incidents.SelectMany(i => i.AffectedServices), r.Incidents.Count, false);
                }
                case ToolNames.GetIncidentDetails:
                {
                    var r = await tools.GetIncidentDetailsAsync(Parse<IncidentDetailsArgs>(json), ctx, token);
                    return Build(call, watch, r, r.SampleLogs, r.Incident.AffectedServices, 1, false);
                }
                case ToolNames.SearchKnowledgeBase:
                {
                    // Geçmiş vakalar log değildir: kanıt listesine (logs) girmez, servisleri doğrulama kümesine eklenmez.
                    var r = await tools.SearchKnowledgeAsync(Parse<SearchKnowledgeArgs>(json), token);
                    return Build(call, watch, r, [], [], r.Cases.Count, false);
                }
                case ToolNames.SearchQuoteLogs:
                {
                    var r = await tools.SearchQuoteLogsAsync(Parse<SearchQuoteLogsArgs>(json), ctx, token);
                    return Logs(call, watch, r);
                }
                case ToolNames.TraceCustomerJourney:
                {
                    var r = await tools.TraceCustomerJourneyAsync(Parse<CustomerJourneyArgs>(json), ctx, token);
                    return Build(call, watch, r, r.Timeline, r.Timeline.Select(l => l.Service), r.Timeline.Count, r.Truncated);
                }
                case ToolNames.GetQuoteFunnel:
                {
                    var r = await tools.GetQuoteFunnelAsync(Parse<QuoteFunnelArgs>(json), ctx, token);
                    return Build(call, watch, r, r.SampleLogs, r.Steps.Select(s => s.Service).Concat(r.SampleLogs.Select(l => l.Service)),
                        (int)r.QuotesRequested, false);
                }
                case ToolNames.TraceNotificationDelivery:
                {
                    var r = await tools.TraceNotificationDeliveryAsync(Parse<NotificationTraceArgs>(json), ctx, token);
                    return Build(call, watch, r, r.Evidence, r.Evidence.Select(l => l.Service), r.Path.Count, false);
                }
                case ToolNames.SearchSmsLogs:
                {
                    var r = await tools.SearchSmsLogsAsync(Parse<SearchSmsLogsArgs>(json), ctx, token);
                    return Logs(call, watch, r);
                }
                case ToolNames.GetCustomerProfile:
                {
                    // Dizin kaydı log değildir: kanıt listesine girmez.
                    var r = await tools.GetCustomerProfileAsync(Parse<CustomerProfileArgs>(json), ctx, token);
                    return Build(call, watch, r, [], [], r.Profiles.Count, false);
                }
                default:
                    return Failed(call, watch, $"Unknown tool '{call.Name}'.");
            }
        }
        catch (ToolArgumentException ex) { return Failed(call, watch, ex.Message); }
        catch (JsonException ex) { return Failed(call, watch, $"Invalid arguments: {ex.Message}"); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Failed(call, watch, "The tool timed out."); }
    }

    private static T Parse<T>(string json)
        => JsonSerializer.Deserialize<T>(json, ToolJson.StrictArguments) ?? throw new JsonException("Arguments must be a JSON object.");

    private ToolInvocation Logs(LlmToolCall call, Stopwatch watch, LogToolResult r)
        => Build(call, watch, r, r.Logs, r.Logs.Select(l => l.Service), r.Logs.Count, r.Truncated);

    private ToolInvocation Build<T>(LlmToolCall call, Stopwatch watch, T result,
        IReadOnlyList<LogSnippet> logs, IEnumerable<string> services, int count, bool truncated)
    {
        // JSON düğümünde dolaşıp SADECE string DEĞERLERİNİ temizlemek: regex JSON yapısını bozamaz.
        var node = JsonSerializer.SerializeToNode(result, ToolJson.Options);
        var detections = Sanitize(node);

        var wrapped = $"<untrusted_log_data tool=\"{call.Name}\">\n{node?.ToJsonString(ToolJson.Options)}\n</untrusted_log_data>";
        return new ToolInvocation(call.Name, call.ArgumentsJson, wrapped, logs, services.Distinct().ToList(),
            count, truncated, detections, null, watch.ElapsedMilliseconds);
    }

    /// Çalıştırılmadan reddedilen çağrı (ör. uydurma kimlik). Modele "Tool error" olarak döner.
    public static ToolInvocation Rejected(LlmToolCall call, string error)
        => new(call.Name, call.ArgumentsJson, "", [], [], 0, false, 0, error, 0);

    private static ToolInvocation Failed(LlmToolCall call, Stopwatch watch, string error)
        => new(call.Name, call.ArgumentsJson, "", [], [], 0, false, 0, error, watch.ElapsedMilliseconds);

    private int Sanitize(JsonNode? node)
    {
        var detections = 0;

        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(kv => kv.Key).ToList())
                {
                    if (obj[key] is JsonValue value && value.TryGetValue<string>(out var text))
                    {
                        var cleaned = guard.Sanitize(text);
                        if (cleaned.Detections > 0) { obj[key] = cleaned.Text; detections += cleaned.Detections; }
                    }
                    else detections += Sanitize(obj[key]);
                }
                break;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue value && value.TryGetValue<string>(out var text))
                    {
                        var cleaned = guard.Sanitize(text);
                        if (cleaned.Detections > 0) { array[i] = cleaned.Text; detections += cleaned.Detections; }
                    }
                    else detections += Sanitize(array[i]);
                }
                break;
        }

        return detections;
    }
}
