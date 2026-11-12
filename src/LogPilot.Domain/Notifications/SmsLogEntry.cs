using LogPilot.Domain.Logs;

namespace LogPilot.Domain.Notifications;

/// SMS'in yaşam döngüsü: kuyruğa alındı → operatöre/sağlayıcıya iletildi → teslim raporu (DLR) geldi.
public enum SmsStatus { Queued, SentToProvider, Delivered, Failed, Undelivered }

/// SMS ağ geçidi logları — `sms_logs` koleksiyonu. Üçüncü, BAĞIMSIZ bir sistem.
/// Bu sistem müşteriyi, teklifi veya poliçeyi TANIMAZ: tek anahtarı telefon numarasıdır.
/// Bu yüzden "poliçe SMS'i müşteriye ulaştı mı?" sorusu zincirleme cevaplanır:
///   quote_logs (quoteId/policyNo → identityNo) → users (identityNo → phone) → sms_logs (phone → teslim durumu)
public sealed class SmsLogEntry
{
    public string? Id { get; init; }
    public required DateTime Timestamp { get; init; }          // UTC
    public required LogSeverity Level { get; init; }
    public required string Service { get; init; }              // NotificationGateway, SmsProviderAdapter
    public string Environment { get; init; } = "Local";

    public required string MessageId { get; init; }            // SMS-xxxxxxxx: bir mesajın tüm durum loglarını bağlar
    public required string Phone { get; init; }                // E.164 (+905...). PII: dışarıya asla açık verilmez
    public required string TemplateCode { get; init; }         // POLICY_ISSUED, QUOTE_READY, OTP ...
    public required SmsStatus Status { get; init; }
    public string? Provider { get; init; }                     // PROV_A, PROV_B (sentetik SMS sağlayıcıları)
    public string? ErrorCode { get; init; }
    public required string Message { get; init; }
    public int? DurationMs { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
}
