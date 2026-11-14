using System.Globalization;
using System.Text.RegularExpressions;
using LogPilot.Application.Purchases;
using LogPilot.Domain.Logs;
using LogPilot.Domain.Purchases;
using LogPilot.Domain.Users;

namespace LogPilot.Application.Ai;

/// Kurumsal web poliçe satın alma: DAĞITIK loglar arasında zincirleme araştırma.
///
///   users                : userCode → kimlik no
///   proposal_step_logs   : kimlik no → satın alma adımları; policyNo response JSON'unun İÇİNDE
///   winsure_transactions : policyNo → Create / Approve / Edit / Cancel (+ XML hata mesajı)
///   payment_transactions : policyNo → clientReferenceCode → (policyNo'SUZ) sağlayıcı sonucu / iade
///
/// Hangi halkadan başlanacağı elde ne olduğuna bağlıdır (kişi, poliçe no, teklif no). Her halka TraceHop olarak
/// kaydedilir. Müşteriye gösterilen hata (PURCHASE_FAILED) her zaman belirtidir; asıl neden Winsure'da veya ödemededir.
public sealed partial class LogAnalysisTools
{
    [GeneratedRegex("\"policyNo\"\\s*:\\s*\"(POL-[0-9A-Za-z-]+)\"")]
    private static partial Regex PolicyNoInJson();

    [GeneratedRegex("<Message>(.*?)</Message>", RegexOptions.Singleline)]
    private static partial Regex XmlMessage();

    private const string CreateStep = "PolicyCreate";

