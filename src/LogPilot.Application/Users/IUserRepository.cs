using LogPilot.Domain.Users;

namespace LogPilot.Application.Users;

/// SALT-OKUNUR görünüm. AI araçları (LogAnalysisTools) yalnızca bu arayüzü görür: yazma/silme metodu derleme
/// zamanında erişilemez. DI'da bu arayüz, okuma yetkili (read-only) MongoDB hesabına bağlanır.
public interface IUserReader
{
    Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct);

    /// Verilen anahtarlardan HERHANGİ BİRİNE uyan kullanıcılar (userCode / customerNo / identityNo).
    Task<IReadOnlyList<User>> FindAsync(
        IReadOnlyCollection<string>? userCodes, IReadOnlyCollection<string>? customerNos,
        IReadOnlyCollection<string>? identityNos, CancellationToken ct);
}

public interface IUserRepository : IUserReader
{
    Task UpsertManyAsync(IReadOnlyCollection<User> users, CancellationToken ct);
}
