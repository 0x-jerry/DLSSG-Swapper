namespace DlssgSwapper.Core.Games;

public sealed record InstallRecord
{
    public string Version { get; init; } = "";
    public string EntryPoint { get; init; } = "";
    public string DllSha256 { get; init; } = "";
    public string IniSha256 { get; init; } = "";
    public DateTimeOffset InstalledAt { get; init; }
}