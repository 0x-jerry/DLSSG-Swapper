using DlssgSwapper.Core.Games;

namespace DlssgSwapper.Core.Tests;

public class ProfileStoreTests
{
    private static string TempStorePath() =>
        Path.Combine(Path.GetTempPath(), $"profiles-{Guid.NewGuid():N}.json");

    [Fact]
    public void AddUpdateRemove_RoundTripsWithSettings()
    {
        string path = TempStorePath();
        try
        {
            var store = new ProfileStore(path);
            var profile = GameProfile.Create("Black Myth: Wukong",
                @"D:\Games\BlackMythWukong\b1\Binaries\Win64\b1-Win64-Shipping.exe",
                GameSource.Manual)
                with
            { Settings = new Configuration.FrameGenSettings { Optimized = 2 } };
            store.Add(profile);
            Assert.Single(store.Profiles);
            store.Save();

            var reloaded = new ProfileStore(path);
            var found = Assert.Single(reloaded.Profiles);
            Assert.Equal(profile.Id, found.Id);
            Assert.Equal(2, found.Settings?.Optimized);
            Assert.Equal(GameSource.Manual, found.Source);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Load_MapsTheRemovedAutoPresetToA()
    {
        string path = TempStorePath();
        try
        {
            // A profile written by a build that still offered [DLSSG] Preset=Auto; 0.4.1 removed that
            // value, and the mod itself runs it as A.
            File.WriteAllText(path, """
            [{
              "Id": "6f9619ff-8b86-d011-b42d-00cf4fc964ff",
              "Name": "Old Game",
              "ExecutablePath": "C:\\Games\\Old\\game.exe",
              "Source": "Manual",
              "Settings": { "Preset": "Auto", "Optimized": 2 }
            }]
            """);

            var store = new ProfileStore(path);
            var profile = Assert.Single(store.Profiles);
            Assert.Equal(Configuration.FrameGenPreset.A, profile.Settings!.Preset);
            Assert.Equal(2, profile.Settings.Optimized);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Remove_DeletesProfile()
    {
        string path = TempStorePath();
        try
        {
            var store = new ProfileStore(path);
            var profile = GameProfile.Create("Game A", @"C:\GameA\game.exe", GameSource.Steam);
            store.Add(profile);

            Assert.True(store.Remove(profile.Id));
            Assert.Empty(new ProfileStore(path).Profiles);
            Assert.False(store.Remove(profile.Id));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}