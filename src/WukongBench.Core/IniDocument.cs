using System.Text.RegularExpressions;

namespace WukongBench.Core;

/// <summary>Preserves unrelated lines, comments and values; rejects ambiguous duplicate keys.</summary>
public sealed class IniDocument(string text)
{
    private readonly List<string> lines = text.Replace("\r\n", "\n").Split('\n').ToList();

    private (int Start, int End) Section(string name, bool create)
    {
        var hits = lines.Select((line, i) => (line, i)).Where(x => x.line.Trim() == $"[{name}]").ToArray();
        if (hits.Length > 1) throw new FormatException($"Duplicate INI section: {name}");
        if (hits.Length == 0)
        {
            if (!create) throw new FormatException($"Missing INI section: {name}");
            lines.Add($"[{name}]");
            return (lines.Count - 1, lines.Count);
        }
        var start = hits[0].i;
        var end = lines.FindIndex(start + 1, l => l.TrimStart().StartsWith('['));
        return (start, end < 0 ? lines.Count : end);
    }

    public string? Get(string section, string key)
    {
        var (start, end) = Section(section, false);
        var matches = lines.Skip(start + 1).Take(end - start - 1)
            .Select(l => l.Split('=', 2)).Where(a => a.Length == 2 && a[0].Trim() == key).ToArray();
        if (matches.Length > 1) throw new FormatException($"Duplicate INI key: {key}");
        return matches.FirstOrDefault()?[1].Trim();
    }

    public void Set(string section, string key, string value)
    {
        var (start, end) = Section(section, true);
        var indices = Enumerable.Range(start + 1, end - start - 1)
            .Where(i => lines[i].Split('=', 2)[0].Trim() == key).ToArray();
        if (indices.Length > 1) throw new FormatException($"Duplicate INI key: {key}");
        if (indices.Length == 1) lines[indices[0]] = $"{key}={value}";
        else lines.Insert(end, $"{key}={value}");
    }

    public void SetUi(string section, IReadOnlyDictionary<string, string> settings)
    {
        var ui = Get(section, "UISettingData") ?? throw new FormatException("Нет UISettingData: неподдерживаемая версия конфигурации.");
        foreach (var (key, value) in settings)
        {
            var regex = new Regex("\\(\\s*\"" + Regex.Escape(key) + "\"\\s*,\\s*\"[^\"]*\"\\s*\\)", RegexOptions.CultureInvariant);
            if (regex.Matches(ui).Count != 1) throw new FormatException($"Нет однозначного UISettingData.{key}; конфигурация не поддерживается.");
            ui = regex.Replace(ui, _ => $"(\"{key}\",\"{value}\")");
        }
        Set(section, "UISettingData", ui);
    }

    public override string ToString() => string.Join("\r\n", lines);
}
