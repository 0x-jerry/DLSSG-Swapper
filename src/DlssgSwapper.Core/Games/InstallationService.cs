using System.Diagnostics;
using DlssgSwapper.Core.Configuration;
using DlssgSwapper.Core.Payloads;

namespace DlssgSwapper.Core.Games;

public enum InstallKind
{
    NotInstalled,
    Installed,
    Partial,
}

public enum FileOrigin
{
    Absent,
    Ours,
    Foreign,
}

public sealed record InstallInspection
{
    public InstallKind Kind { get; init; }
    public PayloadVersion? InstalledVersion { get; init; }
    public PayloadEntryPoint? InstalledEntryPoint { get; init; }
    public FileOrigin ProxyOrigin { get; init; }
    public string? PresentProxyFile { get; init; }
    public bool IniPresent { get; init; }
    public IniSchema? IniSchema { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class GameRunningException : Exception
{
    public GameRunningException(string message) : base(message) { }
}

public sealed class ForeignFileException : Exception
{
    public ForeignFileException(string fileName) : base(
        $"{fileName} already exists and is not a file from this tool's payloads. " +
        "Refusing to overwrite it without confirmation.") { }
}

public sealed class InstallVerificationException : Exception
{
    public InstallVerificationException(string message) : base(message) { }
}

public sealed class InstallationService
{
    public const string IniFileName = "dlssg_sm86.ini";
    private static readonly string[] KnownProxyNames =
        { "version.dll", "winmm.dll", "dinput8.dll", "winhttp.dll", "dxgi.dll" };

    private readonly PayloadCatalog _catalog;
    private readonly Func<Guid, BackupStore> _backupFactory;

    public InstallationService(PayloadCatalog catalog, Func<Guid, BackupStore>? backupFactory = null)
    {
        _catalog = catalog;
        _backupFactory = backupFactory ?? (id => new BackupStore(id));
    }

    public InstallInspection Inspect(GameProfile profile)
    {
        var warnings = new List<string>();
        string gameDir = DirOrEmpty(profile);

        PayloadEntryPoint? foundOurs = null;
        string? presentProxy = null;
        bool foreignPresent = false;
        foreach (string name in KnownProxyNames)
        {
            string path = Path.Combine(gameDir, name);
            if (!File.Exists(path)) continue;
            var entry = IsOursAndWhich(path);
            presentProxy ??= name;
            if (entry != null)
            {
                if (foundOurs == null)
                    foundOurs = entry;
                else
                    warnings.Add($"Found a second proxy from this package ({name}); keeping only one is recommended.");
            }
            else
            {
                foreignPresent = true;
                warnings.Add($"{name} exists but does not match any payload in this tool; it will never be touched.");
            }
        }

        string iniPath = Path.Combine(gameDir, IniFileName);
        bool iniPresent = File.Exists(iniPath);
        IniSchema? schema = null;
        if (iniPresent)
        {
            try
            {
                var ini = IniFile.Load(iniPath);
                schema = ini.Get("General", "Enabled") != null ? IniSchema.Legacy : IniSchema.Native;
            }
            catch (Exception)
            {
                warnings.Add($"{IniFileName} exists but could not be parsed.");
            }
        }

        var origin = foundOurs != null ? FileOrigin.Ours : foreignPresent ? FileOrigin.Foreign : FileOrigin.Absent;
        var kind = (foundOurs != null, iniPresent) switch
        {
            (true, true) => InstallKind.Installed,
            (false, false) => InstallKind.NotInstalled,
            _ => InstallKind.Partial,
        };

        return new InstallInspection
        {
            Kind = kind,
            InstalledVersion = foundOurs != null
                ? _catalog.FindVersionOf(Hashing.Sha256File(Path.Combine(gameDir, foundOurs.FileName)))
                : null,
            InstalledEntryPoint = foundOurs,
            ProxyOrigin = origin,
            PresentProxyFile = presentProxy,
            IniPresent = iniPresent,
            IniSchema = schema,
            Warnings = warnings,
        };
    }

    public IReadOnlyList<string> Install(GameProfile profile, PayloadVersion version,
        PayloadEntryPoint entryPoint, FrameGenSettings settings, bool overwriteForeignFile = false)
    {
        if (!File.Exists(profile.ExecutablePath))
            throw new FileNotFoundException($"Game executable not found: {profile.ExecutablePath}");
        EnsureGameStopped(profile);

        string gameDir = profile.GameDirectory;
        string target = Path.Combine(gameDir, entryPoint.FileName);
        var warnings = new List<string>();

        AssertFileWritable(target);
        if (File.Exists(target) && IsOursAndWhich(target) == null && !overwriteForeignFile)
            throw new ForeignFileException(entryPoint.FileName);
        if (File.Exists(target) && IsOursAndWhich(target) == null)
            warnings.Add($"Overwriting foreign {entryPoint.FileName} after confirmation; the original is backed up.");

        var store = _backupFactory(profile.Id);
        store.BackupOriginal(gameDir, entryPoint.FileName);
        store.BackupOriginal(gameDir, IniFileName);

        RemoveOtherOwnedProxies(gameDir, entryPoint.FileName, warnings);

        string source = _catalog.ResolveBinaryPath(version, entryPoint);
        AssertFileWritable(target);
        File.Copy(source, target, overwrite: true);

        string templatePath = _catalog.ResolveTemplatePath(version.Templates["default"]);
        var ini = IniFile.Load(templatePath);
        IniApplier.Apply(ini, version.Schema, settings);
        string iniPath = Path.Combine(gameDir, IniFileName);
        AssertFileWritable(iniPath);
        ini.Save(iniPath);

        if (!string.Equals(Hashing.Sha256File(target), entryPoint.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InstallVerificationException($"Installed {entryPoint.FileName} failed hash verification.");

        store.RecordInstall(new InstallRecord
        {
            Version = version.Version,
            EntryPoint = entryPoint.FileName,
            DllSha256 = entryPoint.Sha256,
            IniSha256 = Hashing.Sha256File(iniPath),
            InstalledAt = DateTimeOffset.UtcNow,
        });

        return warnings;
    }

    public IReadOnlyList<string> Uninstall(GameProfile profile)
    {
        var warnings = new List<string>();
        string gameDir = DirOrEmpty(profile);
        var store = _backupFactory(profile.Id);

        foreach (string name in KnownProxyNames)
        {
            string path = Path.Combine(gameDir, name);
            bool oursPresent = File.Exists(path) && IsOursAndWhich(path) != null;
            if (File.Exists(path) && !oursPresent)
            {
                warnings.Add($"Left {name}: it does not belong to this tool.");
                continue;
            }

            if (store.HasBackup(name))
                store.RestoreOrDelete(gameDir, name);
            else if (oursPresent)
                File.Delete(path);
        }

        string iniPath = Path.Combine(gameDir, IniFileName);
        if (File.Exists(iniPath) && store.Install?.IniSha256 != null
            && string.Equals(Hashing.Sha256File(iniPath), store.Install.IniSha256, StringComparison.OrdinalIgnoreCase))
        {
            if (store.HasBackup(IniFileName))
                store.RestoreOrDelete(gameDir, IniFileName);
            else
                File.Delete(iniPath);
        }
        else if (File.Exists(iniPath))
        {
            warnings.Add($"Left {IniFileName}: it was modified after installation; not restoring over your changes.");
        }

        store.ClearInstall();
        return warnings;
    }

    public bool IsOurs(string path) => IsOursAndWhich(path) != null;

    private PayloadEntryPoint? IsOursAndWhich(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return _catalog.FindBySha256(Hashing.Sha256File(path));
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void RemoveOtherOwnedProxies(string gameDir, string keep, List<string> warnings)
    {
        foreach (string name in KnownProxyNames)
        {
            if (string.Equals(name, keep, StringComparison.OrdinalIgnoreCase)) continue;
            string path = Path.Combine(gameDir, name);
            if (!File.Exists(path)) continue;
            if (IsOursAndWhich(path) != null)
            {
                File.Delete(path);
                warnings.Add($"Removed previously installed proxy {name} (only one package proxy is kept).");
            }
        }
    }

    private static void EnsureGameStopped(GameProfile profile)
    {
        string processName = Path.GetFileNameWithoutExtension(profile.ExecutablePath);
        if (Process.GetProcessesByName(processName).Length > 0)
            throw new GameRunningException($"\"{profile.Name}\" is running. Exit the game before changing its files.");
    }

    private static void AssertFileWritable(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        }
        catch (IOException e)
        {
            throw new GameRunningException($"Could not write {Path.GetFileName(path)}: the file is in use. Exit the game and try again. ({e.Message})");
        }
    }

    private static string DirOrEmpty(GameProfile profile)
    {
        string dir = profile.GameDirectory;
        return Directory.Exists(dir) ? dir : throw new DirectoryNotFoundException(profile.ExecutablePath);
    }
}