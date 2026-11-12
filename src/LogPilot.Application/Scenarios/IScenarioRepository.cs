using LogPilot.Domain.Scenarios;

namespace LogPilot.Application.Scenarios;

public interface IScenarioRepository
{
    Task<IReadOnlyList<Scenario>> GetAllAsync(CancellationToken ct);
    Task<Scenario?> GetByIdAsync(string id, CancellationToken ct);

    /// Dosyadaki tanımı yazar; DB'deki canlı Settings (enabled/interval/probability/weight) korunur.
    Task SyncDefinitionAsync(Scenario definition, CancellationToken ct);

    /// Canlı ayarları günceller. Senaryo yoksa false.
    Task<bool> UpdateSettingsAsync(string id, ScenarioSettings settings, CancellationToken ct);
}
