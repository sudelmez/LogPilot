namespace LogPilot.Domain.Analytics;

public enum ServiceHealthStatus { Unknown, Healthy, Degraded, Unhealthy }

public static class Rates
{
    /// 0'a bölme güvenli oran (0..1).
    public static double Ratio(long numerator, long denominator)
        => denominator <= 0 ? 0 : (double)numerator / denominator;
}

public static class ServiceHealthClassifier
{
    public const int MinSampleSize = 5;
    public const double DegradedThreshold = 0.05;
    public const double UnhealthyThreshold = 0.20;

    public static ServiceHealthStatus Classify(long totalLogs, double logErrorRatio)
    {
        if (totalLogs < MinSampleSize) return ServiceHealthStatus.Unknown;   // az veriyle yargı verme
        if (logErrorRatio >= UnhealthyThreshold) return ServiceHealthStatus.Unhealthy;
        if (logErrorRatio >= DegradedThreshold) return ServiceHealthStatus.Degraded;
        return ServiceHealthStatus.Healthy;
    }
}

public static class TrendBuckets
{
    /// 1440'ı böldüğü için gün sınırına hizalıdır; Mongo ve C# aynı kovaları üretir.
    public static readonly int[] AllowedMinutes = [1, 5, 10, 15, 30, 60, 120, 360, 1440];
    public const int TargetPoints = 96;
    public const int MaxPoints = 500;

    public static int Choose(TimeSpan range)
    {
        foreach (var minutes in AllowedMinutes)
            if (range.TotalMinutes / minutes <= TargetPoints) return minutes;
        return AllowedMinutes[^1];
    }

    public static bool IsValid(int minutes, TimeSpan range)
        => AllowedMinutes.Contains(minutes) && range.TotalMinutes / minutes <= MaxPoints;

    public static DateTime Floor(DateTime utc, int minutes)
    {
        var unixMs = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        var bucketMs = minutes * 60_000L;
        return DateTimeOffset.FromUnixTimeMilliseconds(unixMs - unixMs % bucketMs).UtcDateTime;
    }
}

/// Aynı hata kodunu paylaşan hata logları (Error/Critical). Incident tespiti ve "birden fazla kullanıcı etkilendi mi?" için.
public sealed record ErrorGroup(
    string ErrorCode,
    long Count,
    IReadOnlyList<string> UserCodes,
    IReadOnlyList<string> Services,
    DateTime FirstSeen,
    DateTime LastSeen,
    IReadOnlyList<string> RecentLogIds);