    public async Task<PurchaseTraceToolResult> TracePolicyPurchaseAsync(PurchaseTraceArgs a, ToolContext ctx, CancellationToken ct)
    {
        var store = RequirePurchases();
        var (from, to) = Window(null, null, ctx);
        var userCode = Identifier(a.UserCode, "userCode")?.ToUpperInvariant();
        var customerNo = Identifier(a.CustomerNo, "customerNo")?.ToUpperInvariant();
        var policyNo = Identifier(a.PolicyNo, "policyNo")?.ToUpperInvariant();
        var proposalNo = Identifier(a.ProposalNo, "proposalNo")?.ToUpperInvariant();
        if (userCode is null && customerNo is null && policyNo is null && proposalNo is null)
            throw new ToolArgumentException("Give userCode, customerNo, policyNo or proposalNo.");

        var path = new List<TraceHop>();
        var findings = new List<string>();
        User? customer = null;
        List<ProposalStepLog> steps = [];
        List<WinsureTransaction> winsure = [];
        List<PaymentTransaction> payments = [];
        string? reference = null;

        void Hop(string source, string by, bool found, string result) => path.Add(new TraceHop(path.Count + 1, source, by, found, result));

        PurchaseTraceToolResult Stop(string verdict) => Result(verdict, null, path, customer, steps, winsure, payments, reference, findings, from, to);

        // ---------- halkaların sırası, elimizdeki anahtara göre ----------
        if (policyNo is not null)
        {
            // policyNo ile başla: Winsure → ödeme → satın alma adımları (response içinde) → kişi
            winsure = await WinsureFor(store, policyNo, from, to, ct);
            Hop(LogSources.Winsure, $"policyNo={policyNo}", winsure.Count > 0, DescribeWinsure(winsure));
            (payments, reference) = await PaymentsFor(store, policyNo, from, to, Hop, ct);
            steps = await StepsByPolicy(store, policyNo, from, to, ct);
            Hop(LogSources.ProposalSteps, $"policyNo inside the PolicyCreate response", steps.Count > 0, DescribeSteps(steps));
            customer = await CustomerOf(steps, ct);
            if (steps.Count > 0)
                Hop("users", $"userIdentityNo={MaskIdentity(steps[0].UserIdentityNo)}", customer is not null,
                    customer is null ? "Identity not in the user directory." : $"Customer {customer.UserCode} ({customer.CustomerNo}).");
            if (winsure.Count == 0 && steps.Count == 0)
            {
                findings.Add($"No Winsure transaction or purchase step mentions {policyNo} in the selected time range.");
                return Stop("Unknown");
            }
        }
        else
        {
            if (proposalNo is not null)
            {
                steps = (await store.SearchProposalStepsAsync(new ProposalStepSearch(from, to, ProposalNo: proposalNo, Limit: MaxResults, Ascending: true), ct)).Items.ToList();
                Hop(LogSources.ProposalSteps, $"proposalNo={proposalNo}", steps.Count > 0, DescribeSteps(steps));
                customer = await CustomerOf(steps, ct);
                if (steps.Count > 0)
                    Hop("users", $"userIdentityNo={MaskIdentity(steps[0].UserIdentityNo)}", customer is not null,
                        customer is null ? "Identity not in the user directory." : $"Customer {customer.UserCode} ({customer.CustomerNo}).");
            }
            else
            {
                var found = await RequireUsers().FindAsync(userCode is null ? null : [userCode], customerNo is null ? null : [customerNo], null, ct);
                customer = found.FirstOrDefault(u => u.IdentityNo is not null) ?? found.FirstOrDefault();
                Hop("users", userCode is not null ? $"userCode={userCode}" : $"customerNo={customerNo}", customer?.IdentityNo is not null,
                    customer is null ? "Not found in the user directory."
                    : customer.IdentityNo is null ? $"{customer.UserCode} has no identity number (roles: {string.Join(", ", customer.Roles)}); corporate web purchases are logged by identity number."
                    : $"Customer {customer.UserCode} ({customer.CustomerNo}), identityNo {MaskIdentity(customer.IdentityNo)}.");
                if (customer?.IdentityNo is null)
                {
                    findings.Add("The person's identity number is unknown, so proposal_step_logs (keyed by identity number) cannot be searched.");
                    return Stop("Unknown");
                }

                // Kullanıcı genelde hatayı sorar: aralıktaki EN SON HATALI satın almayı doğrudan sorgula; yoksa en son satın alma.
                // (Son N adımdan seçmek yanlıştı: çok sayıda yeni başarılı satın alma, eski hatalı denemeyi pencerenin dışına itiyordu.)
                var identities = new[] { customer.IdentityNo };
                var chosen = (await store.SearchProposalStepsAsync(new ProposalStepSearch(from, to, IdentityNos: identities, OnlyErrors: true, Limit: 1), ct)).Items.FirstOrDefault()
                             ?? (await store.SearchProposalStepsAsync(new ProposalStepSearch(from, to, IdentityNos: identities, Limit: 1), ct)).Items.FirstOrDefault();
                if (chosen is not null)
                    steps = (await store.SearchProposalStepsAsync(new ProposalStepSearch(from, to, ProposalNo: chosen.ProposalNo, Limit: MaxResults, Ascending: true), ct)).Items.ToList();
                // Her satın alma tam bir "Start" adımıyla başlar: deneme sayısı = Start adımlarının sayısı.
                var attempts = (await store.SearchProposalStepsAsync(new ProposalStepSearch(from, to, IdentityNos: identities, Step: "Start", Limit: 1), ct)).TotalCount;
                Hop(LogSources.ProposalSteps, $"userIdentityNo={MaskIdentity(customer.IdentityNo)}", steps.Count > 0,
                    steps.Count == 0 ? "No corporate web purchase in the selected time range." :
                    DescribeSteps(steps) + (attempts > 1 ? $" ({attempts} purchase attempts in the range; showing {(chosen!.Status == ProposalStepStatus.Error ? "the latest failed" : "the latest")} one)" : ""));
            }

            if (steps.Count == 0)
            {
                findings.Add("No corporate web purchase was found for this person in the selected time range.");
                return Stop("Unknown");
            }

            policyNo = steps.Select(s => s.ResponseJson is null ? null : PolicyNoInJson().Match(s.ResponseJson)).FirstOrDefault(m => m is { Success: true })?.Groups[1].Value;
            if (policyNo is null)
            {
                findings.Add($"The purchase {steps[0].ProposalNo} never reached the PolicyCreate step (last step: {steps[^1].Step}); no policy number exists.");
                return Stop("AbandonedBeforePolicy");
            }

            winsure = await WinsureFor(store, policyNo, from, to, ct);
            Hop(LogSources.Winsure, $"policyNo={policyNo} (taken from the PolicyCreate response JSON)", winsure.Count > 0, DescribeWinsure(winsure));
            (payments, reference) = await PaymentsFor(store, policyNo, from, to, Hop, ct);
        }

        // ---------- karar ----------
        var symptom = steps.LastOrDefault(s => s.Status == ProposalStepStatus.Error);
        var create = winsure.FirstOrDefault(w => w.Operation == WinsureOperation.Create);
        var approve = winsure.LastOrDefault(w => w.Operation == WinsureOperation.Approve);
        var cancel = winsure.LastOrDefault(w => w.Operation == WinsureOperation.Cancel && w.Status == WinsureStatus.Success);
        var initiated = payments.FirstOrDefault(p => p.Stage == PaymentStage.Initiated);
        var result = payments.LastOrDefault(p => p.Stage == PaymentStage.ProviderResult);
        var refund = payments.LastOrDefault(p => p.Stage == PaymentStage.Refund);
        var shown = symptom is null ? "" : $"The website showed the customer only \"{Masker.MaskRequired(symptom.ErrorMessage ?? "an error")}\" ({symptom.ErrorCode}) at step {symptom.Step}. ";

        string verdict;
        string? stage = null;
        if (create is { Status: WinsureStatus.Failed })
        {
            verdict = "PolicyCreateFailed";
            stage = "PolicyCreate (Winsure)";
            findings.Add(shown + $"The real error is in winsure_transactions: Create failed with {create.ErrorCode} – \"{Masker.MaskRequired(create.ErrorMessage ?? "")}\". " +
                         "The flow stopped before payment, so the customer was NOT charged.");
        }
        else if (initiated is null)
        {
            verdict = "AbandonedBeforePayment";
            stage = "Payment (not started)";
            findings.Add($"The policy {policyNo} was created as a draft but no payment was ever started (no payment_transactions row with this policyNo).");
        }
        else if (result is null)
        {
            verdict = "PaymentPending";
            stage = "Payment (provider result missing)";
            findings.Add(shown + $"payment_transactions has only the Initiated row for {reference} ({initiated.Provider}); no provider result was logged for this clientReferenceCode. " +
                         "The payment provider never returned a result: check the provider/callback; the charge status is unknown until it is reconciled.");
        }
        else if (result.Status == PaymentStatus.Failed)
        {
            verdict = "PaymentFailed";
            stage = $"Payment ({result.Provider})";
            findings.Add(shown + $"The real reason is in payment_transactions: provider {result.Provider} returned code {result.ProviderCode} – \"{Masker.MaskRequired(result.ErrorMessage ?? "")}\" ({result.ErrorCode}). " +
                         $"This provider row has NO policyNo; it was matched through clientReferenceCode {reference}. The customer was not charged and the policy stayed a draft.");
        }
        else if (approve is { Status: WinsureStatus.Failed })
        {
            verdict = "ApproveFailedAfterPayment";
            stage = "PolicyApprove (Winsure)";
            findings.Add(shown + $"The payment SUCCEEDED ({reference}, provider code {result.ProviderCode}), but Winsure refused to approve the policy: {approve.ErrorCode} – \"{Masker.MaskRequired(approve.ErrorMessage ?? "")}\". " +
                         "The customer has been charged without an approved policy: refund or manual approval is required.");
        }
        else if (cancel is not null)
        {
            verdict = "Cancelled";
            findings.Add($"The purchase was completed and paid, then the policy was cancelled in Winsure at {cancel.Timestamp:u}." +
                         (refund is not null ? $" The payment was refunded ({reference}, refund row without policyNo)." : " No refund row was found for the payment."));
        }
        else if (approve is { Status: WinsureStatus.Success })
        {
            verdict = "Completed";
            findings.Add($"The purchase completed: payment approved (provider code {result.ProviderCode}) and the policy {policyNo} was approved in Winsure.");
        }
        else
        {
            verdict = "PaymentFailed";
            stage = "PolicyApprove (not attempted)";
            findings.Add($"The payment succeeded but no Winsure approval was logged for {policyNo}.");
        }

        return Result(verdict, stage, path, customer, steps, winsure, payments, reference, findings, from, to);
    }

