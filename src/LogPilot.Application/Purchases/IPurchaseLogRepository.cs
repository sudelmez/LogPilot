using LogPilot.Application.Common;
using LogPilot.Domain.Purchases;

namespace LogPilot.Application.Purchases;

public sealed record ProposalStepSearch(
    DateTime From, DateTime To,
    IReadOnlyCollection<string>? IdentityNos = null, string? ProposalNo = null, string? Step = null,
    bool OnlyErrors = false, string? ResponseContains = null, int Limit = 50, bool Ascending = false);

public sealed record WinsureSearch(
    DateTime From, DateTime To,
    IReadOnlyCollection<string>? PolicyNos = null, WinsureOperation? Operation = null, bool OnlyFailed = false,
    int Limit = 50, bool Ascending = false);

public sealed record PaymentSearch(
    DateTime From, DateTime To,
    IReadOnlyCollection<string>? PolicyNos = null, IReadOnlyCollection<string>? ClientReferenceCodes = null,
    PaymentStage? Stage = null, bool OnlyFailed = false, int Limit = 50, bool Ascending = false);

/// Seçili penceredeki satın alma logları (istatistik için; üst sınırlı).
public sealed record PurchaseWindow(
    IReadOnlyList<ProposalStepLog> Steps, IReadOnlyList<WinsureTransaction> Winsure, IReadOnlyList<PaymentTransaction> Payments);

/// SALT-OKUNUR görünüm. AI araçları yalnızca bunu görür; DI'da okuma yetkili MongoDB hesabına bağlanır.
public interface IPurchaseLogReader
{
    Task<PagedResult<ProposalStepLog>> SearchProposalStepsAsync(ProposalStepSearch request, CancellationToken ct);
    Task<PagedResult<WinsureTransaction>> SearchWinsureAsync(WinsureSearch request, CancellationToken ct);
    Task<PagedResult<PaymentTransaction>> SearchPaymentsAsync(PaymentSearch request, CancellationToken ct);

    Task<ProposalStepLog?> GetProposalStepByIdAsync(string id, CancellationToken ct);
    Task<WinsureTransaction?> GetWinsureByIdAsync(string id, CancellationToken ct);
    Task<PaymentTransaction?> GetPaymentByIdAsync(string id, CancellationToken ct);

    Task<PurchaseWindow> GetWindowAsync(DateTime from, DateTime to, CancellationToken ct);
    Task<long> GetEstimatedCountAsync(CancellationToken ct);
}

public interface IPurchaseLogRepository : IPurchaseLogReader
{
    Task AddManyAsync(IReadOnlyCollection<ProposalStepLog> steps, IReadOnlyCollection<WinsureTransaction> winsure,
        IReadOnlyCollection<PaymentTransaction> payments, CancellationToken ct);
}
