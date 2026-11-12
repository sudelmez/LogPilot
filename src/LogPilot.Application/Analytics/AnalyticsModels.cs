using LogPilot.Domain.Analytics;

namespace LogPilot.Application.Analytics;

public sealed record TimeRange(DateTime From, DateTime To);

public sealed record LogCounts(long Total, long Errors, long Warnings, long Completed, long Failed);

public sealed record TrendPoint(DateTime BucketStart, long Total, long Errors, long Warnings);

/// Servis başına ham sayılar (repository çıktısı).
public sealed record ServiceStats(
    string Service, long Total, long Errors, long Warnings,
    double? AvgDurationMs, long? MaxDurationMs, DateTime? LastErrorAt);

public sealed record ErrorCodeCount(string ErrorCode, long Count, long DistinctUsers, DateTime LastSeen);

// ---- API çıktıları ----

/// LogErrorRatio: Error/Critical log sayısı / tüm loglar (sistemin ne kadar "gürültülü" olduğu).
/// Requests.FailureRate: Outcome=Failure operasyonlar / tamamlanan (Success+Failure) operasyonlar
/// (kullanıcının gerçekten yaşadığı başarısızlık; bir istek birden çok hata logu üretebilir).
public sealed record AnalyticsSummary(
    DateTime From, DateTime To,
    long TotalLogs, long ErrorCount, long WarningCount,
    double LogErrorRatio, RequestStats Requests);

public sealed record RequestStats(long Completed, long Failed, double FailureRate);

public sealed record ServiceErrorShare(string Service, long Errors, long TotalLogs, double LogErrorRatio, double ShareOfAllErrors);

public sealed record ServiceHealth(
    string Service, long TotalLogs, long Errors, long Warnings, double LogErrorRatio,
    double? AvgDurationMs, long? MaxDurationMs, DateTime? LastErrorAt, ServiceHealthStatus Status);