    public async Task<LogToolResult> SearchProposalStepsAsync(SearchProposalStepsArgs a, ToolContext ctx, CancellationToken ct)
    {
        var store = RequirePurchases();
        var (from, to) = Window(null, null, ctx);
        var limit = Limit(a.Limit);
        var userCode = Identifier(a.UserCode, "userCode")?.ToUpperInvariant();
        var customerNo = Identifier(a.CustomerNo, "customerNo")?.ToUpperInvariant();
        IReadOnlyCollection<string>? identities = null;
        if (userCode is not null || customerNo is not null)
        {
            var people = await RequireUsers().FindAsync(userCode is null ? null : [userCode], customerNo is null ? null : [customerNo], null, ct);
            identities = people.Select(p => p.IdentityNo).OfType<string>().ToList();
            if (identities.Count == 0) return new LogToolResult([], 0, false, $"{from:u}..{to:u}; proposal_step_logs: no identity number could be resolved via users");
        }

        var page = await store.SearchProposalStepsAsync(new ProposalStepSearch(from, to, identities,
            Identifier(a.ProposalNo, "proposalNo")?.ToUpperInvariant(), Identifier(a.Step, "step"), a.OnlyErrors == true,
            Identifier(a.PolicyNo, "policyNo")?.ToUpperInvariant(), limit), ct);
        var items = page.Items.Select(ToStepSnippet).ToList();
        return new LogToolResult(items, page.TotalCount, page.TotalCount > items.Count,
            $"{from:u}..{to:u}; proposal_step_logs (keyed by identity number; policyNo only inside response JSON), newest first, max {limit}");
    }

