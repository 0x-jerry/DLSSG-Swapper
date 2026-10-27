namespace DlssgSwapper.Core.Configuration;

// Frame-generation engine. Common keys live in [FrameGeneration], engine-specific keys in [DLSSG]
// (DLSS) or [XeSSFG] (XeSS); the other engine's section is not read by the mod.
public enum FrameGenEngine
{
    Dlss,
    XeSS,
}

// [DLSSG] Preset. 0.4.1 removed "Auto" (it runs as A and logs one configuration_warning), so the
// tool only ever writes A, B or C. C is the "FG Lite" preset: much cheaper per generated frame,
// slightly lower image quality.
public enum FrameGenPreset
{
    A,
    B,
    C,
}

// A profile saved by an older build may hold "Auto", which is no longer a member: map it to A (what
// the mod itself does with that value) instead of failing to load the whole profile.
public sealed class FrameGenPresetConverter : System.Text.Json.Serialization.JsonConverter<FrameGenPreset>
{
    public override FrameGenPreset Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert,
        System.Text.Json.JsonSerializerOptions options)
    {
        string? value = reader.GetString();
        if (Enum.TryParse(value, ignoreCase: true, out FrameGenPreset preset)) return preset;
        return FrameGenPreset.A;
    }

    public override void Write(System.Text.Json.Utf8JsonWriter writer, FrameGenPreset value,
        System.Text.Json.JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

public sealed record FrameGenSettings
{
    public int? Enabled { get; init; }
    public int? Optimized { get; init; }
    public FrameGenPreset? Preset { get; init; }
    public FrameGenEngine? Engine { get; init; }
    public int? MaxGeneratedFrames { get; init; }
    public int? LoggingLevel { get; init; }

    public static FrameGenSettings Defaults() => new()
    {
        Enabled = 1,
        Optimized = 1,
        Preset = FrameGenPreset.A,
        Engine = FrameGenEngine.Dlss,
        MaxGeneratedFrames = 3,
        LoggingLevel = 1,
    };

    // The exact spelling the mod expects for [FrameGeneration] Engine: DLSS / XeSS.
    public static string EngineValue(FrameGenEngine engine) =>
        engine == FrameGenEngine.XeSS ? "XeSS" : "DLSS";
}

public static class IniApplier
{
    public static void Apply(IniFile ini, FrameGenSettings settings)
    {
        Set(ini, "General", "Enabled", settings.Enabled, ZeroOne, 1);
        ini.Set("FrameGeneration", "Engine",
            FrameGenSettings.EngineValue(settings.Engine ?? FrameGenEngine.Dlss));
        Set(ini, "FrameGeneration", "MaxGeneratedFrames", settings.MaxGeneratedFrames, MaxFrames, 3);
        Set(ini, "DLSSG", "Optimized", settings.Optimized, Tier, 1);
        string preset = (settings.Preset ?? FrameGenPreset.A).ToString();
        if (!PresetValue(preset))
            throw new ArgumentException($"Invalid value \"{preset}\" for DLSSG.Preset");
        ini.Set("DLSSG", "Preset", preset);
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
    private static bool PresetValue(string v) => Enum.TryParse<FrameGenPreset>(v, ignoreCase: true, out _);
}
