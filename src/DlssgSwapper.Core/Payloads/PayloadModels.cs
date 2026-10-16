namespace DlssgSwapper.Core.Payloads;

// A proxy file that belongs to one of the bundled payload lines, tied to the line it installs.
public sealed record PayloadProxyMatch(PayloadVersion Version, PayloadEntryPoint EntryPoint);

public sealed record PayloadEntryPoint(
    string FileName,
    string Source,
    string Sha256,
    bool Recommended,
    string? Note)
{
    public string DisplayName => FileName;
}

// A file the proxy needs beside itself under the game folder, written by Install and removed by
// Uninstall. 0.3.x embedded everything in the proxy; from 0.4.0 the runtime, backend and kernel
// packs are separate files under dlssg_sm86\, so they are part of the installed payload.
public sealed record PayloadDirectoryAsset(
    string Path,
    string Source,
    string Sha256,
    long Bytes,
    string? Note);

public sealed record PayloadVersion(
    string Version,
    string DisplayName,
    string SourceRoot,
    IReadOnlyDictionary<string, string> Templates,
    IReadOnlyList<PayloadEntryPoint> EntryPoints)
{
    // Multiplier ceiling the bundled runtime supports: the INI value is clamped to this.
    public int MaxGeneratedFrames { get; init; } = 5;

    // Files installed under the game folder besides the proxy and the INI.
    public IReadOnlyList<PayloadDirectoryAsset> DirectoryAssets { get; init; } = Array.Empty<PayloadDirectoryAsset>();

    // Hashes of earlier builds of this same payload line, kept only so an already-installed
    // proxy from a previous release is still recognised as ours (for upgrade and uninstall).
    // These files are not shipped and cannot be installed.
    public IReadOnlyList<string> LegacySha256 { get; init; } = Array.Empty<string>();

    // An archived release kept in the catalog purely for recognition: it has no installable
    // entry point, so it never appears in the install UI.
    public bool Installable => EntryPoints.Count > 0;

    // Relative path of the factory INI template, or null when this line declares none.
    public string? DefaultTemplate =>
        Templates.TryGetValue("default", out string? path) ? path : null;

    public PayloadEntryPoint? FindEntryPoint(string fileName) =>
        EntryPoints.FirstOrDefault(e => string.Equals(e.FileName, fileName, StringComparison.OrdinalIgnoreCase));
}
