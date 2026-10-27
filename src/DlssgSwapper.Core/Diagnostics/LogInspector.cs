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

    // Which of the package's proxies the game loaded ("version.dll", "winmm.dll", ...), when the
    // configuration record says: that is the one that is doing the work, and the one to keep.
    public string? Proxy { get; init; }
}

// Reads the log the mod writes next to the game executable (dlssg_sm86\logs\dlssg_sm86.log) and
// extracts the activation markers: loader "runtime_redirect" and "backend_install" status, backend
// "install" active and "routed".
//
// Since 0.4.1 every module of every process appends to that one file, each line being
// "<local time>  pid <n>  <exe name>  <level letter>  {json record}", so a line is only counted for
// the game whose EXE name it carries; the newest "log_session" record starts a new run and resets
// what was collected before it. (0.4.0 wrote per-process loader_<pid>.jsonl / backend_<pid>.jsonl
// instead; those files are no longer written or read.)
public static class LogInspector
{
    public const string LogFileName = "dlssg_sm86.log";

    // Level letters the mod prefixes a record with: E = 1 (errors), I = 2, D = 3.
    private const string LevelLetters = "EID";

    public static LogCheckResult Check(string gameDirectory, string? executableName = null)
    {
        string logDir = Path.Combine(gameDirectory, "dlssg_sm86", "logs");
        string logPath = Path.Combine(logDir, LogFileName);
        if (!File.Exists(logPath))
            return new LogCheckResult(false, null, null, null, Array.Empty<string>());

        var state = new State();
        bool sawGame = false;
        foreach (var line in ReadLines(logPath))
        {
            if (!TryParseLine(line, out string? exe, out JsonElement record)) continue;
            // A line whose EXE name cannot be read is counted rather than dropped: it is this mod's
            // record either way, and dropping it would report "the proxy was not loaded".
            if (exe != null && executableName != null
                && !string.Equals(exe, executableName, StringComparison.OrdinalIgnoreCase))
                continue;
            sawGame = true;

            // A new process (or a restarted game) starts over: what an earlier run said about its
            // route does not describe this one.
            if (Event(record) == "log_session")
            {
                state = new State();
                continue;
            }

            state.Absorb(record);
        }

        if (!sawGame)
        {
            // The file exists but holds nothing for this game yet: the proxy was not loaded (or the
            // game has not been started since the log was last cleared).
            return new LogCheckResult(true, null, null, null, new[]
            {
                executableName == null
                    ? "The mod log exists but holds no records yet."
                    : $"No record for {executableName} in {LogFileName} yet; the proxy may not have been loaded.",
            });
        }

        return state.ToResult();
    }

    private sealed class State
    {
        public bool? InstallActive;
        public int? Status;
        public string? Image;
        public string? Proxy;
        public bool Redirected;
        public bool Routed;
        public readonly List<string> Messages = new();

        public void Absorb(JsonElement root)
        {
            switch (Event(root))
            {
                case "configuration":
                    Proxy ??= ActiveProxy(root);
                    break;
                case "runtime_redirect":
                    Redirected = true;
                    break;
                case "backend_install":
                    Status = root.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.Number
                        ? s.GetInt32()
                        : null;
                    break;
                case "install":
                    InstallActive ??= root.TryGetProperty("active", out var a) && a.ValueKind is JsonValueKind.True or JsonValueKind.False
                        ? a.GetBoolean()
                        : null;
                    Image ??= String(root, "image");
                    break;
                case "mfg_capability":
                    Image ??= String(root, "image");
                    break;
                case "routed":
                    Routed = true;
                    break;
                // Records that explain why the route is not (or not fully) working. A wrong INI value
                // no longer falls back to a default: it disables the feature it belongs to, so the
                // record is the only place the reason shows up.
                case "configuration_invalid":
                case "configuration_error":
                case "install_failed":
                case "backend_unusable":
                case "bundled_runtime_unavailable":
                case "driver_below_floor":
                case "max_generated_frames_unsupported":
                case "fg_kernels_unavailable":
                case "fg_feature_refused":
                case "kernel_pack_missing":
                case "kernel_pack_rejected":
                case "fg_kernel_fatal":
                case "preset_ineffective":
                    AddMessage(root);
                    break;
            }
        }

