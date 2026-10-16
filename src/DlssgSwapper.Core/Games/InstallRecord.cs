namespace DlssgSwapper.Core.Games;

// A file the install wrote under the game folder besides the proxy and the INI. 0.4.0 keeps the
// runtime, backend and kernel packs in a dlssg_sm86\ tree, and recording their hashes is what lets
// Uninstall tell our files from a game's own or another mod's.
public sealed record InstalledFileRecord
{
    // Relative to the game directory, with '\' separators.
    public string Path { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public long Bytes { get; init; }
}

public sealed record InstallRecord
{
    public string Version { get; init; } = "";
    public string EntryPoint { get; init; } = "";
    public string DllSha256 { get; init; } = "";
    public string IniSha256 { get; init; } = "";
    public DateTimeOffset InstalledAt { get; init; }

    // Older records (0.3.x installs) have no manifest; treat that as an empty list.
    public IReadOnlyList<InstalledFileRecord> Files { get; init; } = Array.Empty<InstalledFileRecord>();
}
