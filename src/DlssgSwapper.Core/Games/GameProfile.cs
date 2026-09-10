using DlssgSwapper.Core.Configuration;

namespace DlssgSwapper.Core.Games;

public enum GameSource
{
    Manual,
    Steam,
}

public sealed record GameProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public string ExecutablePath { get; init; } = "";
    public GameSource Source { get; init; } = GameSource.Manual;
    public FrameGenSettings? Settings { get; init; }
    public string? PayloadVersion { get; init; }
    public string? PayloadEntryPoint { get; init; }

    public string GameDirectory =>
        Path.GetDirectoryName(ExecutablePath) ?? throw new InvalidOperationException("Game executable has no directory");

    public static GameProfile Create(string name, string executablePath, GameSource source) =>
        new() { Name = name, ExecutablePath = executablePath, Source = source };
}