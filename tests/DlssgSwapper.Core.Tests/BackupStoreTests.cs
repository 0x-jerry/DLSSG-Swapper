using DlssgSwapper.Core.Games;

namespace DlssgSwapper.Core.Tests;

public class BackupStoreTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"backup-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void BackupOriginal_IsFirstTouchOnly_AndRestoreIsByteIdentical()
    {
        string gameDir = TempDir();
        string backupRoot = TempDir();
        try
        {
            string original = Path.Combine(gameDir, "version.dll");
            byte[] bytes = { 1, 2, 3, 4, 5 };
            File.WriteAllBytes(original, bytes);

            var store = new BackupStore(backupRoot);
            store.BackupOriginal(gameDir, "version.dll");
            store.BackupOriginal(gameDir, "version.dll"); // second call must not duplicate

            byte[] tamper = { 9, 9, 9 };
            File.WriteAllBytes(original, tamper);
            Assert.Single(store.Backups); // first-touch: no duplicate backup

            store.RestoreOrDelete(gameDir, "version.dll");
            Assert.Equal(bytes, File.ReadAllBytes(original));
            Assert.Empty(store.Backups);
        }
        finally
        {
            Directory.Delete(gameDir, recursive: true);
            Directory.Delete(backupRoot, recursive: true);
        }
    }

    [Fact]
    public void RestoreOrDelete_AbsentOriginal_DeletesOurs()
    {
        string gameDir = TempDir();
        string backupRoot = TempDir();
        try
        {
            var store = new BackupStore(backupRoot);
            store.BackupOriginal(gameDir, "version.dll"); // records absent original
            File.WriteAllBytes(Path.Combine(gameDir, "version.dll"), new byte[] { 1 });

            store.RestoreOrDelete(gameDir, "version.dll");
            Assert.False(File.Exists(Path.Combine(gameDir, "version.dll")));
            Assert.Empty(store.Backups);
        }
        finally
        {
            Directory.Delete(gameDir, recursive: true);
            Directory.Delete(backupRoot, recursive: true);
        }
    }

    [Fact]
    public void CorruptBackup_ThrowsWithoutTouchingTarget()
    {
        string gameDir = TempDir();
        string backupRoot = TempDir();
        try
        {
            var store = new BackupStore(backupRoot);
            store.BackupOriginal(gameDir, "dlssg_sm86.ini");
            File.WriteAllBytes(Path.Combine(gameDir, "dlssg_sm86.ini"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(backupRoot, "dlssg_sm86.ini"), new byte[] { 2, 3, 4 });

            Assert.Throws<CorruptBackupException>(() => store.RestoreOrDelete(gameDir, "dlssg_sm86.ini"));
            Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(gameDir, "dlssg_sm86.ini")));
        }
        finally
        {
            Directory.Delete(gameDir, recursive: true);
            Directory.Delete(backupRoot, recursive: true);
        }
    }

    [Fact]
    public void RecordInstall_SurvivesReload()
    {
        string backupRoot = TempDir();
        try
        {
            var store = new BackupStore(backupRoot);
            store.RecordInstall(new InstallRecord
            {
                Version = "0.2.4",
                EntryPoint = "winmm.dll",
                DllSha256 = "abc",
                IniSha256 = "def",
                InstalledAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            });

            var reloaded = new BackupStore(backupRoot);
            Assert.NotNull(reloaded.Install);
            Assert.Equal("winmm.dll", reloaded.Install.EntryPoint);
            Assert.Equal("abc", reloaded.Install.DllSha256);

            reloaded.ClearInstall();
            Assert.Null(new BackupStore(backupRoot).Install);
        }
        finally
        {
            Directory.Delete(backupRoot, recursive: true);
        }
    }
}