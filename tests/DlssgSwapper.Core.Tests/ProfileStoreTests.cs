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
                with { Settings = new Configuration.FrameGenSettings { Router = "SM75" } };
            store.Add(profile);
            Assert.Single(store.Profiles);
            store.Save();

            var reloaded = new ProfileStore(path);
            var found = Assert.Single(reloaded.Profiles);
            Assert.Equal(profile.Id, found.Id);
            Assert.Equal("SM75", found.Settings?.Router);
            Assert.Equal(GameSource.Manual, found.Source);
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