    public async Task<LogToolResult> SearchWinsureTransactionsAsync(SearchWinsureArgs a, ToolContext ctx, CancellationToken ct)
    {
        var store = RequirePurchases();
        var (from, to) = Window(null, null, ctx);
        var limit = Limit(a.Limit);
        var policyNo = Identifier(a.PolicyNo, "policyNo")?.ToUpperInvariant();
        WinsureOperation? op = null;
        if (!string.IsNullOrWhiteSpace(a.Operation))
            op = Enum.TryParse<WinsureOperation>(a.Operation.Trim(), true, out var parsed) && Enum.IsDefined(parsed)
                ? parsed : throw new ToolArgumentException("'operation' must be one of: Create, Approve, Edit, Cancel, Print.");

        var page = await store.SearchWinsureAsync(new WinsureSearch(from, to, policyNo is null ? null : [policyNo], op, a.OnlyFailed == true, limit), ct);
        var items = page.Items.Select(ToWinsureSnippet).ToList();
        return new LogToolResult(items, page.TotalCount, page.TotalCount > items.Count, $"{from:u}..{to:u}; winsure_transactions (keyed by policyNo), newest first, max {limit}");
    }

    public async Task<LogToolResult> SearchPaymentTransactionsAsync(SearchPaymentsArgs a, ToolContext ctx, CancellationToken ct)
    {
        var store = RequirePurchases();
        var (from, to) = Window(null, null, ctx);
        var limit = Limit(a.Limit);
        var policyNo = Identifier(a.PolicyNo, "policyNo")?.ToUpperInvariant();
        var reference = Identifier(a.ClientReferenceCode, "clientReferenceCode")?.ToUpperInvariant();

        var page = await store.SearchPaymentsAsync(new PaymentSearch(from, to, policyNo is null ? null : [policyNo],
            reference is null ? null : [reference], OnlyFailed: a.OnlyFailed == true, Limit: limit), ct);
        var items = page.Items.Select(ToPaymentSnippet).ToList();
        var note = policyNo is not null && reference is null
            ? "; NOTE: provider results and refunds have NO policyNo – search again with the clientReferenceCode of these rows"
            : "";
        return new LogToolResult(items, page.TotalCount, page.TotalCount > items.Count, $"{from:u}..{to:u}; payment_transactions, newest first, max {limit}{note}");
    }

