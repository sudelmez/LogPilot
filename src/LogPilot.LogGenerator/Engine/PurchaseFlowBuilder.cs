using System.Globalization;
using LogPilot.Domain.Logs;
using LogPilot.Domain.Purchases;
using LogPilot.Domain.Scenarios;
using LogPilot.Domain.Users;

namespace LogPilot.LogGenerator.Engine;

/// Bir müşterinin kurumsal web sitesinden poliçe satın alma akışını üç ayrı tabloya DAĞITIK olarak üretir.
/// Bilinçli olarak gerçek sistemlerdeki kopuklukları taklit eder:
///   • proposal_step_logs yalnızca kimlik no ve teklif no bilir; policyNo yalnızca response JSON'unun içindedir.
///   • winsure_transactions yalnızca policyNo bilir.
///   • payment_transactions'ta sağlayıcı sonucu policyNo'suz, sadece clientReferenceCode ile yazılır.
///   • Müşteriye gösterilen hata (proposal adımı) her zaman genel bir metindir; asıl neden başka tablodadır.
internal static class PurchaseFlowBuilder
{
    private static readonly string[] Insurers = ["INS_AKS", "INS_NRD", "INS_GNS"];

    internal sealed record Item(int OffsetMs, object Log);

    public static List<Item> Build(PurchaseFlow flow, User customer, Random rng, string datePart, int baseOffset)
    {
        var items = new List<Item>();
        var identity = customer.IdentityNo ?? throw new InvalidOperationException($"{customer.UserCode} has no identity number.");
        var product = flow.Products[rng.Next(flow.Products.Count)];
        var proposalNo = $"PRP-{datePart}-{rng.Next(10_000, 100_000)}";
        var session = $"WEB-{rng.Next(0x1000000, int.MaxValue):x8}";
        var policyNo = $"POL-{datePart[..4]}-{rng.Next(100_000, 1_000_000)}";
        var reference = $"PAY-{rng.Next(0x10000000, int.MaxValue):X8}";
        var insurer = Insurers[rng.Next(Insurers.Length)];
        var premium = Math.Round((decimal)(product == "Kasko" ? 12_000 + rng.NextDouble() * 6_000 : 3_500 + rng.NextDouble() * 1_500), 2);
        var amount = premium.ToString("0.00", CultureInfo.InvariantCulture);
        var o = flow.Outcome;

        void Step(int at, string step, bool ok, string? request, string? response, string? error = null, string? code = null, int? ms = null)
            => items.Add(new Item(baseOffset + at, new ProposalStepLog
            {
                Timestamp = default, Level = ok ? LogSeverity.Information : LogSeverity.Error, ProposalNo = proposalNo, SessionId = session,
                UserIdentityNo = identity, Product = product, Step = step, Status = ok ? ProposalStepStatus.Success : ProposalStepStatus.Error,
                RequestJson = request, ResponseJson = response, ErrorMessage = error, ErrorCode = code, DurationMs = ms
            }));

        void Winsure(int at, WinsureOperation op, bool ok, string? policyStatus, string request, string response, string? code = null, string? message = null, int? ms = null)
            => items.Add(new Item(baseOffset + at, new WinsureTransaction
            {
                Timestamp = default, Level = ok ? LogSeverity.Information : LogSeverity.Error, PolicyNo = policyNo, Operation = op,
                Status = ok ? WinsureStatus.Success : WinsureStatus.Failed, PolicyStatus = policyStatus, RequestXml = request,
                ResponseXml = response, ErrorCode = code, ErrorMessage = message, DurationMs = ms
            }));

        void Payment(int at, PaymentStage stage, PaymentStatus status, string? policy, string? providerCode = null, string? code = null,
            string? message = null, string? raw = null, int? ms = null)
            => items.Add(new Item(baseOffset + at, new PaymentTransaction
            {
                Timestamp = default, Level = status == PaymentStatus.Failed ? LogSeverity.Error : LogSeverity.Information, PolicyNo = policy,
                ClientReferenceCode = reference, Stage = stage, Status = status, Provider = flow.Provider, Amount = premium,
                ProviderCode = providerCode, ErrorCode = code, ErrorMessage = message, ResponseRaw = raw, DurationMs = ms
            }));

        // ---- 1) Ortak başlangıç: teklif, müşteri bilgisi, teklif seçimi ----
        Step(0, "Start", true, $$"""{"product":"{{product}}","channel":"CorporateWeb"}""", $$"""{"proposalNo":"{{proposalNo}}"}""", ms: 120);
        Step(900, "CustomerInfo", true, $$"""{"identityNo":"{{identity}}","birthDate":"****"}""", """{"verified":true}""", ms: 340);
        Step(2600, "OfferSelected", true, $$"""{"insurer":"{{insurer}}","premium":{{amount}}}""", """{"accepted":true}""", ms: 90);

        // ---- 2) Winsure'da poliçe taslağı ----
        var createRequest = $"<PolicyCreateRequest><ProposalNo>{proposalNo}</ProposalNo><InsuredIdentityNo>{identity}</InsuredIdentityNo>" +
                            $"<Product>{product}</Product><Insurer>{insurer}</Insurer><Premium>{amount}</Premium></PolicyCreateRequest>";
        if (o == PurchaseOutcome.WinsureCreateTimeout)
        {
            Winsure(34_000, WinsureOperation.Create, false, null, createRequest,
                "<Fault><Code>WS-TIMEOUT</Code><Message>Winsure policy service did not respond within 30000 ms</Message></Fault>",
                "WINSURE_TIMEOUT", "Winsure policy service did not respond within 30000 ms", 30_000);
            Step(34_100, "PolicyCreate", false, $$"""{"proposalNo":"{{proposalNo}}"}""", $$"""{"policyNo":"{{policyNo}}","status":"Failed"}""",
                "Poliçeniz şu anda oluşturulamadı, lütfen daha sonra tekrar deneyin.", "PURCHASE_FAILED", 30_100);
            return items;
        }

        Winsure(3_800, WinsureOperation.Create, true, "Draft", createRequest,
            $"<PolicyCreateResponse><Result>OK</Result><PolicyNo>{policyNo}</PolicyNo><PolicyStatus>Draft</PolicyStatus></PolicyCreateResponse>", ms: 1_100);
        Step(3_900, "PolicyCreate", true, $$"""{"proposalNo":"{{proposalNo}}"}""", $$"""{"policyNo":"{{policyNo}}","policyStatus":"Draft"}""", ms: 1_250);

        // ---- 3) Ödeme: önce policyNo ile başlatma, sonra sağlayıcı sonucu policyNo'SUZ ----
        Payment(5_500, PaymentStage.Initiated, PaymentStatus.Pending, policyNo, ms: 300);
        Step(5_600, "Payment", true, $$"""{"policyNo":"{{policyNo}}","amount":{{amount}},"method":"CreditCard"}""",
            $$"""{"status":"RedirectedTo3DS","redirectUrl":"https://{{flow.Provider.ToLowerInvariant()}}.example/3ds"}""", ms: 420);

        switch (o)
        {
            case PurchaseOutcome.PaymentDeclined:
                Payment(41_000, PaymentStage.ProviderResult, PaymentStatus.Failed, null, "51", "PAYMENT_DECLINED", "Yetersiz bakiye (Insufficient funds)",
                    $$"""{"result":"DECLINED","procReturnCode":"51","clientReferenceCode":"{{reference}}"}""", 1_800);
                Step(41_300, "PaymentResult", false, null, """{"status":"Failed"}""", "Ödeme işlemi tamamlanamadı.", "PURCHASE_FAILED");
                return items;

            case PurchaseOutcome.Payment3dsFailed:
                Payment(65_000, PaymentStage.ProviderResult, PaymentStatus.Failed, null, "3D-05", "PAYMENT_3DS_FAILED",
                    "3D Secure doğrulaması başarısız (kart sahibi doğrulanamadı)",
                    $$"""{"result":"3DS_FAILED","mdStatus":"0","clientReferenceCode":"{{reference}}"}""", 900);
                Step(65_200, "PaymentResult", false, null, """{"status":"Failed"}""", "Ödeme işlemi tamamlanamadı.", "PURCHASE_FAILED");
                return items;

            case PurchaseOutcome.PaymentNoCallback:
                Step(126_000, "PaymentResult", false, null, """{"status":"Unknown"}""",
                    "Ödeme sağlayıcısından 120 sn içinde sonuç alınamadı.", "PAYMENT_CALLBACK_TIMEOUT");
                return items;
        }

        Payment(40_000, PaymentStage.ProviderResult, PaymentStatus.Success, null, "00", raw:
            $$"""{"result":"APPROVED","procReturnCode":"00","authCode":"{{rng.Next(100_000, 1_000_000)}}","clientReferenceCode":"{{reference}}"}""", ms: 1_600);
        Step(40_200, "PaymentResult", true, null, """{"status":"Paid"}""");

        // ---- 4) Winsure'da onay ----
        var approveRequest = $"<PolicyApproveRequest><PolicyNo>{policyNo}</PolicyNo><PaymentReference>{reference}</PaymentReference></PolicyApproveRequest>";
        if (o == PurchaseOutcome.ApproveRejected)
        {
            const string message = "Araç için açık hasar dosyası bulunduğundan poliçe onaylanamadı";
            Winsure(42_500, WinsureOperation.Approve, false, "Draft", approveRequest,
                $"<Fault><Code>WS-4012</Code><Message>{message}</Message></Fault>", "WINSURE_APPROVE_REJECTED", message, 1_300);
            Step(42_700, "PolicyApprove", false, $$"""{"policyNo":"{{policyNo}}"}""", """{"status":"Failed"}""",
                "Poliçeniz şu anda onaylanamadı. Müşteri hizmetleri sizinle iletişime geçecek.", "PURCHASE_FAILED", 1_500);
            return items;
        }

        Winsure(42_500, WinsureOperation.Approve, true, "Approved", approveRequest,
            "<PolicyApproveResponse><Result>OK</Result><PolicyStatus>Approved</PolicyStatus></PolicyApproveResponse>", ms: 1_200);
        Step(42_700, "PolicyApprove", true, $$"""{"policyNo":"{{policyNo}}"}""", """{"policyStatus":"Approved"}""", ms: 1_400);
        Step(43_000, "Completed", true, null, $$"""{"policyNo":"{{policyNo}}","status":"Active"}""");

        if (o == PurchaseOutcome.CancelledAfterPurchase)
        {
            Winsure(300_000, WinsureOperation.Cancel, true, "Cancelled",
                $"<PolicyCancelRequest><PolicyNo>{policyNo}</PolicyNo><Reason>CustomerRequest</Reason></PolicyCancelRequest>",
                "<PolicyCancelResponse><Result>OK</Result><PolicyStatus>Cancelled</PolicyStatus></PolicyCancelResponse>", ms: 900);
            Payment(305_000, PaymentStage.Refund, PaymentStatus.Refunded, null, "00", raw:
                $$"""{"result":"REFUNDED","clientReferenceCode":"{{reference}}"}""", ms: 1_200);
        }

        return items;
    }
}
