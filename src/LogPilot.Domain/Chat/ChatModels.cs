using LogPilot.Domain.Ai;

namespace LogPilot.Domain.Chat;

public enum ChatRole { User, Assistant }

public sealed class ChatSession
{
    public string? Id { get; init; }
    public required string UserName { get; init; }
    public required string Title { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
}

public sealed class ChatMessage
{
    public string? Id { get; init; }
    public required string SessionId { get; init; }
    public required ChatRole Role { get; init; }
    public required string Content { get; init; }                 // asistan için: özet metni
    public AssistantAnswer? Answer { get; init; }
    public List<ToolExecution> ToolExecutions { get; init; } = [];
    public DateTime? RangeFrom { get; init; }                     // kullanıcı mesajı: seçilen tarih aralığı
    public DateTime? RangeTo { get; init; }
    public string? Provider { get; init; }
    public required DateTime CreatedAt { get; init; }
    public MessageFeedback? Feedback { get; init; }               // yalnızca asistan mesajlarında
}

/// Kullanıcının bir asistan cevabına verdiği geri bildirim.
public sealed class MessageFeedback
{
    public required bool Helpful { get; init; }
    public string? Resolution { get; init; }                      // "gerçek çözüm neydi?" (varsa vaka hafızasına girer)
    public string? KnowledgeCaseId { get; init; }
    public required DateTime CreatedAt { get; init; }
}

/// Cevaba backend'in eklediği ilgili geçmiş vaka (doğrulanmış hafızadan; kanıt değildir).
public sealed class RelatedCase
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Resolution { get; init; }
    public string? RootCause { get; init; }
    public List<string> ErrorCodes { get; init; } = [];
    public List<string> Services { get; init; } = [];
    public required DateTime CreatedAt { get; init; }
}

/// Asistanın bir araç çağrısının denetim kaydı (hangi araç, hangi parametre, kaç sonuç).
public sealed class ToolExecution
{
    public required string Name { get; init; }
    public string ArgumentsJson { get; init; } = "{}";
    public long DurationMs { get; init; }
    public int ResultCount { get; init; }
    public bool Truncated { get; init; }
    public int FlaggedContent { get; init; }                      // enjeksiyon şüphesiyle temizlenen metin sayısı
    public string? Error { get; init; }

    /// Zincirleme araçlarda (ör. SMS teslimi, poliçe satın alma) gezilen tablolar, sırasıyla. Arayüz bunu hat çizgisi olarak çizer.
    public List<InvestigationHop> Path { get; init; } = [];
}

/// Zincirin bir halkası: hangi tabloya, hangi anahtarla bakıldı, bir şey bulundu mu.
public sealed class InvestigationHop
{
    public required string Source { get; init; }
    public required string LookedUpBy { get; init; }
    public bool Found { get; init; }
    public string? Result { get; init; }
}

/// Yapılandırılmış asistan cevabı. Kanıt ve güven BACKEND tarafından doğrulanıp hesaplanır.
public sealed class AssistantAnswer
{
    public required string Summary { get; init; }
    public string? ProbableCause { get; init; }
    public ConfidenceLevel Confidence { get; init; }
    public int ConfidenceScore { get; init; }
    public List<string> ConfidenceFactors { get; init; } = [];
    public List<EvidenceItem> Evidence { get; init; } = [];
    public List<string> EvidenceLogIds { get; init; } = [];
    public List<string> AffectedServices { get; init; } = [];
    public List<string> SuggestedActions { get; init; } = [];
    public List<string> DataLimitations { get; init; } = [];
    public string? ClarifyingQuestion { get; init; }
    public List<RelatedCase> RelatedCases { get; init; } = [];   // backend ekler, LLM değil

    /// Doluysa soru görev alanı dışındaydı ve kibarca reddedildi: "OffTopic" (alakasız/genel danışmanlık) veya "Abusive" (argo/hakaret).
    public string? OutOfScope { get; init; }
}

public sealed class EvidenceItem
{
    public string? ErrorCode { get; init; }
    public string? Service { get; init; }
    public int Count { get; init; }
    public DateTime? FirstSeen { get; init; }
    public DateTime? LastSeen { get; init; }
    public List<string> LogIds { get; init; } = [];
    public List<string> UserCodes { get; init; } = [];
    public List<string> CorrelationIds { get; init; } = [];
}
