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
    // The payload's dlssg_sm86\ tree: how many of its files are present and intact.
    public int AssetsPresent { get; init; }
    public int AssetsExpected { get; init; }
    // True when the file layout on disk is a 0.3.x install: a proxy and INI but no dlssg_sm86\ tree.
    public bool OutdatedLayout { get; init; }
    // True when the installed proxy matches an earlier release of a bundled payload line
    // (recognised so it is not treated as foreign, but it should be reinstalled).
    public bool OutdatedProxy { get; init; }
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
        "Refusing to overwrite it without confirmation.")
    { }
}

public sealed class InstallVerificationException : Exception
{
    public InstallVerificationException(string message) : base(message) { }
}

public sealed class InstallationService
{
    public const string IniFileName = "dlssg_sm86.ini";
    private static readonly string[] KnownProxyNames =
        { "version.dll", "winmm.dll", "dbghelp.dll", "dinput8.dll", "dxgi.dll", "d3d12.dll" };

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
        PayloadEntryPoint? foundLegacy = null;
        PayloadVersion? foundVersion = null;
        string? presentProxy = null;
        bool foreignPresent = false;
        foreach (string name in KnownProxyNames)
        {
            string path = Path.Combine(gameDir, name);
            if (!File.Exists(path)) continue;
            presentProxy ??= name;

            string? hash = TryHash(path);
            if (hash == null)
            {
                foreignPresent = true;
                warnings.Add($"{name} exists but could not be read; it will never be touched.");
                continue;
            }

            var consumer = _catalog.FindConsumingVersion(hash);
            if (consumer != null)
            {
                if (foundOurs == null)
                {
                    foundOurs = consumer.EntryPoint;
                    foundVersion = consumer.Version;
                }
                else
                    warnings.Add($"Found a second proxy from this package ({name}); keeping only one is recommended.");
                continue;
            }

            if (_catalog.FindLegacyVersionOf(hash) != null)
            {
                foundLegacy ??= new PayloadEntryPoint(name, name, hash, false, "Previous release");
                // The hash names the line that supersedes it, not the release that wrote it, so the
                // message stays unspecific about which build it came from.
                warnings.Add($"{name} is from an earlier payload release; reinstall to update it.");
                continue;
            }

            foreignPresent = true;
            warnings.Add($"{name} exists but does not match any payload in this tool; it will never be touched.");
        }

        string iniPath = Path.Combine(gameDir, IniFileName);
        bool iniPresent = File.Exists(iniPath);
        if (iniPresent)
        {
            try
            {
                IniFile.Load(iniPath);
            }
            catch (Exception)
            {
                warnings.Add($"{IniFileName} exists but could not be parsed.");
            }
        }

        var current = _catalog.InstallableVersions.FirstOrDefault();
        var assets = InspectAssets(gameDir, current, warnings);
        bool ours = foundOurs != null || foundLegacy != null;
        // A 0.3.x install has a proxy and an INI but no dlssg_sm86\ tree, and no release since 0.4.0
        // can run without that tree, so it is an upgrade that has to be redone.
        bool outdatedLayout = ours && !assets.AnyFilePresent && current is { DirectoryAssets.Count: > 0 };
        if (outdatedLayout)
            warnings.Add("Installed with the 0.3.x layout; the current payload also needs the dlssg_sm86 folder. Reinstall to update.");
        else if (assets.Stale > 0)
            warnings.Add($"dlssg_sm86 holds {assets.Stale} file(s) from an earlier payload release; reinstall to update them.");
        else if (assets.Present > 0 && assets.Present < current?.DirectoryAssets.Count)
            warnings.Add($"dlssg_sm86 is incomplete ({assets.Present} of {current!.DirectoryAssets.Count} files); reinstall to repair it.");

        var origin = ours ? FileOrigin.Ours : foreignPresent ? FileOrigin.Foreign : FileOrigin.Absent;
        bool complete = !outdatedLayout && foundOurs != null && iniPresent && assets.Complete
                        && assets.Stale == 0;
        var kind = (ours, iniPresent, complete) switch
        {
            (true, true, true) => InstallKind.Installed,
            // No proxy and no payload tree: at most a leftover INI, which does nothing on its own
            // (the mod only runs from a proxy).
            (false, _, _) when assets.Present == 0 && !assets.AnyFilePresent => InstallKind.NotInstalled,
            _ => InstallKind.Partial,
        };

