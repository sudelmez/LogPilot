using System.Globalization;
using System.Text;
using LogPilot.Domain.Logs;
using LogPilot.Domain.Notifications;
using LogPilot.Domain.Purchases;
using LogPilot.Domain.Quotes;
using LogPilot.Domain.Scenarios;
using LogPilot.Domain.Users;

namespace LogPilot.LogGenerator.Engine;

/// Bir senaryo çalıştırmasının çıktısı: uygulama logları (`logs`) + teklif motoru (`quote_logs`) + SMS ağ geçidi (`sms_logs`).
public sealed record ScenarioRun(
    IReadOnlyList<LogEntry> Logs, IReadOnlyList<string> CorrelationIds, int Seed,
    IReadOnlyList<QuoteLogEntry>? QuoteLogs = null, IReadOnlyList<SmsLogEntry>? SmsLogs = null,
    IReadOnlyList<ProposalStepLog>? ProposalStepLogs = null, IReadOnlyList<WinsureTransaction>? WinsureLogs = null,
    IReadOnlyList<PaymentTransaction>? PaymentLogs = null)
{
    public IReadOnlyList<QuoteLogEntry> Quotes => QuoteLogs ?? [];
    public IReadOnlyList<SmsLogEntry> Sms => SmsLogs ?? [];
    public IReadOnlyList<ProposalStepLog> ProposalSteps => ProposalStepLogs ?? [];
    public IReadOnlyList<WinsureTransaction> Winsure => WinsureLogs ?? [];
    public IReadOnlyList<PaymentTransaction> Payments => PaymentLogs ?? [];
    public int TotalCount => Logs.Count + Quotes.Count + Sms.Count + ProposalSteps.Count + Winsure.Count + Payments.Count;
}

/// Saf (I/O'suz) log üretim motoru: aynı senaryo + kullanıcılar + seed + "şimdi" her zaman aynı logları üretir.
public sealed class ScenarioEngine
{
    public static readonly IReadOnlySet<string> KnownVariables = new HashSet<string>
    {
        "userCode", "userId", "correlationId", "transactionId", "device", "appVersion",
        "quoteId", "product", "customerNo", "customerCode", "channel", "plate", "policyNo", "messageId"
    };

    public const string PolicyIssuedStep = "PolicyIssued";

    private static readonly string[] Devices = ["iPhone", "Android", "Web"];
    private static readonly string[] AppVersions = ["2.3.9", "2.4.0", "2.4.1"];
    private static readonly string[] PlateLetters = ["ABC", "KLP", "RTE", "MNS", "YDZ", "BHT"];

    private sealed record Draft(int OffsetMs, ScenarioStep Step, Flow Flow);

    /// Bir kullanıcı akışının bağlamı. Initiator = akışı başlatan (mobil müşteri / acente), Customer = teklifin sahibi.
    private sealed record Flow(
        User Initiator, User Customer, string CorrelationId, string TransactionId,
        string QuoteId, string Product, QuoteChannel Channel, Dictionary<string, string> Vars);

