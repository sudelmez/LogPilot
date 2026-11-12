using LogPilot.Application.Common;
using LogPilot.Domain.Logs;
using LogPilot.Domain.Quotes;

namespace LogPilot.Application.Quotes;

/// `quote_logs` sorgusu. Kimlik filtreleri (CustomerNos / IdentityNos / UserCodes) "VEYA" ile birleşir:
/// bir müşterinin tüm teklifleri kanal fark etmeksizin (mobil: userCode, acente: customerNo, web: identityNo) tek sorguda gelir.
public sealed record QuoteLogSearchRequest(
    DateTime From, DateTime To,
    string? QuoteId = null, string? CorrelationId = null, string? PolicyNo = null,
    IReadOnlyCollection<string>? UserCodes = null,
    IReadOnlyCollection<string>? CustomerNos = null,
    IReadOnlyCollection<string>? IdentityNos = null,
    string? Product = null, QuoteChannel? Channel = null, string? InsurerCode = null,
    string? Step = null, string? ErrorCode = null, LogSeverity? Level = null,
    IReadOnlyCollection<string>? CorrelationIds = null,
    int Limit = 50, bool Ascending = false);

public sealed record QuoteStepStats(string Step, string Service, long Total, long Failures, long Warnings, double AvgDurationMs);

public sealed record InsurerStats(string InsurerCode, long Priced, long Failed, double AvgDurationMs, decimal? AvgPremium);

public sealed record QuoteErrorStats(string ErrorCode, string Service, string Step, long Count, long DistinctQuotes, IReadOnlyList<string> SampleLogIds);

/// Teklif hunisi: kaç teklif istendi, kaçı verildi, kaçı nerede düştü.
public sealed record QuoteFunnel(
    long QuotesRequested, long QuotesIssued, long QuotesFailed, long QuotesWithPartialOffers,
    IReadOnlyList<QuoteStepStats> Steps, IReadOnlyList<InsurerStats> Insurers,
    IReadOnlyList<QuoteErrorStats> Errors, IReadOnlyDictionary<string, long> RequestsByChannel);

/// SALT-OKUNUR görünüm. AI araçları (LogAnalysisTools) yalnızca bu arayüzü görür: yazma/silme metodu derleme
/// zamanında erişilemez. DI'da bu arayüz, okuma yetkili (read-only) MongoDB hesabına bağlanır.
public interface IQuoteLogReader
{
    Task<PagedResult<QuoteLogEntry>> SearchAsync(QuoteLogSearchRequest request, CancellationToken ct);
    Task<QuoteLogEntry?> GetByIdAsync(string id, CancellationToken ct);
    Task<IReadOnlyList<QuoteLogEntry>> GetByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken ct);
    Task<long> GetEstimatedCountAsync(CancellationToken ct);
    Task<QuoteFunnel> GetFunnelAsync(DateTime from, DateTime to, string? product, QuoteChannel? channel, string? insurerCode, CancellationToken ct);
}

public interface IQuoteLogRepository : IQuoteLogReader
{
    Task AddManyAsync(IReadOnlyCollection<QuoteLogEntry> entries, CancellationToken ct);
}
