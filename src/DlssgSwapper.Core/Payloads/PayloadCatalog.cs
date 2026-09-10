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

    private PayloadCatalog(string root, IReadOnlyList<PayloadVersion> versions)
    {
        _root = root;
        Versions = versions;
        _bySha256 = versions
            .SelectMany(v => v.EntryPoints)
            .ToDictionary(e => e.Sha256, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<PayloadVersion> Versions { get; }

    public PayloadVersion? GetVersion(string version) =>
        Versions.FirstOrDefault(v => string.Equals(v.Version, version, StringComparison.OrdinalIgnoreCase));

    public PayloadEntryPoint? FindBySha256(string sha256) =>
        _bySha256.TryGetValue(sha256, out var entry) ? entry : null;

    public PayloadVersion? FindVersionOf(string sha256)
    {
        if (!_bySha256.TryGetValue(sha256, out var entry)) return null;
        return Versions.FirstOrDefault(v => v.EntryPoints.Contains(entry));
    }

    public string ResolveBinaryPath(PayloadVersion version, PayloadEntryPoint entryPoint) =>
        Path.Combine(_root, "bin", version.Version, entryPoint.FileName);

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
            foreach (var entry in version.EntryPoints)
                catalog.VerifyPayload(version, entry);
        return catalog;
    }

    private static PayloadVersion ToVersion(VersionDto dto) => new(
        dto.Version,
        dto.DisplayName,
        dto.SourceRoot,
        dto.Templates,
        dto.EntryPoints
            .Select(e => new PayloadEntryPoint(e.File, e.Source, e.Sha256, e.Recommended, e.Note))
            .ToList());

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

    private sealed class CatalogDocument
    {
        public List<VersionDto> Versions { get; set; } = new();
    }

    private sealed class VersionDto
    {
        public string Version { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string SourceRoot { get; set; } = "";
        public Dictionary<string, string> Templates { get; set; } = new();
        public List<EntryPointDto> EntryPoints { get; set; } = new();
    }

    private sealed class EntryPointDto
    {
        public string File { get; set; } = "";
        public string Source { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public bool Recommended { get; set; }
        public string? Note { get; set; }
    }
}