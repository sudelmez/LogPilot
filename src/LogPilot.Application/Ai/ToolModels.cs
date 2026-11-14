using LogPilot.Application.Analytics;

namespace LogPilot.Application.Ai;

/// Kullanıcının seçtiği tarih aralığı: araçlar bu aralığın DIŞINA çıkamaz.
/// RangeSource: aralık sorudan alındıysa o ifade ("son 15 dakika"); arayüz seçimiyse null.
public sealed record ToolContext(DateTime From, DateTime To, string? RangeSource = null);

public sealed class ToolArgumentException(string message) : Exception(message);

// ---- Araç argümanları (izinli filtre alanları = sadece bu özellikler; fazlası reddedilir) ----
public sealed record SearchLogsArgs(
    string? Service = null, string? Application = null, string? UserCode = null, string? ErrorCode = null,
    string? Level = null, string? CorrelationId = null, string? TransactionId = null,
    DateTime? From = null, DateTime? To = null, int? Limit = null);

public sealed record UserRecentErrorsArgs(string? UserCode = null, int? Limit = null);
public sealed record ServiceHealthArgs(string? Service = null);
public sealed record ErrorStatisticsArgs(int? Limit = null);
public sealed record RelatedLogsArgs(string? CorrelationId = null, string? TransactionId = null, string? LogId = null, string? QuoteId = null);

// ---- Teklif (quote_logs) ve müşteri dizini (users) araçları ----
public sealed record CustomerProfileArgs(string? UserCode = null, string? CustomerNo = null, string? QuoteId = null);

public sealed record SearchQuoteLogsArgs(
    string? QuoteId = null, string? UserCode = null, string? CustomerNo = null, string? Product = null,
    string? Channel = null, string? InsurerCode = null, string? Step = null, string? ErrorCode = null,
    string? Level = null, string? CorrelationId = null, DateTime? From = null, DateTime? To = null, int? Limit = null);

public sealed record QuoteFunnelArgs(string? Product = null, string? Channel = null, string? InsurerCode = null);

public sealed record NotificationTraceArgs(
    string? QuoteId = null, string? PolicyNo = null, string? UserCode = null, string? CustomerNo = null, string? TemplateCode = null);

public sealed record SearchSmsLogsArgs(
    string? UserCode = null, string? CustomerNo = null, string? QuoteId = null, string? TemplateCode = null,
    string? Status = null, string? ErrorCode = null, bool? OnlyProblems = null, int? Limit = null);

// ---- Kurumsal web satın alma ----
public sealed record PurchaseTraceArgs(string? UserCode = null, string? CustomerNo = null, string? PolicyNo = null, string? ProposalNo = null);
public sealed record SearchProposalStepsArgs(
    string? UserCode = null, string? CustomerNo = null, string? ProposalNo = null, string? PolicyNo = null, string? Step = null,
    bool? OnlyErrors = null, int? Limit = null);
public sealed record SearchWinsureArgs(string? PolicyNo = null, string? Operation = null, bool? OnlyFailed = null, int? Limit = null);
public sealed record SearchPaymentsArgs(string? PolicyNo = null, string? ClientReferenceCode = null, bool? OnlyFailed = null, int? Limit = null);
public sealed record PurchaseStatsArgs(string? Product = null);

public sealed record CustomerJourneyArgs(string? UserCode = null, string? CustomerNo = null, string? QuoteId = null, int? Limit = null);
public sealed record ActiveIncidentsArgs(int? Limit = null);
public sealed record IncidentDetailsArgs(string? IncidentId = null);
public sealed record SearchKnowledgeArgs(string? ErrorCode = null, string? Service = null, string? Text = null, int? Limit = null);

// ---- Araç sonuçları (içerik zaten maskelenmiş) ----
public sealed record LogSnippet(
    string Id, DateTime Timestamp, string Level, string Application, string Service,
    string? UserCode, string? CorrelationId, string? TransactionId, string? Operation, string? Outcome,
    string? ErrorCode, string? ExceptionType, string Message, int? DurationMs,
    IReadOnlyDictionary<string, string> Metadata,
    string Source = LogSources.AppLogs);

/// Bir kanıtın hangi tablodan geldiği: AI'ın "hangi sistemde gördüm?" ayrımını yapabilmesi için her logda taşınır.
public static class LogSources
{
    public const string AppLogs = "app_logs";       // MongoDB `logs`: mobil uygulama + backend servisleri
    public const string QuoteLogs = "quote_logs";   // MongoDB `quote_logs`: sigorta teklif motoru
    public const string SmsLogs = "sms_logs";       // MongoDB `sms_logs`: SMS ağ geçidi (sadece telefon numarasını bilir)
    public const string ProposalSteps = "proposal_step_logs";     // kurumsal web satın alma adımları (kimlik no + teklif no)
    public const string Winsure = "winsure_transactions";         // poliçe çekirdek sistemi (policyNo + XML)
    public const string Payments = "payment_transactions";        // ödeme (policyNo → clientReferenceCode → sağlayıcı sonucu)
}

public sealed record LogToolResult(IReadOnlyList<LogSnippet> Logs, long TotalMatched, bool Truncated, string Scope);

public sealed record ServiceHealthToolResult(IReadOnlyList<ServiceHealth> Services, string Scope);

