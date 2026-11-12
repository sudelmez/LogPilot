namespace LogPilot.Domain.Knowledge;

/// Vakanın hafızaya nereden geldiği. Hepsi bir İNSAN onayı içerir: model kendi cevabını hafızaya yazamaz.
public enum KnowledgeSource
{
    ChatFeedback,        // asistan cevabına 👍 + "gerçek çözüm" notu
    IncidentResolution,  // incident "Resolved" yapılırken yazılan çözüm notu
    Manual               // Knowledge sayfasından elle eklenen
}

/// Doğrulanmış geçmiş vaka: "bu hata/servis kombinasyonu daha önce yaşandı, şu şekilde çözüldü".
/// Yeni sorularda İPUCU olarak kullanılır; kanıt yerine geçmez, güven puanını etkilemez.
public sealed class KnowledgeCase
{
    public string? Id { get; init; }
    public required string Title { get; set; }
    public List<string> ErrorCodes { get; set; } = [];
    public List<string> Services { get; set; } = [];
    public string? Symptom { get; set; }          // kullanıcının sorusu / görülen belirti
    public string? RootCause { get; set; }
    public required string Resolution { get; set; }
    public required KnowledgeSource Source { get; init; }
    public string? SourceId { get; init; }        // sohbet mesajı veya incident kimliği (aynı kaynak tekrar onaylanınca güncellenir)
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; set; }
    public long TimesSuggested { get; set; }      // yeni cevaplarda kaç kez "ilgili vaka" olarak gösterildi
    public DateTime? LastSuggestedAt { get; set; }
}
