using LogPilot.Domain.Logs;
using LogPilot.Domain.Notifications;
using LogPilot.Domain.Quotes;
using LogPilot.Domain.Users;

namespace LogPilot.Domain.Scenarios;

public enum ScenarioKind
{
    Normal,     // arka plan trafiği (ağırlıklı rastgele seçilir)
    Incident    // hata senaryosu (zamanlanır veya elle tetiklenir)
}

/// Bir log akışının tanımı. Tanım JSON dosyasından gelir; Settings canlı ayarlardır ve DB'de yaşar.
public sealed class Scenario
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public required ScenarioKind Kind { get; init; }
    public int? Seed { get; init; }                         // doluysa her çalıştırma aynı logları üretir
    public required UserSelection Users { get; init; }
    public required ScenarioSettings Settings { get; set; }
    public required List<ScenarioStep> Steps { get; init; }

    /// Senaryoda `target: "Quote"` adımı varsa zorunlu: teklifin kanalı ve ürünü.
    public QuoteFlow? Quote { get; init; }

    /// Kurumsal web poliçe satın alma akışı. Doluysa motor adımları değil bu akışı üretir
    /// (proposal_step_logs + winsure_transactions + payment_transactions).
    public PurchaseFlow? Purchase { get; init; }
}

/// Satın almanın nasıl sonuçlanacağı. Her sonuç, hatanın FARKLI bir tabloda görünmesini sağlar.
public enum PurchaseOutcome
{
    Completed,              // her şey başarılı
    PaymentDeclined,        // banka reddi (payment_transactions sağlayıcı kaydı, policyNo boş)
    Payment3dsFailed,       // 3D Secure başarısız (payment_transactions)
    PaymentNoCallback,      // sağlayıcıdan sonuç gelmedi (payment_transactions'ta sadece Initiated var)
    ApproveRejected,        // ödeme alındı ama Winsure onay vermedi (winsure_transactions Approve)
    WinsureCreateTimeout,   // poliçe hiç oluşturulamadı (winsure_transactions Create), ödemeye geçilmedi
    CancelledAfterPurchase  // satın alındı, sonra iptal edildi (Winsure Cancel + ödeme iadesi)
}

public sealed class PurchaseFlow
{
    public PurchaseOutcome Outcome { get; init; } = PurchaseOutcome.Completed;
    public List<string> Products { get; init; } = ["Kasko"];
    public string Provider { get; init; } = "VPOS_A";
}

/// Bir teklif akışının bağlamı. Ürün akış başına listeden seçilir.
public sealed class QuoteFlow
{
    public QuoteChannel Channel { get; init; } = QuoteChannel.Mobile;
    public List<string> Products { get; init; } = ["Kasko"];
}

/// Adımın yazılacağı tablo: uygulama logları (`logs`), teklif motoru (`quote_logs`) veya SMS ağ geçidi (`sms_logs`).
public enum StepTarget { AppLog, Quote, Sms }

public sealed class UserSelection
{
    public UserStatus Status { get; init; } = UserStatus.Active;
    public List<string> UserCodes { get; init; } = [];      // boş değilse havuzu bu kullanıcılarla sınırlar
    public string? Role { get; init; }                      // doluysa havuz bu role sahip kullanıcılarla sınırlanır (Customer, Agent)
    public int MinCount { get; init; } = 1;
    public int MaxCount { get; init; } = 1;
    public int SpacingMs { get; init; }                     // kullanıcı akışları arasındaki başlangıç farkı
}

public sealed class ScenarioSettings
{
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; }                // Incident: ortalama tetikleme aralığı (0 = sadece elle)
    public double Probability { get; set; } = 1.0;          // Incident: aralık dolunca gerçekten tetiklenme olasılığı
    public int Weight { get; set; } = 1;                    // Normal: seçilme ağırlığı
}

/// Her kullanıcı akışı için tekrarlanan tek bir log adımı.
public sealed class ScenarioStep
{
    public int OffsetMs { get; init; }
    public StepTarget Target { get; init; } = StepTarget.AppLog;
    public string Application { get; init; } = "";           // AppLog adımlarında zorunlu (Quote adımlarında "QuoteEngine")
    public required string Service { get; init; }
    public required LogSeverity Level { get; init; }
    public required string Operation { get; init; }          // Quote adımlarında teklif adımı (Step)
    public OperationOutcome? Outcome { get; init; }
    public string? ErrorCode { get; init; }
    public string? ExceptionType { get; init; }
    public int? DurationMs { get; init; }
    public required string Message { get; init; }           // {userCode}, {userId}, {correlationId}, {quoteId}, {product}, {customerNo}, ...
    public double Probability { get; init; } = 1.0;
    public string? InsurerCode { get; init; }                // Quote: InsurerPricing adımının sigorta şirketi
    public decimal? PremiumAmount { get; init; }             // Quote: baz prim (motor ±%15 oynatır)
    public SmsStatus? SmsStatus { get; init; }               // Sms: bu logun mesaj durumu (Operation = şablon kodu)
    public string? Provider { get; init; }                   // Sms: SMS sağlayıcısı
    public Dictionary<string, string> Metadata { get; init; } = new();
}
