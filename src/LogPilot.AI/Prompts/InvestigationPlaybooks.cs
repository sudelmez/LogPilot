using System.Text;
using System.Text.RegularExpressions;
using LogPilot.AI.Providers;

namespace LogPilot.AI.Prompts;

/// Bir veri kaynağı (Mongo koleksiyonu): hangi sistemin logu, hangi anahtarları taşır, hangi araçla okunur.
public sealed record DataSourceInfo(string Name, string Collection, string Contains, string Keys, IReadOnlyList<string> Tools);

/// İki tablo arasındaki köprü: "A tablosundaki X alanı, B tablosundaki Y alanıyla eşleşir".
public sealed record DataLink(string From, string To, string Key, string Note);

/// Bir soru tipi için araştırma planı. Path = tabloların SIRASI (çok adımlı aramada hangi halkadan hangisine geçilir).
public sealed record Playbook(
    string Id, string Title, string When,
    IReadOnlyList<string> Triggers, IReadOnlyList<string> Examples,
    IReadOnlyList<string> Path, IReadOnlyList<string> Tools, string Watch,
    SubjectRule Subject = SubjectRule.Any, int Priority = 0);

/// Playbook'un "belirli bir kişi/teklif" gerektirip gerektirmediği (P2 tek müşteri, P4 toplu soru gibi).
public enum SubjectRule { Any, Required, Forbidden }

/// "Hangi soru gelirse nereye bakılır" bilgisinin TEK kaynağı.
///   • Prompt'taki veri kataloğu ve playbook'lar buradan üretilir (RenderCatalog).
///   • Orkestratör soruyu sınıflandırıp modele bir yönlendirme İPUCU verir (Classify + RenderHint).
///   • Mock model aynı sınıflandırmayı kullanır; testler her playbook'un örnek sorularının kendisine düştüğünü
///     ve andığı araçların gerçekten var olduğunu doğrular.
/// Yeni bir tablo/soru tipi eklemek = buraya bir DataSourceInfo + DataLink + Playbook eklemek.
public static partial class InvestigationPlaybooks
{
    public static readonly IReadOnlyList<DataSourceInfo> Sources =
    [
        new("users", "users", "User/customer directory — the KEY BOOK that connects the log systems.",
            "userCode, customerNo (CUS-…), identityNo (national id, always masked), phone (always masked), status, roles (Customer|Agent|Admin)",
            [ToolNames.GetCustomerProfile]),
        new("app_logs", "logs", "Mobile app and backend services: MobileGateway, AuthenticationService, AuthorizationService, UserService, NotificationApp (OTP flow), ProposalApp (back-office proposal documents, NOT insurance quotes).",
            "userCode, correlationId (COR-…), transactionId (TRX-…); mobile quote requests have operation=RequestQuote",
            [ToolNames.SearchLogs, ToolNames.GetUserRecentErrors, ToolNames.GetErrorStatistics, ToolNames.GetServiceHealth, ToolNames.FindRelatedLogs]),
        new("quote_logs", "quote_logs", "Insurance QUOTE ENGINE and policy issuing (Kasko, Trafik). One quoteId passes QuoteRequested → CustomerVerification → VehicleLookup → RiskScoring → InsurerPricing ×N → QuoteIssued|QuoteFailed → PolicyIssued (PolicyService, policyNo POL-…).",
            "quoteId (TKL-…), policyNo (POL-…), identityNo (every channel), customerNo (mobile/agent), userCode (mobile: customer, agent: the AGENT, web: empty), correlationId (mobile only)",
            [ToolNames.TraceCustomerJourney, ToolNames.SearchQuoteLogs, ToolNames.GetQuoteFunnel, ToolNames.FindRelatedLogs]),
        new("sms_logs", "sms_logs", "SMS gateway: NotificationGateway + SmsProviderAdapter. Every message: Queued → SentToProvider → Delivered | Failed | Undelivered (operator delivery report).",
            "phone ONLY (no userCode, customerNo, quoteId or policyNo), messageId (SMS-…), templateCode (POLICY_ISSUED, QUOTE_READY, OTP)",
            [ToolNames.TraceNotificationDelivery, ToolNames.SearchSmsLogs])
    ];

    public static readonly IReadOnlyList<DataLink> Links =
    [
        new("app_logs", "quote_logs", "correlationId", "same mobile request (mobile channel only); app logs may also carry metadata.quoteId"),
        new("users", "app_logs", "userCode", "the customer's own mobile activity"),
        new("users", "quote_logs", "customerNo / identityNo / userCode", "web quotes carry ONLY identityNo; agent quotes have the agent in userCode"),
        new("quote_logs", "users", "identityNo → phone", "the only way from a quote/policy to a phone number"),
        new("users", "sms_logs", "phone", "the SMS system knows nothing else")
    ];

