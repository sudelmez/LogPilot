namespace LogPilot.Domain.Users;

public enum UserStatus { Active, Inactive }

/// Sentetik kullanıcı/müşteri dizini (`users` koleksiyonu). İki log tablosunu birbirine bağlayan "anahtar defteri":
///   userCode   → `logs` (mobil uygulama) ve mobil kanaldaki `quote_logs`
///   customerNo → `quote_logs` (her kanal)
///   identityNo → `quote_logs` (web kanalında TEK anahtar). PII: yalnızca backend eşleştirmede kullanır, dışarıya maskeli çıkar.
///   phone      → `sms_logs` (SMS sisteminin tanıdığı TEK anahtar). PII: aynı kural.
public sealed class User
{
    public required string UserCode { get; init; }   // Mongo'da _id
    public required string UserId { get; init; }
    public required UserStatus Status { get; init; }
    public List<string> Roles { get; init; } = [];
    public string? CustomerNo { get; init; }         // CUS-100xxx (acente/admin kullanıcılarında boş)
    public string? IdentityNo { get; init; }         // 11 haneli, SENTETİK (gerçek T.C. no algoritmasına uymaz)
    public string? Phone { get; init; }              // E.164, SENTETİK. `sms_logs`'un TEK anahtarı. PII: dışarıya maskeli çıkar
}
