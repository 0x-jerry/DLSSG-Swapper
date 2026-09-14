using DlssgSwapper.Core.Configuration;
using DlssgSwapper.Core.Games;
using DlssgSwapper.Core.Payloads;

namespace DlssgSwapper.Core.Tests;

public class InstallationServiceTests : IDisposable
{
    private readonly string _gameDir;
    private readonly string _backupRoot;
    private readonly PayloadCatalog _catalog;
    private readonly InstallationService _service;
    private readonly GameProfile _profile;

    public InstallationServiceTests()
    {
        _gameDir = Path.Combine(Path.GetTempPath(), $"game-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_gameDir);
        _backupRoot = Path.Combine(Path.GetTempPath(), $"backup-host-{Guid.NewGuid():N}");
        _catalog = PayloadCatalog.Load(Path.Combine(AppContext.BaseDirectory, "payloads"));
        _service = new InstallationService(_catalog, _ => new BackupStore(_backupRoot));
        string exe = Path.Combine(_gameDir, "FakeGame-Win64-Shipping.exe");
        File.WriteAllBytes(exe, new byte[] { 0x4D, 0x5A });
        _profile = GameProfile.Create("Fake Game", exe, GameSource.Manual);
    }

    public void Dispose()
    {
        if (Directory.Exists(_gameDir)) Directory.Delete(_gameDir, recursive: true);
        if (Directory.Exists(_backupRoot)) Directory.Delete(_backupRoot, recursive: true);
    }

    private PayloadVersion Current => _catalog.GetVersion("0.3.0")!;
    private PayloadVersion Legacy => _catalog.GetVersion("0.3.0-310.1")!;
    private string InstalledIni => Path.Combine(_gameDir, InstallationService.IniFileName);

    private static FrameGenSettings Defaults => FrameGenSettings.Defaults();

    [Fact]
    public void Install_WritesMatchingDllAndIni()
    {
        var entry = Current.FindEntryPoint("version.dll")!;
        _service.Install(_profile, Current, entry, Defaults);

        string dll = Path.Combine(_gameDir, "version.dll");
        Assert.Equal(entry.Sha256, Hashing.Sha256File(dll), ignoreCase: true);
        var ini = IniFile.Load(InstalledIni);
        Assert.Equal("1", ini.Get("General", "Enabled"));
        Assert.Equal("1", ini.Get("FrameGeneration", "Optimized"));
        Assert.Equal("5", ini.Get("FrameGeneration", "MaxGeneratedFrames"));
        Assert.Equal("Auto", ini.Get("Compatibility", "Router"));
        Assert.Equal("Auto", ini.Get("Compatibility", "KernelImage"));
        Assert.Equal("Auto", ini.Get("Compatibility", "Preset"));
        Assert.Equal("1", ini.Get("Logging", "Level"));

        var inspect = _service.Inspect(_profile);
        Assert.Equal(InstallKind.Installed, inspect.Kind);
        Assert.Equal("version.dll", inspect.InstalledEntryPoint!.FileName);
        Assert.Same(Current, inspect.InstalledVersion);
    }

    [Fact]
    public void Install_ClampsFramesToTheRuntimeCeiling()
    {
        var entry = Legacy.FindEntryPoint("version.dll")!;
        _service.Install(_profile, Legacy, entry, Defaults with { MaxGeneratedFrames = 5 });

        Assert.Equal("3", IniFile.Load(InstalledIni).Get("FrameGeneration", "MaxGeneratedFrames"));
    }

    [Fact]
    public void SwapEntryPoint_KeepsOnlyNewProxy()
    {
        var version = Current.FindEntryPoint("version.dll")!;
        var winmm = Current.FindEntryPoint("winmm.dll")!;
        _service.Install(_profile, Current, version, Defaults);
        _service.Install(_profile, Current, winmm, Defaults);

        Assert.False(File.Exists(Path.Combine(_gameDir, "version.dll")));
        Assert.Equal(winmm.Sha256, Hashing.Sha256File(Path.Combine(_gameDir, "winmm.dll")), ignoreCase: true);

        _service.Uninstall(_profile);
        Assert.False(File.Exists(Path.Combine(_gameDir, "winmm.dll")));
        Assert.False(File.Exists(InstalledIni));
        Assert.Equal(InstallKind.NotInstalled, _service.Inspect(_profile).Kind);
    }

    [Fact]
    public void Uninstall_RestoresOriginalFilesByteIdentically()
    {
        byte[] originalDll = { 0x10, 0x20, 0x30 };
        byte[] originalIni = { 0xAA, 0xBB };
        File.WriteAllBytes(Path.Combine(_gameDir, "version.dll"), originalDll);
        File.WriteAllBytes(InstalledIni, originalIni);

        _service.Install(_profile, Current, Current.FindEntryPoint("version.dll")!, Defaults, overwriteForeignFile: true);
        _service.Uninstall(_profile);

        Assert.Equal(originalDll, File.ReadAllBytes(Path.Combine(_gameDir, "version.dll")));
        Assert.Equal(originalIni, File.ReadAllBytes(InstalledIni));
    }

    [Fact]
    public void Uninstall_WithNoOriginals_ReturnsToPreInstallListing()
    {
        string[] before = Directory.GetFileSystemEntries(_gameDir);
        _service.Install(_profile, Current, Current.FindEntryPoint("winmm.dll")!, Defaults);
        _service.Uninstall(_profile);

        Assert.Equal(before.OrderBy(x => x), Directory.GetFileSystemEntries(_gameDir).OrderBy(x => x));
    }

    [Fact]
    public void ForeignFile_RefusedThenBackedUpAndRestoredAfterConfirmation()
    {
        byte[] foreign = { 0xDE, 0xAD, 0xBE, 0xEF };
        File.WriteAllBytes(Path.Combine(_gameDir, "dxgi.dll"), foreign);

        Assert.Throws<ForeignFileException>(() =>
            _service.Install(_profile, Current, Current.FindEntryPoint("dxgi.dll")!, Defaults));

        _service.Install(_profile, Current, Current.FindEntryPoint("dxgi.dll")!, Defaults, overwriteForeignFile: true);
        string ours = Hashing.Sha256File(Path.Combine(_gameDir, "dxgi.dll"));
        Assert.NotEqual(Convert.ToHexString(foreign).ToLowerInvariant(), ours);

        _service.Uninstall(_profile);
        Assert.Equal(foreign, File.ReadAllBytes(Path.Combine(_gameDir, "dxgi.dll")));
    }

    [Fact]
    public void LockedTargetFile_BlocksInstall()
    {
        string target = Path.Combine(_gameDir, "version.dll");
        File.Copy(_catalog.ResolveBinaryPath(Current, Current.FindEntryPoint("version.dll")!), target);
        using var handle = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.Throws<GameRunningException>(() =>
            _service.Install(_profile, Current, Current.FindEntryPoint("version.dll")!, Defaults));
    }

    [Fact]
    public void Inspect_DetectsPartialInstall()
    {
        var inspection = _service.Inspect(_profile);
        Assert.Equal(InstallKind.NotInstalled, inspection.Kind);
        Assert.Equal(FileOrigin.Absent, inspection.ProxyOrigin);

        string dll = Path.Combine(_gameDir, "version.dll");
        File.Copy(_catalog.ResolveBinaryPath(Current, Current.FindEntryPoint("version.dll")!), dll);

        inspection = _service.Inspect(_profile);
        Assert.Equal(InstallKind.Partial, inspection.Kind);
        Assert.Equal(FileOrigin.Ours, inspection.ProxyOrigin);

        File.WriteAllBytes(InstalledIni, new byte[] { 1 });
        Assert.Equal(InstallKind.Installed, _service.Inspect(_profile).Kind);
    }

    [Fact]
    public void ModifiedIni_IsLeftAloneOnUninstall()
    {
        _service.Install(_profile, Current, Current.FindEntryPoint("version.dll")!, Defaults);
        File.WriteAllText(InstalledIni, "[Compatibility]\nRouter=SM75\n");

        var warnings = _service.Uninstall(_profile);
        Assert.Contains(warnings, w => w.Contains("modified"));
        Assert.True(File.Exists(InstalledIni));
        Assert.False(File.Exists(Path.Combine(_gameDir, "version.dll")));
    }

    [Fact]
    public void SwapRestoresOriginalOfFirstEntryPoint()
    {
        byte[] originalDll = { 0x01, 0x02 };
        File.WriteAllBytes(Path.Combine(_gameDir, "version.dll"), originalDll);

        var version = Current.FindEntryPoint("version.dll")!;
        var winmm = Current.FindEntryPoint("winmm.dll")!;
        _service.Install(_profile, Current, version, Defaults, overwriteForeignFile: true);
        _service.Install(_profile, Current, winmm, Defaults);
        _service.Uninstall(_profile);

        Assert.Equal(originalDll, File.ReadAllBytes(Path.Combine(_gameDir, "version.dll")));
    }
}
