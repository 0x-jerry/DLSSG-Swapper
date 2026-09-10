using System.Text.Json;

namespace DlssgSwapper.Core.Games;

public sealed class BackupEntry
{
    public string FileName { get; set; } = "";
    public bool Existed { get; set; }
    public string? StoredAs { get; set; }
    public string? Sha256 { get; set; }
}

public sealed class Manifest
{
    public List<BackupEntry> Backups { get; set; } = new();
    public InstallRecord? Install { get; set; }
}

public sealed class CorruptBackupException : Exception
{
    public CorruptBackupException(string message) : base(message) { }
}

// First-touch backup store: a pre-existing game file is backed up exactly once
// per profile; restoring returns the byte-identical original. Files that did
// not exist are recorded so uninstall deletes instead of restoring.
public sealed class BackupStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly string _root;
    private readonly string _manifestPath;
    private readonly Manifest _manifest;

    public BackupStore(Guid profileId) : this(RootFor(profileId)) { }

    public BackupStore(string root)
    {
        _root = root;
        _manifestPath = Path.Combine(root, "manifest.json");
        _manifest = File.Exists(_manifestPath)
            ? JsonSerializer.Deserialize<Manifest>(File.ReadAllText(_manifestPath), JsonOptions) ?? new Manifest()
            : new Manifest();
    }

    public static string RootFor(Guid profileId) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DlssgSwapper", "backups", profileId.ToString("N"));

    public IReadOnlyList<BackupEntry> Backups => _manifest.Backups;
    public InstallRecord? Install => _manifest.Install;

    public bool HasBackup(string fileName) =>
        _manifest.Backups.Any(b => string.Equals(b.FileName, fileName, StringComparison.OrdinalIgnoreCase));

    public void BackupOriginal(string gameDirectory, string fileName)
    {
        if (HasBackup(fileName)) return;

        string source = Path.Combine(gameDirectory, fileName);
        if (File.Exists(source))
        {
            Directory.CreateDirectory(_root);
            File.Copy(source, Path.Combine(_root, fileName), overwrite: true);
            _manifest.Backups.Add(new BackupEntry
            {
                FileName = fileName,
                Existed = true,
                StoredAs = fileName,
                Sha256 = Hashing.Sha256File(Path.Combine(_root, fileName)),
            });
        }
        else
        {
            _manifest.Backups.Add(new BackupEntry { FileName = fileName, Existed = false });
        }
        Save();
    }

    public void RestoreOrDelete(string gameDirectory, string fileName)
    {
        var entry = _manifest.Backups
            .FirstOrDefault(b => string.Equals(b.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return;

        string target = Path.Combine(gameDirectory, fileName);
        if (entry.Existed && entry.StoredAs is not null)
        {
            string stored = Path.Combine(_root, entry.StoredAs);
            if (!File.Exists(stored))
                throw new CorruptBackupException($"Backup for {fileName} is missing: {stored}");
            string actual = Hashing.Sha256File(stored);
            if (!string.Equals(actual, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new CorruptBackupException($"Backup for {fileName} failed hash verification: {stored}");

            Directory.CreateDirectory(gameDirectory);
            File.Copy(stored, target, overwrite: true);
        }
        else
        {
            if (File.Exists(target)) File.Delete(target);
        }

        _manifest.Backups.Remove(entry);
        Save();
    }

    public void RecordInstall(InstallRecord record)
    {
        _manifest.Install = record;
        Save();
    }

    public void ClearInstall()
    {
        _manifest.Install = null;
        Save();
    }

    private void Save()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(_manifestPath, JsonSerializer.Serialize(_manifest, JsonOptions));
    }
}