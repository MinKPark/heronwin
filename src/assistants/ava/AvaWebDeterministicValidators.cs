using System.Text.Json;

namespace HeronWin.Ava;

internal static class AvaWebDeterministicValidators
{
    private static readonly HashSet<string> InteractiveRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "button",
        "checkbox",
        "combobox",
        "link",
        "menuitem",
        "option",
        "radio",
        "searchbox",
        "slider",
        "spinbutton",
        "switch",
        "tab",
        "textbox",
        "treeitem"
    };

    public static IReadOnlyList<AvaAccessibilityFinding> Validate(AvaDeterministicValidationContext context)
    {
        var findings = new List<AvaAccessibilityFinding>();
        var records = context.EvidenceRecords
            .Where(static record => string.Equals(record.ToolName, "web_dom_snapshot", StringComparison.Ordinal) &&
                string.Equals(record.Status, AvaEvidenceStatus.Captured, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(record.RawOutput))
            .ToArray();

        foreach (var record in records)
        {
            InspectCdpEvidence(context, record, findings);
        }

        return findings;
    }

    private static void InspectCdpEvidence(
        AvaDeterministicValidationContext context,
        AvaEvidenceRecord record,
        List<AvaAccessibilityFinding> findings)
    {
        using var document = TryParseJson(record.RawOutput, out _);
        if (document is null ||
            !TryGetNestedProperty(document.RootElement, ["accessibilityTree", "result", "nodes"], out var nodes) ||
            nodes.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var actionableIndex = 0;
        foreach (var node in nodes.EnumerateArray())
        {
            if (IsIgnored(node) ||
                !TryGetAxValue(node, "role", out var role) ||
                !IsPotentiallyActionableWebNode(node, role))
            {
                continue;
            }

            actionableIndex++;
            var nodeId = TryGetStringProperty(node, "nodeId", out var foundNodeId)
                ? foundNodeId
                : $"ax-{actionableIndex:000}";
            var name = TryGetAxValue(node, "name", out var foundName)
                ? foundName
                : null;
            if (!RequiresAccessibleName(role) || !string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            findings.Add(CreateFinding(
                context,
                $"AVA-NAME-MISSING-{context.StepNumber:000}-WEB-DOM-SNAPSHOT-{actionableIndex:000}",
                AvaFindingStatus.Fail,
                "Interactive web node is missing an accessible name.",
                record.ToolName,
                nodeId,
                FormatNodeTrace(role, name, nodeId),
                FormatWebProperties(node)));
        }
    }

    private static bool IsPotentiallyActionableWebNode(JsonElement node, string role)
        => InteractiveRoles.Contains(role) || HasBooleanProperty(node, "focusable", expectedValue: true);

    private static bool RequiresAccessibleName(string role)
        => InteractiveRoles.Contains(role);

    private static bool IsIgnored(JsonElement node)
        => TryGetProperty(node, "ignored", out var ignored) &&
            ignored.ValueKind is JsonValueKind.True;

    private static string FormatNodeTrace(string role, string? name, string? nodeId)
    {
        var label = string.IsNullOrWhiteSpace(name)
            ? role
            : $"{role} \"{name}\"";
        return string.IsNullOrWhiteSpace(nodeId)
            ? $"Web {label}"
            : $"Web {label} (AX node {nodeId})";
    }

    private static string? FormatWebProperties(JsonElement node)
    {
        var properties = new List<string>();
        if (TryGetAxValue(node, "role", out var role))
        {
            properties.Add($"computed-role: {role}");
        }

        if (HasBooleanProperty(node, "focusable", expectedValue: true))
        {
            properties.Add("focusable: true");
        }

        return properties.Count == 0 ? null : string.Join("; ", properties);
    }

    private static AvaAccessibilityFinding CreateFinding(
        AvaDeterministicValidationContext context,
        string id,
        string status,
        string summary,
        string? toolName,
        string? nodeReference,
        string? nodeTrace,
        string? ariaProperties)
    {
        var rule = AvaProfileCatalog.ResolveRule(AvaProfileIds.FederalWebMin, id);
        return new AvaAccessibilityFinding(
            id,
            status,
            context.Checkpoint,
            summary,
            rule?.ProfileId ?? AvaProfileIds.FederalWebMin,
            rule?.RuleId,
            rule?.SourceStandard,
            context.EvidenceReference.ManifestPath,
            context.StepId,
            toolName,
            nodeReference,
            nodeTrace,
            automationId: null,
            ariaProperties);
    }

    private static bool TryGetAxValue(JsonElement node, string propertyName, out string value)
    {
        if (TryGetProperty(node, propertyName, out var directValue) &&
            TryGetAxValueString(directValue, out value))
        {
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryGetAxValueString(JsonElement element, out string value)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString()?.Trim() ?? string.Empty;
            return value.Length > 0;
        }

        if (element.ValueKind == JsonValueKind.Object &&
            TryGetProperty(element, "value", out var wrappedValue) &&
            wrappedValue.ValueKind == JsonValueKind.String)
        {
            value = wrappedValue.GetString()?.Trim() ?? string.Empty;
            return value.Length > 0;
        }

        value = string.Empty;
        return false;
    }

    private static bool HasBooleanProperty(JsonElement node, string propertyName, bool expectedValue)
    {
        if (!TryGetProperty(node, "properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var property in properties.EnumerateArray())
        {
            if (!TryGetStringProperty(property, "name", out var name) ||
                !string.Equals(name, propertyName, StringComparison.OrdinalIgnoreCase) ||
                !TryGetProperty(property, "value", out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Object &&
                TryGetProperty(value, "value", out var wrappedValue))
            {
                value = wrappedValue;
            }

            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                value.GetBoolean() == expectedValue)
            {
                return true;
            }
        }

        return false;
    }

    private static JsonDocument? TryParseJson(string? rawOutput, out string parseError)
    {
        try
        {
            parseError = string.Empty;
            return string.IsNullOrWhiteSpace(rawOutput)
                ? null
                : JsonDocument.Parse(rawOutput);
        }
        catch (JsonException ex)
        {
            parseError = ex.Message;
            return null;
        }
    }

    private static bool TryGetNestedProperty(JsonElement element, IReadOnlyList<string> propertyPath, out JsonElement value)
    {
        value = element;
        foreach (var propertyName in propertyPath)
        {
            if (!TryGetProperty(value, propertyName, out value))
            {
                value = default;
                return false;
            }
        }

        return true;
    }

    private static bool TryGetStringProperty(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!TryGetProperty(element, propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString()?.Trim() ?? string.Empty;
        return value.Length > 0;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
