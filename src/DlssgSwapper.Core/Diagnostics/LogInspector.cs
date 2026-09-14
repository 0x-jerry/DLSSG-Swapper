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

// Reads the newest loader_/backend_ jsonl logs the proxy writes next to the game
// executable (dlssg_sm86/logs) and extracts the activation markers: loader
// "runtime_redirect" and "backend_install" status, backend "install" active and "routed".
public static class LogInspector
{
    public static LogCheckResult Check(string gameDirectory)
    {
        string logDir = Path.Combine(gameDirectory, "dlssg_sm86", "logs");
        if (!Directory.Exists(logDir))
            return new LogCheckResult(false, null, null, null, Array.Empty<string>());

        var messages = new List<string>();
        bool? installActive = null;
        int? status = null;
        string? image = null;
        bool redirected = false;
        bool routed = false;

        string? loader = Newest(logDir, "loader_*.jsonl");
        if (loader != null)
        {
            foreach (var root in JsonLines(loader))
            {
                switch (Event(root))
                {
                    case "runtime_redirect":
                        redirected = true;
                        break;
                    case "backend_install":
                        status = root.TryGetProperty("status", out var s) ? s.GetInt32() : null;
                        break;
                    case "configuration_error":
                        messages.Add(Message(root) ?? "The proxy rejected the INI (configuration_error).");
                        break;
                }
            }
        }

        string? backend = Newest(logDir, "backend_*.jsonl");
        if (backend != null)
        {
            foreach (var root in JsonLines(backend))
            {
                switch (Event(root))
                {
                    case "install":
                        installActive ??= root.TryGetProperty("active", out var a) ? a.GetBoolean() : null;
                        image ??= String(root, "image");
                        break;
                    case "mfg_capability":
                        image ??= String(root, "image");
                        break;
                    case "routed":
                        routed = true;
                        break;
                    case "install_failed":
                        messages.Add(Message(root) ?? "The backend failed to install (install_failed).");
                        break;
                }
            }
        }

        if (!redirected) messages.Add("No runtime_redirect event in the loader log; the proxy may not have been loaded.");
        if (installActive == null) messages.Add("No install event found in the backend log; is frame generation enabled in the game?");
        if (installActive == false) messages.Add("install.active=false: the SM86 route was not enabled.");
        if (status != null && status != 0) messages.Add($"backend_install.status={status}.");
        if (status == null) messages.Add("No backend_install event found in the loader log.");
        if (installActive == true && !routed) messages.Add("No routed event yet; start the game and enable DLSS frame generation.");

        return new LogCheckResult(true, installActive, status, image, messages);
    }

    private static string? Event(JsonElement root) =>
        root.TryGetProperty("event", out var e) ? e.GetString() : null;

    private static string? Message(JsonElement root) => String(root, "message");

    private static string? String(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

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
