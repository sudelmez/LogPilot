using LogPilot.Domain.Logs;
using LogPilot.Domain.Quotes;

namespace LogPilot.Application.Quotes;

/// Huni hesabını tek yerde tutar: Mongo deposu filtrelenmiş logları çeker, hesap burada yapılır (testlerde de aynısı).
/// Teklif sayısı küçük olduğu için (TTL'li, saatlik binler) bellek içi gruplama yeterli ve okunaklı.
public static class QuoteFunnelCalculator
{
    public const string RequestedStep = "QuoteRequested";
    public const string IssuedStep = "QuoteIssued";
    public const string PricingStep = "InsurerPricing";

    public static QuoteFunnel Calculate(IReadOnlyList<QuoteLogEntry> logs)
    {
        var byQuote = logs.GroupBy(l => l.QuoteId).ToList();
        var requested = byQuote.Count(g => g.Any(l => l.Step == RequestedStep));
        var issued = byQuote.Count(g => g.Any(l => l.Step == IssuedStep && l.Outcome == OperationOutcome.Success));
        var failed = byQuote.Count(g => g.Any(l => l.Outcome == OperationOutcome.Failure && l.Step != PricingStep));
        var partial = byQuote.Count(g => g.Any(l => l.Step == IssuedStep && l.Outcome == OperationOutcome.Success)
                                         && g.Any(l => l.Step == PricingStep && l.ErrorCode is not null));

        var steps = logs.GroupBy(l => (l.Step, l.Service))
            .Select(g => new QuoteStepStats(g.Key.Step, g.Key.Service, g.Count(),
                g.Count(l => l.Level >= LogSeverity.Error), g.Count(l => l.Level == LogSeverity.Warning),
                Math.Round(g.Where(l => l.DurationMs is not null).Select(l => (double)l.DurationMs!.Value).DefaultIfEmpty(0).Average(), 1)))
            .OrderBy(s => s.Step, StringComparer.Ordinal).ToList();

        var insurers = logs.Where(l => l.Step == PricingStep && l.InsurerCode is not null)
            .GroupBy(l => l.InsurerCode!)
            .Select(g => new InsurerStats(g.Key, g.Count(l => l.ErrorCode is null), g.Count(l => l.ErrorCode is not null),
                Math.Round(g.Where(l => l.DurationMs is not null).Select(l => (double)l.DurationMs!.Value).DefaultIfEmpty(0).Average(), 1),
                g.Where(l => l.PremiumAmount is not null).Select(l => l.PremiumAmount!.Value) is var premiums && premiums.Any()
                    ? Math.Round(premiums.Average(), 2) : null))
            .OrderByDescending(i => i.Failed).ThenBy(i => i.InsurerCode, StringComparer.Ordinal).ToList();

        var errors = logs.Where(l => l.ErrorCode is not null)
            .GroupBy(l => (l.ErrorCode!, l.Service, l.Step))
            .Select(g => new QuoteErrorStats(g.Key.Item1, g.Key.Service, g.Key.Step, g.Count(), g.Select(l => l.QuoteId).Distinct().Count(),
                g.OrderByDescending(l => l.Timestamp).Take(3).Select(l => l.Id).OfType<string>().ToList()))
            .OrderByDescending(e => e.Count).ThenBy(e => e.ErrorCode, StringComparer.Ordinal).ToList();

        var channels = byQuote.Where(g => g.Any(l => l.Step == RequestedStep))
            .GroupBy(g => g.First().Channel.ToString())
            .ToDictionary(g => g.Key, g => (long)g.Count());

        return new QuoteFunnel(requested, issued, failed, partial, steps, insurers, errors, channels);
    }
}
