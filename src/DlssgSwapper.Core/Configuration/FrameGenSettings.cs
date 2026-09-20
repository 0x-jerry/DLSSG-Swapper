namespace DlssgSwapper.Core.Configuration;

public sealed record FrameGenSettings
{
    public int? Enabled { get; init; }
    public int? Optimized { get; init; }
    public int? MaxGeneratedFrames { get; init; }
    public int? LoggingLevel { get; init; }

    public static FrameGenSettings Defaults() => new()
    {
        Enabled = 1,
        Optimized = 1,
        MaxGeneratedFrames = 3,
        LoggingLevel = 1,
    };
}

public static class IniApplier
{
    public static void Apply(IniFile ini, FrameGenSettings settings)
    {
        Set(ini, "General", "Enabled", settings.Enabled, ZeroOne, 1);
        Set(ini, "FrameGeneration", "Optimized", settings.Optimized, Tier, 1);
        Set(ini, "FrameGeneration", "MaxGeneratedFrames", settings.MaxGeneratedFrames, MaxFrames, 3);
        Set(ini, "Logging", "Level", settings.LoggingLevel, LogLevel, 1);
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
    private static bool Tier(int v) => v is >= 0 and <= 3;
    private static bool MaxFrames(int v) => v is >= 0 and <= 5;
    private static bool LogLevel(int v) => v is >= 0 and <= 3;
}