public sealed record ErrorGroupSummary(
    string ErrorCode, long Count, long DistinctUsers, IReadOnlyList<string> UserCodes, IReadOnlyList<string> Services,
    DateTime FirstSeen, DateTime LastSeen, IReadOnlyList<LogSnippet> SampleLogs);

public sealed record ErrorStatisticsToolResult(IReadOnlyList<ErrorGroupSummary> Groups, string Scope);

public sealed record IncidentToolItem(
    string Id, string Title, string Severity, string Status, string ErrorCode,
    IReadOnlyList<string> AffectedServices, int AffectedUserCount, long LogCount,
    DateTime FirstSeenAt, DateTime LastSeenAt, string? AiSummary);

public sealed record IncidentListToolResult(IReadOnlyList<IncidentToolItem> Incidents, string Scope);

public sealed record IncidentDetailsToolResult(
    IncidentToolItem Incident, IReadOnlyList<string> AffectedUserCodes, IReadOnlyList<LogSnippet> SampleLogs);

/// İnsanların doğruladığı geçmiş vaka (ipucu; kanıt değildir). Tarih aralığıyla sınırlı DEĞİLDİR: amaç geçmişi hatırlamak.
public sealed record KnowledgeSnippet(
    string Id, string Title, IReadOnlyList<string> ErrorCodes, IReadOnlyList<string> Services,
    string? Symptom, string? RootCause, string Resolution, string Source, DateTime UpdatedAt);

public sealed record KnowledgeToolResult(IReadOnlyList<KnowledgeSnippet> Cases, string Note);

/// Kullanıcı dizininden bir kayıt. identityNo ASLA açık dönmez (KVKK): sadece son iki hane.
public sealed record CustomerProfile(
    string UserCode, string UserId, string? CustomerNo, string Status, IReadOnlyList<string> Roles,
    string? IdentityNoMasked, bool HasMobileAccess, string? PhoneMasked = null);

public sealed record CustomerProfileToolResult(IReadOnlyList<CustomerProfile> Profiles, string Note);

public sealed record QuoteFunnelToolResult(
    long QuotesRequested, long QuotesIssued, long QuotesFailed, long QuotesWithPartialOffers,
    IReadOnlyDictionary<string, long> RequestsByChannel,
    IReadOnlyList<Quotes.QuoteStepStats> Steps, IReadOnlyList<Quotes.InsurerStats> Insurers,
    IReadOnlyList<Quotes.QuoteErrorStats> Errors, IReadOnlyList<LogSnippet> SampleLogs, string Scope);

/// İki tablonun birleştirildiği yer: bir müşterinin mobil logları + tüm kanallardaki teklif logları tek zaman çizelgesinde.
public sealed record JourneyLink(
    string? CorrelationId, string? QuoteId, string Channel, string? MobileOutcome, string? QuoteOutcome, string? RootErrorCode);

public sealed record CustomerJourneyToolResult(
    CustomerProfile? Customer, IReadOnlyList<JourneyLink> Links, IReadOnlyList<string> Findings,
    IReadOnlyList<LogSnippet> Timeline, long AppLogCount, long QuoteLogCount, bool Truncated, string Scope);

/// Zincirleme aramanın bir halkası: hangi tabloya, hangi anahtarla bakıldı, ne bulundu.
/// Cevabın "nasıl buldum" kısmı budur; kullanıcıya da aynen anlatılabilir.
public sealed record TraceHop(int Order, string Source, string LookedUpBy, bool Found, string Result);

public sealed record SmsMessageSummary(
    string MessageId, string TemplateCode, string FinalStatus, string? Provider, int Attempts,
    DateTime FirstAt, DateTime LastAt, string? ErrorCode);

/// Teslim kararı: Delivered | NotDeliveredToHandset | FailedToSend | Pending | NeverSent | NoPolicy | Unknown
public sealed record NotificationTraceToolResult(
    IReadOnlyList<TraceHop> Path, CustomerProfile? Customer, string? QuoteId, string? PolicyNo,
    string Verdict, IReadOnlyList<SmsMessageSummary> Messages, IReadOnlyList<string> Findings,
    IReadOnlyList<LogSnippet> Evidence, string Scope);

/// Satın alma zinciri: her halka (TraceHop) + karar. FailedStage: hatanın gerçekten oluştuğu yer.
/// Verdict: Completed | Cancelled | PaymentFailed | PaymentPending | ApproveFailedAfterPayment | PolicyCreateFailed |
///          AbandonedBeforePolicy | AbandonedBeforePayment | Unknown
public sealed record PurchaseTraceToolResult(
    IReadOnlyList<TraceHop> Path, CustomerProfile? Customer, string? ProposalNo, string? PolicyNo, string? ClientReferenceCode,
    string Verdict, string? FailedStage, string? PolicyStatus, string? PaymentStatus, bool CustomerCharged,
    IReadOnlyList<string> Findings, IReadOnlyList<LogSnippet> Evidence, string Scope);

public sealed record PurchaseErrorStat(string ErrorCode, string Source, string Stage, long Count, IReadOnlyList<string> SampleLogIds);

public sealed record PurchaseStatsToolResult(
    long Started, long PolicyCreated, long PolicyCreateFailed, long PaymentStarted, long Paid, long PaymentFailed, long PaymentPending,
    long Approved, long ApproveFailedAfterPayment, long Cancelled, IReadOnlyList<PurchaseErrorStat> Errors,
    IReadOnlyList<LogSnippet> SampleLogs, string Scope);
