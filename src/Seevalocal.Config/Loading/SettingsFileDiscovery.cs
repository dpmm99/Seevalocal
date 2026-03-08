namespace Seevalocal.Config.Loading;

/// <summary>
/// Locates candidate settings files according to the discovery order defined in
/// 03-config.md §7.
/// </summary>
public static class SettingsFileDiscovery
{
    private static readonly string[] CandidateNames = ["Seevalocal.yml", "Seevalocal.yaml", "Seevalocal.json"];

    /// <summary>
    /// Returns the first discoverable default settings file path, or null if none found.
    /// Discovery order:
    ///   1. ./Seevalocal.yml (or .yaml / .json) in the current working directory
    ///   2. ~/.Seevalocal/default.yml
    /// </summary>
    public static string? FindDefault()
    {
        foreach (var name in CandidateNames)
        {
            var cwdPath = Path.Combine(Directory.GetCurrentDirectory(), name);
            if (File.Exists(cwdPath))
                return cwdPath;
        }

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var homeCandidates = new[]
        {
            Path.Combine(homeDir, ".Seevalocal", "default.yml"),
            Path.Combine(homeDir, ".Seevalocal", "default.yaml"),
            Path.Combine(homeDir, ".Seevalocal", "default.json"),
        };

        foreach (var path in homeCandidates)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }
}
