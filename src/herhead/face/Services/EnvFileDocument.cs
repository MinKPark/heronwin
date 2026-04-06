using System.IO;

namespace HeronWin.Face.Services;

public sealed class EnvFileDocument
{
    private readonly List<EnvFileLine> _lines;

    private EnvFileDocument(List<EnvFileLine> lines)
    {
        _lines = lines;
    }

    public static EnvFileDocument Load(string path)
    {
        if (!File.Exists(path))
        {
            return new EnvFileDocument([]);
        }

        var lines = File.ReadAllLines(path)
            .Select(ParseLine)
            .ToList();
        return new EnvFileDocument(lines);
    }

    public string GetValue(string key, string fallback = "")
    {
        var entry = _lines.LastOrDefault(line => line.Kind == EnvFileLineKind.KeyValue &&
                                                 string.Equals(line.Key, key, StringComparison.OrdinalIgnoreCase));
        return entry?.Value ?? fallback;
    }

    public void SetValue(string key, string? value)
    {
        var normalizedValue = value?.Trim() ?? string.Empty;
        var existingIndex = _lines.FindLastIndex(line => line.Kind == EnvFileLineKind.KeyValue &&
                                                         string.Equals(line.Key, key, StringComparison.OrdinalIgnoreCase));
        var renderedValue = normalizedValue.Contains(' ') || normalizedValue.Contains('"')
            ? $"\"{normalizedValue.Replace("\"", "\\\"")}\""
            : normalizedValue;

        if (existingIndex >= 0)
        {
            _lines[existingIndex] = new EnvFileLine(EnvFileLineKind.KeyValue, key, normalizedValue, $"{key}={renderedValue}");
            return;
        }

        _lines.Add(new EnvFileLine(EnvFileLineKind.KeyValue, key, normalizedValue, $"{key}={renderedValue}"));
    }

    public string Render()
    {
        return string.Join(Environment.NewLine, _lines.Select(line => line.Rendered));
    }

    public async Task SaveAsync(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, Render());
    }

    private static EnvFileLine ParseLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0)
        {
            return new EnvFileLine(EnvFileLineKind.Blank, null, null, string.Empty);
        }

        if (trimmed.StartsWith('#'))
        {
            return new EnvFileLine(EnvFileLineKind.Comment, null, null, line);
        }

        var separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0)
        {
            return new EnvFileLine(EnvFileLineKind.Raw, null, null, line);
        }

        var key = line[..separatorIndex].Trim();
        var value = line[(separatorIndex + 1)..].Trim();
        if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
        {
            value = value[1..^1].Replace("\\\"", "\"");
        }

        return new EnvFileLine(EnvFileLineKind.KeyValue, key, value, line);
    }

    private sealed record EnvFileLine(EnvFileLineKind Kind, string? Key, string? Value, string Rendered);

    private enum EnvFileLineKind
    {
        Blank,
        Comment,
        KeyValue,
        Raw
    }
}