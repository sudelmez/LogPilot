using LogPilot.Domain.Logs;

namespace LogPilot.Domain.Purchases;

// Kurumsal web sitesinden poliçe satın alma. Gerçek şirket sistemlerindeki gibi DAĞITIK loglanır:
//   proposal_step_logs   : satın alma adımları. Tek kişi anahtarı userIdentityNo; policyNo ayrı bir alan DEĞİL,
//                          PolicyCreate adımının response JSON'unun İÇİNDE geçer.
//   winsure_transactions : poliçe çekirdek sistemi (Winsure). policyNo + işlem (Create/Approve/Edit/Cancel) +
//                          request/response XML + hata mesajı.
//   payment_transactions : ödeme. İlk kayıt policyNo + clientReferenceCode ile yazılır; sağlayıcı (VPOS) sonucu
//                          AYNI clientReferenceCode ile ama policyNo BOŞ yazılır. Sonuç ancak iki adımda bulunur:
//                          policyNo → clientReferenceCode → sağlayıcı kaydı.

public enum ProposalStepStatus { Success, Error }

public sealed class ProposalStepLog
{
    public string? Id { get; init; }
    public required DateTime Timestamp { get; init; }
    public required LogSeverity Level { get; init; }
    public string Environment { get; init; } = "Local";
    public string Channel { get; init; } = "CorporateWeb";

    public required string ProposalNo { get; init; }           // PRP-yyyyMMdd-nnnnn: bir satın alma oturumunun tüm adımları
    public required string SessionId { get; init; }            // WEB-xxxxxxxx (tarayıcı oturumu)
    public required string UserIdentityNo { get; init; }       // T.C. kimlik no (SENTETİK, PII): tek kişi anahtarı
    public required string Product { get; init; }
    public required string Step { get; init; }                 // Start, CustomerInfo, OfferSelected, PolicyCreate, Payment, PolicyApprove, Completed
    public required ProposalStepStatus Status { get; init; }
    public string? RequestJson { get; init; }
    public string? ResponseJson { get; init; }                 // PolicyCreate yanıtında {"policyNo":"POL-…"} geçer
    public string? ErrorCode { get; init; }                    // genelde semptom: PURCHASE_FAILED
    public string? ErrorMessage { get; init; }
    public int? DurationMs { get; init; }
}

public enum WinsureOperation { Create, Approve, Edit, Cancel, Print }
public enum WinsureStatus { Success, Failed }

public sealed class WinsureTransaction
{
    public string? Id { get; init; }
    public required DateTime Timestamp { get; init; }
    public required LogSeverity Level { get; init; }
    public string Environment { get; init; } = "Local";

    public required string PolicyNo { get; init; }             // TEK anahtar
    public required WinsureOperation Operation { get; init; }
    public required WinsureStatus Status { get; init; }
    public string? PolicyStatus { get; init; }                 // işlem sonrası poliçe durumu: Draft, Approved, Cancelled…
    public required string RequestXml { get; init; }
    public string? ResponseXml { get; init; }
    public string? ErrorCode { get; init; }                    // WINSURE_APPROVE_REJECTED, WINSURE_TIMEOUT…
    public string? ErrorMessage { get; init; }                 // XML'deki <Message> metni
    public int? DurationMs { get; init; }
}

public enum PaymentStage { Initiated, ProviderResult, Refund }
public enum PaymentStatus { Pending, Success, Failed, Refunded }

public sealed class PaymentTransaction
{
    public string? Id { get; init; }
    public required DateTime Timestamp { get; init; }
    public required LogSeverity Level { get; init; }
    public string Environment { get; init; } = "Local";

    public string? PolicyNo { get; init; }                     // YALNIZCA Initiated kaydında dolu; sağlayıcı kaydında BOŞ
    public required string ClientReferenceCode { get; init; }  // PAY-xxxxxxxx: iki kaydı bağlayan anahtar
    public required PaymentStage Stage { get; init; }
    public required PaymentStatus Status { get; init; }
    public required string Provider { get; init; }             // VPOS_A, VPOS_B (sentetik sanal POS)
    public decimal? Amount { get; init; }                      // TL
    public string? ProviderCode { get; init; }                 // banka/sağlayıcı kodu: "00" onay, "51" yetersiz bakiye…
    public string? ErrorCode { get; init; }                    // PAYMENT_DECLINED, PAYMENT_3DS_FAILED, PAYMENT_CALLBACK_TIMEOUT
    public string? ErrorMessage { get; init; }
    public string? ResponseRaw { get; init; }                  // sağlayıcının ham yanıtı (JSON)
    public int? DurationMs { get; init; }
}