    public async Task<PurchaseStatsToolResult> GetPurchaseStatsAsync(PurchaseStatsArgs a, ToolContext ctx, CancellationToken ct)
    {
        var store = RequirePurchases();
        var (from, to) = Window(null, null, ctx);
        var product = Identifier(a.Product, "product");
        var w = await store.GetWindowAsync(from, to, ct);

        var steps = w.Steps.Where(s => product is null || string.Equals(s.Product, product, StringComparison.OrdinalIgnoreCase)).ToList();
        var policyNos = steps.Select(s => s.ResponseJson is null ? null : PolicyNoInJson().Match(s.ResponseJson)).Where(m => m is { Success: true })
            .Select(m => m!.Groups[1].Value).ToHashSet();
        var winsure = w.Winsure.Where(x => policyNos.Contains(x.PolicyNo)).ToList();
        var initiated = w.Payments.Where(p => p.Stage == PaymentStage.Initiated && p.PolicyNo is not null && policyNos.Contains(p.PolicyNo)).ToList();
        var refs = initiated.Select(p => p.ClientReferenceCode).ToHashSet();
        var results = w.Payments.Where(p => p.Stage == PaymentStage.ProviderResult && refs.Contains(p.ClientReferenceCode)).ToList();
        var paidRefs = results.Where(r => r.Status == PaymentStatus.Success).Select(r => r.ClientReferenceCode).ToHashSet();
        var paidPolicies = initiated.Where(i => paidRefs.Contains(i.ClientReferenceCode)).Select(i => i.PolicyNo!).ToHashSet();

        var errors = steps.Where(s => s.ErrorCode is not null).Select(s => (Code: s.ErrorCode!, Source: LogSources.ProposalSteps, Stage: s.Step, Id: s.Id))
            .Concat(winsure.Where(x => x.ErrorCode is not null).Select(x => (Code: x.ErrorCode!, Source: LogSources.Winsure, Stage: x.Operation.ToString(), Id: x.Id)))
            .Concat(results.Where(x => x.ErrorCode is not null).Select(x => (Code: x.ErrorCode!, Source: LogSources.Payments, Stage: x.Stage.ToString(), Id: x.Id)))
            .GroupBy(e => (e.Code, e.Source, e.Stage))
            .Select(g => new PurchaseErrorStat(g.Key.Code, g.Key.Source, g.Key.Stage, g.Count(), g.Select(e => e.Id).OfType<string>().Take(3).ToList()))
            .OrderBy(e => ErrorCodeCatalog.IsSymptom(e.ErrorCode)).ThenByDescending(e => e.Count).ToList();

        var sampleIds = errors.Where(e => !ErrorCodeCatalog.IsSymptom(e.ErrorCode)).SelectMany(e => e.SampleLogIds).ToHashSet();
        var samples = w.Winsure.Where(x => x.Id is not null && sampleIds.Contains(x.Id)).Select(ToWinsureSnippet)
            .Concat(w.Payments.Where(x => x.Id is not null && sampleIds.Contains(x.Id)).Select(ToPaymentSnippet))
            .Concat(w.Steps.Where(x => x.Id is not null && sampleIds.Contains(x.Id)).Select(ToStepSnippet)).ToList();

        return new PurchaseStatsToolResult(
            Started: steps.Select(s => s.ProposalNo).Distinct().Count(),
            PolicyCreated: winsure.Count(x => x is { Operation: WinsureOperation.Create, Status: WinsureStatus.Success }),
            PolicyCreateFailed: winsure.Count(x => x is { Operation: WinsureOperation.Create, Status: WinsureStatus.Failed }),
            PaymentStarted: initiated.Count,
            Paid: paidRefs.Count,
            PaymentFailed: results.Count(r => r.Status == PaymentStatus.Failed),
            PaymentPending: refs.Count(r => results.All(x => x.ClientReferenceCode != r)),
            Approved: winsure.Count(x => x is { Operation: WinsureOperation.Approve, Status: WinsureStatus.Success }),
            ApproveFailedAfterPayment: winsure.Count(x => x is { Operation: WinsureOperation.Approve, Status: WinsureStatus.Failed } && paidPolicies.Contains(x.PolicyNo)),
            Cancelled: winsure.Count(x => x is { Operation: WinsureOperation.Cancel, Status: WinsureStatus.Success }),
            Errors: errors.Take(10).ToList(), SampleLogs: samples,
            Scope: $"{from:u}..{to:u}; corporate web purchases joined over proposal_step_logs → winsure_transactions → payment_transactions" +
                   (product is null ? "" : $"; product {product}"));
    }

    // ---------------- halkalar ----------------

    private static async Task<List<WinsureTransaction>> WinsureFor(IPurchaseLogReader store, string policyNo, DateTime from, DateTime to, CancellationToken ct)
        => (await store.SearchWinsureAsync(new WinsureSearch(from, to, [policyNo], Limit: MaxResults, Ascending: true), ct)).Items.ToList();

