using DlssgSwapper.Core.Games;

namespace DlssgSwapper.Core.Tests;

public class EntryPointDetectorTests
{
    private static readonly string[] ProxyNames =
        { "version.dll", "winmm.dll", "dbghelp.dll", "dinput8.dll", "dxgi.dll", "d3d12.dll" };

    [Fact]
    public void FindPresent_ReturnsNamesThatExist()
    {
        WithTempDir(temp =>
        {
            File.WriteAllBytes(Path.Combine(temp, "dxgi.dll"), new byte[1]);
            var present = EntryPointDetector.FindPresent(temp, ProxyNames);
            Assert.Single(present);
            Assert.Contains("dxgi.dll", present);
        });
    }

    [Fact]
    public void FindPresent_ReturnsEmptyWhenDirectoryMissing()
    {
        Assert.Empty(EntryPointDetector.FindPresent(null, ProxyNames));
        Assert.Empty(EntryPointDetector.FindPresent(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"), ProxyNames));
    }

    [Fact]
    public void FindPresent_IgnoresUnrelatedFiles()
    {
        WithTempDir(temp =>
        {
            File.WriteAllBytes(Path.Combine(temp, "game.exe"), new byte[1]);
            File.WriteAllBytes(Path.Combine(temp, "nvngx_dlssg.dll"), new byte[1]);
            Assert.Empty(EntryPointDetector.FindPresent(temp, ProxyNames));
        });
    }

    [Fact]
    public void FindPresent_ReturnsEveryPresentName()
    {
        WithTempDir(temp =>
        {
            File.WriteAllBytes(Path.Combine(temp, "version.dll"), new byte[1]);
            File.WriteAllBytes(Path.Combine(temp, "winmm.dll"), new byte[1]);
            var present = EntryPointDetector.FindPresent(temp, ProxyNames);
            Assert.Equal(2, present.Count);
            Assert.Contains("version.dll", present);
            Assert.Contains("winmm.dll", present);
        });
    }

    [Fact]
    public void FindPresent_OnlyChecksProvidedNames()
    {
        WithTempDir(temp =>
        {
            File.WriteAllBytes(Path.Combine(temp, "dxgi.dll"), new byte[1]);
            Assert.Empty(EntryPointDetector.FindPresent(temp, new[] { "version.dll" }));
        });
    }

    private static void WithTempDir(Action<string> action)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"entrypoint-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            action(temp);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}
