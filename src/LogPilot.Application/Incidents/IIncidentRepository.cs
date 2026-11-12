using LogPilot.Application.Common;
using LogPilot.Domain.Incidents;

namespace LogPilot.Application.Incidents;

/// SALT-OKUNUR görünüm. AI araçları (LogAnalysisTools) yalnızca bu arayüzü görür: yazma/silme metodu derleme
/// zamanında erişilemez. DI'da bu arayüz, okuma yetkili (read-only) MongoDB hesabına bağlanır.
public interface IIncidentReader
{
    Task<PagedResult<Incident>> SearchAsync(IncidentStatus? status, IncidentSeverity? severity, bool activeOnly, int page, int pageSize, CancellationToken ct);
    Task<Incident?> GetByIdAsync(string id, CancellationToken ct);

    /// Çözülmemiş (Open/Investigating) incident'lar, en son görülen önce.
    Task<IReadOnlyList<Incident>> GetActiveAsync(int limit, CancellationToken ct);
    Task<long> CountActiveAsync(CancellationToken ct);
}

public interface IIncidentRepository : IIncidentReader
{

    Task<Incident?> FindActiveByErrorCodeAsync(string errorCode, CancellationToken ct);
    Task<Incident> InsertAsync(Incident incident, CancellationToken ct);
    Task ReplaceAsync(Incident incident, CancellationToken ct);
    Task<bool> UpdateStatusAsync(string id, IncidentStatus status, DateTime nowUtc, CancellationToken ct);
}
