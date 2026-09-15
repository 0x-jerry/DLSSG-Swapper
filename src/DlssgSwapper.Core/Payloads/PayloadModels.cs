namespace DlssgSwapper.Core.Payloads;

public sealed record PayloadEntryPoint(
    string FileName,
    string Source,
    string Sha256,
    bool Recommended,
    string? Note)
{
    public string DisplayName => FileName;
}

public sealed record PayloadVersion(
    string Version,
    string DisplayName,
    string SourceRoot,
    IReadOnlyDictionary<string, string> Templates,
    IReadOnlyList<PayloadEntryPoint> EntryPoints)
{
    // Multiplier ceiling the bundled runtime supports: the INI value is clamped to this.
    public int MaxGeneratedFrames { get; init; } = 5;

    // Hashes of earlier builds of this same payload line, kept only so an already-installed
    // proxy from a previous release is still recognised as ours (for upgrade and uninstall).
    // These files are not shipped and cannot be installed.
    public IReadOnlyList<string> LegacySha256 { get; init; } = Array.Empty<string>();

    public PayloadEntryPoint? FindEntryPoint(string fileName) =>
        EntryPoints.FirstOrDefault(e => string.Equals(e.FileName, fileName, StringComparison.OrdinalIgnoreCase));
}