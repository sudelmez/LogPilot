using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using LogPilot.AI.Prompts;
using LogPilot.Application.Ai;
using LogPilot.Domain.Logs;

namespace LogPilot.AI.Providers;

/// API anahtarı gerektirmeyen, KURAL TABANLI sahte "LLM". Gerçek bir modelin yaptığını taklit eder:
/// soruyu anlar → araç çağırır → sonuçları okuyup yapılandırılmış JSON cevap yazar.
/// Deterministik olduğu için AI değerlendirme (evaluation) testlerinin temelidir.
public sealed partial class MockLlmProvider(TimeProvider time) : ILlmProvider
{
    public string Name => "mock";

    private sealed record Knowledge(string Cause, string[] Actions, string Limitation);

    private static readonly Dictionary<string, Knowledge> Kb = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TOKEN_EXPIRED"] = new(
            "The user's access token had expired, so AuthenticationService rejected the login.",
            ["Ask the user to sign in again to get a fresh token.", "Check access/refresh token lifetimes in AuthenticationService.", "Check for clock skew between the mobile gateway and AuthenticationService."],
            "The logs show that the token expired but not why it was not refreshed."),
        ["TOKEN_VALIDATION_FAILED"] = new(
            "Token validation failed in AuthenticationService.",
            ["Check token validity and signing keys.", "Check AuthenticationService health."],
            "The logs do not explain why token validation failed."),
        ["USER_INACTIVE"] = new(
            "The user's account is inactive, so the login was rejected by design.",
            ["Verify the account status in the user directory.", "Reactivate the account through the approved process if the user should have access."],
            "The logs do not say why or when the account was deactivated."),
        ["REQUEST_TIMEOUT"] = new(
            "A downstream call exceeded its timeout threshold, so the request failed.",
            ["Check the health and latency of the slow service.", "Review recent deployments or load on that service.", "Review the configured timeout threshold."],
            "The logs show the timeout but not what made the service slow."),
        ["MONGO_CONNECTION_FAILED"] = new(
            "Several services could not reach MongoDB (connection or server selection failed).",
            ["Check that MongoDB is running and reachable from the services.", "Check connection limits and network connectivity.", "Check for an active MongoDB-related incident."],
            "The logs show the connection failures but not why MongoDB became unreachable."),
        ["DEPENDENCY_UNAVAILABLE"] = new(
            "A required dependency (for example the pricing service) was unavailable.",
            ["Check the health of the failing dependency.", "Retry after the dependency recovers."],
            "The logs do not show why the dependency was unavailable."),
        ["SMS_PROVIDER_ERROR"] = new(
            "The external SMS provider returned an error, so the message could not be sent.",
            ["Check the SMS provider status page and API credentials.", "Retry failed messages once the provider recovers."],
            "The logs show the provider error but not its root cause on the provider side."),
        ["FORBIDDEN_ACCESS"] = new(
            "The user attempted to access a resource they are not authorised for.",
            ["Check the user's roles and permissions.", "Confirm whether the access attempt was legitimate."],
            "The logs do not say whether the access attempt was a mistake or malicious."),
        ["INVALID_CREDENTIALS"] = new(
            "The user supplied invalid credentials.",
            ["Ask the user to retry or reset their password."],
            "The logs do not show whether the user forgot the password or the account is targeted."),
        ["LOGIN_FAILED"] = new(
            "The login failed, but this is a symptom code: the underlying cause is reported by another service.",
            ["Look at the related logs of the same correlation ID to find the underlying error."],
            "LOGIN_FAILED alone does not identify the root cause."),
        ["VEHICLE_REGISTRY_TIMEOUT"] = new(
            "The external vehicle registry (SBM) did not answer in time, so QuoteEngine could not build the quote.",
            ["Check the SBM vehicle registry integration status and latency.", "Retry the quote once the registry responds again.", "Review the VehicleRegistryGateway timeout and retry policy."],
            "The logs show the timeout but not why the external registry was slow."),
        ["INSURER_PRICING_ERROR"] = new(
            "One insurer's pricing API failed, so quotes were issued without that insurer's offer.",
            ["Check the failing insurer's pricing API status and credentials.", "Inform the insurer integration team; affected quotes were issued with fewer offers."],
            "The logs do not show why the insurer's API returned an error."),
        ["CUSTOMER_IDENTITY_MISMATCH"] = new(
            "The customer's identity data did not match the customer master record (KPS check). This is a data/business-rule rejection, not a system fault.",
            ["Ask the agent/customer to verify the identity data (e.g. birth date) and update the customer master record.", "Retry the quote after the data is corrected."],
            "The logs do not show which identity field is wrong in detail."),
        ["RISK_REJECTED"] = new(
            "The request was declined by underwriting rules (risk score above the auto-underwriting limit). This is expected behaviour, not a technical fault.",
            ["Explain the decline to the customer.", "Route the request to manual underwriting if an exception is needed."],
            "The logs show the score and the limit but not the full underwriting assessment."),
        ["SMS_UNDELIVERED"] = new(
            "The SMS left our systems and the provider accepted it, but the operator reported it could not be delivered to the handset (phone off, out of coverage or wrong number). This is outside our systems.",
            ["Verify the customer's phone number in the user directory.", "Resend the policy SMS later or send the policy by e-mail.", "If many messages are undelivered on one operator, raise it with the SMS provider."],
            "The delivery report does not say exactly why the handset was unreachable."),
        ["NOTIFICATION_PUBLISH_FAILED"] = new(
            "The policy was issued, but PolicyService could not publish the notification event, so the SMS was never requested from the SMS gateway.",
            ["Check the message broker / notification queue health.", "Re-publish the PolicyIssued event (or resend the SMS manually) for the affected policies."],
            "The logs show the broker timeout but not why the broker was slow."),
        ["QUOTE_FAILED"] = new(
            "The quote failed, but this is a symptom code: the underlying cause is an earlier step of the same quote.",
            ["Look at the earlier steps of the same quoteId to find the underlying error."],
            "QUOTE_FAILED alone does not identify the root cause."),
        ["QUOTE_REQUEST_FAILED"] = new(
            "The app's quote request failed, but this is a symptom code: the cause is in authentication or in quote_logs.",
            ["Follow the quoteId/correlationId into quote_logs, or check authentication errors of the same request."],
            "QUOTE_REQUEST_FAILED alone does not identify the root cause."),
        ["PROPOSAL_CREATION_FAILED"] = new(
            "Proposal creation failed, but this is a symptom code: the underlying cause is reported by another service.",
            ["Look at the related logs of the same correlation ID to find the underlying error."],
            "PROPOSAL_CREATION_FAILED alone does not identify the root cause.")
    };

    private static readonly string[] LoginWords = ["log in", "login", "logging in", "sign in", "signin", "giriş", "oturum", "authenticat"];
    private static readonly string[] HealthWords = ["error rate", "hata oran", "which service", "hangi servis", "service health", "servis sağlığı", "en yüksek", "highest"];
    private static readonly string[] MultiUserWords = ["multiple users", "same error", "affected users", "how many users", "birden fazla", "aynı hata", "kaç kullanıcı", "etkilen"];
    private static readonly string[] QuoteWords = ["teklif", "quote", "kasko", "trafik", "sigorta", "insurer", "prim", "premium", "poliçe"];
    private static readonly string[] IncidentWords = ["incident", "critical", "kritik", "summarize", "summarise", "özetle"];
    private static readonly HashSet<string> NotUserCodes = new(StringComparer.Ordinal)
    {
        "API", "SMS", "HTTP", "HTTPS", "JSON", "COR", "TRX", "USR", "MONGO", "MONGODB", "AI", "UTC", "LOGIN", "ERROR",
        "TODAY", "WHY", "THE", "AND", "CAN", "ARE", "IS", "ANY", "WHAT", "WHICH", "HOW", "SQL",
        "TKL", "CUS", "INS", "SBM", "KPS", "EGM", "NRD", "AKS", "GNS", "POL", "OTP", "DLR"
    };

    [GeneratedRegex(@"\bCOR-\d{8}-[0-9A-Fa-f]{6}\b", RegexOptions.IgnoreCase)] private static partial Regex CorrelationIdRx();
    [GeneratedRegex(@"\bTRX-\d{4,}\b", RegexOptions.IgnoreCase)] private static partial Regex TransactionIdRx();
    [GeneratedRegex(@"\b[0-9a-f]{24}\b")] private static partial Regex ObjectIdRx();
    [GeneratedRegex(@"\bTKL-\d{8}-\d{5}\b", RegexOptions.IgnoreCase)] private static partial Regex QuoteIdRx();
    [GeneratedRegex(@"\bCUS-\d{4,}\b", RegexOptions.IgnoreCase)] private static partial Regex CustomerNoRx();
    [GeneratedRegex(@"(?:last|past|son)\s+(\d{1,4})\s*(?:minutes?|mins?|min|dakika|dk)", RegexOptions.IgnoreCase)] private static partial Regex LastMinutesRx();
    [GeneratedRegex(@"\b[A-Z][A-Z0-9]{2,7}\b")] private static partial Regex UserTokenRx();

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(request.Purpose == LlmPurpose.IncidentSummary ? SummarizeIncident(request) : Chat(request));
    }

    // ---------------- sohbet ----------------

    private LlmResponse Chat(LlmRequest request)
    {
        var messages = request.Messages.ToList();
        var lastUser = messages.FindLastIndex(m => m.Role == LlmRole.User);
        if (lastUser < 0) return LlmResponse.FromText(Json(Clarify()));

        var question = messages[lastUser].Content;
        var turn = messages.Skip(lastUser + 1).ToList();
        var toolMessages = turn.Where(m => m.Role == LlmRole.Tool).ToList();

        if (request.Tools.Count > 0)
        {
            var calls = PlanCalls(question, turn, toolMessages);
            if (calls.Count > 0) return new LlmResponse(null, calls);
        }

        return LlmResponse.FromText(Json(Compose(toolMessages)));
    }

    private List<LlmToolCall> PlanCalls(string question, List<LlmMessage> turn, List<LlmMessage> toolMessages)
    {
        // Bu turda zaten çağrılmış (ad + argüman) araçlar tekrar planlanmaz.
        var executed = turn.Where(m => m.ToolCalls is not null).SelectMany(m => m.ToolCalls!)
            .Select(c => c.Name + c.ArgumentsJson).ToHashSet();
        var calls = new List<LlmToolCall>();

        void Add(string name, object args)
        {
            var json = JsonSerializer.Serialize(args, ToolJson.Options);
            if (executed.Add(name + json)) calls.Add(new LlmToolCall($"call_{executed.Count}", name, json));
        }

        var q = question.ToLowerInvariant();
        var correlation = CorrelationIdRx().Match(question);
        var transaction = TransactionIdRx().Match(question);
        var objectId = ObjectIdRx().Match(q);
        var user = ExtractUserCode(question);

        // P6 (bildirim/SMS teslimi): zincirleme araç. Sınıflandırma, orkestratörün modele verdiği ipucuyla AYNI kaynaktan gelir.
        if (InvestigationPlaybooks.Classify(question)?.Id == "P6")
        {
            var s = InvestigationPlaybooks.Extract(question);
            if (s.QuoteId is not null) Add(ToolNames.TraceNotificationDelivery, new { quoteId = s.QuoteId });
            else if (s.PolicyNo is not null) Add(ToolNames.TraceNotificationDelivery, new { policyNo = s.PolicyNo });
            else if (s.CustomerNo is not null) Add(ToolNames.TraceNotificationDelivery, new { customerNo = s.CustomerNo });
            else if (s.UserCode is not null) Add(ToolNames.TraceNotificationDelivery, new { userCode = s.UserCode });
            else Add(ToolNames.SearchSmsLogs, new { templateCode = LogAnalysisTools.PolicySmsTemplate, onlyProblems = true, limit = 30 });
            return calls;
        }

        var quoteId = QuoteIdRx().Match(question);
        var customerNo = CustomerNoRx().Match(question);
        var quoteIntent = quoteId.Success || customerNo.Success || ContainsAny(q, QuoteWords);

        // Teklif soruları quote_logs'a yönlenir (PLAYBOOK P2–P4); mobil/giriş soruları uygulama loglarına (P1).
        if (quoteId.Success)
        {
            Add(ToolNames.FindRelatedLogs, new { quoteId = quoteId.Value.ToUpperInvariant() });
            Add(ToolNames.GetCustomerProfile, new { quoteId = quoteId.Value.ToUpperInvariant() });
        }
        else if (quoteIntent && (user is not null || customerNo.Success))
        {
            if (customerNo.Success) Add(ToolNames.TraceCustomerJourney, new { customerNo = customerNo.Value.ToUpperInvariant() });
            else Add(ToolNames.TraceCustomerJourney, new { userCode = user });
        }
        else if (quoteIntent)
        {
            Add(ToolNames.GetQuoteFunnel, new { });
            // İkinci adım: hunideki en sık KÖK hata için kanıt logları.
            var funnel = toolMessages.LastOrDefault(m => m.ToolName == ToolNames.GetQuoteFunnel);
            if (funnel is not null && ParseTool<QuoteFunnelToolResult>(funnel) is { } f
                && f.Errors.FirstOrDefault(e => !ErrorCodeCatalog.IsSymptom(e.ErrorCode)) is { } top)
                Add(ToolNames.SearchQuoteLogs, new { errorCode = top.ErrorCode, limit = 20 });
        }

        if (quoteIntent) return calls;

        if (correlation.Success) Add(ToolNames.FindRelatedLogs, new { correlationId = correlation.Value.ToUpperInvariant() });
        if (transaction.Success) Add(ToolNames.FindRelatedLogs, new { transactionId = transaction.Value.ToUpperInvariant() });
        if (objectId.Success) Add(ToolNames.GetIncidentDetails, new { incidentId = objectId.Value });

        if (ContainsAny(q, LoginWords))
        {
            if (user is not null) Add(ToolNames.GetUserRecentErrors, new { userCode = user, limit = 20 });
            else Add(ToolNames.SearchLogs, new { errorCode = "LOGIN_FAILED", limit = 20 });
        }
        else if (user is not null && !correlation.Success && !transaction.Success)
        {
            Add(ToolNames.GetUserRecentErrors, new { userCode = user, limit = 20 });
        }

        if (q.Contains("proposal"))
            Add(ToolNames.SearchLogs, new { application = "ProposalApp", level = "Error", limit = 20 });

        if (ContainsAny(q, HealthWords)) Add(ToolNames.GetServiceHealth, new { });
        if (ContainsAny(q, MultiUserWords)) Add(ToolNames.GetErrorStatistics, new { limit = 8 });

        if (q.Contains("mongo"))
        {
            Add(ToolNames.SearchLogs, new { errorCode = "MONGO_CONNECTION_FAILED", limit = 30 });
            Add(ToolNames.GetActiveIncidents, new { limit = 10 });
        }

        var minutes = LastMinutesRx().Match(q);
        if (minutes.Success && int.TryParse(minutes.Groups[1].Value, out var n))
        {
            var now = time.GetUtcNow().UtcDateTime;
            var from = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc).AddMinutes(-Math.Clamp(n, 1, 10_080));
            Add(ToolNames.SearchLogs, new { level = "Error", from, limit = 30 });
        }

        if (ContainsAny(q, IncidentWords)) Add(ToolNames.GetActiveIncidents, new { limit = 10 });

        // İkinci adım: kullanıcı araması sonuç verdiyse, en yeni hatanın tüm akışını (aynı correlationId) getir.
        var userResult = toolMessages.LastOrDefault(m => m.ToolName == ToolNames.GetUserRecentErrors);
        if (userResult is not null && ParseTool<LogToolResult>(userResult) is { } found)
        {
            var newest = found.Logs
                .Where(l => l.Level is "Error" or "Critical" && l.CorrelationId is not null)
                .OrderByDescending(l => l.Timestamp).FirstOrDefault();
            if (newest is not null) Add(ToolNames.FindRelatedLogs, new { correlationId = newest.CorrelationId });
        }

        return calls;
    }

    private object Compose(List<LlmMessage> toolMessages)
    {
        if (toolMessages.Count == 0) return Clarify();

        var summary = new List<string>();
        var limits = new List<string>();
        var actions = new List<string>();
        var evidenceIds = new List<string>();
        var services = new List<string>();
        string? cause = null, causeCode = null;

        limits.AddRange(toolMessages.Where(m => m.Content.StartsWith("Tool error", StringComparison.Ordinal))
            .Select(m => $"The {m.ToolName} tool failed: {m.Content}"));

        var logs = toolMessages
            .Where(m => m.ToolName is ToolNames.SearchLogs or ToolNames.GetUserRecentErrors or ToolNames.FindRelatedLogs or ToolNames.SearchQuoteLogs or ToolNames.SearchSmsLogs)
            .Select(ParseTool<LogToolResult>).OfType<LogToolResult>()
            .SelectMany(r => r.Logs).DistinctBy(l => l.Id).ToList();

        var journey = toolMessages.Where(m => m.ToolName == ToolNames.TraceCustomerJourney).Select(ParseTool<CustomerJourneyToolResult>).OfType<CustomerJourneyToolResult>().FirstOrDefault();
        var funnel = toolMessages.Where(m => m.ToolName == ToolNames.GetQuoteFunnel).Select(ParseTool<QuoteFunnelToolResult>).OfType<QuoteFunnelToolResult>().FirstOrDefault();
        var profiles = toolMessages.Where(m => m.ToolName == ToolNames.GetCustomerProfile).Select(ParseTool<CustomerProfileToolResult>).OfType<CustomerProfileToolResult>().FirstOrDefault();

        var trace = toolMessages.Where(m => m.ToolName == ToolNames.TraceNotificationDelivery).Select(ParseTool<NotificationTraceToolResult>).OfType<NotificationTraceToolResult>().FirstOrDefault();
        if (trace is not null)
        {
            // Kural 11: çok adımlı aramada izlenen yolu sırasıyla söyle.
            summary.Add("Path: " + string.Join(" → ", trace.Path.Select(h => $"{h.Source} ({h.LookedUpBy}){(h.Found ? "" : " – nothing found")}")) + ".");
            summary.Add($"Verdict: {trace.Verdict}.");
            summary.AddRange(trace.Findings);
            logs.AddRange(trace.Evidence.Where(l => logs.All(x => x.Id != l.Id)));
            evidenceIds.AddRange(trace.Evidence.Where(l => l.ErrorCode is null && l.Outcome == "Success").Select(l => l.Id));
            limits.Add("The SMS system only stores phone numbers; the customer was matched to the phone through the user directory.");
        }

        if (journey is not null)
        {
            if (journey.Customer is { } c) summary.Add($"Customer {c.UserCode} ({c.CustomerNo ?? "no customer number"}): {journey.AppLogCount} app log(s) and {journey.QuoteLogCount} quote log(s) in the period.");
            summary.AddRange(journey.Findings.Take(4));
            logs.AddRange(journey.Timeline.Where(l => logs.All(x => x.Id != l.Id)));
            limits.Add("Quote activity is matched to the customer through the user directory (userCode, customerNo and identity number).");
        }

        if (profiles is not null)
        {
            summary.AddRange(profiles.Profiles.Take(2).Select(p => $"The quote belongs to {p.UserCode} (customer {p.CustomerNo ?? "-"}, status {p.Status})."));
            if (profiles.Note.Contains("AGENT", StringComparison.Ordinal)) summary.Add(profiles.Note.Split(';')[0] + ".");
        }

        if (funnel is not null)
        {
            summary.Add($"In the period {funnel.QuotesRequested} quote(s) were requested: {funnel.QuotesIssued} issued, {funnel.QuotesFailed} failed, {funnel.QuotesWithPartialOffers} issued with missing insurer offers.");
            var badInsurer = funnel.Insurers.FirstOrDefault(i => i.Failed > 0);
            if (badInsurer is not null) summary.Add($"Insurer {badInsurer.InsurerCode} failed {badInsurer.Failed} of {badInsurer.Failed + badInsurer.Priced} pricing call(s).");
            logs.AddRange(funnel.SampleLogs.Where(l => logs.All(x => x.Id != l.Id)));
            services.AddRange(funnel.Errors.Take(3).Select(e => e.Service));
        }

        var health = toolMessages.Where(m => m.ToolName == ToolNames.GetServiceHealth).Select(ParseTool<ServiceHealthToolResult>).OfType<ServiceHealthToolResult>().FirstOrDefault();
        var stats = toolMessages.Where(m => m.ToolName == ToolNames.GetErrorStatistics).Select(ParseTool<ErrorStatisticsToolResult>).OfType<ErrorStatisticsToolResult>().FirstOrDefault();
        var incidents = toolMessages.Where(m => m.ToolName == ToolNames.GetActiveIncidents).Select(ParseTool<IncidentListToolResult>).OfType<IncidentListToolResult>().FirstOrDefault();
        var details = toolMessages.Where(m => m.ToolName == ToolNames.GetIncidentDetails).Select(ParseTool<IncidentDetailsToolResult>).OfType<IncidentDetailsToolResult>().FirstOrDefault();

        if (health is not null)
        {
            var ranked = health.Services.Where(s => s.Errors > 0).OrderByDescending(s => s.LogErrorRatio).ToList();
            if (ranked.Count == 0) summary.Add("No service logged errors in the selected period.");
            else
            {
                var top = ranked[0];
                summary.Add($"{top.Service} has the highest log error ratio: {Pct(top.LogErrorRatio)} ({top.Errors} error logs out of {top.TotalLogs}), status {top.Status}.");
                if (ranked.Count > 1)
                    summary.Add($"Next: {string.Join(", ", ranked.Skip(1).Take(2).Select(s => $"{s.Service} ({Pct(s.LogErrorRatio)})"))}.");
                services.Add(top.Service);
            }
            limits.Add("The ratio is based on log counts, not on the number of failed user requests.");
        }

        if (stats is not null)
        {
            if (stats.Groups.Count == 0) summary.Add("No error logs were found in the selected period.");
            else
            {
                var top = stats.Groups[0];
                summary.Add($"{top.ErrorCode} is the most frequent error ({top.Count} logs, {top.DistinctUsers} distinct user(s)).");
                var multi = stats.Groups.Where(g => g.DistinctUsers >= 2).ToList();
                summary.Add(multi.Count > 0
                    ? $"Multiple users are affected by the same error: {string.Join(", ", multi.Take(3).Select(g => $"{g.ErrorCode} ({g.DistinctUsers} users)"))}."
                    : "No single error code currently affects more than one user.");
                evidenceIds.AddRange(stats.Groups.Take(3).SelectMany(g => g.SampleLogs.Select(l => l.Id)));
                services.AddRange(top.Services);
            }
        }

        if (incidents is not null)
        {
            if (incidents.Incidents.Count == 0) summary.Add("There are no active incidents in the selected period.");
            else
            {
                summary.Add($"There {(incidents.Incidents.Count == 1 ? "is 1 active incident" : $"are {incidents.Incidents.Count} active incidents")}: " +
                            string.Join("; ", incidents.Incidents.Take(3).Select(i => $"{i.Title} [{i.Severity}, {i.Status}]")) + ".");
                services.AddRange(incidents.Incidents.SelectMany(i => i.AffectedServices));
            }
        }

        if (details is not null)
        {
            var i = details.Incident;
            summary.Add($"Incident \"{i.Title}\" is {i.Status} with {i.Severity} severity: {i.LogCount} {i.ErrorCode} logs, {i.AffectedUserCount} user(s), last seen {i.LastSeenAt:u}.");
            evidenceIds.AddRange(details.SampleLogs.Select(l => l.Id));
            services.AddRange(i.AffectedServices);
            logs.AddRange(details.SampleLogs.Where(l => logs.All(x => x.Id != l.Id)));
        }

        if (logs.Count > 0)
        {
            var problems = logs.Where(l => l.Level is "Error" or "Critical" || (l.Level == "Warning" && l.ErrorCode is not null)).ToList();
            if (problems.Count == 0)
            {
                summary.Add("I found no error or warning logs matching the question in the selected time range.");
            }
            else
            {
                var specific = problems.Where(l => l.ErrorCode is not null && !ErrorCodeCatalog.IsSymptom(l.ErrorCode)).ToList();
                var pool = specific.Count > 0 ? specific : problems.Where(l => l.ErrorCode is not null).ToList();

                if (pool.Count == 0)
                {
                    summary.Add("Errors were found but they carry no error code, so I cannot name a specific cause.");
                    evidenceIds.AddRange(problems.Take(5).Select(l => l.Id));
                }
                else
                {
                    var top = pool.GroupBy(l => l.ErrorCode!, StringComparer.Ordinal)
                        .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).First();
                    causeCode = top.Key;
                    var causeLogs = top.OrderByDescending(l => l.Timestamp).ToList();
                    var users = causeLogs.Select(l => l.UserCode).OfType<string>().Distinct().ToList();
                    var newest = causeLogs[0];

                    summary.Add(users.Count switch
                    {
                        0 => $"{causeLogs.Count} {causeCode} log(s) were found; the most recent was at {newest.Timestamp:u} in {newest.Service}.",
                        1 => $"{users[0]} hit {causeCode} {causeLogs.Count} time(s) in the selected period; the most recent was at {newest.Timestamp:u} in {newest.Service}.",
                        _ => $"{causeLogs.Count} {causeCode} errors affected {users.Count} users ({string.Join(", ", users.Take(4))}) across {string.Join(", ", causeLogs.Select(l => l.Service).Distinct().Take(4))}."
                    });

                    var flows = causeLogs.Select(l => l.CorrelationId).OfType<string>().ToHashSet();
                    var symptoms = problems.Where(l => ErrorCodeCatalog.IsSymptom(l.ErrorCode) && l.CorrelationId is not null && flows.Contains(l.CorrelationId)).Take(5).ToList();

                    var knowledge = Kb.GetValueOrDefault(causeCode);
                    cause = knowledge?.Cause ?? $"The logs report error {causeCode}; the underlying reason is not described in the available data.";
                    var sources = causeLogs.Select(l => l.Source).Distinct().ToList();
                    summary.Add($"The root cause was found in {string.Join(" and ", sources)}" +
                                (symptoms.Count > 0 ? $"; {string.Join(", ", symptoms.Select(l => l.ErrorCode).Distinct())} is only the symptom." : "."));
                    actions.AddRange(knowledge?.Actions ?? ["Review the related logs and recent changes of the affected service."]);
                    limits.Add(knowledge?.Limitation ?? "The logs do not explain the underlying reason.");

                    evidenceIds.AddRange(causeLogs.Take(10).Select(l => l.Id));
                    evidenceIds.AddRange(symptoms.Select(l => l.Id));
                    services.AddRange(causeLogs.Select(l => l.Service));
                    services.AddRange(symptoms.Select(l => l.Service));
                }
            }
        }

        if (summary.Count == 0)
        {
            summary.Add("I did not find matching data for this question in the selected time range.");
            limits.Add("No matching logs, statistics or incidents were returned by the tools.");
        }

        return new
        {
            summary = string.Join(" ", summary),
            probableCause = cause,
            probableCauseCode = causeCode,
            evidenceLogIds = evidenceIds.Distinct().ToList(),
            affectedServices = services.Distinct().ToList(),
            suggestedActions = actions.Distinct().ToList(),
            dataLimitations = limits.Distinct().ToList(),
            clarifyingQuestion = (string?)null
        };
    }

    private static object Clarify() => new
    {
        summary = "I need a bit more detail before I can investigate.",
        clarifyingQuestion = "Which user, service, error code or time range should I look at? For example: \"Why can't TCLI log in?\" or \"What errors occurred in the last 15 minutes?\"",
        evidenceLogIds = Array.Empty<string>(),
        affectedServices = Array.Empty<string>(),
        suggestedActions = Array.Empty<string>(),
        dataLimitations = Array.Empty<string>()
    };

    // ---------------- incident özeti ----------------

    private static LlmResponse SummarizeIncident(LlmRequest request)
    {
        var content = request.Messages.LastOrDefault()?.Content ?? "";
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');

        IncidentFacts? facts = null;
        if (start >= 0 && end > start)
        {
            try { facts = JsonSerializer.Deserialize<IncidentFacts>(content[start..(end + 1)], ToolJson.Options); }
            catch (JsonException) { /* aşağıda ele alınır */ }
        }

        if (facts is null) return LlmResponse.FromText("No incident facts were provided, so no summary could be generated.");

        var cause = Kb.GetValueOrDefault(facts.ErrorCode)?.Cause ?? "The underlying reason is not described in the available data.";
        return LlmResponse.FromText(
            $"{facts.Severity} severity incident: {facts.LogCount} {facts.ErrorCode} error log(s) affecting {facts.UserCodes.Count} user(s) across {string.Join(", ", facts.Services)}; " +
            $"first seen {facts.FirstSeen:u}, last seen {facts.LastSeen:u}. Likely explanation: {cause} " +
            "This summary is derived from log patterns only and does not confirm that the incident is resolved.");
    }

    // ---------------- yardımcılar ----------------

    private static string? ExtractUserCode(string question)
    {
        foreach (Match match in UserTokenRx().Matches(question))
            if (!NotUserCodes.Contains(match.Value)) return match.Value;
        return null;
    }

    private static bool ContainsAny(string text, string[] words) => words.Any(text.Contains);

    private static string Pct(double ratio) => (ratio * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static string Json(object value) => JsonSerializer.Serialize(value, ToolJson.Options);

    private static T? ParseTool<T>(LlmMessage message) where T : class
    {
        var c = message.Content;
        var start = c.IndexOf('{');
        var end = c.LastIndexOf('}');
        if (start < 0 || end <= start || c.StartsWith("Tool error", StringComparison.Ordinal)) return null;

        try { return JsonSerializer.Deserialize<T>(c[start..(end + 1)], ToolJson.Options); }
        catch (JsonException) { return null; }
    }
}