    /// İki adımlı ödeme araması: policyNo → clientReferenceCode → (policyNo'suz) tüm kayıtlar.
    private static async Task<(List<PaymentTransaction> Payments, string? Reference)> PaymentsFor(IPurchaseLogReader store, string policyNo,
        DateTime from, DateTime to, Action<string, string, bool, string> hop, CancellationToken ct)
    {
        var byPolicy = (await store.SearchPaymentsAsync(new PaymentSearch(from, to, [policyNo], Limit: MaxResults, Ascending: true), ct)).Items;
        var reference = byPolicy.Select(p => p.ClientReferenceCode).FirstOrDefault();
        hop(LogSources.Payments, $"policyNo={policyNo}", reference is not null,
            reference is null ? "No payment was started for this policy." : $"Payment started: clientReferenceCode {reference} ({byPolicy[0].Provider}, {byPolicy[0].Amount?.ToString("0.00", CultureInfo.InvariantCulture)} TRY).");
        if (reference is null) return ([], null);

        var all = (await store.SearchPaymentsAsync(new PaymentSearch(from, to, ClientReferenceCodes: [reference], Limit: MaxResults, Ascending: true), ct)).Items.ToList();
        var later = all.Where(p => p.Stage != PaymentStage.Initiated).ToList();
        hop(LogSources.Payments, $"clientReferenceCode={reference} (these rows have no policyNo)", later.Count > 0,
            later.Count == 0 ? "No provider result or refund was logged for this reference." :
            string.Join("; ", later.Select(p => $"{p.Stage}: {p.Status}" + (p.ProviderCode is null ? "" : $" (code {p.ProviderCode})") + (p.ErrorCode is null ? "" : $" {p.ErrorCode}"))));
        return (all, reference);
    }

    private static async Task<List<ProposalStepLog>> StepsByPolicy(IPurchaseLogReader store, string policyNo, DateTime from, DateTime to, CancellationToken ct)
    {
        var create = (await store.SearchProposalStepsAsync(new ProposalStepSearch(from, to, Step: CreateStep, ResponseContains: policyNo, Limit: 1), ct)).Items.FirstOrDefault();
        if (create is null) return [];
        return (await store.SearchProposalStepsAsync(new ProposalStepSearch(from, to, ProposalNo: create.ProposalNo, Limit: MaxResults, Ascending: true), ct)).Items.ToList();
    }

    private async Task<User?> CustomerOf(IReadOnlyList<ProposalStepLog> steps, CancellationToken ct)
    {
        if (steps.Count == 0 || Users is null) return null;
        return (await Users.FindAsync(null, null, [steps[0].UserIdentityNo], ct)).FirstOrDefault();
    }

    private PurchaseTraceToolResult Result(string verdict, string? stage, List<TraceHop> path, User? customer, List<ProposalStepLog> steps,
        List<WinsureTransaction> winsure, List<PaymentTransaction> payments, string? reference, List<string> findings, DateTime from, DateTime to)
    {
        var policyNo = winsure.FirstOrDefault()?.PolicyNo
                       ?? steps.Select(s => s.ResponseJson is null ? null : PolicyNoInJson().Match(s.ResponseJson)).FirstOrDefault(m => m is { Success: true })?.Groups[1].Value;
        var result = payments.LastOrDefault(p => p.Stage == PaymentStage.ProviderResult);
        var evidence = steps.Where(s => s.Status == ProposalStepStatus.Error || s.Step is CreateStep or "Completed").Select(ToStepSnippet)
            .Concat(winsure.Select(ToWinsureSnippet))
            .Concat(payments.Select(ToPaymentSnippet))
            .OrderBy(l => l.Timestamp).ToList();

        return new PurchaseTraceToolResult(path, customer is null ? null : ToProfile(customer), steps.FirstOrDefault()?.ProposalNo, policyNo, reference,
            verdict, stage, winsure.LastOrDefault(w => w.Status == WinsureStatus.Success)?.PolicyStatus ?? winsure.LastOrDefault()?.PolicyStatus,
            payments.LastOrDefault()?.Status.ToString(), result?.Status == PaymentStatus.Success && payments.All(p => p.Stage != PaymentStage.Refund),
            findings, evidence,
            $"{from:u}..{to:u}; distributed purchase logs: proposal_step_logs (identity) → winsure_transactions (policyNo) → payment_transactions (policyNo → clientReferenceCode)");
    }

    private static string DescribeSteps(IReadOnlyList<ProposalStepLog> steps)
    {
        if (steps.Count == 0) return "No purchase steps found.";
        var error = steps.LastOrDefault(s => s.Status == ProposalStepStatus.Error);
        return $"Purchase {steps[0].ProposalNo} ({steps[0].Product}): {string.Join(" → ", steps.Select(s => s.Status == ProposalStepStatus.Error ? s.Step + " ✗" : s.Step))}" +
               (error is null ? "." : $"; error shown to the customer: \"{error.ErrorMessage}\".");
    }

