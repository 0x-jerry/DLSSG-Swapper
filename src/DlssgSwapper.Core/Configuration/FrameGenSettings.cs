namespace DlssgSwapper.Core.Configuration;

public sealed record FrameGenSettings
{
    public string? Router { get; init; }
    public string? KernelImage { get; init; }
    public int? HardwareBilinear { get; init; }
    public int? MaxGeneratedFrames { get; init; }
    public int? LoggingLevel { get; init; }

    public static FrameGenSettings Defaults() => new()
    {
        Router = "SM86",
        KernelImage = "PTX",
        HardwareBilinear = 0,
        MaxGeneratedFrames = 3,
        LoggingLevel = 1,
    };
}

public static class IniApplier
{
    private static readonly string[] RouterValues = { "SM86", "SM75" };
    private static readonly string[] KernelImageValues = { "PTX", "Auto", "Cubin" };

    public static void Apply(IniFile ini, FrameGenSettings settings)
    {
        Set(ini, "Compatibility", "Router", settings.Router, RouterValues, "SM86");
        Set(ini, "Compatibility", "KernelImage", settings.KernelImage, KernelImageValues, "PTX");
        Set(ini, "Compatibility", "HardwareBilinear", settings.HardwareBilinear, ZeroOne, 0);
        Set(ini, "FrameGeneration", "MaxGeneratedFrames", settings.MaxGeneratedFrames, MaxFrames, 3);
        Set(ini, "Logging", "Level", settings.LoggingLevel, LogLevel, 1);
    }

    private static void Set(IniFile ini, string section, string key, string? value,
        string[]? allowed = null, string fallback = "")
    {
        string text = value ?? fallback;
        if (allowed != null && !allowed.Contains(text, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Invalid value \"{text}\" for {section}.{key}; allowed: {string.Join(", ", allowed)}");
        ini.Set(section, key, text);
    }

    private static void Set(IniFile ini, string section, string key, int? value,
        Func<int, bool> validate, int fallback)
    {
        int v = value ?? fallback;
        if (!validate(v))
            throw new ArgumentException($"Invalid value \"{v}\" for {section}.{key}");
        ini.Set(section, key, v.ToString());
    }

    private static bool ZeroOne(int v) => v is 0 or 1;
    private static bool MaxFrames(int v) => v is >= 0 and <= 3;
    private static bool LogLevel(int v) => v is >= 0 and <= 3;
}