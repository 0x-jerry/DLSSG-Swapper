using DlssgSwapper.Core.Payloads;

namespace DlssgSwapper.Core.Tests;

public class PayloadCatalogTests
{
    private static string PayloadRoot => Path.Combine(AppContext.BaseDirectory, "payloads");

    [Fact]
    public void Load_ExposesOnlyCurrentLineAsInstallable()
    {
        var catalog = PayloadCatalog.Load(PayloadRoot);

        var current = catalog.GetVersion("0.4.1");
        Assert.NotNull(current);
        Assert.Equal(5, current.MaxGeneratedFrames);
        Assert.Equal(3, current.XeSSMaxGeneratedFrames);
        Assert.Equal(6, current.EntryPoints.Count);
        Assert.Equal("version.dll", current.EntryPoints.First(e => e.Recommended).FileName);
        Assert.Equal(4, current.DirectoryAssets.Count);
        // Version IDs from before the current release resolve to the current line, including the
        // archive-only labels, which is what a saved profile from an older release carries.
        Assert.Equal("0.4.1", catalog.GetVersion("0.4.0")!.Version);
        Assert.Equal("0.4.1", catalog.GetVersion("0.3.5")!.Version);
        Assert.Equal("0.4.1", catalog.GetVersion("0.3.5-310.1")!.Version);
        Assert.Null(catalog.GetVersion("0.2.4"));

        // The single installable line is what the UI offers.
        Assert.Equal(new[] { "0.4.1" }, catalog.InstallableVersions.Select(v => v.Version));

        // The older lines stay in the catalog for recognition only.
        var archive = catalog.Versions.Single(v => v.Version == "0.3.5");
        Assert.False(archive.Installable);
        Assert.Empty(archive.EntryPoints);
        Assert.NotEmpty(archive.LegacySha256);
    }

    [Fact]
    public void GetVersion_MapsPreviousReleaseIds()
    {
        var catalog = PayloadCatalog.Load(PayloadRoot);
        var current = catalog.GetVersion(PayloadCatalog.CurrentVersionId);

        Assert.Same(current, catalog.GetVersion("0.4.0"));
        Assert.Same(current, catalog.GetVersion("0.3.0"));
        Assert.Same(current, catalog.GetVersion("0.3.0-310.1"));
        Assert.Same(current, catalog.GetVersion("0.3.1"));
        Assert.Same(current, catalog.GetVersion("0.3.1-310.1"));
        Assert.Same(current, catalog.GetVersion("0.3.2"));
        Assert.Same(current, catalog.GetVersion("0.3.2-310.1"));
        Assert.Same(current, catalog.GetVersion("0.3.5"));
        Assert.Same(current, catalog.GetVersion("0.3.5-310.1"));
    }

    [Fact]
    public void FindLegacyVersionOf_RecognisesPreviousReleaseHashes()
    {
        var catalog = PayloadCatalog.Load(PayloadRoot);
        var archive = catalog.Versions.Single(v => v.Version == "0.3.5");
        var current = catalog.GetVersion(PayloadCatalog.CurrentVersionId)!;
        Assert.NotEmpty(current.LegacySha256);

        // A proxy from the archived 0.3.5 release and one from the 0.4.0 line both belong to us; the
        // one from the immediately preceding release resolves to the line that supersedes it.
        Assert.Same(archive, catalog.FindLegacyVersionOf(archive.LegacySha256[0]));
        Assert.Same(current, catalog.FindLegacyVersionOf(current.LegacySha256[0]));
        Assert.Null(catalog.FindBySha256(archive.LegacySha256[0]));
        Assert.Null(catalog.FindBySha256(current.LegacySha256[0]));
    }

    [Fact]
    public void FindVersionOfAsset_RecognisesThePreviousReleaseTree()
    {
        var catalog = PayloadCatalog.Load(PayloadRoot);
        var current = catalog.GetVersion(PayloadCatalog.CurrentVersionId)!;

        // 0.4.1 replaced every file of the tree, so a 0.4.0 install has to be reported as "another
        // payload version" rather than as a stranger's file.
        Assert.NotEmpty(current.LegacyAssetSha256);
        foreach (string hash in current.LegacyAssetSha256)
        {
            Assert.NotNull(catalog.FindVersionOfAsset(hash));
            Assert.Null(catalog.FindVersionOf(hash));
        }
    }

    [Fact]
    public void Load_AcceptsBundledDllAndAssetHashes()
    {
        var catalog = PayloadCatalog.Load(PayloadRoot);

        foreach (var version in catalog.InstallableVersions)
        {
            foreach (var entryPoint in version.EntryPoints)
            {
                string path = catalog.ResolveBinaryPath(version, entryPoint);
                Assert.Equal(entryPoint.Sha256, Hashing.Sha256File(path), ignoreCase: true);
            }

            foreach (var asset in version.DirectoryAssets)
            {
                string path = catalog.ResolveAssetSourcePath(version, asset);
                Assert.Equal(asset.Sha256, Hashing.Sha256File(path), ignoreCase: true);
                Assert.Equal(asset.Bytes, new FileInfo(path).Length);
            }
        }
    }

