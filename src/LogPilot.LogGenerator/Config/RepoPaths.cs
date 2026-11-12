namespace LogPilot.LogGenerator.Config;

public static class RepoPaths
{
    /// Derleme dizininden yukarı doğru çıkıp verilen isimde bir klasör arar (ör. "scenarios").
    /// Çalışma dizininden bağımsızdır; hem "dotnet run" hem testler için aynı şekilde çalışır.
    public static string FindDirectory(string name, string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return Path.GetFullPath(configuredPath);

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, name);
            if (Directory.Exists(candidate)) return candidate;
        }

        throw new DirectoryNotFoundException(
            $"Could not find a '{name}' directory above '{AppContext.BaseDirectory}'. " +
            "Set Generator:ScenariosPath / Generator:SeedPath in appsettings.json.");
    }
}
