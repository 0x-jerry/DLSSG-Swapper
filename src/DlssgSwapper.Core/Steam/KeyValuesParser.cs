using System.Text;

namespace DlssgSwapper.Core.Steam;

// Minimal parser for Valve's KeyValues format: "key" "value" pairs and
// nested "key" { ... } blocks, with // and /* */ comments.
public sealed class KeyValueNode
{
    public KeyValueNode(string name) => Name = name;

    public string Name { get; }
    public string? Value { get; set; }
    public List<KeyValueNode> Children { get; } = new();

    public string? GetValue(string name) =>
        Children.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    public KeyValueNode? Find(string name) =>
        Children.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}

public static class KeyValuesParser
{
    public static KeyValueNode Parse(string text)
    {
        int pos = 0;
        var root = new KeyValueNode("<root>");
        ParseBlock(text, ref pos, root);
        return root;
    }

    private static void ParseBlock(string text, ref int pos, KeyValueNode parent)
    {
        while (true)
        {
            SkipWhitespaceAndComments(text, ref pos);
            if (pos >= text.Length) return;
            if (text[pos] == '}')
            {
                pos++;
                return;
            }

            string key = ReadToken(text, ref pos);
            SkipWhitespaceAndComments(text, ref pos);
            if (pos < text.Length && text[pos] == '{')
            {
                pos++;
                var child = new KeyValueNode(key);
                ParseBlock(text, ref pos, child);
                parent.Children.Add(child);
            }
            else
            {
                string value = ReadToken(text, ref pos);
                if (value.Length == 0) return;
                parent.Children.Add(new KeyValueNode(key) { Value = value });
            }
        }
    }

    private static string ReadToken(string text, ref int pos)
    {
        if (pos < text.Length && text[pos] == '"')
            return ReadQuoted(text, ref pos);

        int start = pos;
        while (pos < text.Length && !char.IsWhiteSpace(text[pos]) && text[pos] != '{' && text[pos] != '}')
            pos++;
        return text[start..pos];
    }

    private static string ReadQuoted(string text, ref int pos)
    {
        pos++; // opening quote
        var sb = new StringBuilder();
        while (pos < text.Length)
        {
            char c = text[pos++];
            if (c == '\\' && pos < text.Length)
            {
                char next = text[pos++];
                sb.Append(next switch
                {
                    '"' => '"',
                    '\\' => '\\',
                    'n' => '\n',
                    't' => '\t',
                    _ => next,
                });
            }
            else if (c == '"')
            {
                break;
            }
            else if (c is '\r' or '\n')
            {
                break; // unterminated string; stop cleanly
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static void SkipWhitespaceAndComments(string text, ref int pos)
    {
        while (pos < text.Length)
        {
            char c = text[pos];
            if (char.IsWhiteSpace(c)) { pos++; continue; }
            if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '/')
            {
                pos += 2;
                while (pos < text.Length && text[pos] != '\n') pos++;
                continue;
            }
            if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '*')
            {
                pos += 2;
                while (pos + 1 < text.Length && !(text[pos] == '*' && text[pos + 1] == '/')) pos++;
                pos = Math.Min(pos + 2, text.Length);
                continue;
            }
            break;
        }
    }
}