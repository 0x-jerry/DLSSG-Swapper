using DlssgSwapper.Core.Configuration;

namespace DlssgSwapper.Core.Tests;

public class IniFileTests
{
    private const string Sample = """
        ; Header comment
        [Compatibility]
        ; SM86 for Ampere
        Router=SM86
        KernelImage=PTX
        HardwareBilinear=0

        [FrameGeneration]
        MaxGeneratedFrames=3

        [Logging]
        Level=1
        """;

    [Fact]
    public void Set_ChangesOnlyTargetKeyAndPreservesCommentsAndOrder()
    {
        var ini = IniFile.Parse(Sample);
        ini.Set("Compatibility", "Router", "SM75");
        ini.Set("Logging", "Level", "2");

        string rendered = ini.Render();
        Assert.Contains("; SM86 for Ampere", rendered);
        Assert.Contains("Router=SM75", rendered);
        Assert.Contains("Level=2", rendered);
        Assert.Contains("KernelImage=PTX", rendered); // untouched
        Assert.DoesNotContain("HardwareBilinear=1", rendered);
        Assert.True(rendered.IndexOf("Router=SM75") < rendered.IndexOf("[FrameGeneration]"));
    }

    [Fact]
    public void Set_CreatesKeyInsideExistingSection_NotDuplicateSection()
    {
        var ini = IniFile.Parse("[A]\nx=1\n[B]\ny=2\n");
        ini.Set("A", "z", "3");

        string rendered = ini.Render();
        int a = rendered.IndexOf("[A]");
        int b = rendered.IndexOf("[B]");
        Assert.InRange(rendered.IndexOf("z=3"), a, b);
        Assert.Equal(rendered.IndexOf("[A]"), rendered.LastIndexOf("[A]"));
    }

    [Fact]
    public void Set_CreatesMissingSectionAtEnd()
    {
        var ini = IniFile.Parse("[A]\nx=1\n");
        ini.Set("Logging", "Level", "1");

        string rendered = ini.Render();
        Assert.Contains("[Logging]", rendered);
        Assert.True(rendered.IndexOf("[Logging]") > rendered.IndexOf("[A]"));
    }

    [Fact]
    public void Render_IsIdempotent()
    {
        var ini = IniFile.Parse(Sample);
        ini.Set("Compatibility", "KernelImage", "Auto");
        string once = ini.Render();
        var again = IniFile.Parse(once);
        Assert.Equal(once, again.Render());
    }

    [Fact]
    public void Get_ReturnsValue_IgnoringCase()
    {
        var ini = IniFile.Parse(Sample);
        Assert.Equal("SM86", ini.Get("compatibility", "router"));
        Assert.Null(ini.Get("Logging", "Missing"));
    }
}

public class IniApplierTests
{
    private static string Template(string name) =>
        Path.Combine(AppContext.BaseDirectory, "payloads", "templates", name);

    [Fact]
    public void Native_AppliesFiveKeysOnly()
    {
        var ini = IniFile.Load(Template("native-default.ini"));
        IniApplier.Apply(ini, IniSchema.Native, new FrameGenSettings
        {
            Router = "SM86",
            KernelImage = "Cubin",
            HardwareBilinear = 1,
            MaxGeneratedFrames = 2,
            LoggingLevel = 0,
        });

        string rendered = ini.Render();
        Assert.Contains("Router=SM86", rendered);
        Assert.Contains("KernelImage=Cubin", rendered);
        Assert.Contains("HardwareBilinear=1", rendered);
        Assert.Contains("MaxGeneratedFrames=2", rendered);
        Assert.Contains("Level=0", rendered);
    }

    [Fact]
    public void Legacy_DoesNotEmitNativeOnlyKeys()
    {
        var ini = IniFile.Load(Template("legacy-default.ini"));
        IniApplier.Apply(ini, IniSchema.Legacy, new FrameGenSettings
        {
            Enabled = 1,
            KernelImage = "Auto",
            MaxGeneratedFrames = 2,
            LoggingLevel = 0,
        });

        string rendered = ini.Render();
        Assert.DoesNotContain("Router=", rendered);
        Assert.DoesNotContain("HardwareBilinear=", rendered);
        Assert.Contains("Enabled=1", rendered);
        Assert.Contains("MaxGeneratedFrames=2", rendered);
        Assert.Contains("Level=0", rendered);
    }

    [Theory]
    [InlineData(IniSchema.Native, 5)]
    [InlineData(IniSchema.Legacy, 16)]
    public void OutOfRange_MaxGeneratedFrames_Throws(IniSchema schema, int value)
    {
        var ini = IniFile.Load(Template(schema == IniSchema.Native ? "native-default.ini" : "legacy-default.ini"));
        var settings = FrameGenSettings.DefaultsFor(schema) with { MaxGeneratedFrames = value };

        var ex = Assert.Throws<ArgumentException>(() => IniApplier.Apply(ini, schema, settings));
        Assert.Contains("MaxGeneratedFrames", ex.Message);
    }

    [Fact]
    public void DefaultsFor_MatchesTemplateDefaults()
    {
        var settings = FrameGenSettings.DefaultsFor(IniSchema.Native);
        Assert.Equal("SM86", settings.Router);
        Assert.Equal("PTX", settings.KernelImage);
        Assert.Equal(0, settings.HardwareBilinear);
        Assert.Equal(3, settings.MaxGeneratedFrames);
        Assert.Equal(1, settings.LoggingLevel);
    }
}