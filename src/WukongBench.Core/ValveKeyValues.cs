using System.Text;

namespace WukongBench.Core;

/// <summary>Valve KeyValues: nested objects, quoted/bare tokens, escapes and // comments.</summary>
public sealed class ValveKeyValues
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ValveKeyValues> Objects { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static ValveKeyValues Parse(string source)
    {
        var tokens = Tokenize(source).ToArray();
        var position = 0;
        ValveKeyValues Read(bool nested)
        {
            var node = new ValveKeyValues();
            while (position < tokens.Length)
            {
                var key = tokens[position++];
                if (key == "}")
                {
                    if (!nested) throw new FormatException("Unexpected closing brace in VDF.");
                    return node;
                }
                if (key == "{" || position == tokens.Length) throw new FormatException("Invalid VDF key/value.");
                var value = tokens[position++];
                if (value == "{") node.Objects.Add(key, Read(true));
                else if (value == "}") throw new FormatException("Missing VDF value.");
                else node.Values.Add(key, value);
            }
            if (nested) throw new FormatException("Unclosed VDF object.");
            return node;
        }
        return Read(false);
    }

    private static IEnumerable<string> Tokenize(string source)
    {
        for (var i = 0; i < source.Length;)
        {
            if (char.IsWhiteSpace(source[i]) || source[i] == '\uFEFF') { i++; continue; }
            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '/')
            { while (i < source.Length && source[i] != '\n') i++; continue; }
            if (source[i] is '{' or '}') { yield return source[i++].ToString(); continue; }
            var value = new StringBuilder();
            if (source[i] == '"')
            {
                i++;
                while (i < source.Length && source[i] != '"')
                {
                    if (source[i] == '\\' && i + 1 < source.Length && source[i + 1] is '\\' or '"') i++;
                    value.Append(source[i++]);
                }
                if (i == source.Length) throw new FormatException("Unterminated VDF string.");
                i++;
            }
            else while (i < source.Length && !char.IsWhiteSpace(source[i]) && source[i] is not '{' and not '}')
                value.Append(source[i++]);
            yield return value.ToString();
        }
    }
}
