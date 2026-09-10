namespace DlssgSwapper.Core.Configuration;

// Superset of every key this tool edits. Properties are null when the schema
// does not use them (e.g. no Router on the legacy 0.1.0 schema).
public sealed record FrameGenSettings
{
    public string? Router { get; init; }
    public string? KernelImage { get; init; }
    public int? HardwareBilinear { get; init; }
    public int? MaxGeneratedFrames { get; init; }
    public int? LoggingLevel { get; init; }
    public int? Enabled { get; init; }

    public static FrameGenSettings DefaultsFor(IniSchema schema) => schema switch
    {
        IniSchema.Native => new FrameGenSettings
        {
            Router = "SM86",
            KernelImage = "PTX",
            HardwareBilinear = 0,
            MaxGeneratedFrames = 3,
            LoggingLevel = 1,
        },
        _ => new FrameGenSettings
        {
            Enabled = 1,
            KernelImage = "Auto",
            MaxGeneratedFrames = 3,
            LoggingLevel = 2,
        },
    };
}

public static class IniApplier
{
    public static void Apply(IniFile ini, IniSchema schema, FrameGenSettings settings)
    {
        switch (schema)
        {
            case IniSchema.Native:
                Set(ini, "Compatibility", "Router", settings.Router, RouterValues, "SM86");
                Set(ini, "Compatibility", "KernelImage", settings.KernelImage, KernelImageValues, "PTX");
                Set(ini, "Compatibility", "HardwareBilinear", settings.HardwareBilinear, ZeroOne, 0);
                Set(ini, "FrameGeneration", "MaxGeneratedFrames", settings.MaxGeneratedFrames, MaxFrames, 3);
                Set(ini, "Logging", "Level", settings.LoggingLevel, LogLevel, 1);
                break;
            default:
                Set(ini, "General", "Enabled", settings.Enabled, ZeroOne, 1);
                Set(ini, "Compatibility", "KernelImage", settings.KernelImage, KernelImageValues, "Auto");
                Set(ini, "FrameGeneration", "MaxGeneratedFrames", settings.MaxGeneratedFrames, MaxFrames, 3);
                Set(ini, "Logging", "Level", settings.LoggingLevel, LogLevel, 2);
                break;
        }
    }

    private static readonly string[] RouterValues = { "SM86", "SM75" };
    private static readonly string[] KernelImageValues = { "PTX", "Auto", "Cubin" };

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