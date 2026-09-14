using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DlssgSwapper.Core.Hardware;

public enum SmTarget
{
    Unknown,
    Sm86,
    Unsupported,
}

public sealed record GpuInfo(string? Name, string? ComputeCapability, string? DriverVersion, SmTarget Sm)
{
    public bool IsSupported => Sm != SmTarget.Unsupported;

    public string? SuggestedRouter => Sm == SmTarget.Sm86 ? "SM86" : null;

    public string Describe() => Name ?? (ComputeCapability != null ? $"compute capability {ComputeCapability}" : "unknown GPU");
}

public static partial class GpuDetector
{
    public static GpuInfo Detect()
    {
        foreach (string candidate in NvidiaSmiCandidates())
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                string output = Run(candidate);
                if (!string.IsNullOrWhiteSpace(output))
                    return ParseOutput(output);
            }
            catch (Exception)
            {
                // Try the next candidate location.
            }
        }
        return new GpuInfo(null, null, null, SmTarget.Unknown);
    }

    public static GpuInfo ParseOutput(string output)
    {
        string? line = output.Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0);
        if (line is null) return new GpuInfo(null, null, null, SmTarget.Unknown);

        // nvidia-smi csv: name, compute_cap, driver_version (fields may be quoted).
        var fields = SplitCsv(line);
        string? name = Unquote(fields.ElementAtOrDefault(0));
        string? computeCap = Unquote(fields.ElementAtOrDefault(1));
        string? driver = Unquote(fields.ElementAtOrDefault(2));
        return new GpuInfo(name, computeCap, driver, Classify(computeCap, name));
    }

    // Only Ampere consumer parts (SM86 / RTX 30 series) are supported; anything else that can be
    // identified is reported as unsupported rather than silently routed.
    public static SmTarget Classify(string? computeCapability, string? name)
    {
        if (!string.IsNullOrWhiteSpace(computeCapability) && computeCapability.Trim() == "8.6")
            return SmTarget.Sm86;
        if (!string.IsNullOrWhiteSpace(name) && NameRegex().IsMatch(name))
            return SmTarget.Sm86;
        return string.IsNullOrWhiteSpace(computeCapability) && string.IsNullOrWhiteSpace(name)
            ? SmTarget.Unknown
            : SmTarget.Unsupported;
    }

    private static IEnumerable<string> NvidiaSmiCandidates()
    {
        string system32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe");
        yield return system32;

        string? pathVar = Environment.GetEnvironmentVariable("PATH");
        if (pathVar != null)
            foreach (string dir in pathVar.Split(';', StringSplitOptions.RemoveEmptyEntries))
                yield return Path.Combine(dir.Trim(), "nvidia-smi.exe");

        foreach (string programFiles in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 })
        {
            yield return Path.Combine(programFiles, "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
        }
    }

    private static string Run(string executable)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "--query-gpu=name,compute_cap,driver_version --format=csv,noheader",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            },
        };
        process.Start();
        if (!process.WaitForExit(10_000))
        {
            process.Kill();
            throw new TimeoutException("nvidia-smi did not respond");
        }
        return process.StandardOutput.ReadToEnd();
    }

    private static string[] SplitCsv(string line)
    {
        // Trivial CSV: split on commas outside quotes (names rarely contain commas).
        var parts = new List<string>();
        bool inQuotes = false;
        int start = 0;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') inQuotes = !inQuotes;
            if (line[i] == ',' && !inQuotes)
            {
                parts.Add(line[start..i]);
                start = i + 1;
            }
        }
        parts.Add(line[start..]);
        return parts.ToArray();
    }

    private static string? Unquote(string? field) =>
        string.IsNullOrEmpty(field) ? null : field.Trim(' ', '"');

    [GeneratedRegex(@"RTX\s+30", RegexOptions.IgnoreCase)]
    private static partial Regex NameRegex();
}