    /// Logların HEPSİ nowUtc'de veya öncesinde biter (gelecek tarihli log yok): akış geriye doğru yerleştirilir.
    public ScenarioRun Generate(Scenario scenario, IReadOnlyList<User> allUsers, int seed, DateTime nowUtc)
    {
        var rng = new Random(seed);
        var selected = SelectUsers(scenario, allUsers, rng);
        var customers = allUsers
            .Where(u => u.Status == UserStatus.Active && u.CustomerNo is not null && u.Roles.Contains("Customer"))
            .OrderBy(u => u.UserCode, StringComparer.Ordinal).ToList();

        var drafts = new List<Draft>();
        var correlationIds = new List<string>();
        var datePart = nowUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var spacing = scenario.Users.SpacingMs;

        if (scenario.Purchase is { } purchase) return GeneratePurchase(purchase, selected, rng, seed, nowUtc, datePart, spacing);

        for (var i = 0; i < selected.Count; i++)
        {
            var user = selected[i];

            // Kullanıcı akışları kademeli başlar (küçük sapmayla): çok kullanıcılı olay "yayılır".
            var baseOffset = i * spacing + (i > 0 && spacing > 0 ? rng.Next(-spacing / 4, spacing / 4 + 1) : 0);

            // Her kullanıcı akışı kendi CorrelationId / TransactionId / QuoteId'sine sahiptir.
            var correlationId = $"COR-{datePart}-{rng.Next(0x1000000):X6}";
            var transactionId = $"TRX-{rng.Next(100_000, 1_000_000)}";
            var quoteId = $"TKL-{datePart}-{rng.Next(10_000, 100_000)}";
            var channel = scenario.Quote?.Channel ?? QuoteChannel.Mobile;
            var products = scenario.Quote?.Products is { Count: > 0 } p ? p : ["Kasko"];
            var product = products[rng.Next(products.Count)];

            // Acente kanalında teklifin sahibi acente değil, bir müşteridir.
            var customer = channel == QuoteChannel.Agent && customers.Count > 0 ? customers[rng.Next(customers.Count)] : user;

            var vars = new Dictionary<string, string>
            {
                ["userCode"] = user.UserCode,
                ["userId"] = user.UserId,
                ["correlationId"] = correlationId,
                ["transactionId"] = transactionId,
                ["device"] = Devices[rng.Next(Devices.Length)],
                ["appVersion"] = AppVersions[rng.Next(AppVersions.Length)],
                ["quoteId"] = quoteId,
                ["product"] = product,
                ["customerNo"] = customer.CustomerNo ?? "",
                ["customerCode"] = customer.UserCode,
                ["channel"] = channel.ToString(),
                ["plate"] = $"34 {PlateLetters[rng.Next(PlateLetters.Length)]} {rng.Next(100, 1000)}",
                ["policyNo"] = $"POL-{nowUtc:yyyy}-{rng.Next(100_000, 1_000_000)}",
                ["messageId"] = $"SMS-{rng.Next(0x10000000, int.MaxValue):X8}"
            };
            var flow = new Flow(user, customer, correlationId, transactionId, quoteId, product, channel, vars);

            var contributed = false;
            foreach (var step in scenario.Steps)
            {
                if (step.Probability < 1.0 && rng.NextDouble() >= step.Probability) continue;
                drafts.Add(new Draft(baseOffset + step.OffsetMs, step, flow));
                contributed = true;
            }

            if (contributed) correlationIds.Add(correlationId);
        }

        if (drafts.Count == 0) return new ScenarioRun([], [], seed, [], []);

        var start = nowUtc.AddMilliseconds(-drafts.Max(d => d.OffsetMs));
        var logs = new List<LogEntry>();
        var quotes = new List<QuoteLogEntry>();
        var sms = new List<SmsLogEntry>();
        foreach (var d in drafts)
        {
            switch (d.Step.Target)
            {
                case StepTarget.Quote: quotes.Add(ToQuoteLog(d, start, rng)); break;
                case StepTarget.Sms: if (ToSmsLog(d, start, rng) is { } m) sms.Add(m); break;
                default: logs.Add(ToLog(d, start, rng)); break;
            }
        }

        return new ScenarioRun(
            logs.OrderBy(l => l.Timestamp).ToList(), correlationIds, seed,
            quotes.OrderBy(q => q.Timestamp).ToList(), sms.OrderBy(m => m.Timestamp).ToList());
    }

