using LogPilot.Application.Common;
using LogPilot.Domain.Knowledge;

namespace LogPilot.Application.Knowledge;

public sealed record KnowledgeQuery(
    IReadOnlyCollection<string>? ErrorCodes = null,
    IReadOnlyCollection<string>? Services = null,
    string? Text = null,
    int Page = 1,
    int PageSize = 20);

/// SALT-OKUNUR görünüm. AI araçları (LogAnalysisTools) yalnızca bu arayüzü görür: yazma/silme metodu derleme
/// zamanında erişilemez. DI'da bu arayüz, okuma yetkili (read-only) MongoDB hesabına bağlanır.
public interface IKnowledgeReader
{
    /// Hata kodu VEYA servis eşleşen (ve metin filtresine uyan) vakalar, en yeni önce. Sıralama/puanlama serviste.
    Task<PagedResult<KnowledgeCase>> SearchAsync(KnowledgeQuery query, CancellationToken ct);
}

public interface IKnowledgeRepository : IKnowledgeReader
{
    Task<KnowledgeCase> InsertAsync(KnowledgeCase knowledgeCase, CancellationToken ct);
    Task<KnowledgeCase?> GetByIdAsync(string id, CancellationToken ct);
    Task<KnowledgeCase?> FindBySourceAsync(KnowledgeSource source, string sourceId, CancellationToken ct);
    Task ReplaceAsync(KnowledgeCase knowledgeCase, CancellationToken ct);
    Task<bool> DeleteAsync(string id, CancellationToken ct);

    Task MarkSuggestedAsync(IReadOnlyCollection<string> ids, DateTime nowUtc, CancellationToken ct);
}
