using LogPilot.Domain.Scenarios;

namespace LogPilot.LogGenerator.Engine;

public static class WeightedPicker
{
    /// Settings.Weight ağırlığıyla rastgele senaryo seçer. Liste boş olmamalı ve en az bir ağırlık > 0 olmalı.
    public static Scenario Pick(IReadOnlyList<Scenario> scenarios, Random rng)
    {
        var total = scenarios.Sum(s => s.Settings.Weight);
        if (total <= 0) throw new InvalidOperationException("No scenario with a positive weight.");

        var roll = rng.Next(total);
        foreach (var scenario in scenarios)
        {
            roll -= scenario.Settings.Weight;
            if (roll < 0) return scenario;
        }

        return scenarios[^1];
    }
}
