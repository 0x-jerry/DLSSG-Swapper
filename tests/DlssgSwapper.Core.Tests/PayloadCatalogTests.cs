using DlssgSwapper.Core.Payloads;

namespace DlssgSwapper.Core.Tests;

public class PayloadCatalogTests
{
    private static string PayloadRoot => Path.Combine(AppContext.BaseDirectory, "payloads");

    [Fact]
    public void Load_ReturnsSingleVersionWithAllEntryPoints()
    {
        var catalog = PayloadCatalog.Load(PayloadRoot);

        Assert.Single(catalog.Versions);
        var native = catalog.GetVersion("0.2.4");
        Assert.NotNull(native);
        Assert.Equal(5, native.EntryPoints.Count);
        Assert.Equal("version.dll", native.EntryPoints.First(e => e.Recommended).FileName);
        Assert.Null(catalog.GetVersion("0.1.0"));
    }

    [Fact]
    public void Load_AcceptsInstalledDllHashes()
    {
        var catalog = PayloadCatalog.Load(PayloadRoot);
        var native = catalog.GetVersion("0.2.4")!;

        foreach (var entryPoint in native.EntryPoints)
        {
            string path = catalog.ResolveBinaryPath(native, entryPoint);
            Assert.Equal(entryPoint.Sha256, Hashing.Sha256File(path), ignoreCase: true);
        }
    }

    [Fact]
    public void FindBySha256_ResolvesInstalledProxy()
    {
        var catalog = PayloadCatalog.Load(PayloadRoot);
        string hash = Hashing.Sha256File(catalog.ResolveBinaryPath(
            catalog.GetVersion("0.2.4")!,
            catalog.GetVersion("0.2.4")!.FindEntryPoint("winmm.dll")!));

        var entry = catalog.FindBySha256(hash);
        Assert.NotNull(entry);
        Assert.Equal("winmm.dll", entry.FileName);
        Assert.NotNull(catalog.FindVersionOf(hash));
    }

    [Fact]
    public void Load_RejectsTamperedPayload()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"payload-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempRoot);
            Directory.CreateDirectory(Path.Combine(tempRoot, "bin", "0.2.4"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "templates"));
            File.WriteAllText(Path.Combine(tempRoot, "bin", "0.2.4", "version.dll"), "not a dll");
            File.WriteAllText(Path.Combine(tempRoot, "templates", "native-default.ini"), "[Compatibility]\nRouter=SM86\n");
            File.WriteAllText(Path.Combine(tempRoot, "catalog.json"), """
            {
              "versions": [{
                "version": "0.2.4",
                "displayName": "Native 0.2.4",
                "iniSchema": "native",
                "sourceRoot": "external/dlssg_for_sm86",
                "templates": { "default": "templates/native-default.ini" },
                "entryPoints": [
                  { "file": "version.dll", "source": "version.dll",
                    "sha256": "c844646d835a7b88ed1382eea80403d38b433f8ac09cf92581c73698c44ae7c2",
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