    [Fact]
    public void FindBySha256_ResolvesInstalledProxy()
    {
        var catalog = PayloadCatalog.Load(PayloadRoot);
        var current = catalog.GetVersion(PayloadCatalog.CurrentVersionId)!;
        string hash = Hashing.Sha256File(catalog.ResolveBinaryPath(current, current.FindEntryPoint("dbghelp.dll")!));

        var entry = catalog.FindBySha256(hash);
        Assert.NotNull(entry);
        Assert.Equal("dbghelp.dll", entry.FileName);
        Assert.Same(current, catalog.FindVersionOf(hash));
        Assert.Same(current, catalog.FindConsumingVersion(hash)!.Version);
    }

    [Fact]
    public void FindVersionOfAsset_ResolvesInstalledTreeFiles()
    {
        var catalog = PayloadCatalog.Load(PayloadRoot);
        var current = catalog.GetVersion(PayloadCatalog.CurrentVersionId)!;
        var asset = current.DirectoryAssets.First(a => a.Path.EndsWith("nvngx_dlssg.dll"));
        string hash = Hashing.Sha256File(catalog.ResolveAssetSourcePath(current, asset));

        Assert.Same(current, catalog.FindVersionOfAsset(hash));
        Assert.Null(catalog.FindVersionOfAsset(new string('0', 64)));
    }

    [Fact]
    public void FindBySha256_DistinguishesProxyFromTreeFile()
    {
        var catalog = PayloadCatalog.Load(PayloadRoot);
        var current = catalog.GetVersion(PayloadCatalog.CurrentVersionId)!;
        var asset = current.DirectoryAssets[0];
        string hash = Hashing.Sha256File(catalog.ResolveAssetSourcePath(current, asset));

        Assert.Null(catalog.FindBySha256(hash));
        Assert.Null(catalog.FindVersionOf(hash));
    }

    [Fact]
    public void Load_RejectsTamperedPayload()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"payload-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempRoot);
            Directory.CreateDirectory(Path.Combine(tempRoot, "bin", "0.3.0"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "templates"));
            File.WriteAllText(Path.Combine(tempRoot, "bin", "0.3.0", "version.dll"), "not a dll");
            File.WriteAllText(Path.Combine(tempRoot, "templates", "sm86-default.ini"), "[General]\nEnabled=1\n");
            File.WriteAllText(Path.Combine(tempRoot, "catalog.json"), """
            {
              "versions": [{
                "version": "0.3.0",
                "displayName": "0.3.0",
                "sourceRoot": "external/dlssg_for_sm86",
                "maxGeneratedFrames": 5,
                "templates": { "default": "templates/sm86-default.ini" },
                "entryPoints": [
                  { "file": "version.dll", "source": "version.dll",
                    "sha256": "a22d2453f25d7df3fdc0d6d683c21f01769a115439d58f1341183a75faaf8c7d",
                    "recommended": true }
                ]
              }]
            }
            """);

            var ex = Assert.Throws<PayloadValidationException>(() => PayloadCatalog.Load(tempRoot));
            Assert.Contains("Hash mismatch", ex.Message);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Load_RejectsTamperedDirectoryAsset()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"payload-asset-test-{Guid.NewGuid():N}");
        try
        {
            string bin = Path.Combine(tempRoot, "bin", "0.4.1");
            Directory.CreateDirectory(Path.Combine(bin, "dlssg_sm86", "runtime"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "templates"));
            File.WriteAllText(Path.Combine(bin, "version.dll"), "proxy");
            File.WriteAllText(Path.Combine(bin, "dlssg_sm86", "runtime", "nvngx_dlssg.dll"), "tampered");
            File.WriteAllText(Path.Combine(tempRoot, "templates", "sm86-default.ini"), "[General]\nEnabled=1\n");
            File.WriteAllText(Path.Combine(tempRoot, "catalog.json"), """
            {
              "versions": [{
                "version": "0.4.1",
                "displayName": "0.4.1",
                "sourceRoot": "external/dlssg_for_sm86",
                "templates": { "default": "templates/sm86-default.ini" },
                "entryPoints": [
                  { "file": "version.dll", "source": "version.dll",
                    "sha256": "a22d2453f25d7df3fdc0d6d683c21f01769a115439d58f1341183a75faaf8c7d",
                    "recommended": true }
                ],
                "directoryAssets": [
                  { "path": "dlssg_sm86/runtime/nvngx_dlssg.dll",
                    "source": "dlssg_sm86/runtime/nvngx_dlssg.dll", "sha256": "00", "bytes": 8 }
                ]
              }]
            }
            """);

            var ex = Assert.Throws<PayloadValidationException>(() => PayloadCatalog.Load(tempRoot));
            Assert.Contains("Hash mismatch", ex.Message);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