    public static readonly IReadOnlyList<Playbook> All =
    [
        new("P1", "Mobile app / login / OTP problem of a user",
            "the question is about logging in, the app, sessions, tokens or OTP codes",
            ["login", "log in", "sign in", "giriş", "oturum", "uygulamaya", "uygulama açıl", "şifre", "password", "token", "otp"],
            ["Why can't TCLI log in to the mobile application?", "BOB uygulamaya giriş yapamıyor"],
            ["app_logs"], [ToolNames.GetUserRecentErrors, ToolNames.FindRelatedLogs],
            "follow the newest failing correlationId; LOGIN_FAILED is only a symptom"),
        new("P2", "A specific customer cannot get an insurance quote",
            "a person (userCode / customerNo) and a quote/insurance word appear together",
            ["teklif", "quote", "kasko", "trafik", "sigorta"],
            ["BOB mobil uygulamadan kasko teklifi alamıyor, neden?", "ELIF neden teklif alamadı?", "CUS-100105 teklif alabildi mi?"],
            ["users", "quote_logs", "app_logs"], [ToolNames.TraceCustomerJourney],
            "'never reached QuoteEngine' ⇒ cause is in app_logs; root error inside a quote ⇒ that is the cause; Agent/Web channel ⇒ the customer may never have used the app",
            SubjectRule.Required),
        new("P3", "A quote number is given",
            "the question contains TKL-…",
            ["tkl-"],
            ["TKL-20261112-48213 numaralı teklif neden başarısız oldu?"],
            ["quote_logs", "app_logs", "users"], [ToolNames.FindRelatedLogs, ToolNames.GetCustomerProfile],
            "FindRelatedLogs(quoteId) returns both collections; GetCustomerProfile(quoteId) tells whose quote it is",
            Priority: 5),
        new("P4", "Aggregate quote questions (counts, insurers, conversion)",
            "quote/insurer words WITHOUT a specific person",
            ["teklif", "quote", "kasko", "trafik", "sigorta şirket", "sigortacı", "insurer", "kaç teklif", "hangi sigorta"],
            ["Kasko tekliflerinde neden bazı sigorta şirketlerinin teklifi gelmiyor?", "How many quotes failed today?"],
            ["quote_logs"], [ToolNames.GetQuoteFunnel, ToolNames.SearchQuoteLogs],
            "take the evidence for the top root error code with SearchQuoteLogs(errorCode=…)",
            SubjectRule.Forbidden),
        new("P5", "Quotes made by an agent",
            "the question is about an agent's (acente) quotes",
            ["acente", "agent"],
            ["DEVR acentesinin yaptığı teklifler neden düşüyor?"],
            ["quote_logs", "users"], [ToolNames.SearchQuoteLogs, ToolNames.GetCustomerProfile],
            "SearchQuoteLogs(userCode=agent, channel=Agent); the customer of a quote via GetCustomerProfile(quoteId)",
            SubjectRule.Required, Priority: 2),
        new("P6", "Did a notification SMS reach the customer? (policy SMS, delivery)",
            "the question is about an SMS/message/notification being sent or delivered, e.g. after a policy was issued",
            ["sms", "mesaj", "bildirim", "ulaştı", "ulaşmadı", "ulaşmış", "gitti mi", "gitmedi", "gitmiş", "notification", "deliver", "poliçe", "policy", "pol-"],
            ["TKL-20261112-48213 teklifinden basılan poliçenin SMS'i müşteriye ulaştı mı?", "ELIF'e poliçe SMS'i gitti mi?",
             "POL-2026-123456 poliçe bildirimi müşteriye ulaştı mı?", "Poliçe SMS'leri neden ulaşmıyor?"],
            ["quote_logs", "users", "sms_logs"], [ToolNames.TraceNotificationDelivery, ToolNames.SearchSmsLogs],
            "the SMS system only knows phone numbers: never conclude 'not sent' from sms_logs alone without resolving the phone; NeverSent + NOTIFICATION_PUBLISH_FAILED ⇒ cause is in quote_logs; Undelivered ⇒ outside our systems",
            Priority: 10),
        new("P7", "System-wide health, error rates, incidents",
            "no specific person; the question is about services, error rates, incidents or a time window",
            ["incident", "critical", "kritik", "error rate", "hata oran", "hangi servis", "which service", "mongo", "son ", "last ", "same error", "aynı hata", "özetle", "summar"],
            ["Which service has the highest error rate today?", "Is there an active MongoDB-related incident?"],
            ["app_logs"], [ToolNames.GetServiceHealth, ToolNames.GetErrorStatistics, ToolNames.GetActiveIncidents, ToolNames.SearchLogs],
            "incidents and dashboard statistics are computed from app_logs only",
            SubjectRule.Forbidden),
        new("P8", "Unclear which system",
            "a person is named but nothing says app, quote or SMS",
            [],
            [],
            ["app_logs", "quote_logs"], [ToolNames.GetUserRecentErrors, ToolNames.TraceCustomerJourney],
            "look at both, then decide; ask one clarifying question only if nobody/nothing is named",
            SubjectRule.Required, Priority: -10)
    ];

    // ---------------- sınıflandırma ----------------