    /// Satın alma akışı: her müşteri için üç tabloya dağıtık loglar; tamamı nowUtc'de veya öncesinde biter.
    private static ScenarioRun GeneratePurchase(PurchaseFlow flow, List<User> users, Random rng, int seed, DateTime nowUtc, string datePart, int spacing)
    {
        var items = new List<PurchaseFlowBuilder.Item>();
        for (var i = 0; i < users.Count; i++)
            items.AddRange(PurchaseFlowBuilder.Build(flow, users[i], rng, datePart, i * spacing));
        if (items.Count == 0) return new ScenarioRun([], [], seed);

        var start = nowUtc.AddMilliseconds(-items.Max(x => x.OffsetMs));
        DateTime At(PurchaseFlowBuilder.Item x) => start.AddMilliseconds(x.OffsetMs);
        var steps = items.Where(x => x.Log is ProposalStepLog).Select(x => With((ProposalStepLog)x.Log, At(x))).OrderBy(x => x.Timestamp).ToList();
        var winsure = items.Where(x => x.Log is WinsureTransaction).Select(x => With((WinsureTransaction)x.Log, At(x))).OrderBy(x => x.Timestamp).ToList();
        var payments = items.Where(x => x.Log is PaymentTransaction).Select(x => With((PaymentTransaction)x.Log, At(x))).OrderBy(x => x.Timestamp).ToList();
        return new ScenarioRun([], [], seed, [], [], steps, winsure, payments);
    }

    private static ProposalStepLog With(ProposalStepLog l, DateTime t) => new()
    {
        Timestamp = t, Level = l.Level, ProposalNo = l.ProposalNo, SessionId = l.SessionId, UserIdentityNo = l.UserIdentityNo, Product = l.Product,
        Step = l.Step, Status = l.Status, RequestJson = l.RequestJson, ResponseJson = l.ResponseJson, ErrorCode = l.ErrorCode,
        ErrorMessage = l.ErrorMessage, DurationMs = l.DurationMs
    };

    private static WinsureTransaction With(WinsureTransaction l, DateTime t) => new()
    {
        Timestamp = t, Level = l.Level, PolicyNo = l.PolicyNo, Operation = l.Operation, Status = l.Status, PolicyStatus = l.PolicyStatus,
        RequestXml = l.RequestXml, ResponseXml = l.ResponseXml, ErrorCode = l.ErrorCode, ErrorMessage = l.ErrorMessage, DurationMs = l.DurationMs
    };

    private static PaymentTransaction With(PaymentTransaction l, DateTime t) => new()
    {
        Timestamp = t, Level = l.Level, PolicyNo = l.PolicyNo, ClientReferenceCode = l.ClientReferenceCode, Stage = l.Stage, Status = l.Status,
        Provider = l.Provider, Amount = l.Amount, ProviderCode = l.ProviderCode, ErrorCode = l.ErrorCode, ErrorMessage = l.ErrorMessage,
        ResponseRaw = l.ResponseRaw, DurationMs = l.DurationMs
    };

