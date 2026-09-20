using DlssgSwapper.Core.Configuration;

namespace DlssgSwapper.Core.Tests;

public class IniFileTests
{
    private const string Sample = """
        ; Header comment
        [General]
        ; frame generation switch
        Enabled=1
        Capture=0

        [FrameGeneration]
        Optimized=1
        MaxGeneratedFrames=5

        [Logging]
        Level=1
        """;

    [Fact]
    public void Set_ChangesOnlyTargetKeyAndPreservesCommentsAndOrder()
    {
        var ini = IniFile.Parse(Sample);
        ini.Set("General", "Enabled", "0");
        ini.Set("Logging", "Level", "2");

        string rendered = ini.Render();
        Assert.Contains("; frame generation switch", rendered);
        Assert.Contains("Enabled=0", rendered);
        Assert.Contains("Level=2", rendered);
        Assert.Contains("Capture=0", rendered); // untouched
        Assert.True(rendered.IndexOf("Enabled=0") < rendered.IndexOf("[FrameGeneration]"));
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
        ini.Set("Logging", "Directory", "custom");
        string once = ini.Render();
        var again = IniFile.Parse(once);
        Assert.Equal(once, again.Render());
    }

    [Fact]
    public void Get_ReturnsValue_IgnoringCase()
    {
        var ini = IniFile.Parse(Sample);
        Assert.Equal("1", ini.Get("general", "enabled"));
        Assert.Null(ini.Get("Logging", "Missing"));
    }
}

public class IniApplierTests
{
    private static string Template() =>
        Path.Combine(AppContext.BaseDirectory, "payloads", "templates", "sm86-default.ini");

    [Fact]
    public void Apply_WritesEveryManagedKey()
    {
        var ini = IniFile.Load(Template());
        IniApplier.Apply(ini, new FrameGenSettings
        {
            Enabled = 0,
            Optimized = 0,
            MaxGeneratedFrames = 2,
            LoggingLevel = 0,
        });

        string rendered = ini.Render();
        Assert.Contains("Enabled=0", rendered);
        Assert.Contains("Optimized=0", rendered);
        Assert.Contains("MaxGeneratedFrames=2", rendered);
        Assert.Contains("Level=0", rendered);
    }

    [Fact]
    public void Apply_LeavesUnmanagedKeysAtTemplateDefaults()
    {
        var ini = IniFile.Load(Template());
        IniApplier.Apply(ini, FrameGenSettings.Defaults());

        string rendered = ini.Render();
        Assert.Contains("Mode=Bundled", rendered);
        Assert.Contains("CacheDirectory=", rendered);
        Assert.Contains("Directory=dlssg_sm86\\logs", rendered);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(16)]
    public void OutOfRange_MaxGeneratedFrames_Throws(int value)
    {
        var ini = IniFile.Load(Template());
        var settings = FrameGenSettings.Defaults() with { MaxGeneratedFrames = value };

        var ex = Assert.Throws<ArgumentException>(() => IniApplier.Apply(ini, settings));
        Assert.Contains("MaxGeneratedFrames", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void OptimizedTier_AcceptsZeroToThree(int tier)
    {
        var ini = IniFile.Load(Template());
        IniApplier.Apply(ini, FrameGenSettings.Defaults() with { Optimized = tier });

        Assert.Contains($"Optimized={tier}", ini.Render());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void OptimizedTier_OutOfRange_Throws(int tier)
    {
        var ini = IniFile.Load(Template());
        var settings = FrameGenSettings.Defaults() with { Optimized = tier };

        var ex = Assert.Throws<ArgumentException>(() => IniApplier.Apply(ini, settings));
        Assert.Contains("Optimized", ex.Message);
    }

    [Fact]
    public void Defaults_MatchTheFactoryIni()
    {
        var settings = FrameGenSettings.Defaults();
        Assert.Equal(1, settings.Enabled);
        Assert.Equal(1, settings.Optimized);
        Assert.Equal(3, settings.MaxGeneratedFrames);
        Assert.Equal(1, settings.LoggingLevel);
    }
}