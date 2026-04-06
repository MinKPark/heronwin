namespace HeronWin.Brain;

internal abstract record BrainYamlNode;

internal sealed record BrainYamlScalar(string Value) : BrainYamlNode;

internal sealed record BrainYamlSequence(IReadOnlyList<BrainYamlNode> Items) : BrainYamlNode;

internal sealed record BrainYamlMapping(IReadOnlyDictionary<string, BrainYamlNode> Entries) : BrainYamlNode
{
    public bool TryGetValue(string key, out BrainYamlNode value)
    {
        if (Entries.TryGetValue(key, out value!))
        {
            return true;
        }

        foreach (var entry in Entries)
        {
            if (!string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = entry.Value;
            return true;
        }

        value = default!;
        return false;
    }
}

internal static class BrainYamlParser
{
    public static BrainYamlNode Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("YAML content was empty.");
        }

        var lines = Tokenize(text);
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("YAML content did not contain any data.");
        }

        var index = 0;
        var root = ParseNode(lines, ref index, lines[0].Indent);
        if (index < lines.Count)
        {
            var line = lines[index];
            throw new InvalidOperationException(
                $"Unexpected trailing YAML content on line {line.Number}: {line.Content}");
        }

        return root;
    }

    private static BrainYamlNode ParseNode(IReadOnlyList<YamlLine> lines, ref int index, int indent)
    {
        if (index >= lines.Count)
        {
            throw new InvalidOperationException("Unexpected end of YAML content.");
        }

        var line = lines[index];
        if (line.Indent != indent)
        {
            throw new InvalidOperationException(
                $"Unexpected indentation on line {line.Number}. Expected {indent} spaces, got {line.Indent}.");
        }

        return line.Content.StartsWith("- ", StringComparison.Ordinal) || line.Content == "-"
            ? ParseSequence(lines, ref index, indent)
            : ParseMapping(lines, ref index, indent);
    }

    private static BrainYamlSequence ParseSequence(IReadOnlyList<YamlLine> lines, ref int index, int indent)
    {
        var items = new List<BrainYamlNode>();

        while (index < lines.Count)
        {
            var line = lines[index];
            if (line.Indent < indent)
            {
                break;
            }

            if (line.Indent > indent)
            {
                throw new InvalidOperationException(
                    $"Unexpected indentation on line {line.Number}: {line.Content}");
            }

            if (!line.Content.StartsWith("-", StringComparison.Ordinal))
            {
                break;
            }

            var itemText = line.Content.Length == 1
                ? string.Empty
                : line.Content[1..].TrimStart();

            if (itemText.Length == 0)
            {
                index += 1;
                items.Add(ParseNestedValueOrEmpty(lines, ref index, indent, line.Number));
                continue;
            }

            if (TrySplitMappingEntry(itemText, out var key, out var valueText))
            {
                var entries = new Dictionary<string, BrainYamlNode>(StringComparer.OrdinalIgnoreCase);
                index += 1;

                if (valueText.Length == 0)
                {
                    entries[key] = ParseNestedValueOrEmpty(lines, ref index, indent, line.Number);
                }
                else
                {
                    entries[key] = ParseScalar(valueText);
                }

                if (index < lines.Count && lines[index].Indent > indent)
                {
                    var siblingIndent = lines[index].Indent;
                    foreach (var entry in ParseMappingEntries(lines, ref index, siblingIndent))
                    {
                        entries[entry.Key] = entry.Value;
                    }
                }

                items.Add(new BrainYamlMapping(entries));
                continue;
            }

            items.Add(ParseScalar(itemText));
            index += 1;
        }

        return new BrainYamlSequence(items);
    }

    private static BrainYamlMapping ParseMapping(IReadOnlyList<YamlLine> lines, ref int index, int indent)
        => new(ParseMappingEntries(lines, ref index, indent));

    private static IReadOnlyDictionary<string, BrainYamlNode> ParseMappingEntries(
        IReadOnlyList<YamlLine> lines,
        ref int index,
        int indent)
    {
        var entries = new Dictionary<string, BrainYamlNode>(StringComparer.OrdinalIgnoreCase);

        while (index < lines.Count)
        {
            var line = lines[index];
            if (line.Indent < indent)
            {
                break;
            }

            if (line.Indent > indent)
            {
                throw new InvalidOperationException(
                    $"Unexpected indentation on line {line.Number}: {line.Content}");
            }

            if (line.Content.StartsWith("-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Unexpected YAML sequence item on line {line.Number}: {line.Content}");
            }

            if (!TrySplitMappingEntry(line.Content, out var key, out var valueText))
            {
                throw new InvalidOperationException(
                    $"Expected a YAML mapping entry on line {line.Number}: {line.Content}");
            }

            index += 1;
            entries[key] = valueText.Length == 0
                ? ParseNestedValueOrEmpty(lines, ref index, indent, line.Number)
                : ParseScalar(valueText);
        }

        return entries;
    }

    private static BrainYamlNode ParseNestedValueOrEmpty(
        IReadOnlyList<YamlLine> lines,
        ref int index,
        int parentIndent,
        int parentLineNumber)
    {
        if (index >= lines.Count || lines[index].Indent <= parentIndent)
        {
            return new BrainYamlScalar(string.Empty);
        }

        return ParseNode(lines, ref index, lines[index].Indent);
    }

    private static BrainYamlScalar ParseScalar(string rawValue)
    {
        var value = rawValue.Trim();
        if (value.Length >= 2)
        {
            if (value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1]
                    .Replace("\\\"", "\"", StringComparison.Ordinal)
                    .Replace("\\\\", "\\", StringComparison.Ordinal)
                    .Replace("\\n", "\n", StringComparison.Ordinal);
            }
            else if (value[0] == '\'' && value[^1] == '\'')
            {
                value = value[1..^1].Replace("''", "'", StringComparison.Ordinal);
            }
        }

        return new BrainYamlScalar(value);
    }

    private static bool TrySplitMappingEntry(string content, out string key, out string value)
    {
        var separatorIndex = FindMappingSeparator(content);
        if (separatorIndex <= 0)
        {
            key = string.Empty;
            value = string.Empty;
            return false;
        }

        key = content[..separatorIndex].Trim();
        value = content[(separatorIndex + 1)..].Trim();
        return key.Length > 0;
    }

    private static int FindMappingSeparator(string content)
    {
        var inSingleQuotes = false;
        var inDoubleQuotes = false;

        for (var index = 0; index < content.Length; index += 1)
        {
            var character = content[index];
            if (character == '\'' && !inDoubleQuotes)
            {
                inSingleQuotes = !inSingleQuotes;
                continue;
            }

            if (character == '"' && !inSingleQuotes)
            {
                inDoubleQuotes = !inDoubleQuotes;
                continue;
            }

            if (character != ':' || inSingleQuotes || inDoubleQuotes)
            {
                continue;
            }

            if (index + 1 < content.Length && !char.IsWhiteSpace(content[index + 1]))
            {
                continue;
            }

            return index;
        }

        return -1;
    }

    private static IReadOnlyList<YamlLine> Tokenize(string text)
    {
        var lines = new List<YamlLine>();
        var rawLines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (var index = 0; index < rawLines.Length; index += 1)
        {
            var rawLine = rawLines[index];
            if (rawLine.Contains('\t'))
            {
                throw new InvalidOperationException(
                    $"Tabs are not supported in YAML files. Replace the tab on line {index + 1} with spaces.");
            }

            var contentWithoutComments = StripComments(rawLine);
            if (string.IsNullOrWhiteSpace(contentWithoutComments))
            {
                continue;
            }

            var indent = rawLine.Length - rawLine.TrimStart(' ').Length;
            var trimmedContent = contentWithoutComments.Length >= indent
                ? contentWithoutComments[indent..].TrimEnd()
                : contentWithoutComments.Trim();
            lines.Add(new YamlLine(index + 1, indent, trimmedContent));
        }

        return lines;
    }

    private static string StripComments(string line)
    {
        var inSingleQuotes = false;
        var inDoubleQuotes = false;

        for (var index = 0; index < line.Length; index += 1)
        {
            var character = line[index];
            if (character == '\'' && !inDoubleQuotes)
            {
                inSingleQuotes = !inSingleQuotes;
                continue;
            }

            if (character == '"' && !inSingleQuotes)
            {
                inDoubleQuotes = !inDoubleQuotes;
                continue;
            }

            if (character == '#' && !inSingleQuotes && !inDoubleQuotes)
            {
                if (index == 0 || char.IsWhiteSpace(line[index - 1]))
                {
                    return line[..index];
                }
            }
        }

        return line;
    }

    private sealed record YamlLine(int Number, int Indent, string Content);
}

internal static class BrainCommandFileLoader
{
    public static IReadOnlyList<string> LoadFromFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Command file was not found.", fullPath);
        }

        var root = BrainYamlParser.Parse(File.ReadAllText(fullPath));
        var commands = ExtractCommands(root).ToArray();
        if (commands.Length == 0)
        {
            throw new InvalidOperationException($"Command file \"{fullPath}\" did not contain any runnable commands.");
        }

        return commands;
    }

    private static IEnumerable<string> ExtractCommands(BrainYamlNode root)
    {
        return root switch
        {
            BrainYamlSequence sequence => sequence.Items
                .OfType<BrainYamlScalar>()
                .Select(static item => item.Value.Trim())
                .Where(static item => item.Length > 0),
            BrainYamlMapping mapping when mapping.TryGetValue("commands", out var commandsNode) => ExtractCommands(commandsNode),
            _ => []
        };
    }
}
