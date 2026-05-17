using System.Text.Json;

namespace HeronWin.Ava;

internal sealed record AvaDeterministicValidationContext(
    int StepNumber,
    string StepId,
    string ProfileId,
    string Checkpoint,
    AvaStepEvidenceReference EvidenceReference,
    IReadOnlyList<AvaEvidenceRecord> EvidenceRecords);

internal static class AvaDeterministicValidators
{
    private static readonly string[] TreePropertyNames = ["compactTree", "llmTree"];

    private static readonly string[] NamePropertyNames =
    [
        "name",
        "accessibleName",
        "automationName",
        "label",
        "title",
        "text"
    ];

    private static readonly string[] RolePropertyNames =
    [
        "role",
        "controlType",
        "localizedControlType"
    ];

    private static readonly string[] UiPathPropertyNames =
    [
        "uiPath",
        "path"
    ];

    private static readonly string[] AutomationIdPropertyNames =
    [
        "automationId",
        "id"
    ];

    private const int MaxNodeTraceSegments = 8;
    private const int LeadingNodeTraceSegments = 2;
    private const int TrailingNodeTraceSegments = 5;
    private const int MaxNodeTraceValueLength = 80;

    private static readonly string[] KeyboardFocusablePropertyNames =
    [
        "isKeyboardFocusable",
        "keyboardFocusable",
        "focusable",
        "canKeyboardFocus"
    ];

    private static readonly string[] InteractiveRoleTerms =
    [
        "button",
        "checkbox",
        "combo",
        "edit",
        "hyperlink",
        "link",
        "list item",
        "listitem",
        "menu item",
        "menuitem",
        "radio",
        "slider",
        "spinner",
        "splitbutton",
        "tab item",
        "tabitem",
        "text box",
        "textbox",
        "tree item",
        "treeitem"
    ];

    private static readonly string[] StrongActionRoleTerms =
    [
        "button",
        "checkbox",
        "combo",
        "hyperlink",
        "link",
        "menu item",
        "menuitem",
        "radio",
        "splitbutton"
    ];

    public static IReadOnlyList<AvaAccessibilityFinding> Validate(AvaDeterministicValidationContext context)
    {
        var findings = new List<AvaAccessibilityFinding>();

        AddEvidenceAvailabilityFindings(context, findings);
        AddEvidenceErrorFindings(context, findings);

        var capturedWindowRecords = context.EvidenceRecords
            .Where(static record => IsTool(record, "describe_window") && IsCaptured(record))
            .ToArray();
        var capturedFocusRecords = context.EvidenceRecords
            .Where(static record => IsTool(record, "describe_window_focus") && IsCaptured(record))
            .ToArray();

        if (capturedWindowRecords.Length > 0 && capturedFocusRecords.Length == 0)
        {
            findings.Add(CreateFinding(
                context,
                $"AVA-FOCUS-MISSING-{context.StepNumber:000}",
                AvaFindingStatus.NeedsReview,
                "Keyboard focus evidence was not captured for a step that has window tree evidence.",
                toolName: "describe_window_focus"));
        }

        foreach (var record in capturedWindowRecords.Concat(capturedFocusRecords))
        {
            InspectJsonTreeEvidence(context, record, findings);
        }

        return findings;
    }

    private static void AddEvidenceAvailabilityFindings(
        AvaDeterministicValidationContext context,
        List<AvaAccessibilityFinding> findings)
    {
        if (context.EvidenceRecords.Count == 0)
        {
            findings.Add(CreateFinding(
                context,
                $"AVA-EVIDENCE-MISSING-{context.StepNumber:000}",
                AvaFindingStatus.NotTested,
                "No deterministic evidence records were available for this step."));
            return;
        }

        var hasCapturedDeterministicRecord = context.EvidenceRecords.Any(static record =>
            IsCaptured(record) &&
            (IsTool(record, "describe_window") || IsTool(record, "describe_window_focus")));

        if (hasCapturedDeterministicRecord)
        {
            return;
        }

        var missingRecord = context.EvidenceRecords.FirstOrDefault(static record =>
            string.Equals(record.Status, AvaEvidenceStatus.Missing, StringComparison.Ordinal));
        if (missingRecord is null)
        {
            findings.Add(CreateFinding(
                context,
                $"AVA-TREE-EVIDENCE-MISSING-{context.StepNumber:000}",
                AvaFindingStatus.NotTested,
                "No captured UI tree evidence was available for this step.",
                toolName: context.EvidenceRecords.FirstOrDefault()?.ToolName));
            return;
        }

        findings.Add(CreateFinding(
            context,
            $"AVA-EVIDENCE-MISSING-{context.StepNumber:000}",
            AvaFindingStatus.NotTested,
            missingRecord.Summary ?? "Deterministic window evidence was not captured for this step.",
            toolName: missingRecord.ToolName));
    }

