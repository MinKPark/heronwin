using System.Text;
using System.Text.Json;

namespace HeronWin.HerFace;

internal static class UiSnapshotCompactor
{
    private static readonly HashSet<string> HighValueControlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Window",
        "Dialog",
        "Pane",
        "ToolBar",
        "Tab",
        "TabItem",
        "Button",
        "Edit",
        "ComboBox",
        "List",
        "ListItem",
        "Menu",
        "MenuItem",
        "Tree",
        "TreeItem",
        "Document"
    };

    private const int MaxIncludedNodes = 18;

    internal static string CompactToolTextForContext(
        string toolName,
        string toolText,
        LlmModelProfile modelProfile)
        => toolName switch
        {
            "describe_selected_window" => CompactSnapshot(toolText, modelProfile, focusMode: false),
            "describe_selected_window_focus" => CompactSnapshot(toolText, modelProfile, focusMode: true),
            _ => toolText
        };

    internal static string CompactSnapshot(
        string snapshotText,
        LlmModelProfile modelProfile,
        bool focusMode)
    {
        if (string.IsNullOrWhiteSpace(snapshotText))
        {
            return snapshotText;
        }

        var budget = focusMode
            ? modelProfile.FocusSnapshotCharBudget
            : modelProfile.WindowSnapshotCharBudget;
        if (snapshotText.Length <= budget)
        {
            return snapshotText;
        }

        try
        {
            using var document = JsonDocument.Parse(snapshotText);
            var lines = BuildCompactSummary(document.RootElement, snapshotText.Length, modelProfile, focusMode);
            return FitLinesToBudget(lines, budget);
        }
        catch
        {
            return TruncateRawSnapshot(snapshotText, budget, modelProfile.ModelName);
        }
    }

    private static IReadOnlyList<string> BuildCompactSummary(
        JsonElement root,
        int rawLength,
        LlmModelProfile modelProfile,
        bool focusMode)
    {
        var lines = new List<string>
        {
            $"UI snapshot compacted for {modelProfile.ModelName} to reduce token usage. Raw size: {rawLength:N0} chars."
        };

        if (TryGetJsonProperty(root, "window", out var window))
        {
            lines.Add($"Window: {DescribeWindow(window)}");
        }
        else if (TryGetJsonProperty(root, "selectedWindow", out var selectedWindow) &&
                 selectedWindow.ValueKind == JsonValueKind.Object)
        {
            lines.Add($"Selected window: {DescribeWindow(selectedWindow)}");
        }

        if (TryGetJsonProperty(root, "focusedElement", out var focusedElement) &&
            focusedElement.ValueKind == JsonValueKind.Object)
        {
            lines.Add($"Focused element: {DescribeElement(focusedElement)}");
        }

        if (TryGetJsonProperty(root, "elementTree", out var elementTree) &&
            elementTree.ValueKind == JsonValueKind.Object)
        {
            var totalNodes = CountNodes(elementTree);
            var selectedLines = new List<string>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectImportantElements(
                elementTree,
                depth: 0,
                focusMode,
                selectedLines,
                seenPaths);

            lines.Add($"Key UI elements ({selectedLines.Count} of {totalNodes} nodes kept):");
            lines.AddRange(selectedLines.Select(line => $"- {line}"));

            var omittedNodes = Math.Max(0, totalNodes - selectedLines.Count);
            if (omittedNodes > 0)
            {
                lines.Add(
                    $"Omitted {omittedNodes} lower-value nodes. If this is still too sparse, call describe_selected_window again or capture_selected_window_screenshot.");
            }
        }
        else
        {
            lines.Add("No elementTree was available in this snapshot.");
        }

        return lines;
    }

    private static void CollectImportantElements(
        JsonElement element,
        int depth,
        bool focusMode,
        List<string> selectedLines,
        HashSet<string> seenPaths)
    {
        if (selectedLines.Count >= MaxIncludedNodes || element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (ShouldIncludeElement(element, depth, focusMode))
        {
            var path = TryGetJsonStringProperty(element, "uiPath")
                       ?? TryGetJsonStringProperty(element, "path")
                       ?? "root";
            if (seenPaths.Add(path))
            {
                selectedLines.Add(DescribeElement(element));
            }
        }

        if (!TryGetJsonProperty(element, "children", out var children) ||
            children.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var child in children.EnumerateArray())
        {
            if (selectedLines.Count >= MaxIncludedNodes)
            {
                break;
            }

            CollectImportantElements(child, depth + 1, focusMode, selectedLines, seenPaths);
        }
    }

    private static bool ShouldIncludeElement(JsonElement element, int depth, bool focusMode)
    {
        if (depth == 0)
        {
            return true;
        }

        var name = TryGetJsonStringProperty(element, "name");
        var controlType = TryGetJsonStringProperty(element, "controlType");
        var className = TryGetJsonStringProperty(element, "className");
        var hasKeyboardFocus = TryGetJsonBooleanProperty(element, "hasKeyboardFocus") == true;
        var isSelected = TryGetJsonBooleanProperty(element, "isSelected") == true;
        var isKeyboardFocusable = TryGetJsonBooleanProperty(element, "isKeyboardFocusable") == true;
        var actions = GetActions(element);
        var hasInterestingAction = actions.Any(action => !string.Equals(action, "scroll_into_view", StringComparison.OrdinalIgnoreCase));
        var browserChrome = LooksLikeBrowserChrome(name, controlType, className);

        if (hasKeyboardFocus || isSelected || browserChrome)
        {
            return true;
        }

        if (depth <= 1 && (!string.IsNullOrWhiteSpace(name) || hasInterestingAction))
        {
            return true;
        }

        if (depth <= 2 &&
            (HighValueControlTypes.Contains(controlType ?? string.Empty) || isKeyboardFocusable) &&
            (!string.IsNullOrWhiteSpace(name) || hasInterestingAction))
        {
            return true;
        }

        return focusMode &&
               depth <= 3 &&
               isKeyboardFocusable &&
               hasInterestingAction;
    }

    private static string DescribeWindow(JsonElement window)
    {
        var title = TryGetJsonStringProperty(window, "title");
        var handle = TryGetJsonStringProperty(window, "handle");
        var className = TryGetJsonStringProperty(window, "className");
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(title))
        {
            parts.Add(title);
        }

        if (!string.IsNullOrWhiteSpace(handle))
        {
            parts.Add(handle);
        }

        if (!string.IsNullOrWhiteSpace(className))
        {
            parts.Add($"class={className}");
        }

        return parts.Count == 0 ? "(unlabeled window)" : string.Join("; ", parts);
    }

    private static string DescribeElement(JsonElement element)
    {
        var path = TryGetJsonStringProperty(element, "uiPath")
                   ?? TryGetJsonStringProperty(element, "path")
                   ?? "root";
        var controlType = TryGetJsonStringProperty(element, "controlType") ?? "Element";
        var name = NormalizeInlineText(TryGetJsonStringProperty(element, "name"));
        var className = NormalizeInlineText(TryGetJsonStringProperty(element, "className"));
        var actions = GetActions(element)
            .Where(action => !string.Equals(action, "scroll_into_view", StringComparison.OrdinalIgnoreCase))
            .Take(4)
            .ToArray();

        var description = new StringBuilder();
        description.Append(path);
        description.Append(": ");
        description.Append(controlType);
        if (!string.IsNullOrWhiteSpace(name))
        {
            description.Append(" \"");
            description.Append(name);
            description.Append('"');
        }

        var flags = new List<string>();
        if (!string.IsNullOrWhiteSpace(className) && LooksLikeBrowserChrome(name, controlType, className))
        {
            flags.Add($"class={className}");
        }

        if (TryGetJsonBooleanProperty(element, "hasKeyboardFocus") == true)
        {
            flags.Add("focused");
        }

        if (TryGetJsonBooleanProperty(element, "isSelected") == true)
        {
            flags.Add("selected");
        }

        if (actions.Length > 0)
        {
            flags.Add($"actions={string.Join(",", actions)}");
        }

        if (flags.Count > 0)
        {
            description.Append("; ");
            description.Append(string.Join("; ", flags));
        }

        return description.ToString();
    }

    private static int CountNodes(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        var total = 1;
        if (!TryGetJsonProperty(element, "children", out var children) ||
            children.ValueKind != JsonValueKind.Array)
        {
            return total;
        }

        foreach (var child in children.EnumerateArray())
        {
            total += CountNodes(child);
        }

        return total;
    }

    private static string FitLinesToBudget(IReadOnlyList<string> lines, int budget)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var pending = builder.Length == 0 ? line : $"{Environment.NewLine}{line}";
            if (builder.Length + pending.Length > budget)
            {
                var suffix = $"{Environment.NewLine}[Further condensed to stay within the active model budget.]";
                if (builder.Length + suffix.Length <= budget)
                {
                    builder.Append(suffix);
                }

                break;
            }

            builder.Append(pending);
        }

        return builder.Length == 0
            ? "[UI snapshot omitted because it exceeded the active model budget.]"
            : builder.ToString();
    }

    private static string TruncateRawSnapshot(string snapshotText, int budget, string modelName)
    {
        const string suffixTemplate = "\n[Raw snapshot truncated for {0} to reduce token usage.]";
        var suffix = string.Format(suffixTemplate, modelName);
        if (budget <= suffix.Length + 1)
        {
            return suffix.Trim();
        }

        var keepLength = Math.Max(1, budget - suffix.Length);
        return snapshotText[..Math.Min(keepLength, snapshotText.Length)] + suffix;
    }

    private static IReadOnlyList<string> GetActions(JsonElement element)
    {
        if (!TryGetJsonProperty(element, "availableActions", out var actions) ||
            actions.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return actions
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool LooksLikeBrowserChrome(string? name, string? controlType, string? className)
    {
        if (!string.IsNullOrWhiteSpace(className) &&
            (className.Contains("Omnibox", StringComparison.OrdinalIgnoreCase)
             || className.Contains("EdgeTab", StringComparison.OrdinalIgnoreCase)
             || className.Contains("EdgeToolbar", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var normalizedName = name?.Trim() ?? string.Empty;
        if (normalizedName.Length == 0)
        {
            return string.Equals(controlType, "Tab", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(controlType, "TabItem", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(controlType, "ToolBar", StringComparison.OrdinalIgnoreCase);
        }

        return normalizedName.Contains("address and search bar", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Contains("new tab", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Contains("tab bar", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Contains("search tabs", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Equals("Back", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Equals("Forward", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Equals("Refresh", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Equals("Reload", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Equals("Home", StringComparison.OrdinalIgnoreCase)
               || normalizedName.Contains("site information", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetJsonStringProperty(JsonElement element, string propertyName)
    {
        if (!TryGetJsonProperty(element, propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static bool? TryGetJsonBooleanProperty(JsonElement element, string propertyName)
    {
        if (!TryGetJsonProperty(element, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static bool TryGetJsonProperty(JsonElement element, string propertyName, out JsonElement property)
    {
        property = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (element.TryGetProperty(propertyName, out property))
        {
            return true;
        }

        foreach (var candidate in element.EnumerateObject())
        {
            if (!string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            property = candidate.Value.Clone();
            return true;
        }

        return false;
    }

    private static string? NormalizeInlineText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ");
        if (normalized.Length <= 80)
        {
            return normalized;
        }

        return normalized[..77].TrimEnd() + "...";
    }
}