    [GeneratedRegex(@"\b[A-Z][A-Z0-9]{2,7}\b")] private static partial Regex UserTokenRx();
    [GeneratedRegex(@"\bCUS-\d{4,}\b", RegexOptions.IgnoreCase)] private static partial Regex CustomerNoRx();
    [GeneratedRegex(@"\bTKL-\d{8}-\d{5}\b", RegexOptions.IgnoreCase)] private static partial Regex QuoteIdRx();
    [GeneratedRegex(@"\bPOL-\d{4}-\d{6}\b", RegexOptions.IgnoreCase)] private static partial Regex PolicyNoRx();

    private static readonly HashSet<string> NotPeople = new(StringComparer.Ordinal)
    {
        "API", "SMS", "HTTP", "HTTPS", "JSON", "COR", "TRX", "USR", "MONGO", "MONGODB", "AI", "UTC", "LOGIN", "ERROR", "TODAY", "WHY",
        "THE", "AND", "CAN", "ARE", "IS", "ANY", "WHAT", "WHICH", "HOW", "SQL", "TKL", "CUS", "POL", "INS", "SBM", "KPS", "EGM",
        "NRD", "AKS", "GNS", "OTP", "DLR"
    };

    /// Sorudaki somut tanımlayıcılar (kişi kodu, müşteri no, teklif no, poliçe no).
    public sealed record Signals(string? UserCode, string? CustomerNo, string? QuoteId, string? PolicyNo)
    {
        public bool HasSubject => UserCode is not null || CustomerNo is not null || QuoteId is not null || PolicyNo is not null;

        /// "userCode=BOB, quoteId=TKL-…" veya hiçbiri yoksa null.
        public string? Describe()
        {
            var parts = new[] { ("userCode", UserCode), ("customerNo", CustomerNo), ("quoteId", QuoteId), ("policyNo", PolicyNo) }
                .Where(x => x.Item2 is not null).Select(x => $"{x.Item1}={x.Item2}").ToList();
            return parts.Count == 0 ? null : string.Join(", ", parts);
        }
    }

    public static Signals Extract(string question)
    {
        string? user = null;
        foreach (Match m in UserTokenRx().Matches(question))
            if (!NotPeople.Contains(m.Value)) { user = m.Value; break; }

        static string? Upper(Match m) => m.Success ? m.Value.ToUpperInvariant() : null;
        return new Signals(user, Upper(CustomerNoRx().Match(question)), Upper(QuoteIdRx().Match(question)), Upper(PolicyNoRx().Match(question)));
    }

    /// Sorunun en olası playbook'u (yoksa null). Basit ve açıklanabilir: tetikleyici sayısı + öncelik + "kişi var mı" kuralı.
    public static Playbook? Classify(string question)
    {
        var q = question.ToLowerInvariant();
        var signals = Extract(question);

        var scored = All
            .Where(p => p.Subject switch
            {
                SubjectRule.Required => signals.HasSubject,
                SubjectRule.Forbidden => !signals.HasSubject,
                _ => true
            })
            .Select(p => (Playbook: p, Hits: p.Triggers.Count(q.Contains)))
            .Where(x => x.Hits > 0 || x.Playbook.Triggers.Count == 0)
            .OrderByDescending(x => x.Hits > 0)
            .ThenByDescending(x => x.Playbook.Priority)
            .ThenByDescending(x => x.Hits)
            .ToList();

        return scored.FirstOrDefault().Playbook;
    }

    // ---------------- prompt üretimi ----------------

    public static string RenderCatalog()
    {
        var sb = new StringBuilder();
        sb.AppendLine("DATA CATALOG (MongoDB, read through tools only; you never write queries or joins yourself)");
        foreach (var s in Sources)
            sb.AppendLine($"- {s.Name} (collection `{s.Collection}`): {s.Contains}\n    keys: {s.Keys}\n    tools: {string.Join(", ", s.Tools)}");

        sb.AppendLine("HOW THE COLLECTIONS CONNECT (the platform's tools do these lookups for you)");
        foreach (var l in Links) sb.AppendLine($"- {l.From} → {l.To} via {l.Key}: {l.Note}");
        sb.AppendLine("Every log in a tool result has \"source\": app_logs | quote_logs | sms_logs. Say in which system you saw the evidence.");
        sb.AppendLine("Masked identity numbers and phone numbers can not be searched by you; give userCode/customerNo/quoteId/policyNo and the tools resolve them.");

        sb.AppendLine();
        sb.AppendLine("PLAYBOOKS (pick by intent; Path = the ORDER in which collections are visited in a multi-hop investigation)");
        foreach (var p in All)
        {
            sb.AppendLine($"{p.Id} {p.Title} — when {p.When}.");
            sb.AppendLine($"    path: {string.Join(" → ", p.Path)}; tools: {string.Join(", ", p.Tools)}");
            sb.AppendLine($"    watch: {p.Watch}");
        }
        return sb.ToString();
    }

    public static string RenderHint(Playbook p, Signals? signals = null) =>
        $"ROUTING HINT (automatic, may be wrong): this question most likely matches {p.Id} \"{p.Title}\". " +
        $"Suggested path: {string.Join(" → ", p.Path)} using {string.Join(", ", p.Tools)}. Follow it unless the data points elsewhere." +
        (signals?.Describe() is { } ids ? $" Identifiers in the question: {ids}. Pass exactly these; never invent other ids." : "");
}
