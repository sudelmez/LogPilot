using LogPilot.Application.Common;
using LogPilot.Domain.Logs;
using LogPilot.Domain.Notifications;

namespace LogPilot.Application.Notifications;

/// `sms_logs` sorgusu. Telefon numaraları yalnızca backend'in kullanıcı dizininden çözdüğü değerlerdir (LLM'den gelmez).
public sealed record SmsLogSearchRequest(
    DateTime From, DateTime To,
    IReadOnlyCollection<string>? Phones = null, string? MessageId = null, string? TemplateCode = null,
    SmsStatus? Status = null, string? ErrorCode = null, LogSeverity? MinLevel = null,
    int Limit = 50, bool Ascending = false);

/// SALT-OKUNUR görünüm. AI araçları (LogAnalysisTools) yalnızca bu arayüzü görür: yazma/silme metodu derleme
/// zamanında erişilemez. DI'da bu arayüz, okuma yetkili (read-only) MongoDB hesabına bağlanır.
public interface ISmsLogReader
{
    Task<PagedResult<SmsLogEntry>> SearchAsync(SmsLogSearchRequest request, CancellationToken ct);
    Task<SmsLogEntry?> GetByIdAsync(string id, CancellationToken ct);
    Task<long> GetEstimatedCountAsync(CancellationToken ct);
}

public interface ISmsLogRepository : ISmsLogReader
{
    Task AddManyAsync(IReadOnlyCollection<SmsLogEntry> entries, CancellationToken ct);
}
