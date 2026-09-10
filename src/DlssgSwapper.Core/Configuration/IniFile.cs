
namespace DlssgSwapper.Core.Configuration;

// Line-based INI reader/writer that preserves comments, blank lines, ordering,
// and unknown keys. Only keys passed to Set are touched; sections are never duplicated.
public sealed class IniFile
{
    private readonly List<IniLine> _lines = new();

    private IniFile(string text)
    {
        string currentSection = "";
        foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            string trimmed = raw.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                currentSection = trimmed[1..^1].Trim();
                _lines.Add(new SectionLine(currentSection));
            }
            else if (currentSection.Length > 0 && !trimmed.StartsWith(';')
                     && !trimmed.StartsWith('#') && trimmed.Length > 0
                     && TrySplit(trimmed, out string key, out string value))
            {
                _lines.Add(new KeyValueLine(currentSection, key, value));
            }
            else
            {
                _lines.Add(new RawLine(raw));
            }
        }
    }

    public static IniFile Load(string path) => new(File.ReadAllText(path));
    public static IniFile Parse(string text) => new(text);

    public string? Get(string section, string key)
    {
        foreach (var line in _lines)
        {
            if (line is KeyValueLine kv
                && string.Equals(kv.Section, section, StringComparison.OrdinalIgnoreCase)
                && string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }
        return null;
    }

    public void Set(string section, string key, string value)
    {
        for (int i = 0; i < _lines.Count; i++)
        {
            if (_lines[i] is KeyValueLine kv
                && string.Equals(kv.Section, section, StringComparison.OrdinalIgnoreCase)
                && string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                _lines[i] = new KeyValueLine(kv.Section, kv.Key, value);
                return;
            }
        }

        int insertAt = _lines.Count;
        int? sectionIndex = null;
        for (int i = 0; i < _lines.Count; i++)
        {
            if (_lines[i] is SectionLine sl && string.Equals(sl.Name, section, StringComparison.OrdinalIgnoreCase))
            {
                sectionIndex = i;
                break;
            }
        }

        if (sectionIndex is int si)
        {
            int j = si + 1;
            while (j < _lines.Count && _lines[j] is not SectionLine) j++;
            insertAt = j;
        }
        else
        {
            _lines.Add(new SectionLine(section));
            insertAt = _lines.Count;
        }
        _lines.Insert(insertAt, new KeyValueLine(section, key, value));
    }

    public string Render()
    {
        var parts = new List<string>(_lines.Count);
        foreach (var line in _lines)
        {
            switch (line)
            {
                case SectionLine s:
                    parts.Add($"[{s.Name}]");
                    break;
                case KeyValueLine kv:
                    parts.Add($"{kv.Key}={kv.Value}");
                    break;
                case RawLine r:
                    parts.Add(r.Text);
                    break;
            }
        }
        return string.Join(Environment.NewLine, parts);
    }

    public void Save(string path) => File.WriteAllText(path, Render());

    private static bool TrySplit(string line, out string key, out string value)
    {
        int eq = line.IndexOf('=');
        if (eq <= 0)
        {
            key = "";
            value = "";
            return false;
        }
        key = line[..eq].Trim();
        value = line[(eq + 1)..].Trim();
        return key.Length > 0;
    }

    private abstract record IniLine;
    private sealed record SectionLine(string Name) : IniLine;
    private sealed record KeyValueLine(string Section, string Key, string Value) : IniLine;
    private sealed record RawLine(string Text) : IniLine;
}