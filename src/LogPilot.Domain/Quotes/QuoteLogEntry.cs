using LogPilot.Domain.Logs;

namespace LogPilot.Domain.Quotes;

/// Teklifin hangi kanaldan istendiği. Kanal, hangi tabloların birlikte inceleneceğini belirler:
///   Mobile → `logs` (MobileGateway) ile aynı correlationId'yi taşır.
///   Web    → oturum yok: sadece kimlik numarası (identityNo) bilinir, userCode boştur.
///   Agent  → teklifi acente kullanıcısı ister: userCode ACENTENİN kodudur, müşteri customerNo/identityNo ile bulunur.
public enum QuoteChannel { Mobile, Web, Agent }

/// Sigorta teklif motorunun (QuoteEngine) logları — `quote_logs` koleksiyonu.
/// Uygulama loglarından (`logs`) ayrı bir sistemdir; iki tablo correlationId, customerNo ve identityNo ile eşlenir.
public sealed class QuoteLogEntry
{
    public string? Id { get; init; }
    public required DateTime Timestamp { get; init; }          // UTC
    public required LogSeverity Level { get; init; }
    public required string Service { get; init; }              // QuoteOrchestrator, VehicleRegistryGateway, ...
    public string Environment { get; init; } = "Local";

    public required string QuoteId { get; init; }              // TKL-yyyyMMdd-xxxxx: bir teklifin tüm adımlarını bağlar
    public required string Step { get; init; }                 // QuoteRequested, VehicleLookup, RiskScoring, InsurerPricing, QuoteIssued...
    public required QuoteChannel Channel { get; init; }
    public required string Product { get; init; }              // Kasko, Trafik, Konut, Saglik

    /// Teklifi BAŞLATAN kullanıcı (mobilde müşterinin kendisi, acentede acente kullanıcısı, web'de boş).
    public string? UserCode { get; init; }
    public string? CustomerNo { get; init; }                   // müşteri numarası (PII değil). Web kanalında boş olabilir.
    public string? IdentityNo { get; init; }                   // T.C. kimlik no (SENTETİK; PII: dışarıya asla açık verilmez)
    public string? CorrelationId { get; init; }                // Mobil kanalda `logs` ile ortak

    public string? PolicyNo { get; init; }                     // POL-...: sadece PolicyIssued adımında (teklif poliçeye dönüştü)
    public string? InsurerCode { get; init; }                  // InsurerPricing adımlarında hangi sigorta şirketi
    public decimal? PremiumAmount { get; init; }               // TL
    public OperationOutcome? Outcome { get; init; }
    public string? ErrorCode { get; init; }
    public string? ExceptionType { get; init; }
    public required string Message { get; init; }
    public int? DurationMs { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
}