    private static void AddEvidenceErrorFindings(
        AvaDeterministicValidationContext context,
        List<AvaAccessibilityFinding> findings)
    {
        var sequence = 1;
        foreach (var record in context.EvidenceRecords.Where(static record =>
            string.Equals(record.Status, AvaEvidenceStatus.Error, StringComparison.Ordinal)))
        {
            findings.Add(CreateFinding(
                context,
                $"AVA-EVIDENCE-ERROR-{context.StepNumber:000}-{sequence:000}",
                AvaFindingStatus.NeedsReview,
                string.IsNullOrWhiteSpace(record.Error)
                    ? "Deterministic evidence collection returned an error."
                    : $"Deterministic evidence collection returned an error: {record.Error}",
                toolName: record.ToolName));
            sequence++;
        }
    }

    private static void InspectJsonTreeEvidence(
        AvaDeterministicValidationContext context,
        AvaEvidenceRecord record,
        List<AvaAccessibilityFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(record.RawOutput))
        {
            findings.Add(CreateFinding(
                context,
                $"AVA-TREE-PARSE-{context.StepNumber:000}-{ToolToken(record.ToolName)}",
                AvaFindingStatus.NeedsReview,
                "Captured tree evidence did not include raw JSON output.",
                toolName: record.ToolName));
            return;
        }

        using var document = TryParseJson(record.RawOutput, out var parseError);
        if (document is null)
        {
            findings.Add(CreateFinding(
                context,
                $"AVA-TREE-PARSE-{context.StepNumber:000}-{ToolToken(record.ToolName)}",
                AvaFindingStatus.NeedsReview,
                $"Captured tree evidence raw JSON could not be parsed: {parseError}",
                toolName: record.ToolName));
            return;
        }

        var treeRoots = TreePropertyNames
            .Select(propertyName => TryGetProperty(document.RootElement, propertyName, out var tree) ? tree : (JsonElement?)null)
            .Where(static tree => tree is not null)
            .Select(static tree => tree!.Value)
            .ToArray();

        if (treeRoots.Length == 0)
        {
            findings.Add(CreateFinding(
                context,
                $"AVA-TREE-MISSING-{context.StepNumber:000}-{ToolToken(record.ToolName)}",
                AvaFindingStatus.NeedsReview,
                "Captured tree evidence must include compactTree or llmTree.",
                toolName: record.ToolName));
            return;
        }

