using DlssgSwapper.Core.Hardware;

namespace DlssgSwapper.Core.Tests;

public class GpuDetectorTests
{
    [Fact]
    public void ParseOutput_AmpereCsv_YieldsSm86()
    {
        var info = GpuDetector.ParseOutput("NVIDIA GeForce RTX 3080 Ti, 8.6, 591.86");
        Assert.Equal("NVIDIA GeForce RTX 3080 Ti", info.Name);
        Assert.Equal("8.6", info.ComputeCapability);
        Assert.Equal("591.86", info.DriverVersion);
        Assert.Equal(SmTarget.Sm86, info.Sm);
        Assert.Equal("SM86", info.SuggestedRouter);
    }

    [Fact]
    public void ParseOutput_TuringCsv_YieldsSm75()
    {
        var info = GpuDetector.ParseOutput("\"NVIDIA GeForce RTX 2060 Super\", 7.5, 591.86");
        Assert.Equal("NVIDIA GeForce RTX 2060 Super", info.Name);
        Assert.Equal(SmTarget.Sm75, info.Sm);
        Assert.Equal("SM75", info.SuggestedRouter);
    }

    [Fact]
    public void Classify_FallsBackToNamePattern()
    {
        Assert.Equal(SmTarget.Sm86, GpuDetector.Classify(null, "NVIDIA GeForce RTX 3060"));
        Assert.Equal(SmTarget.Sm75, GpuDetector.Classify(null, "NVIDIA GeForce RTX 2070"));
        Assert.Equal(SmTarget.Unknown, GpuDetector.Classify(null, "AMD Radeon RX 6800 XT"));
    }

    [Fact]
    public void ParseOutput_EmptyOutput_YieldsUnknown()
    {
        var info = GpuDetector.ParseOutput(" \n\n");
        Assert.Equal(SmTarget.Unknown, info.Sm);
        Assert.Null(info.Name);
    }
}