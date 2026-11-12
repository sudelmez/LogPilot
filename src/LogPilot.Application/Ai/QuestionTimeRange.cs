using System.Globalization;
using System.Text.RegularExpressions;

namespace LogPilot.Application.Ai;

/// Sorudaki AÇIK zaman ifadesi ("son 15 dakika", "last 2 hours", "bugün", "yesterday") → analiz aralığı.
/// Sağlayıcıdan bağımsızdır: Mock, Ollama ve Claude aynı aralıkla çalışır; model aralığı seçmez, backend belirler.
/// Açık bir ifade yoksa null döner ve arayüzde seçilen aralık kullanılır.
public sealed record QuestionRange(DateTime From, DateTime To, string Label);

public static partial class QuestionTimeRange
{
    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(7);

    // "son 15 dakika", "son 15 dakikada", "son 15 dk", "last 15 minutes", "past 30 mins", "within the last 2 hours",
    // "son 3 saat", "son 2 gün", "son bir saat", "last two hours", "geçen 10 dakika"
    [GeneratedRegex(
        @"\b(?:last|past|son|geçen|gecen)\s+(?<n>\d{1,4}(?:[.,]\d+)?|a|an|one|two|three|four|five|six|ten|twelve|bir|iki|üç|uc|dört|dort|beş|bes|altı|alti|on|on\s?iki)?\s*" +
        @"(?<unit>minutes?|mins?|min\b|m\b|dakika\w*|dk\b|hours?|hrs?|h\b|saat\w*|days?|d\b|gün\w*|gun\w*|weeks?|hafta\w*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LastN();

    // "son yarım saat", "last half hour", "past half an hour"
    [GeneratedRegex(@"\b(?:last|past|son)\s+(?:half\s+(?:an\s+)?hour|yarım\s+saat\w*|yarim\s+saat\w*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HalfHour();

    [GeneratedRegex(@"\b(?:today|bugün\w*|bugun\w*)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Today();

    [GeneratedRegex(@"\b(?:yesterday|dün|dünkü|dünü|dünden|dun)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Yesterday();

    /// nowUtc: aralığın sonu (genelde "şimdi"). localZone: "bugün"/"dün" için gün sınırları (Docker'da TZ=Europe/Istanbul).
    public static QuestionRange? TryParse(string? question, DateTime nowUtc, TimeZoneInfo localZone)
    {
        if (string.IsNullOrWhiteSpace(question)) return null;
        var q = question.Trim();

        var half = HalfHour().Match(q);
        if (half.Success)
            return Relative(nowUtc, TimeSpan.FromMinutes(30), half.Value);

        var last = LastN().Match(q);
        if (last.Success)
        {
            var amount = ParseAmount(last.Groups["n"].Value);
            var unit = Unit(last.Groups["unit"].Value);
            if (amount is > 0 && unit is not null)
                return Relative(nowUtc, Clamp(TimeSpan.FromTicks((long)(unit.Value.Ticks * amount.Value))), last.Value);
        }

        if (Yesterday().IsMatch(q))
        {
            var todayStart = LocalMidnightUtc(nowUtc, localZone);
            var yesterdayStart = LocalMidnightUtc(todayStart.AddHours(-12), localZone);
            return new QuestionRange(yesterdayStart, todayStart, Yesterday().Match(q).Value);
        }

        if (Today().IsMatch(q))
        {
            var start = LocalMidnightUtc(nowUtc, localZone);
            if (start < nowUtc) return new QuestionRange(start, nowUtc, Today().Match(q).Value);
        }

        return null;
    }

    private static QuestionRange Relative(DateTime nowUtc, TimeSpan span, string label) =>
        new(nowUtc - span, nowUtc, label.Trim());

    private static TimeSpan Clamp(TimeSpan span) =>
        span < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : span > MaxRange ? MaxRange : span;

    /// Sayı yoksa ("son saat", "last hour") 1 kabul edilir.
    private static double? ParseAmount(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 1;
        var s = raw.Trim().ToLowerInvariant().Replace(" ", "");
        if (double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return number;
        return s switch
        {
            "a" or "an" or "one" or "bir" => 1,
            "two" or "iki" => 2,
            "three" or "üç" or "uc" => 3,
            "four" or "dört" or "dort" => 4,
            "five" or "beş" or "bes" => 5,
            "six" or "altı" or "alti" => 6,
            "ten" or "on" => 10,
            "twelve" or "oniki" => 12,
            _ => null
        };
    }

    private static TimeSpan? Unit(string raw)
    {
        var u = raw.ToLowerInvariant();
        if (u.StartsWith("min") || u == "m" || u.StartsWith("dakika") || u == "dk") return TimeSpan.FromMinutes(1);
        if (u.StartsWith("hour") || u.StartsWith("hr") || u == "h" || u.StartsWith("saat")) return TimeSpan.FromHours(1);
        if (u.StartsWith("day") || u == "d" || u.StartsWith("gün") || u.StartsWith("gun")) return TimeSpan.FromDays(1);
        if (u.StartsWith("week") || u.StartsWith("hafta")) return TimeSpan.FromDays(7);
        return null;
    }

    private static DateTime LocalMidnightUtc(DateTime utc, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local.Date, DateTimeKind.Unspecified), zone);
    }
}