    private static List<User> SelectUsers(Scenario scenario, IReadOnlyList<User> allUsers, Random rng)
    {
        var selection = scenario.Users;

        // Mongo sırası garanti değil: deterministik olması için önce sıralıyoruz.
        var pool = allUsers
            .Where(u => u.Status == selection.Status)
            .Where(u => selection.Role is null || u.Roles.Contains(selection.Role, StringComparer.OrdinalIgnoreCase))
            .Where(u => selection.UserCodes.Count == 0
                        || selection.UserCodes.Contains(u.UserCode, StringComparer.OrdinalIgnoreCase))
            .OrderBy(u => u.UserCode, StringComparer.Ordinal)
            .ToList();

        if (pool.Count == 0)
            throw new InvalidOperationException(
                $"Scenario '{scenario.Id}' needs {selection.Status} users, but none matched.");

        var count = Math.Min(pool.Count, rng.Next(selection.MinCount, selection.MaxCount + 1));

        for (var i = pool.Count - 1; i > 0; i--)   // Fisher–Yates
        {
            var j = rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        return pool.GetRange(0, count);
    }

    private static LogEntry ToLog(Draft d, DateTime start, Random rng)
    {
        var step = d.Step;
        return new LogEntry
        {
            Timestamp = start.AddMilliseconds(d.OffsetMs),
            Level = step.Level,
            Application = step.Application,
            Service = step.Service,
            UserCode = d.Flow.Initiator.UserCode,
            UserId = d.Flow.Initiator.UserId,
            CorrelationId = d.Flow.CorrelationId,
            TransactionId = d.Flow.TransactionId,
            Operation = step.Operation,
            Outcome = step.Outcome,
            ErrorCode = step.ErrorCode,
            Message = Render(step.Message, d.Flow.Vars),
            ExceptionType = step.ExceptionType,
            DurationMs = step.DurationMs is { } ms ? Jitter(ms, rng) : null,
            Metadata = step.Metadata.ToDictionary(kv => kv.Key, kv => Render(kv.Value, d.Flow.Vars))
        };
    }

    /// Kanal, hangi anahtarların loga yazıldığını belirler (gerçek hayattaki gibi her kanal her bilgiyi bilmez).
    private static QuoteLogEntry ToQuoteLog(Draft d, DateTime start, Random rng)
    {
        var step = d.Step;
        var f = d.Flow;
        var metadata = step.Metadata.ToDictionary(kv => kv.Key, kv => Render(kv.Value, f.Vars));
        if (f.Channel == QuoteChannel.Agent) metadata.TryAdd("agentCode", f.Initiator.UserCode);

        return new QuoteLogEntry
        {
            Timestamp = start.AddMilliseconds(d.OffsetMs),
            Level = step.Level,
            Service = step.Service,
            QuoteId = f.QuoteId,
            Step = step.Operation,
            Channel = f.Channel,
            Product = f.Product,
            UserCode = f.Channel == QuoteChannel.Web ? null : f.Initiator.UserCode,
            CustomerNo = f.Channel == QuoteChannel.Web ? null : f.Customer.CustomerNo,
            IdentityNo = f.Customer.IdentityNo,
            CorrelationId = f.Channel == QuoteChannel.Mobile ? f.CorrelationId : null,
            InsurerCode = step.InsurerCode,
            PolicyNo = step.Operation == PolicyIssuedStep ? f.Vars["policyNo"] : null,
            PremiumAmount = step.PremiumAmount is { } p ? Math.Round(p * (decimal)(0.85 + rng.NextDouble() * 0.3), 2) : null,
            Outcome = step.Outcome,
            ErrorCode = step.ErrorCode,
            ExceptionType = step.ExceptionType,
            Message = Render(step.Message, f.Vars),
            DurationMs = step.DurationMs is { } ms ? Jitter(ms, rng) : null,
            Metadata = metadata
        };
    }

    /// SMS sistemi müşteriyi tanımaz: sadece telefon + şablon + mesaj no yazar. Telefonu olmayan müşteriye SMS üretilmez.
    private static SmsLogEntry? ToSmsLog(Draft d, DateTime start, Random rng)
    {
        var step = d.Step;
        var phone = d.Flow.Customer.Phone;
        if (phone is null) return null;

        return new SmsLogEntry
        {
            Timestamp = start.AddMilliseconds(d.OffsetMs),
            Level = step.Level,
            Service = step.Service,
            MessageId = d.Flow.Vars["messageId"],
            Phone = phone,
            TemplateCode = step.Operation,
            Status = step.SmsStatus ?? SmsStatus.Queued,
            Provider = step.Provider,
            ErrorCode = step.ErrorCode,
            Message = Render(step.Message, d.Flow.Vars),
            DurationMs = step.DurationMs is { } ms ? Jitter(ms, rng) : null,
            Metadata = step.Metadata.ToDictionary(kv => kv.Key, kv => Render(kv.Value, d.Flow.Vars))
        };
    }

    private static int Jitter(int ms, Random rng) => (int)Math.Round(ms * (0.85 + rng.NextDouble() * 0.3));

    private static string Render(string template, Dictionary<string, string> vars)
    {
        if (!template.Contains('{')) return template;

        var sb = new StringBuilder(template);
        foreach (var (key, value) in vars) sb.Replace("{" + key + "}", value);
        return sb.ToString();
    }
}
