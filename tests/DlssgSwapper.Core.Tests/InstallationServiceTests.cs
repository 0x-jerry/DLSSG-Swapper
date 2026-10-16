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

    private PayloadVersion Current => _catalog.GetVersion("0.4.0")!;
    private string InstalledIni => Path.Combine(_gameDir, InstallationService.IniFileName);
    private string AssetFile(string relative) =>
        Path.Combine(_gameDir, relative.Replace('/', Path.DirectorySeparatorChar));

    private static FrameGenSettings Defaults => FrameGenSettings.Defaults();

    [Fact]
    public void Install_WritesMatchingDllIniAndTree()
    {
        var entry = Current.FindEntryPoint("version.dll")!;
        _service.Install(_profile, Current, entry, Defaults);

        string dll = Path.Combine(_gameDir, "version.dll");
        Assert.Equal(entry.Sha256, Hashing.Sha256File(dll), ignoreCase: true);

        var ini = IniFile.Load(InstalledIni);
        Assert.Equal("1", ini.Get("General", "Enabled"));
        Assert.Equal("2", ini.Get("DLSSG", "Optimized"));
        Assert.Equal("Auto", ini.Get("DLSSG", "Preset"));
        Assert.Equal("DLSS", ini.Get("FrameGeneration", "Engine"));
        Assert.Equal("3", ini.Get("FrameGeneration", "MaxGeneratedFrames"));
        Assert.Equal("1", ini.Get("Logging", "Level"));

        foreach (var asset in Current.DirectoryAssets)
            Assert.Equal(asset.Sha256, Hashing.Sha256File(AssetFile(asset.Path)), ignoreCase: true);

        var inspect = _service.Inspect(_profile);
        Assert.Equal(InstallKind.Installed, inspect.Kind);
        Assert.Equal("version.dll", inspect.InstalledEntryPoint!.FileName);
        Assert.Same(Current, inspect.InstalledVersion);
        Assert.Equal(4, inspect.AssetsPresent);
        Assert.Equal(4, inspect.AssetsExpected);
        Assert.False(inspect.OutdatedLayout);
        Assert.Empty(inspect.Warnings);
    }

    [Fact]
    public void Install_KeepsSixXOnlyWhenAsked()
    {
        var entry = Current.FindEntryPoint("version.dll")!;
        _service.Install(_profile, Current, entry, Defaults with { MaxGeneratedFrames = 5 });

        Assert.Equal("5", IniFile.Load(InstalledIni).Get("FrameGeneration", "MaxGeneratedFrames"));
    }

    [Fact]
    public void Install_ClampsFramesToTheRuntimeCeiling()
    {
        // A line whose runtime only reaches 4X (the 0.3.x 310.1 ceiling) clamps a 6X request.
        var limited = Current with { MaxGeneratedFrames = 3 };

        Assert.Equal(3, InstallationService.ClampToRuntime(Defaults with { MaxGeneratedFrames = 5 }, limited)
            .MaxGeneratedFrames);
        // The bundled 0.4.0 runtime keeps a 6X request.
        Assert.Equal(5, InstallationService.ClampToRuntime(Defaults with { MaxGeneratedFrames = 5 }, Current)
            .MaxGeneratedFrames);
        // XeSS-FG is 2X only, whatever the runtime allows.
        Assert.Equal(1, InstallationService.ClampToRuntime(
            Defaults with { MaxGeneratedFrames = 5, Engine = FrameGenEngine.XeSS }, Current).MaxGeneratedFrames);
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
        Assert.False(Directory.Exists(Path.Combine(_gameDir, "dlssg_sm86")));
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
        Assert.False(Directory.Exists(Path.Combine(_gameDir, "dlssg_sm86")));
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
    public void Uninstall_LeavesModifiedTreeFilesAlone()
    {
        _service.Install(_profile, Current, Current.FindEntryPoint("version.dll")!, Defaults);
        var asset = Current.DirectoryAssets.First(a => a.Path.EndsWith("nvngx_dlssg.dll"));
        string patched = AssetFile(asset.Path);
        File.WriteAllText(patched, "patched by hand");

        var warnings = _service.Uninstall(_profile);

        Assert.Contains(warnings, w =>
            w.Contains("nvngx_dlssg.dll") && w.Contains("no longer matches"));
        Assert.True(File.Exists(patched));
        Assert.True(Directory.Exists(Path.Combine(_gameDir, "dlssg_sm86")));
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
        Assert.Equal(0, inspection.AssetsPresent);

        File.WriteAllBytes(InstalledIni, new byte[] { 1 });
        inspection = _service.Inspect(_profile);
        Assert.Equal(InstallKind.Partial, inspection.Kind);
        Assert.True(inspection.OutdatedLayout);
        Assert.Contains(inspection.Warnings, w => w.Contains("0.3.x"));
    }

    [Fact]
    public void Inspect_FlagsAnIncompleteTree()
    {
        _service.Install(_profile, Current, Current.FindEntryPoint("version.dll")!, Defaults);
        var asset = Current.DirectoryAssets[0];
        File.Delete(AssetFile(asset.Path));

        var inspection = _service.Inspect(_profile);

        Assert.Equal(InstallKind.Partial, inspection.Kind);
        Assert.Equal(3, inspection.AssetsPresent);
        Assert.Equal(4, inspection.AssetsExpected);
        Assert.Contains(inspection.Warnings, w => w.Contains(asset.Path));
    }

    [Fact]
    public void Inspect_DoesNotClaimAForeignFileInTheTree()
    {
        _service.Install(_profile, Current, Current.FindEntryPoint("version.dll")!, Defaults);
        var asset = Current.DirectoryAssets[0];
        File.WriteAllText(AssetFile(asset.Path), "another mod's file");

        var inspection = _service.Inspect(_profile);

        Assert.Contains(inspection.Warnings, w => w.Contains("does not match this payload"));
        Assert.Equal(3, inspection.AssetsPresent);
    }

    [Fact]
    public void ModifiedIni_IsLeftAloneOnUninstall()
    {
        _service.Install(_profile, Current, Current.FindEntryPoint("version.dll")!, Defaults);
        File.WriteAllText(InstalledIni, "[General]\nEnabled=0\n");

        var warnings = _service.Uninstall(_profile);
        Assert.Contains(warnings, w => w.Contains("modified"));
        Assert.True(File.Exists(InstalledIni));
        Assert.False(File.Exists(Path.Combine(_gameDir, "version.dll")));
        // The tree is still ours, so it goes even when the INI was edited.
        Assert.False(Directory.Exists(Path.Combine(_gameDir, "dlssg_sm86")));
    }

    [Fact]
    public void LegacyProxy_IsRecognisedAndUpgradedInPlace()
    {
        string root = Path.Combine(Path.GetTempPath(), $"legacy-catalog-{Guid.NewGuid():N}");
        string gameDir = Path.Combine(Path.GetTempPath(), $"legacy-game-{Guid.NewGuid():N}");
        string backupRoot = Path.Combine(Path.GetTempPath(), $"legacy-backup-{Guid.NewGuid():N}");
        try
        {
            byte[] currentBytes = { 0x4D, 0x5A, 0x03, 0x01 };
            byte[] legacyBytes = { 0x4D, 0x5A, 0x02, 0x00 };
            byte[] assetBytes = { 0x4D, 0x5A, 0x04, 0x00 };
            string binDir = Path.Combine(root, "bin", "0.4.0");
            string templateDir = Path.Combine(root, "templates");
            Directory.CreateDirectory(binDir);
            Directory.CreateDirectory(Path.Combine(binDir, "dlssg_sm86", "runtime"));
            Directory.CreateDirectory(templateDir);
            Directory.CreateDirectory(gameDir);

            string binPath = Path.Combine(binDir, "version.dll");
            File.WriteAllBytes(binPath, currentBytes);
            string currentHash = Hashing.Sha256File(binPath);
            string assetPath = Path.Combine(binDir, "dlssg_sm86", "runtime", "nvngx_dlssg.dll");
            File.WriteAllBytes(assetPath, assetBytes);
            string assetHash = Hashing.Sha256File(assetPath);
            string legacyProbe = Path.Combine(root, "legacy.bin");
            File.WriteAllBytes(legacyProbe, legacyBytes);
            string legacyHash = Hashing.Sha256File(legacyProbe);
            File.Delete(legacyProbe);

            File.WriteAllText(Path.Combine(templateDir, "sm86-default.ini"),
                "[General]\nEnabled=1\n[FrameGeneration]\nEngine=DLSS\nMaxGeneratedFrames=3\n" +
                "[DLSSG]\nOptimized=2\nPreset=Auto\n[Logging]\nLevel=1\n");
            File.WriteAllText(Path.Combine(root, "catalog.json"), $$"""
            {
              "versions": [{
                "version": "0.4.0",
                "displayName": "0.4.0",
                "sourceRoot": "external/dlssg_for_sm86",
                "maxGeneratedFrames": 5,
                "templates": { "default": "templates/sm86-default.ini" },
                "legacySha256": ["{{legacyHash}}"],
                "entryPoints": [
                  { "file": "version.dll", "source": "version.dll", "sha256": "{{currentHash}}", "recommended": true }
                ],
                "directoryAssets": [
                  { "path": "dlssg_sm86/runtime/nvngx_dlssg.dll",
                    "source": "dlssg_sm86/runtime/nvngx_dlssg.dll",
                    "sha256": "{{assetHash}}", "bytes": {{assetBytes.Length}} }
                ]
              }]
            }
            """);

            var catalog = PayloadCatalog.Load(root);
            var service = new InstallationService(catalog, _ => new BackupStore(backupRoot));
            var version = catalog.GetVersion("0.4.0")!;
            var entry = version.FindEntryPoint("version.dll")!;
            string exe = Path.Combine(gameDir, "FakeGame.exe");
            File.WriteAllBytes(exe, new byte[] { 0x4D, 0x5A });
            var profile = GameProfile.Create("Legacy Game", exe, GameSource.Manual);

            File.WriteAllBytes(Path.Combine(gameDir, "version.dll"), legacyBytes);
            File.WriteAllText(Path.Combine(gameDir, InstallationService.IniFileName), "[General]\nEnabled=1\n");
            var inspection = service.Inspect(profile);
            Assert.Equal(FileOrigin.Ours, inspection.ProxyOrigin);
            Assert.True(inspection.OutdatedProxy);
            Assert.True(inspection.OutdatedLayout);
            Assert.Null(inspection.InstalledVersion);
            Assert.Equal("version.dll", inspection.InstalledEntryPoint!.FileName);

            service.Install(profile, version, entry, Defaults);
            Assert.Equal(entry.Sha256, Hashing.Sha256File(Path.Combine(gameDir, "version.dll")), ignoreCase: true);
            string installedAsset = Path.Combine(gameDir, "dlssg_sm86", "runtime", "nvngx_dlssg.dll");
            Assert.Equal(assetHash, Hashing.Sha256File(installedAsset), ignoreCase: true);
            Assert.Equal(InstallKind.Installed, service.Inspect(profile).Kind);
            string installedIni = Path.Combine(gameDir, InstallationService.IniFileName);

            var warnings = service.Uninstall(profile);
            Assert.Empty(warnings);
            Assert.False(File.Exists(Path.Combine(gameDir, "version.dll")));
            // The INI that was in the folder before the upgrade is restored, not deleted.
            Assert.Equal("[General]\nEnabled=1\n", File.ReadAllText(installedIni));
            Assert.False(Directory.Exists(Path.Combine(gameDir, "dlssg_sm86")));
            Assert.Equal(InstallKind.NotInstalled, service.Inspect(profile).Kind);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (Directory.Exists(gameDir)) Directory.Delete(gameDir, recursive: true);
            if (Directory.Exists(backupRoot)) Directory.Delete(backupRoot, recursive: true);
        }
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