    private static string DescribeWinsure(IReadOnlyList<WinsureTransaction> rows)
        => rows.Count == 0 ? "No Winsure transaction for this policy."
            : string.Join("; ", rows.Select(w => $"{w.Operation}: {w.Status}" + (w.PolicyStatus is null ? "" : $" → {w.PolicyStatus}") + (w.ErrorCode is null ? "" : $" ({w.ErrorCode})")));

    private IPurchaseLogReader RequirePurchases() => Purchases ?? throw new ToolArgumentException("The purchase log store is not available.");

    // ---------------- kanıt kartları ----------------

    private LogSnippet ToStepSnippet(ProposalStepLog s)
    {
        var policy = s.ResponseJson is null ? null : PolicyNoInJson().Match(s.ResponseJson) is { Success: true } m ? m.Groups[1].Value : null;
        var metadata = new Dictionary<string, string> { ["proposalNo"] = s.ProposalNo, ["step"] = s.Step, ["channel"] = s.Channel, ["product"] = s.Product };
        if (MaskIdentity(s.UserIdentityNo) is { } id) metadata["userIdentityNo"] = id;
        if (policy is not null) metadata["policyNo"] = policy;
        if (s.ResponseJson is not null) metadata["response"] = Excerpt(s.ResponseJson);
        return new LogSnippet(s.Id ?? "", s.Timestamp, s.Level.ToString(), "CorporateWeb", "ProposalService", null, null, null, s.Step,
            s.Status == ProposalStepStatus.Success ? "Success" : "Failure", s.ErrorCode, null,
            Masker.MaskRequired(s.ErrorMessage ?? $"Step {s.Step}: {s.Status}"), s.DurationMs, metadata, LogSources.ProposalSteps);
    }

    private LogSnippet ToWinsureSnippet(WinsureTransaction w)
    {
        var metadata = new Dictionary<string, string> { ["policyNo"] = w.PolicyNo, ["operation"] = w.Operation.ToString(), ["status"] = w.Status.ToString() };
        if (w.PolicyStatus is not null) metadata["policyStatus"] = w.PolicyStatus;
        if (w.ResponseXml is not null) metadata["responseXml"] = Excerpt(w.ResponseXml);
        var message = w.ErrorMessage ?? (w.ResponseXml is not null && XmlMessage().Match(w.ResponseXml) is { Success: true } m ? m.Groups[1].Value : null)
                      ?? $"{w.Operation} {w.Status}" + (w.PolicyStatus is null ? "" : $" → {w.PolicyStatus}");
        return new LogSnippet(w.Id ?? "", w.Timestamp, w.Level.ToString(), "Winsure", "WinsurePolicyService", null, null, null, w.Operation.ToString(),
            w.Status == WinsureStatus.Success ? "Success" : "Failure", w.ErrorCode, null, Masker.MaskRequired(message), w.DurationMs, metadata, LogSources.Winsure);
    }

    private LogSnippet ToPaymentSnippet(PaymentTransaction p)
    {
        var metadata = new Dictionary<string, string>
        {
            ["clientReferenceCode"] = p.ClientReferenceCode, ["policyNo"] = p.PolicyNo ?? "(empty)", ["stage"] = p.Stage.ToString(), ["status"] = p.Status.ToString()
        };
        if (p.ProviderCode is not null) metadata["providerCode"] = p.ProviderCode;
        if (p.Amount is { } amount) metadata["amountTry"] = amount.ToString("0.00", CultureInfo.InvariantCulture);
        if (p.ResponseRaw is not null) metadata["providerResponse"] = Excerpt(p.ResponseRaw);
        var outcome = p.Status switch { PaymentStatus.Success or PaymentStatus.Refunded => "Success", PaymentStatus.Failed => "Failure", _ => null };
        return new LogSnippet(p.Id ?? "", p.Timestamp, p.Level.ToString(), "PaymentGateway", p.Provider, null, null, null, p.Stage.ToString(),
            outcome, p.ErrorCode, null, Masker.MaskRequired(p.ErrorMessage ?? $"Payment {p.Stage}: {p.Status}"), p.DurationMs, metadata, LogSources.Payments);
    }

    /// Ham JSON/XML'in kısa, maskeli bir parçası (kimlik no gibi değerler maskeleyiciden geçer).
    private string Excerpt(string raw) => Masker.MaskRequired(raw.Length <= 200 ? raw : raw[..200] + "…");
}
