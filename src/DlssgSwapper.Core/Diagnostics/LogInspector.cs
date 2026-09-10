using System.Text.Json;

namespace DlssgSwapper.Core.Diagnostics;

public sealed record LogCheckResult(
    bool LogsFound,
    bool? InstallActive,
    int? BackendInstallStatus,
    string? Image,
    IReadOnlyList<string> Messages)
{
    public bool Verified =>
        LogsFound && InstallActive == true && BackendInstallStatus == 0;
}

// Reads the newest loader_/backend_ jsonl logs the mod writes next to the game
// executable (dlssg_sm86/logs) and extracts the SM86-route activation markers
// documented in Native 0.2.4: loader "install" active=true and backend_install status=0.
public static class LogInspector
{
    public static LogCheckResult Check(string gameDirectory)
    {
        string logDir = Path.Combine(gameDirectory, "dlssg_sm86", "logs");
        if (!Directory.Exists(logDir))
            return new LogCheckResult(false, null, null, null, Array.Empty<string>());

        string? loader = Newest(logDir, "loader_*.jsonl");
        string? backend = Newest(logDir, "backend_*.jsonl");

        bool? installActive = null;
        int? status = null;
        string? image = null;
        var messages = new List<string>();

        if (loader != null)
        {
            foreach (var root in JsonLines(loader))
            {
                string? evt = root.TryGetProperty("event", out var e) ? e.GetString() : null;
                switch (evt)
                {
                    case "install":
                        installActive = root.TryGetProperty("active", out var a) ? a.GetBoolean() : null;
                        image ??= root.TryGetProperty("image", out var img) ? img.GetString() : null;
                        break;
                    case "backend_install":
                        status = root.TryGetProperty("status", out var s) ? s.GetInt32() : null;
                        break;
                    case "mfg_capability":
                        image ??= root.TryGetProperty("image", out var m) ? m.GetString() : null;
                        break;
                }
            }
        }

        if (backend != null)
            foreach (var root in JsonLines(backend))
                messages.Add(root.ToString().Length > 200 ? root.ToString()[..200] : root.ToString());

        if (installActive == null) messages.Add("No loader install event found; is the proxy DLL loaded?");
        if (installActive == false) messages.Add("install.active=false: SM86 route was not enabled.");
        if (status != null && status != 0) messages.Add($"backend_install.status={status}.");
        if (status == null) messages.Add("No backend_install event found.");

        return new LogCheckResult(true, installActive, status, image, messages);
    }

    private static string? Newest(string dir, string pattern) =>
        Directory.GetFiles(dir, pattern)
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
            .FirstOrDefault();

    private static IEnumerable<JsonElement> JsonLines(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonElement element;
            try
            {
                using var doc = JsonDocument.Parse(line);
                element = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                continue; // skip malformed lines; the file may be mid-write
            }
            yield return element;
        }
    }
}