        return new InstallInspection
        {
            Kind = kind,
            InstalledVersion = foundVersion,
            InstalledEntryPoint = foundOurs ?? foundLegacy,
            ProxyOrigin = origin,
            PresentProxyFile = presentProxy,
            IniPresent = iniPresent,
            AssetsPresent = assets.Present,
            AssetsExpected = current?.DirectoryAssets.Count ?? 0,
            OutdatedLayout = outdatedLayout,
            OutdatedProxy = foundOurs == null && foundLegacy != null,
            Warnings = warnings,
        };
    }

    // Outcome of looking at the payload's dlssg_sm86\ tree in the game folder.
    private readonly record struct AssetInspection(int Present, int Stale, bool AnyFilePresent, bool Complete);

    // Counts the payload's directory files that are present and byte-identical. A file with a
    // different hash is reported but never claimed, and unrelated files are ignored.
    private AssetInspection InspectAssets(string gameDir, PayloadVersion? version, List<string> warnings)
    {
        if (version == null || version.DirectoryAssets.Count == 0) return new AssetInspection(0, 0, false, true);

        int present = 0;
        int stale = 0;
        bool anyFilePresent = false;
        foreach (var asset in version.DirectoryAssets)
        {
            string path = Path.Combine(gameDir, ToLocalPath(asset.Path));
            if (!File.Exists(path))
            {
                warnings.Add($"{asset.Path} is missing from the game folder.");
                continue;
            }

            anyFilePresent = true;
            string? hash = TryHash(path);
            if (hash == null)
            {
                warnings.Add($"{asset.Path} could not be read; it will never be touched.");
                continue;
            }

            if (string.Equals(hash, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                present++;
                continue;
            }

            if (_catalog.FindVersionOfAsset(hash) != null)
            {
                stale++;
                continue;
            }

            warnings.Add($"{asset.Path} does not match this payload; it will never be touched.");
        }

        return new AssetInspection(present, stale, anyFilePresent, present == version.DirectoryAssets.Count);
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
        if (File.Exists(target) && !IsOursOrLegacy(target) && !overwriteForeignFile)
            throw new ForeignFileException(entryPoint.FileName);
        if (File.Exists(target) && !IsOursOrLegacy(target))
            warnings.Add($"Overwriting foreign {entryPoint.FileName} after confirmation; the original is backed up.");

        var store = _backupFactory(profile.Id);
        // Never treat a proxy we installed (current or previous release) as the game's original.
        if (!File.Exists(target) || !IsOursOrLegacy(target))
            store.BackupOriginal(gameDir, entryPoint.FileName);
        // Same for the INI: an upgrade must not record the previous release's INI - ours - as the
        // file to restore on uninstall, or uninstalling would leave a mod INI behind.
        if (!IsOurIni(Path.Combine(gameDir, IniFileName), store.Install))
            store.BackupOriginal(gameDir, IniFileName);

        RemoveOtherOwnedProxies(gameDir, entryPoint.FileName, warnings);

        string source = _catalog.ResolveBinaryPath(version, entryPoint);
        AssertFileWritable(target);
        File.Copy(source, target, overwrite: true);

        string? templateRelative = version.DefaultTemplate;
        if (templateRelative != null)
        {
            var ini = IniFile.Load(_catalog.ResolveTemplatePath(templateRelative));
            IniApplier.Apply(ini, ClampToRuntime(settings, version));
            string iniPath = Path.Combine(gameDir, IniFileName);
            AssertFileWritable(iniPath);
            ini.Save(iniPath);
        }

        var installed = new List<InstalledFileRecord>();
        foreach (var asset in version.DirectoryAssets)
            installed.Add(InstallAsset(gameDir, version, asset, overwriteForeignFile, warnings));

        if (!string.Equals(Hashing.Sha256File(target), entryPoint.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InstallVerificationException($"Installed {entryPoint.FileName} failed hash verification.");

        var iniInstalled = Path.Combine(gameDir, IniFileName);
        store.RecordInstall(new InstallRecord
        {
            Version = version.Version,
            EntryPoint = entryPoint.FileName,
            DllSha256 = entryPoint.Sha256,
            IniSha256 = File.Exists(iniInstalled) ? Hashing.Sha256File(iniInstalled) : "",
            InstalledAt = DateTimeOffset.UtcNow,
            Files = installed,
        });

        return warnings;
    }

    private InstalledFileRecord InstallAsset(string gameDir, PayloadVersion version, PayloadDirectoryAsset asset,
        bool overwriteForeignFile, List<string> warnings)
    {
        string relative = ToLocalPath(asset.Path);
        string source = _catalog.ResolveAssetSourcePath(version, asset);
        string destination = Path.Combine(gameDir, relative);

        string? directory = Path.GetDirectoryName(destination);
        if (directory != null) Directory.CreateDirectory(directory);

        // A file already at this path is replaced when it is ours - the current release's, or one an
        // earlier release left behind, which is exactly what an upgrade does. Anything else belongs
        // to the game or another mod and is only overwritten on request, like a foreign proxy.
        if (File.Exists(destination))
        {
            string? existing = TryHash(destination);
            bool ours = existing != null
                        && (_catalog.FindVersionOfAsset(existing) != null
                            || string.Equals(existing, asset.Sha256, StringComparison.OrdinalIgnoreCase));
            if (!ours)
            {
                if (!overwriteForeignFile)
                    throw new ForeignFileException(asset.Path);
                warnings.Add($"Overwriting foreign {asset.Path} after confirmation; it cannot be backed up.");
            }
        }

        File.Copy(source, destination, overwrite: true);

        string actual = Hashing.Sha256File(destination);
        if (!string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InstallVerificationException($"Installed {asset.Path} failed hash verification.");

        return new InstalledFileRecord { Path = relative, Sha256 = asset.Sha256, Bytes = asset.Bytes };
    }

    public IReadOnlyList<string> Uninstall(GameProfile profile)
    {
        var warnings = new List<string>();
        string gameDir = DirOrEmpty(profile);
        var store = _backupFactory(profile.Id);
        // Read the record before anything clears it: it is the manifest of the dlssg_sm86\ files.
        var record = store.Install;

        foreach (string name in KnownProxyNames)
        {
            string path = Path.Combine(gameDir, name);
            bool oursPresent = IsOursOrLegacy(path);
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
        if (File.Exists(iniPath))
        {
            // A superseded 0.3.x INI is removed rather than restored over: it cannot configure the
            // current payload anyway, and restoring the game's original would leave the folder in
            // the 0.3.x state.
            bool superseded = LooksLikeSupersededIni(iniPath);
            if (superseded || IsCurrentIni(iniPath, record?.IniSha256))
            {
                if (store.HasBackup(IniFileName) && !superseded)
                    store.RestoreOrDelete(gameDir, IniFileName);
                else
                    File.Delete(iniPath);
            }
            else
            {
                warnings.Add($"Left {IniFileName}: it was modified after installation; not restoring over your changes.");
            }
        }

        RemoveInstalledAssets(gameDir, record, warnings);

        store.ClearInstall();
        return warnings;
    }

    // The INI is one this tool wrote: the untouched template, a superseded 0.3.x one it used to
    // write, or the file its own install record names (which is how a modified install is
    // recognised across releases).
    private bool IsOurIni(string iniPath, InstallRecord? record)
    {
        if (!File.Exists(iniPath)) return false;
        if (LooksLikeSupersededIni(iniPath)) return true;
        return IsCurrentIni(iniPath, record?.IniSha256);
    }

    // The INI still holds what this install wrote (recorded hash) or the untouched factory content
    // of the bundled template.
    public bool IsCurrentIni(string iniPath, string? recordedSha256)
    {
        string actual = Hashing.Sha256File(iniPath);
        if (!string.IsNullOrEmpty(recordedSha256)
            && string.Equals(actual, recordedSha256, StringComparison.OrdinalIgnoreCase))
            return true;

        string? template = _catalog.CurrentVersion?.DefaultTemplate;
        if (template == null) return false;

        string templatePath = _catalog.ResolveTemplatePath(template);
        return File.Exists(templatePath)
            && string.Equals(actual, Hashing.Sha256File(templatePath), StringComparison.OrdinalIgnoreCase);
    }

    // A 0.3.x INI: keys this tool used to write that 0.4.0 moved into other sections, and no
    // [DLSSG] / [XeSSFG] section. Such a file cannot configure the current payload correctly
    // anyway, so an uninstall or upgrade removes it rather than restoring the original over it.
    public static bool LooksLikeSupersededIni(string iniPath)
    {
        try
        {
            var ini = IniFile.Load(iniPath);
            bool modern = ini.Get("DLSSG", "Optimized") != null
                || ini.Get("DLSSG", "Preset") != null
                || ini.Get("FrameGeneration", "Engine") != null
                || ini.Get("XeSSFG", "FlowResolution") != null;
            if (modern) return false;

            return ini.Get("FrameGeneration", "Optimized") != null
                || ini.Get("FrameGeneration", "MaxGeneratedFrames") != null
                || ini.Get("Runtime", "CacheDirectory") != null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Removes the dlssg_sm86\ files this install wrote, and only those: a file whose content no
    // longer matches the record (patched by hand, or replaced by another version) is left alone.
    private void RemoveInstalledAssets(string gameDir, InstallRecord? record, List<string> warnings)
    {
        if (record == null || record.Files.Count == 0) return;

        foreach (var file in record.Files)
        {
            string path = Path.Combine(gameDir, ToLocalPath(file.Path));
            if (!File.Exists(path)) continue;

            string? hash = TryHash(path);
            if (hash == null)
            {
                warnings.Add($"Left {file.Path}: it could not be read.");
                continue;
            }

            if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Left {file.Path}: it no longer matches what was installed.");
                continue;
            }

            File.Delete(path);
        }

        RemoveEmptyDirectories(gameDir, record.Files.Select(f => f.Path));
    }

    // Deletes the payload's directories bottom-up, but only the ones that ended up empty. Every
    // ancestor of a recorded file is a candidate, so the dlssg_sm86 root goes too when nothing
    // else was in it, while a directory holding someone else's file is left in place.
    private static void RemoveEmptyDirectories(string gameDir, IEnumerable<string> relativePaths)
    {
        var directories = new List<string>();
        foreach (string relative in relativePaths)
        {
            string? directory = Path.GetDirectoryName(ToLocalPath(relative));
            while (!string.IsNullOrEmpty(directory))
            {
                directories.Add(directory!);
                directory = Path.GetDirectoryName(directory);
            }
        }

        var ordered = directories
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(d => d.Length)
            .ToList();

        foreach (string relative in ordered)
        {
            string path = Path.Combine(gameDir, relative);
            if (!Directory.Exists(path)) continue;
            if (Directory.EnumerateFileSystemEntries(path).Any()) continue;
            try
            {
                Directory.Delete(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A locked directory is not worth failing an uninstall over; its files are gone.
            }
        }
    }

    public bool IsOurs(string path) => IsOursOrLegacy(path);

    // The payload's ceiling is the main limit; the XeSS-FG engine has its own, lower one (2X in
    // 0.4.0, 2X-4X in 0.4.1), and a value above it makes the mod refuse frame generation
    // (max_generated_frames_unsupported) rather than run at a lower count.
    public static FrameGenSettings ClampToRuntime(FrameGenSettings settings, PayloadVersion version)
    {
        int ceiling = CeilingFor(settings, version);
        if (settings.MaxGeneratedFrames is not int frames || frames <= ceiling) return settings;
        return settings with { MaxGeneratedFrames = ceiling };
    }

    // The highest MaxGeneratedFrames the given payload line accepts for this engine.
    public static int CeilingFor(FrameGenSettings settings, PayloadVersion version) =>
        settings.Engine == FrameGenEngine.XeSS
            ? Math.Min(version.MaxGeneratedFrames, version.XeSSMaxGeneratedFrames)
            : version.MaxGeneratedFrames;

    // Ours, including a proxy left by an earlier payload release (matched by legacy hash).
    private bool IsOursOrLegacy(string path)
    {
        if (!File.Exists(path)) return false;
        string? hash = TryHash(path);
        return hash != null && (_catalog.FindBySha256(hash) != null || _catalog.FindLegacyVersionOf(hash) != null);
    }

    private static string? TryHash(string path)
    {
        try
        {
            return Hashing.Sha256File(path);
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
            if (IsOursOrLegacy(path))
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

    private static string ToLocalPath(string relative) =>
        relative.Replace('/', Path.DirectorySeparatorChar);

    private static string DirOrEmpty(GameProfile profile)
    {
        string dir = profile.GameDirectory;
        return Directory.Exists(dir) ? dir : throw new DirectoryNotFoundException(profile.ExecutablePath);
    }
}
