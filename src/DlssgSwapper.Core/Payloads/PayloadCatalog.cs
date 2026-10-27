using System.Text.Json;

namespace DlssgSwapper.Core.Payloads;

public sealed class PayloadValidationException : Exception
{
    public PayloadValidationException(string message) : base(message) { }
    public PayloadValidationException(string message, Exception inner) : base(message, inner) { }
}

public sealed class PayloadCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _root;
    private readonly Dictionary<string, PayloadEntryPoint> _bySha256;
    private readonly Dictionary<string, PayloadVersion> _legacyBySha256;
    private readonly Dictionary<string, PayloadVersion> _assetBySha256;
    private readonly Dictionary<string, PayloadVersion> _legacyAssetBySha256;

    // Catalog version IDs were renamed as the payload moved forward; saved profiles still carry the
    // old IDs, so map them to the current line. 0.4.0 replaced the 0.3.x layout outright (the
    // runtime, backend and kernels moved out of the proxy into dlssg_sm86\), and 0.4.1 keeps that
    // layout while changing every file in it, so every older ID resolves to the current line and
    // installing it replaces the old files.
    private static readonly Dictionary<string, string> VersionAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["0.3.0"] = CurrentVersionId,
        ["0.3.0-310.1"] = CurrentVersionId,
        ["0.3.1"] = CurrentVersionId,
        ["0.3.1-310.1"] = CurrentVersionId,
        ["0.3.2"] = CurrentVersionId,
        ["0.3.2-310.1"] = CurrentVersionId,
        ["0.3.5"] = CurrentVersionId,
        ["0.3.5-310.1"] = CurrentVersionId,
        ["0.4.0"] = CurrentVersionId,
    };

    // The catalog id of the release this build installs; kept in one place because the alias table
    // and the tests both name it.
    public const string CurrentVersionId = "0.4.1";

    private PayloadCatalog(string root, IReadOnlyList<PayloadVersion> versions)
    {
        _root = root;
        Versions = versions;
        InstallableVersions = versions.Where(v => v.Installable).ToList();
        _bySha256 = new Dictionary<string, PayloadEntryPoint>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in versions.SelectMany(v => v.EntryPoints))
            _bySha256[entry.Sha256] = entry;
        _assetBySha256 = new Dictionary<string, PayloadVersion>(StringComparer.OrdinalIgnoreCase);
        foreach (var version in versions)
            foreach (var asset in version.DirectoryAssets)
                _assetBySha256[asset.Sha256] = version;
        _legacyBySha256 = new Dictionary<string, PayloadVersion>(StringComparer.OrdinalIgnoreCase);
        _legacyAssetBySha256 = new Dictionary<string, PayloadVersion>(StringComparer.OrdinalIgnoreCase);
        // First line in catalog order wins: the current release claims every file an older install
        // of it may still hold, and the archive-only lines below it are documentation.
        foreach (var version in versions)
        {
            foreach (string hash in version.LegacySha256)
                _legacyBySha256.TryAdd(hash, version);
            foreach (string hash in version.LegacyAssetSha256)
                _legacyAssetBySha256.TryAdd(hash, version);
        }
    }

    // Every catalog line, including the archive-only entries kept for recognition.
    public IReadOnlyList<PayloadVersion> Versions { get; }

    // The lines that can actually be installed, in catalog order.
    public IReadOnlyList<PayloadVersion> InstallableVersions { get; }

    // Saved profiles and the UI may name a release that has since been superseded (0.4.0 and the
    // 0.3.x ids now resolve to the current line, which replaced their files), so the alias table is
    // consulted before the literal catalog id.
    public PayloadVersion? GetVersion(string version)
    {
        if (VersionAliases.TryGetValue(version, out string? alias))
        {
            var aliased = Versions.FirstOrDefault(v => string.Equals(v.Version, alias, StringComparison.OrdinalIgnoreCase));
            if (aliased != null) return aliased;
        }
        return Versions.FirstOrDefault(v => string.Equals(v.Version, version, StringComparison.OrdinalIgnoreCase));
    }

    // The catalog lines that can be installed, in catalog order; the first is the current release.
    public PayloadVersion? CurrentVersion => InstallableVersions.FirstOrDefault();

    public PayloadEntryPoint? FindBySha256(string sha256) =>
        _bySha256.TryGetValue(sha256, out var entry) ? entry : null;

    // The payload line and entry point a proxy file with this content belongs to.
    public PayloadProxyMatch? FindConsumingVersion(string sha256)
    {
        var entry = FindBySha256(sha256);
        if (entry == null) return null;
        var version = FindVersionOf(sha256);
        return version == null ? null : new PayloadProxyMatch(version, entry);
    }

    // A hash from an earlier release of one of the bundled payload lines. The file is ours but
    // cannot be installed from this catalog; it only needs to be recognised and replaced.
    public PayloadVersion? FindLegacyVersionOf(string sha256) =>
        _legacyBySha256.TryGetValue(sha256, out var version) ? version : null;

    // A file that belongs to a payload's dlssg_sm86\ tree, matched by content hash. A file from an
    // earlier release of one of the bundled lines counts too: it is ours and has to be replaced,
    // not treated as a stranger's file.
    public PayloadVersion? FindVersionOfAsset(string sha256) =>
        _assetBySha256.TryGetValue(sha256, out var version)
            ? version
            : _legacyAssetBySha256.TryGetValue(sha256, out version) ? version : null;

    public PayloadVersion? FindVersionOf(string sha256)
    {
        if (!_bySha256.TryGetValue(sha256, out var entry)) return null;
        return Versions.FirstOrDefault(v => v.EntryPoints.Contains(entry));
    }

    public string ResolveBinaryPath(PayloadVersion version, PayloadEntryPoint entryPoint) =>
        Path.Combine(_root, "bin", version.Version, entryPoint.FileName);

    // Source file of an installed directory asset, inside the bundled payload copy.
    public string ResolveAssetSourcePath(PayloadVersion version, PayloadDirectoryAsset asset) =>
        Path.Combine(_root, "bin", version.Version, asset.Source.Replace('/', Path.DirectorySeparatorChar));

    public string ResolveTemplatePath(string templateRelativePath) =>
        Path.Combine(_root, templateRelativePath);

    public static PayloadCatalog Load(string root)
    {
        string catalogPath = Path.Combine(root, "catalog.json");
        if (!File.Exists(catalogPath))
            throw new PayloadValidationException($"catalog.json not found under {root}");

        CatalogDocument doc;
        try
        {
            doc = JsonSerializer.Deserialize<CatalogDocument>(File.ReadAllText(catalogPath), JsonOptions)
                ?? throw new PayloadValidationException($"catalog.json under {root} is empty");
        }
        catch (JsonException e)
        {
            throw new PayloadValidationException($"catalog.json under {root} is not valid JSON: {e.Message}", e);
        }

        var versions = doc.Versions.Select(ToVersion).ToList();
        var catalog = new PayloadCatalog(root, versions);
        foreach (var version in versions)
        {
            foreach (var entryPoint in version.EntryPoints)
                catalog.VerifyPayload(version, entryPoint);
            foreach (var asset in version.DirectoryAssets)
                catalog.VerifyAsset(version, asset);
            catalog.VerifyTemplates(version);
        }
        return catalog;
    }

    private static PayloadVersion ToVersion(VersionDto dto) => new(
        dto.Version,
        dto.DisplayName,
        dto.SourceRoot,
        dto.Templates,
        dto.EntryPoints
            .Select(e => new PayloadEntryPoint(e.File, e.Source, e.Sha256, e.Recommended, e.Note))
            .ToList())
    {
        MaxGeneratedFrames = dto.MaxGeneratedFrames,
        XeSSMaxGeneratedFrames = dto.XeSSMaxGeneratedFrames,
        LegacySha256 = dto.LegacySha256,
        LegacyAssetSha256 = dto.LegacyAssetSha256,
        DirectoryAssets = dto.DirectoryAssets
            .Select(a => new PayloadDirectoryAsset(a.Path, a.Source, a.Sha256, a.Bytes, a.Note))
            .ToList(),
    };

    private void VerifyPayload(PayloadVersion version, PayloadEntryPoint entryPoint)
    {
        string path = ResolveBinaryPath(version, entryPoint);
        if (!File.Exists(path))
            throw new PayloadValidationException($"Payload DLL missing: {path}");

        string actual = Hashing.Sha256File(path);
        if (!string.Equals(actual, entryPoint.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new PayloadValidationException(
                $"Hash mismatch for {path}: expected {entryPoint.Sha256}, got {actual}. " +
                "The payload was replaced or corrupted; re-clone the submodule and rebuild.");
    }

    private void VerifyAsset(PayloadVersion version, PayloadDirectoryAsset asset)
    {
        string path = ResolveAssetSourcePath(version, asset);
        if (!File.Exists(path))
            throw new PayloadValidationException($"Payload file missing: {path}");

        string actual = Hashing.Sha256File(path);
        if (!string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new PayloadValidationException(
                $"Hash mismatch for {path}: expected {asset.Sha256}, got {actual}. " +
                "The payload was replaced or corrupted; re-clone the submodule and rebuild.");
    }

    private void VerifyTemplates(PayloadVersion version)
    {
        if (!version.Installable) return;
        string? template = version.DefaultTemplate;
        if (template == null)
            throw new PayloadValidationException($"Payload {version.Version} has no default INI template.");

        string path = ResolveTemplatePath(template);
        if (!File.Exists(path))
            throw new PayloadValidationException($"Payload INI template missing: {path}");
    }

    private sealed class CatalogDocument
    {
        public List<VersionDto> Versions { get; set; } = new();
    }

    private sealed class VersionDto
    {
        public string Version { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string SourceRoot { get; set; } = "";
        public int MaxGeneratedFrames { get; set; } = 5;
        public int XeSSMaxGeneratedFrames { get; set; } = 1;
        public List<string> LegacySha256 { get; set; } = new();
        public List<string> LegacyAssetSha256 { get; set; } = new();
        public Dictionary<string, string> Templates { get; set; } = new();
        public List<EntryPointDto> EntryPoints { get; set; } = new();
        public List<DirectoryAssetDto> DirectoryAssets { get; set; } = new();
    }

    private sealed class EntryPointDto
    {
        public string File { get; set; } = "";
        public string Source { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public bool Recommended { get; set; }
        public string? Note { get; set; }
    }

    private sealed class DirectoryAssetDto
    {
        public string Path { get; set; } = "";
        public string Source { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public long Bytes { get; set; }
        public string? Note { get; set; }
    }
}
