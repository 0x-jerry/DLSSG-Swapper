using DlssgSwapper.Core.Steam;

namespace DlssgSwapper.Core.Tests;

public class KeyValuesParserTests
{
    [Fact]
    public void Parse_LibraryFolders_HandlesEscapedPathsAndMultipleLibraries()
    {
        const string vdf = """
            "libraryfolders"
            {
                "contentstatsid"		"3013227085"
                "0"		"C:\\Program Files (x86)\\Steam"
                "1"		"D:\\Games\\SteamLibrary"
                "2"
                {
                    "path"		"E:\\SteamLibrary"
                    "label"		"SSD"
                    "contentid"		"123"
                }
            }
            """;

        var doc = KeyValuesParser.Parse(vdf);
        var libraries = Assert.IsType<KeyValueNode>(doc.Find("libraryfolders"));
        // contentstatsid is a sibling entry; the three libraries follow it.
        Assert.Equal(4, libraries.Children.Count);
        Assert.Equal(@"C:\Program Files (x86)\Steam", libraries.Children[1].Value);
        Assert.Equal(@"E:\SteamLibrary", libraries.Children[3].GetValue("path"));
        Assert.Equal("SSD", libraries.Children[3].GetValue("label"));
    }

    [Fact]
    public void Parse_AppManifest_YieldsAppStateFields()
    {
        const string acf = """
            "AppState"
            {
                "appid"		"2453640"
                "universe"		"1"
                "name"		"Black Myth: Wukong"
                "stateflags"		"4"
                "installdir"		"BlackMythWukong"
            }
            """;

        var doc = KeyValuesParser.Parse(acf);
        var state = Assert.IsType<KeyValueNode>(doc.Find("AppState"));
        Assert.Equal("2453640", state.GetValue("appid"));
        Assert.Equal("Black Myth: Wukong", state.GetValue("name"));
        Assert.Equal("BlackMythWukong", state.GetValue("installdir"));
    }

    [Fact]
    public void Parse_SkipsLineAndBlockComments()
    {
        const string text = """
            // header comment
            "root" {
                "a" "1" /* block
                comment */ "b" "2"
            }
            """;

        var root = KeyValuesParser.Parse(text);
        var node = Assert.IsType<KeyValueNode>(root.Find("root"));
        Assert.Equal(2, node.Children.Count);
        Assert.Equal("2", node.Find("b")?.Value);
    }
}

public class SteamLibraryScannerTests
{
    [Fact]
    public void FindInstalledApps_ResolvesInstallDirUnderCommon()
    {
        string temp = Path.Combine(Path.GetTempPath(), $"steam-test-{Guid.NewGuid():N}");
        try
        {
            string appsDir = Path.Combine(temp, "steamapps");
            string gameDir = Path.Combine(appsDir, "common", "MyGame");
            Directory.CreateDirectory(gameDir);
            File.WriteAllText(Path.Combine(appsDir, "appmanifest_123.acf"),
                "\"AppState\"\n{\n\"appid\" \"123\"\n\"name\" \"My Game\"\n\"installdir\" \"MyGame\"\n}\n");
            File.WriteAllText(Path.Combine(appsDir, "appmanifest_456.acf"),
                "\"AppState\"\n{\n\"appid\" \"456\"\n\"name\" \"Broken Path\"\n\"installdir\" \"MissingDir\"\n}\n");
            File.WriteAllText(Path.Combine(appsDir, "notamanifest.txt"), "ignore me");

            var apps = SteamLibraryScanner.FindInstalledApps(new[] { temp });
            Assert.Equal(2, apps.Count);
            var game = apps.Single(a => a.AppId == "123");
            Assert.Equal("My Game", game.Name);
            Assert.Equal(gameDir, game.InstallDir, ignoreCase: true);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void FindInstalledApps_ListsAGameOnceWhenItsLibraryIsScannedTwice()
    {
        string temp = Path.Combine(Path.GetTempPath(), $"steam-dupe-{Guid.NewGuid():N}");
        try
        {
            string appsDir = Path.Combine(temp, "steamapps");
            Directory.CreateDirectory(Path.Combine(appsDir, "common", "MyGame"));
            File.WriteAllText(Path.Combine(appsDir, "appmanifest_123.acf"),
                "\"AppState\"\n{\n\"appid\" \"123\"\n\"name\" \"My Game\"\n\"installdir\" \"MyGame\"\n}\n");

            var apps = SteamLibraryScanner.FindInstalledApps(new[] { temp, temp + Path.DirectorySeparatorChar });
            Assert.Single(apps);
            Assert.Equal("123", apps[0].AppId);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void FindCandidateExecutables_RanksWin64AndNvngxDllSibling()
    {
        string temp = Path.Combine(Path.GetTempPath(), $"exe-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(temp, "binaries", "win64", "renderer"));
            Directory.CreateDirectory(Path.Combine(temp, "binaries", "win64", "modded"));
            Directory.CreateDirectory(Path.Combine(temp, "launcher"));
            string launcher = Path.Combine(temp, "launcher", "Launcher.exe");
            string renderer = Path.Combine(temp, "binaries", "win64", "renderer", "Game-Win64-Shipping.exe");
            string withNvngx = Path.Combine(temp, "binaries", "win64", "modded", "Modded-Win64-Shipping.exe");
            File.WriteAllBytes(launcher, new byte[1024]);
            File.WriteAllBytes(renderer, new byte[4096]);
            File.WriteAllBytes(withNvngx, new byte[2048]);
            File.WriteAllBytes(Path.Combine(temp, "binaries", "win64", "modded", "nvngx_dlssg.dll"), new byte[16]);

            var candidates = SteamLibraryScanner.FindCandidateExecutables(temp);
            Assert.Equal(3, candidates.Count);
            Assert.Equal(withNvngx, candidates[0].Path, ignoreCase: true);
            Assert.Equal(renderer, candidates[1].Path, ignoreCase: true);
            Assert.Equal(launcher, candidates[2].Path, ignoreCase: true);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void FindSteamInstallDir_ReturnsNullOrExistingDirectory()
    {
        string? dir = SteamLibraryScanner.FindSteamInstallDir();
        Assert.True(dir is null || Directory.Exists(dir));
    }
}