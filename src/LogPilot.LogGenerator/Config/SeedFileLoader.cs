using System.Text.Json;
using System.Text.Json.Serialization;
using LogPilot.Domain.Scenarios;
using LogPilot.Domain.Users;

namespace LogPilot.LogGenerator.Config;

public static class SeedFileLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// Klasördeki tüm *.json senaryo dosyalarını okur. Hatalar tek tek değil, hepsi birlikte raporlanır.
    public static IReadOnlyList<Scenario> LoadScenarios(string directory)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Scenario directory not found: {directory}");

        var scenarios = new List<Scenario>();
        var errors = new List<string>();

        foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            try
            {
                var scenario = JsonSerializer.Deserialize<Scenario>(File.ReadAllText(file), JsonOptions)
                               ?? throw new JsonException("The file is empty.");

                var problems = ScenarioValidator.Validate(scenario, stem);
                if (problems.Count == 0) scenarios.Add(scenario);
                else errors.AddRange(problems.Select(p => $"{stem}.json: {p}"));
            }
            catch (JsonException ex)
            {
                errors.Add($"{stem}.json: invalid JSON: {ex.Message}");
            }
        }

        if (errors.Count > 0)
            throw new InvalidDataException("Invalid scenario files:" + Environment.NewLine + string.Join(Environment.NewLine, errors));

        return scenarios;
    }

    public static IReadOnlyList<User> LoadUsers(string file)
    {
        if (!File.Exists(file)) throw new FileNotFoundException("User seed file not found.", file);

        try
        {
            var users = JsonSerializer.Deserialize<List<User>>(File.ReadAllText(file), JsonOptions)
                        ?? throw new JsonException("The file is empty.");

            var duplicate = users.GroupBy(u => u.UserCode, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (duplicate is not null) throw new InvalidDataException($"Duplicate userCode '{duplicate.Key}' in {file}.");

            return users;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Invalid JSON in {file}: {ex.Message}", ex);
        }
    }
}