        var inspector = new TreeInspector(context, record, findings);
        foreach (var treeRoot in treeRoots)
        {
            inspector.InspectTreeRoot(treeRoot);
        }
    }

    private static JsonDocument? TryParseJson(string rawOutput, out string parseError)
    {
        try
        {
            parseError = string.Empty;
            return JsonDocument.Parse(rawOutput);
        }
        catch (JsonException ex)
        {
            parseError = ex.Message;
            return null;
        }
    }

    private static bool IsTool(AvaEvidenceRecord record, string toolName)
        => string.Equals(record.ToolName, toolName, StringComparison.Ordinal);

    private static bool IsCaptured(AvaEvidenceRecord record)
        => string.Equals(record.Status, AvaEvidenceStatus.Captured, StringComparison.Ordinal);

    private static AvaAccessibilityFinding CreateFinding(
        AvaDeterministicValidationContext context,
        string id,
        string status,
        string summary,
        string? toolName = null,
        string? nodeReference = null,
        string? nodeTrace = null)
    {
        var rule = AvaProfileCatalog.ResolveRule(context.ProfileId, id);
        return new AvaAccessibilityFinding(
            id,
            status,
            context.Checkpoint,
            summary,
            rule?.ProfileId ?? context.ProfileId,
            rule?.RuleId,
            rule?.SourceStandard,
            context.EvidenceReference.ManifestPath,
            context.StepId,
            toolName,
            nodeReference,
            nodeTrace);
    }

    private static string ToolToken(string toolName)
        => toolName.Replace('_', '-').ToUpperInvariant();

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

    private static bool TryGetFirstString(JsonElement element, IReadOnlyList<string> propertyNames, out string value)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryGetProperty(element, propertyName, out var property) &&
                property.ValueKind == JsonValueKind.String)
            {
                value = property.GetString()?.Trim() ?? string.Empty;
                if (value.Length > 0)
                {
                    return true;
                }
            }
        }

        value = string.Empty;
        return false;
    }

    private static bool TryGetFirstBoolean(JsonElement element, IReadOnlyList<string> propertyNames, out bool value)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(element, propertyName, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False)
            {
                value = property.GetBoolean();
                return true;
            }

            if (property.ValueKind == JsonValueKind.String &&
                bool.TryParse(property.GetString(), out value))
            {
                return true;
            }
        }

        value = false;
        return false;
    }

    private sealed class TreeInspector(
        AvaDeterministicValidationContext context,
        AvaEvidenceRecord record,
        List<AvaAccessibilityFinding> findings)
    {
        private int actionableNodeIndex;

        public void InspectTreeRoot(JsonElement treeRoot)
        {
            if (treeRoot.ValueKind == JsonValueKind.String)
            {
                InspectStringTreeRoot(treeRoot.GetString());
                return;
            }

            var nodeTrace = new List<string>();
            InspectElement(treeRoot, nodeTrace);
        }

        private void InspectStringTreeRoot(string? treeText)
        {
            if (string.IsNullOrWhiteSpace(treeText))
            {
                return;
            }

            var trimmed = treeText.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal) &&
                !trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                return;
            }

            using var nestedDocument = TryParseJson(trimmed, out _);
            if (nestedDocument is not null)
            {
                var nodeTrace = new List<string>();
                InspectElement(nestedDocument.RootElement, nodeTrace);
            }
        }

        private void InspectElement(JsonElement element, List<string> nodeTrace)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var addedTraceSegment = false;
                    if (TryBuildNodeTraceSegment(element, out var traceSegment))
                    {
                        nodeTrace.Add(traceSegment);
                        addedTraceSegment = true;
                    }

                    InspectNodeIfActionable(element, FormatNodeTrace(nodeTrace));
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        {
                            InspectElement(property.Value, nodeTrace);
                        }
                    }

                    if (addedTraceSegment)
                    {
                        nodeTrace.RemoveAt(nodeTrace.Count - 1);
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        InspectElement(item, nodeTrace);
                    }

                    break;
            }
        }

        private void InspectNodeIfActionable(JsonElement node, string? nodeTrace)
        {
            var hasActions = HasNonEmptyCollectionLikeProperty(node, "actions");
            var hasPatterns = HasNonEmptyCollectionLikeProperty(node, "patterns");
            var hasKeyboardFocusable = TryGetFirstBoolean(node, KeyboardFocusablePropertyNames, out var isKeyboardFocusable) &&
                isKeyboardFocusable;
            var hasRole = TryGetFirstString(node, RolePropertyNames, out var role);
            var hasInteractiveRole = hasRole && ContainsAnyTerm(role, InteractiveRoleTerms);

            if (!hasActions && !hasPatterns && !hasKeyboardFocusable && !hasInteractiveRole)
            {
                return;
            }

            actionableNodeIndex++;
            var nodeReference = $"actionable-{actionableNodeIndex:000}";

            if (!TryGetFirstString(node, NamePropertyNames, out _))
            {
                findings.Add(CreateFinding(
                    context,
                    $"AVA-NAME-MISSING-{context.StepNumber:000}-{ToolToken(record.ToolName)}-{actionableNodeIndex:000}",
                    AvaFindingStatus.Fail,
                    "Actionable UI node is missing an accessible name.",
                    record.ToolName,
                    nodeReference,
                    nodeTrace));
            }

            if (!hasRole)
            {
                findings.Add(CreateFinding(
                    context,
                    $"AVA-ROLE-MISSING-{context.StepNumber:000}-{ToolToken(record.ToolName)}-{actionableNodeIndex:000}",
                    AvaFindingStatus.Fail,
                    "Actionable UI node is missing a role or control type.",
                    record.ToolName,
                    nodeReference,
                    nodeTrace));
            }

            if (hasActions || hasPatterns)
            {
                return;
            }

            // Conservative actionability heuristic:
            // command-like controls normally need an automation pattern or explicit action to explain how they can be invoked.
            // Generic focusable controls can still be valid custom controls, so those are routed to review instead of failure.
            var status = hasInteractiveRole && ContainsAnyTerm(role, StrongActionRoleTerms)
                ? AvaFindingStatus.Fail
                : AvaFindingStatus.NeedsReview;
            findings.Add(CreateFinding(
                context,
                $"AVA-ACTION-MISSING-{context.StepNumber:000}-{ToolToken(record.ToolName)}-{actionableNodeIndex:000}",
                status,
                "Actionable UI node has no exposed control patterns or explicit actions.",
                record.ToolName,
                nodeReference,
                nodeTrace));
        }

        private static bool TryBuildNodeTraceSegment(JsonElement node, out string traceSegment)
        {
            var hasRole = TryGetFirstString(node, RolePropertyNames, out var role);
            var hasName = TryGetFirstString(node, NamePropertyNames, out var name);
            var hasAutomationId = TryGetFirstString(node, AutomationIdPropertyNames, out var automationId);
            var hasPath = TryGetTracePath(node, out var pathName, out var path);

            if (!hasRole && !hasName && !hasAutomationId && !hasPath)
            {
                traceSegment = string.Empty;
                return false;
            }

            var parts = new List<string>
            {
                hasRole ? CleanTraceValue(role) : "node"
            };

            if (hasName)
            {
                parts.Add($"\"{CleanTraceValue(name)}\"");
            }

            if (hasAutomationId)
            {
                parts.Add($"#{CleanTraceValue(automationId)}");
            }

            if (hasPath)
            {
                parts.Add($"[{pathName}={CleanTraceValue(path)}]");
            }

            traceSegment = string.Join(" ", parts);
            return true;
        }

        private static bool TryGetTracePath(JsonElement node, out string pathName, out string path)
        {
            foreach (var propertyName in UiPathPropertyNames)
            {
                if (TryGetProperty(node, propertyName, out var property) &&
                    property.ValueKind == JsonValueKind.String)
                {
                    path = property.GetString()?.Trim() ?? string.Empty;
                    if (path.Length > 0)
                    {
                        pathName = propertyName;
                        return true;
                    }
                }
            }

            pathName = string.Empty;
            path = string.Empty;
            return false;
        }

        private static string? FormatNodeTrace(IReadOnlyList<string> nodeTrace)
        {
            if (nodeTrace.Count == 0)
            {
                return null;
            }

            if (nodeTrace.Count <= MaxNodeTraceSegments)
            {
                return string.Join(" / ", nodeTrace);
            }

            var shortened = nodeTrace
                .Take(LeadingNodeTraceSegments)
                .Concat(["..."])
                .Concat(nodeTrace.Skip(nodeTrace.Count - TrailingNodeTraceSegments));
            return string.Join(" / ", shortened);
        }

        private static string CleanTraceValue(string value)
        {
            var normalized = string.Join(
                " ",
                value.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
            normalized = normalized.Replace('`', '\'');
            return normalized.Length <= MaxNodeTraceValueLength
                ? normalized
                : normalized[..(MaxNodeTraceValueLength - 3)] + "...";
        }

        private static bool HasNonEmptyCollectionLikeProperty(JsonElement node, string propertyName)
        {
            if (!TryGetProperty(node, propertyName, out var property))
            {
                return false;
            }

            return property.ValueKind switch
            {
                JsonValueKind.Array => property.EnumerateArray().Any(HasMeaningfulValue),
                JsonValueKind.Object => property.EnumerateObject().Any(static item => HasMeaningfulValue(item.Value)),
                JsonValueKind.String => IsMeaningfulString(property.GetString()),
                JsonValueKind.True => true,
                _ => false
            };
        }

        private static bool HasMeaningfulValue(JsonElement element)
            => element.ValueKind switch
            {
                JsonValueKind.String => IsMeaningfulString(element.GetString()),
                JsonValueKind.Object => element.EnumerateObject().Any(static property => HasMeaningfulValue(property.Value)),
                JsonValueKind.Array => element.EnumerateArray().Any(HasMeaningfulValue),
                JsonValueKind.True => true,
                JsonValueKind.Number => true,
                _ => false
            };

        private static bool IsMeaningfulString(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = value.Trim();
            return !string.Equals(normalized, "none", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(normalized, "[]", StringComparison.Ordinal);
        }

        private static bool ContainsAnyTerm(string value, IReadOnlyList<string> terms)
            => terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