        private void AddMessage(JsonElement root)
        {
            string name = Event(root) ?? "error";
            string? message = Message(root);
            if (message == null && name is "configuration_invalid" or "configuration_warning")
                message = DescribeKey(root, name);

            string text = message != null ? $"{message} ({name})" : $"The mod reported {name}.";
            if (!Messages.Contains(text)) Messages.Add(text);
        }

        // {"section":"DLSSG","key":"Preset","value":"Auto","allowed":"A/B/C","effect":"..."} - a wrong
        // value disables the feature it belongs to, so say which key and what it means.
        private static string? DescribeKey(JsonElement root, string name)
        {
            string? section = String(root, "section");
            string? key = String(root, "key");
            if (section == null || key == null) return null;

            string text = $"[{section}] {key}={String(root, "value")} " +
                          (name == "configuration_warning" ? "had no effect" : "was rejected by the mod");
            if (String(root, "allowed") is string allowed) text += $"; allowed: {allowed}";
            if (String(root, "effect") is string effect) text += $"; effect: {effect}";
            return text;
        }

        public LogCheckResult ToResult()
        {
            var messages = new List<string>(Messages);
            if (!Redirected)
                messages.Add("No runtime_redirect record; the proxy may not have been loaded.");
            if (InstallActive == null)
                messages.Add("No install record yet; start the game and enable DLSS frame generation.");
            if (InstallActive == false)
                messages.Add("install.active=false: the SM86 route was not enabled.");
            if (Status != null && Status != 0)
                messages.Add($"backend_install.status={Status}.");
            if (Status == null)
                messages.Add("No backend_install record found.");
            if (InstallActive == true && !Routed)
                messages.Add("No routed record yet; start the game and enable DLSS frame generation.");

            return new LogCheckResult(true, InstallActive, Status, Image, messages) { Proxy = Proxy };
        }
    }

    // {"active":"version.dll","standby":[]} - which of the package's proxies the game loaded.
    private static string? ActiveProxy(JsonElement root)
    {
        if (!root.TryGetProperty("proxies", out var proxies) || proxies.ValueKind != JsonValueKind.Object)
            return null;
        return String(proxies, "active");
    }

    // One log line: "<time>  pid <n>  <exe>  <letter>  {json}", e.g.
    //   2026-10-22 14:03:11.482  pid 12345  Cyberpunk2077.exe  E  {"event":...}
    // The EXE name is the token just before the level letter, so a name containing spaces still
    // parses; when that token is the pid's number there is no name and the line is unattributed.
    private static bool TryParseLine(string line, out string? exe, out JsonElement record)
    {
        exe = null;
        record = default;
        if (string.IsNullOrWhiteSpace(line)) return false;

        int jsonStart = line.IndexOf('{');
        if (jsonStart <= 0) return false;

        string prefix = line[..jsonStart].TrimEnd();
        int level = prefix.Length - 1;
        if (level < 0 || !LevelLetters.Contains(prefix[level])) return false;

        string head = prefix[..level].TrimEnd();
        int exeStart = head.Length;
        while (exeStart > 0 && !char.IsWhiteSpace(head[exeStart - 1])) exeStart--;
        string candidate = head[exeStart..];
        if (candidate.Length > 0 && !int.TryParse(candidate, out _))
            exe = candidate;

        try
        {
            using var doc = JsonDocument.Parse(line[jsonStart..]);
            record = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return false; // a line the mod is still writing
        }
        return true;
    }

    private static IEnumerable<string> ReadLines(string path)
    {
        // The game may be appending while this runs: a sharing violation is not worth failing over,
        // and a half-written last line is dropped by the JSON parse.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line) yield return line;
    }

    private static string? Event(JsonElement root) =>
        root.TryGetProperty("event", out var e) ? e.GetString() : null;

    private static string? Message(JsonElement root) => String(root, "message");

    private static string? String(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
