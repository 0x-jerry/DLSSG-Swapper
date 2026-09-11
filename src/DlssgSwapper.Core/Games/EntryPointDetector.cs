namespace DlssgSwapper.Core.Games;

public static class EntryPointDetector
{
    public static IReadOnlySet<string> FindPresent(string? gameDirectory, IEnumerable<string> fileNames)
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(gameDirectory) || !Directory.Exists(gameDirectory))
            return present;

        foreach (string name in fileNames)
            if (File.Exists(Path.Combine(gameDirectory, name)))
                present.Add(name);
        return present;
    }
}
