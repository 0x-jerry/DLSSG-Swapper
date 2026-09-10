using System.Text.Json;

namespace DlssgSwapper.Core.Games;

public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly string _path;

    public ProfileStore(string path)
    {
        _path = path;
        Profiles = File.Exists(path)
            ? JsonSerializer.Deserialize<List<GameProfile>>(File.ReadAllText(path), JsonOptions) ?? new()
            : new List<GameProfile>();
    }

    public List<GameProfile> Profiles { get; }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DlssgSwapper", "profiles.json");

    public static ProfileStore OpenDefault() => new(DefaultPath);

    public GameProfile? Find(Guid id) => Profiles.FirstOrDefault(p => p.Id == id);

    public void Add(GameProfile profile)
    {
        Profiles.Add(profile);
        Save();
    }

    public void Update(GameProfile profile)
    {
        int index = Profiles.FindIndex(p => p.Id == profile.Id);
        if (index < 0) throw new InvalidOperationException($"Profile {profile.Id} not found");
        Profiles[index] = profile;
        Save();
    }

    public bool Remove(Guid id)
    {
        int removed = Profiles.RemoveAll(p => p.Id == id);
        if (removed > 0) Save();
        return removed > 0;
    }

    public void Save()
    {
        string? dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(Profiles, JsonOptions));
    }
}