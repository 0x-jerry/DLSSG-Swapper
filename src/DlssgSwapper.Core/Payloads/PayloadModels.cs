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
    Configuration.IniSchema Schema,
    string SourceRoot,
    IReadOnlyDictionary<string, string> Templates,
    IReadOnlyList<PayloadEntryPoint> EntryPoints)
{
    public PayloadEntryPoint? FindEntryPoint(string fileName) =>
        EntryPoints.FirstOrDefault(e => string.Equals(e.FileName, fileName, StringComparison.OrdinalIgnoreCase));
}