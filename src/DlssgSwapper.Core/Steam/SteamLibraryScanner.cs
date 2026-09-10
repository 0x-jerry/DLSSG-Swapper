using Microsoft.Win32;

namespace DlssgSwapper.Core.Steam;

public sealed record SteamApp(string AppId, string Name, string InstallDir);

public sealed record CandidateExe(string Path, int Score, long Size)
{
    public string DisplayName => Path;
}

public static class SteamLibraryScanner
{
    public static string? FindSteamInstallDir()
    {
        foreach (var (hive, subKey) in new (RegistryHive, string)[]
                 {
                     (RegistryHive.CurrentUser, @"Software\Valve\Steam"),
                     (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam"),
                 })
        {
            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(subKey);
            string? path = key?.GetValue("SteamPath") as string;
            if (!string.IsNullOrEmpty(path)) return path;
        }
        return null;
    }

    public static IReadOnlyList<string> FindLibraryRoots(string steamInstallDir)
    {
        var roots = new List<string>();
        string mainAppsDir = Path.Combine(steamInstallDir, "steamapps");
        if (Directory.Exists(mainAppsDir)) roots.Add(steamInstallDir);

        string vdfPath = Path.Combine(mainAppsDir, "libraryfolders.vdf");
        if (File.Exists(vdfPath))
        {
            try
            {
                var doc = KeyValuesParser.Parse(File.ReadAllText(vdfPath));
                var libraries = doc.Find("libraryfolders");
                if (libraries != null)
                {
                    foreach (var entry in libraries.Children)
                    {
                        string? path = entry.GetValue("path");
                        if (!string.IsNullOrEmpty(path)) roots.Add(path);
                    }
                }
            }
            catch (Exception)
            {
                // Treat an unparsable libraryfolders.vdf as no extra libraries.
            }
        }

        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<SteamApp> FindInstalledApps(IEnumerable<string> libraryRoots)
    {
        var apps = new List<SteamApp>();
        var seenAppIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in libraryRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string appsDir = Path.Combine(root, "steamapps");
            if (!Directory.Exists(appsDir)) continue;

            foreach (string file in Directory.EnumerateFiles(appsDir, "appmanifest_*.acf"))
            {
                try
                {
                    var doc = KeyValuesParser.Parse(File.ReadAllText(file));
                    var state = doc.Find("AppState");
                    string appId = state?.GetValue("appid") ?? "";
                    string name = state?.GetValue("name") ?? "";
                    string installDir = state?.GetValue("installdir") ?? "";
                    if (name.Length == 0) continue;
                    // The same library can be listed twice (registry plus libraryfolders.vdf),
                    // so one game may have a manifest in more than one scanned root.
                    if (appId.Length > 0 && !seenAppIds.Add(appId)) continue;

                    string resolved = Path.Combine(appsDir, "common", installDir);
                    apps.Add(new SteamApp(appId, name, Directory.Exists(resolved) ? resolved : Path.Combine(root, "steamapps", "common", installDir)));
                }
                catch (Exception)
                {
                    // Skip unparsable manifests.
                }
            }
        }
        return apps.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<CandidateExe> FindCandidateExecutables(string gameInstallDir, int limit = 50)
    {
        if (!Directory.Exists(gameInstallDir)) return Array.Empty<CandidateExe>();

        var candidates = new List<CandidateExe>();
        foreach (string exe in Directory.EnumerateFiles(gameInstallDir, "*.exe", SearchOption.AllDirectories))
        {
            var info = new FileInfo(exe);
            string relative = Path.GetRelativePath(gameInstallDir, exe);
            int score = relative.Contains("Win64", StringComparison.OrdinalIgnoreCase) ? 60 : 0;
            score += relative.Contains("Binaries", StringComparison.OrdinalIgnoreCase) ? 40 : 0;
            if (File.Exists(Path.Combine(Path.GetDirectoryName(exe)!, "nvngx_dlssg.dll")))
                score += 200;
            candidates.Add(new CandidateExe(exe, score, info.Length));
        }

        return candidates
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.Size)
            .Take(limit)
            .ToList();
    }
}