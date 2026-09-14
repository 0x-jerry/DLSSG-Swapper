using DlssgSwapper.Core.Payloads;

namespace DlssgSwapper.Core.Tests;

public class PayloadCatalogTests
{
    private static string PayloadRoot => Path.Combine(AppContext.BaseDirectory, "payloads");

    [Fact]
    public void Load_ReturnsBoth3109And3101Builds()
    {
        var catalog = PayloadCatalog.Load(PayloadRoot);

        Assert.Equal(2, catalog.Versions.Count);
        var current = catalog.GetVersion("0.3.0");
        Assert.NotNull(current);
        Assert.Equal(5, current.MaxGeneratedFrames);
        Assert.Equal(6, current.EntryPoints.Count);
        Assert.Equal("version.dll", current.EntryPoints.First(e => e.Recommended).FileName);
        Assert.Equal(3, catalog.GetVersion("0.3.0-310.1")!.MaxGeneratedFrames);
        Assert.Null(catalog.GetVersion("0.2.4"));
    }

    [Fact]
    public void Load_AcceptsInstalledDllHashes()
    {
        var catalog = PayloadCatalog.Load(PayloadRoot);

        foreach (var version in catalog.Versions)
            foreach (var entryPoint in version.EntryPoints)
            {
                string path = catalog.ResolveBinaryPath(version, entryPoint);
                Assert.Equal(entryPoint.Sha256, Hashing.Sha256File(path), ignoreCase: true);
            }
    }

    [Fact]
    public void FindBySha256_ResolvesInstalledProxy()
    {
        var catalog = PayloadCatalog.Load(PayloadRoot);
        var current = catalog.GetVersion("0.3.0")!;
        string hash = Hashing.Sha256File(catalog.ResolveBinaryPath(current, current.FindEntryPoint("dbghelp.dll")!));

        var entry = catalog.FindBySha256(hash);
        Assert.NotNull(entry);
        Assert.Equal("dbghelp.dll", entry.FileName);
        Assert.Same(current, catalog.FindVersionOf(hash));
    }

    [Fact]
    public void FindBySha256_DistinguishesTheTwoBudgets()
    {
        var catalog = PayloadCatalog.Load(PayloadRoot);
        var current = catalog.GetVersion("0.3.0")!;
        var legacy = catalog.GetVersion("0.3.0-310.1")!;

        string hash = Hashing.Sha256File(catalog.ResolveBinaryPath(legacy, legacy.FindEntryPoint("version.dll")!));
        Assert.Same(legacy, catalog.FindVersionOf(hash));
        Assert.NotSame(current, catalog.FindVersionOf(hash));
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
}
