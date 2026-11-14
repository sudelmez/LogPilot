using System.Text.RegularExpressions;
using LogPilot.Domain.Logs;
using LogPilot.Domain.Quotes;
using LogPilot.Domain.Scenarios;
using LogPilot.LogGenerator.Engine;

namespace LogPilot.LogGenerator.Config;

/// Senaryo dosyalarındaki hataları (typo, mantık hatası) başlangıçta yakalar; çalışma zamanında sürpriz olmaz.
public static partial class ScenarioValidator
{
    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex PlaceholderRegex();

    public static IReadOnlyList<string> Validate(Scenario s, string fileStem)
    {
        var errors = new List<string>();

        if (s.Id != fileStem) errors.Add($"id '{s.Id}' must match the file name '{fileStem}'.");
        if (string.IsNullOrWhiteSpace(s.Name)) errors.Add("name is required.");
        if (s.Steps.Count == 0 && s.Purchase is null) errors.Add("at least one step (or a purchase flow) is required.");
        if (s.Purchase is not null && s.Steps.Count > 0) errors.Add("purchase: a purchase scenario must not define steps.");
        if (s.Purchase is { Products.Count: 0 }) errors.Add("purchase: products must not be empty.");
        if (s.Purchase is not null && !string.Equals(s.Users.Role, "Customer", StringComparison.OrdinalIgnoreCase))
            errors.Add("purchase: users.role must be 'Customer' (the buyer needs an identity number).");

        if (s.Users.MinCount < 1 || s.Users.MaxCount < s.Users.MinCount)
            errors.Add("users: minCount must be >= 1 and maxCount must be >= minCount.");
        if (s.Users.SpacingMs < 0) errors.Add("users: spacingMs must be >= 0.");

        if (s.Settings.Probability is < 0 or > 1) errors.Add("settings: probability must be between 0 and 1.");
        if (s.Settings.IntervalSeconds < 0) errors.Add("settings: intervalSeconds must be >= 0.");
        if (s.Settings.Weight < 0) errors.Add("settings: weight must be >= 0.");

        var hasQuoteSteps = s.Steps.Any(st => st.Target == StepTarget.Quote);
        if (hasQuoteSteps && s.Quote is null) errors.Add("quote: required when a step has target 'Quote'.");
        if (s.Quote is { Products.Count: 0 }) errors.Add("quote: products must not be empty.");
        if (s.Steps.Any(st => st.Target == StepTarget.Sms) && s.Quote is null)
            errors.Add("quote: required when a step has target 'Sms' (the SMS goes to the quote's customer).");
        if (s.Quote is { Channel: not QuoteChannel.Mobile } && s.Steps.Any(st => st.Target == StepTarget.AppLog))
            errors.Add("quote: only the Mobile channel writes app logs; Web/Agent scenarios may only have Quote steps.");

        for (var i = 0; i < s.Steps.Count; i++)
        {
            var step = s.Steps[i];
            var at = $"steps[{i}]";

            if (step.Target == StepTarget.AppLog && string.IsNullOrWhiteSpace(step.Application))
                errors.Add($"{at}: application is required for app log steps.");
            if (step.Target == StepTarget.AppLog && (step.InsurerCode is not null || step.PremiumAmount is not null))
                errors.Add($"{at}: insurerCode/premiumAmount are only allowed on Quote steps.");
            if (step.PremiumAmount is < 0) errors.Add($"{at}: premiumAmount must be >= 0.");
            if (step.Target == StepTarget.Sms && step.SmsStatus is null) errors.Add($"{at}: smsStatus is required for Sms steps.");
            if (step.Target != StepTarget.Sms && (step.SmsStatus is not null || step.Provider is not null))
                errors.Add($"{at}: smsStatus/provider are only allowed on Sms steps.");

            if (step.OffsetMs < 0) errors.Add($"{at}: offsetMs must be >= 0.");
            if (step.Probability is < 0 or > 1) errors.Add($"{at}: probability must be between 0 and 1.");
            if (step.DurationMs is < 0) errors.Add($"{at}: durationMs must be >= 0.");
            if (step.Level >= LogSeverity.Error && string.IsNullOrWhiteSpace(step.ErrorCode))
                errors.Add($"{at}: Error/Critical steps must define an errorCode.");

            foreach (var text in step.Metadata.Values.Append(step.Message))
            foreach (Match match in PlaceholderRegex().Matches(text))
            {
                if (!ScenarioEngine.KnownVariables.Contains(match.Groups[1].Value))
                    errors.Add($"{at}: unknown placeholder '{match.Value}'.");
            }
        }

        return errors;
    }
}
