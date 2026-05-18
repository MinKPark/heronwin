using System.Text;
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
    private static readonly string[] FallbackTreePropertyNames = ["compactTree"];

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

    private static readonly string[] AutomationIdPropertyNames =
    [
        "automationId",
        "id"
    ];

    private static readonly string[] ActionPropertyNames =
    [
        "actions",
        "availableActions"
    ];

    private static readonly string[] PatternPropertyNames =
    [
        "patterns",
        "availablePatterns"
    ];

    private static readonly HashSet<string> StructuralActionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "focus",
        "scroll_into_view"
    };

    private static readonly string[] AriaContainerPropertyNames =
    [
        "aria",
        "ariaProperties",
        "attributes",
        "properties"
    ];

    private const int MaxNodeTraceSegments = 8;
    private const int LeadingNodeTraceSegments = 2;
    private const int TrailingNodeTraceSegments = 5;
    private const int MaxNodeTraceValueLength = 80;
    private const int MaxAriaPropertyCount = 8;

    private static readonly string[] KeyboardFocusablePropertyNames =
    [
        "isKeyboardFocusable",
        "keyboardFocusable",
        "focusable",
        "canKeyboardFocus"
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

    private static readonly string[] GenericContainerRoleTerms =
    [
        "group"
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

        var treeRoots = GetValidationTreeRoots(document.RootElement, record.ToolName);

        if (treeRoots.Length == 0)
        {
            findings.Add(CreateFinding(
                context,
                $"AVA-TREE-MISSING-{context.StepNumber:000}-{ToolToken(record.ToolName)}",
                AvaFindingStatus.NeedsReview,
                "Captured tree evidence must include original UIA tree evidence or compactTree.",
                toolName: record.ToolName));
            return;
        }

        var inspector = new TreeInspector(context, record, findings);
        foreach (var treeRoot in treeRoots)
        {
            inspector.InspectTreeRoot(treeRoot);
        }
    }

    private static JsonElement[] GetValidationTreeRoots(JsonElement root, string toolName)
    {
        if (TryGetOriginalTreeRoot(root, toolName, out var originalTreeRoot))
        {
            return [originalTreeRoot];
        }

        return FallbackTreePropertyNames
            .Select(propertyName => TryGetProperty(root, propertyName, out var tree) ? tree : (JsonElement?)null)
            .Where(static tree => tree is not null)
            .Select(static tree => tree!.Value)
            .ToArray();
    }

    private static bool TryGetOriginalTreeRoot(JsonElement root, string toolName, out JsonElement treeRoot)
    {
        if (TryGetProperty(root, "debugEvidence", out var debugEvidence) &&
            debugEvidence.ValueKind == JsonValueKind.Object)
        {
            if (IsFocusTool(toolName) &&
                TryGetNestedProperty(debugEvidence, ["focusTree", "focusedElement"], out treeRoot))
            {
                return true;
            }

            if (TryGetNestedProperty(debugEvidence, ["fullTree", "elementTree"], out treeRoot))
            {
                return true;
            }

            if (TryGetNestedProperty(debugEvidence, ["focusTree", "focusedElement"], out treeRoot))
            {
                return true;
            }
        }

        if (IsFocusTool(toolName) &&
            TryGetProperty(root, "focusedElement", out treeRoot))
        {
            return true;
        }

        if (TryGetProperty(root, "elementTree", out treeRoot))
        {
            return true;
        }

        if (TryGetProperty(root, "focusedElement", out treeRoot))
        {
            return true;
        }

        treeRoot = default;
        return false;
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

    private static bool IsFocusTool(string toolName)
        => string.Equals(toolName, "describe_window_focus", StringComparison.OrdinalIgnoreCase);

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
        string? nodeTrace = null,
        string? automationId = null,
        string? ariaProperties = null,
        AvaElementBounds? elementBounds = null)
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
            nodeTrace,
            automationId,
            ariaProperties,
            elementBounds);
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
            var hasActions = HasAnyMeaningfulActionProperty(node);
            var hasPatterns = HasAnyNonEmptyCollectionLikeProperty(node, PatternPropertyNames);
            var hasKeyboardFocusable = TryGetFirstBoolean(node, KeyboardFocusablePropertyNames, out var isKeyboardFocusable) &&
                isKeyboardFocusable;
            var hasRole = TryGetFirstString(node, RolePropertyNames, out var role);
            var hasStrongActionRole = hasRole && ContainsAnyTerm(role, StrongActionRoleTerms);

            if (!hasActions && !hasPatterns && !hasKeyboardFocusable && !hasStrongActionRole)
            {
                return;
            }

            var elementBounds = TryGetBestElementBounds(node, out var parsedBounds)
                ? parsedBounds
                : null;
            if (elementBounds is null && !hasKeyboardFocusable)
            {
                return;
            }

            var hasName = TryGetFirstString(node, NamePropertyNames, out _);
            if (IsIgnorableContainerInvokeNode(node, hasName, hasPatterns, hasKeyboardFocusable, hasRole ? role : null))
            {
                return;
            }

            actionableNodeIndex++;
            var nodeReference = $"actionable-{actionableNodeIndex:000}";
            var automationId = TryGetFirstString(node, AutomationIdPropertyNames, out var foundAutomationId)
                ? foundAutomationId
                : null;
            var ariaProperties = FormatAriaProperties(node);

            if (!hasName)
            {
                findings.Add(CreateFinding(
                    context,
                    $"AVA-NAME-MISSING-{context.StepNumber:000}-{ToolToken(record.ToolName)}-{actionableNodeIndex:000}",
                    AvaFindingStatus.Fail,
                    "Actionable UI node is missing an accessible name.",
                    record.ToolName,
                    nodeReference,
                    nodeTrace,
                    automationId,
                    ariaProperties,
                    elementBounds));
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
                    nodeTrace,
                    automationId,
                    ariaProperties,
                    elementBounds));
            }

            if (hasActions || hasPatterns)
            {
                return;
            }

            // Conservative actionability heuristic:
            // command-like controls normally need an automation pattern or explicit action to explain how they can be invoked.
            // Generic focusable controls can still be valid custom controls, so those are routed to review instead of failure.
            var status = hasStrongActionRole
                ? AvaFindingStatus.Fail
                : AvaFindingStatus.NeedsReview;
            findings.Add(CreateFinding(
                context,
                $"AVA-ACTION-MISSING-{context.StepNumber:000}-{ToolToken(record.ToolName)}-{actionableNodeIndex:000}",
                status,
                "Actionable UI node has no exposed control patterns or explicit actions.",
                record.ToolName,
                nodeReference,
                nodeTrace,
                automationId,
                ariaProperties,
                elementBounds));
        }

        private static bool TryGetElementBounds(JsonElement node, out AvaElementBounds bounds)
        {
            bounds = null!;
            if (!TryGetProperty(node, "bounds", out var boundsElement) ||
                boundsElement.ValueKind != JsonValueKind.Object ||
                !TryGetDoubleProperty(boundsElement, "left", out var left) ||
                !TryGetDoubleProperty(boundsElement, "top", out var top) ||
                !TryGetDoubleProperty(boundsElement, "width", out var width) ||
                !TryGetDoubleProperty(boundsElement, "height", out var height) ||
                width <= 0 ||
                height <= 0)
            {
                return false;
            }

            bounds = new AvaElementBounds(left, top, width, height);
            return true;
        }

        private static bool TryGetBestElementBounds(JsonElement node, out AvaElementBounds bounds)
        {
            if (TryGetElementBounds(node, out bounds))
            {
                return true;
            }

            return TryGetDescendantBounds(node, out bounds);
        }

        private static bool TryGetDescendantBounds(JsonElement element, out AvaElementBounds bounds)
        {
            AvaElementBounds? aggregate = null;
            AddDescendantBounds(element, ref aggregate, includeCurrentElement: false);
            if (aggregate is null)
            {
                bounds = null!;
                return false;
            }

            bounds = aggregate;
            return true;
        }

        private static void AddDescendantBounds(
            JsonElement element,
            ref AvaElementBounds? aggregate,
            bool includeCurrentElement)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    if (includeCurrentElement && TryGetElementBounds(element, out var bounds))
                    {
                        aggregate = aggregate is null ? bounds : UnionBounds(aggregate, bounds);
                    }

                    foreach (var property in element.EnumerateObject())
                    {
                        if (string.Equals(property.Name, "bounds", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        {
                            AddDescendantBounds(property.Value, ref aggregate, includeCurrentElement: true);
                        }
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        AddDescendantBounds(item, ref aggregate, includeCurrentElement: true);
                    }

                    break;
            }
        }

        private static AvaElementBounds UnionBounds(AvaElementBounds first, AvaElementBounds second)
        {
            var left = Math.Min(first.Left, second.Left);
            var top = Math.Min(first.Top, second.Top);
            var right = Math.Max(first.Left + first.Width, second.Left + second.Width);
            var bottom = Math.Max(first.Top + first.Height, second.Top + second.Height);
            return new AvaElementBounds(left, top, right - left, bottom - top);
        }

        private static bool IsIgnorableContainerInvokeNode(
            JsonElement node,
            bool hasName,
            bool hasPatterns,
            bool hasKeyboardFocusable,
            string? role)
        {
            if (hasName || hasPatterns || hasKeyboardFocusable || string.IsNullOrWhiteSpace(role))
            {
                return false;
            }

            if (!ContainsAnyTerm(role, GenericContainerRoleTerms) ||
                !HasChildElements(node) ||
                !TryGetMeaningfulActionNames(node, out var actionNames))
            {
                return false;
            }

            return actionNames.Count == 1 &&
                string.Equals(actionNames[0], "invoke", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasChildElements(JsonElement node)
            => TryGetProperty(node, "children", out var children) &&
                children.ValueKind == JsonValueKind.Array &&
                children.GetArrayLength() > 0;

        private static bool TryGetDoubleProperty(JsonElement element, string propertyName, out double value)
        {
            value = 0;
            if (!TryGetProperty(element, propertyName, out var property))
            {
                return false;
            }

            if (property.ValueKind == JsonValueKind.Number)
            {
                return property.TryGetDouble(out value);
            }

            if (property.ValueKind == JsonValueKind.String &&
                double.TryParse(
                    property.GetString(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out value))
            {
                return true;
            }

            return false;
        }

        private static bool TryBuildNodeTraceSegment(JsonElement node, out string traceSegment)
        {
            var hasRole = TryGetFirstString(node, RolePropertyNames, out var role);
            var hasName = TryGetFirstString(node, NamePropertyNames, out var name);

            if (!hasRole && !hasName)
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

            traceSegment = string.Join(" ", parts);
            return true;
        }

        private static string? FormatAriaProperties(JsonElement node)
        {
            var properties = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            CollectAriaProperties(node, properties);
            if (properties.Count == 0)
            {
                return null;
            }

            return string.Join(
                "; ",
                properties
                    .Take(MaxAriaPropertyCount)
                    .Select(static property => $"{property.Key}: {property.Value}"));
        }

        private static void CollectAriaProperties(
            JsonElement node,
            IDictionary<string, string> properties)
        {
            if (node.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var property in node.EnumerateObject())
            {
                if (property.Name.Equals("ariaProperties", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String &&
                    TryCollectSerializedAriaProperties(property.Value.GetString(), properties))
                {
                    continue;
                }

                if (IsAriaPropertyName(property.Name) &&
                    TryFormatAriaValue(property.Value, out var value))
                {
                    properties[NormalizeAriaPropertyName(property.Name)] = value;
                    continue;
                }

                if (IsAriaContainerPropertyName(property.Name) &&
                    property.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var childProperty in property.Value.EnumerateObject())
                    {
                        if (childProperty.Name.Equals("ariaProperties", StringComparison.OrdinalIgnoreCase) &&
                            childProperty.Value.ValueKind == JsonValueKind.String &&
                            TryCollectSerializedAriaProperties(childProperty.Value.GetString(), properties))
                        {
                            continue;
                        }

                        if (IsAriaPropertyName(childProperty.Name) &&
                            TryFormatAriaValue(childProperty.Value, out value))
                        {
                            properties[NormalizeAriaPropertyName(childProperty.Name)] = value;
                        }
                    }
                }
            }
        }

        private static bool TryCollectSerializedAriaProperties(
            string? ariaProperties,
            IDictionary<string, string> properties)
        {
            if (string.IsNullOrWhiteSpace(ariaProperties) ||
                IsUnsupportedPropertyValue(ariaProperties))
            {
                return false;
            }

            var collected = false;
            foreach (var part in ariaProperties.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separatorIndex = part.IndexOf('=');
                if (separatorIndex < 0)
                {
                    separatorIndex = part.IndexOf(':');
                }

                if (separatorIndex <= 0 || separatorIndex == part.Length - 1)
                {
                    continue;
                }

                var name = part[..separatorIndex].Trim();
                var value = part[(separatorIndex + 1)..].Trim().Trim('"');
                if (name.Length == 0 ||
                    !IsMeaningfulString(value) ||
                    IsUnsupportedPropertyValue(value))
                {
                    continue;
                }

                properties[NormalizeSerializedAriaPropertyName(name)] = CleanTraceValue(value);
                collected = true;
            }

            return collected;
        }

        private static string NormalizeSerializedAriaPropertyName(string propertyName)
        {
            var trimmed = propertyName.Trim().Replace('_', '-');
            if (trimmed.StartsWith("aria-", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("aria", StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeAriaPropertyName(trimmed);
            }

            return $"aria-{trimmed.ToLowerInvariant()}";
        }

        private static bool IsAriaContainerPropertyName(string propertyName)
            => AriaContainerPropertyNames.Contains(propertyName, StringComparer.OrdinalIgnoreCase);

        private static bool IsAriaPropertyName(string propertyName)
            => propertyName.StartsWith("aria-", StringComparison.OrdinalIgnoreCase) ||
                propertyName.StartsWith("aria", StringComparison.OrdinalIgnoreCase);

        private static string NormalizeAriaPropertyName(string propertyName)
        {
            var trimmed = propertyName.Trim();
            if (trimmed.StartsWith("aria-", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.ToLowerInvariant();
            }

            if (trimmed.Equals("aria", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("ariaProperties", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            var suffix = trimmed[4..];
            if (suffix.Length == 0)
            {
                return trimmed;
            }

            var builder = new StringBuilder("aria");
            foreach (var character in suffix)
            {
                if (char.IsUpper(character))
                {
                    builder.Append('-');
                    builder.Append(char.ToLowerInvariant(character));
                }
                else
                {
                    builder.Append(char.ToLowerInvariant(character));
                }
            }

            return builder.ToString();
        }

        private static bool TryFormatAriaValue(JsonElement valueElement, out string value)
        {
            value = string.Empty;
            switch (valueElement.ValueKind)
            {
                case JsonValueKind.String:
                    var stringValue = valueElement.GetString() ?? string.Empty;
                    if (IsUnsupportedPropertyValue(stringValue))
                    {
                        return false;
                    }

                    value = CleanTraceValue(stringValue);
                    return value.Length > 0;
                case JsonValueKind.True:
                    value = "true";
                    return true;
                case JsonValueKind.False:
                    value = "false";
                    return true;
                case JsonValueKind.Number:
                    value = valueElement.GetRawText();
                    return true;
                case JsonValueKind.Array:
                    var values = valueElement
                        .EnumerateArray()
                        .Select(static item => item.ValueKind == JsonValueKind.String
                            ? CleanTraceValue(item.GetString() ?? string.Empty)
                            : item.GetRawText())
                        .Where(static item => item.Length > 0 && !IsUnsupportedPropertyValue(item))
                        .ToArray();
                    if (values.Length == 0)
                    {
                        return false;
                    }

                    value = string.Join(", ", values);
                    return true;
                default:
                    return false;
            }
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

        private static bool HasAnyNonEmptyCollectionLikeProperty(JsonElement node, IReadOnlyList<string> propertyNames)
            => propertyNames.Any(propertyName => HasNonEmptyCollectionLikeProperty(node, propertyName));

        private static bool HasAnyMeaningfulActionProperty(JsonElement node)
            => ActionPropertyNames.Any(propertyName =>
                TryGetProperty(node, propertyName, out var property) &&
                HasMeaningfulActionValue(property));

        private static bool TryGetMeaningfulActionNames(JsonElement node, out IReadOnlyList<string> actionNames)
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var propertyName in ActionPropertyNames)
            {
                if (TryGetProperty(node, propertyName, out var property))
                {
                    AddMeaningfulActionNames(property, names);
                }
            }

            actionNames = names.ToArray();
            return actionNames.Count > 0;
        }

        private static void AddMeaningfulActionNames(JsonElement element, ISet<string> names)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    var actionName = element.GetString()?.Trim();
                    if (IsMeaningfulActionString(actionName))
                    {
                        names.Add(actionName!);
                    }

                    break;
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (HasMeaningfulActionValue(property.Value) &&
                            IsMeaningfulActionString(property.Name))
                        {
                            names.Add(property.Name.Trim());
                        }

                        AddMeaningfulActionNames(property.Value, names);
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        AddMeaningfulActionNames(item, names);
                    }

                    break;
            }
        }

        private static bool HasMeaningfulActionValue(JsonElement element)
            => element.ValueKind switch
            {
                JsonValueKind.String => IsMeaningfulActionString(element.GetString()),
                JsonValueKind.Object => element.EnumerateObject().Any(static property => HasMeaningfulActionValue(property.Value)),
                JsonValueKind.Array => element.EnumerateArray().Any(HasMeaningfulActionValue),
                JsonValueKind.True => true,
                JsonValueKind.Number => true,
                _ => false
            };

        private static bool IsMeaningfulActionString(string? value)
        {
            if (!IsMeaningfulString(value))
            {
                return false;
            }

            return !StructuralActionNames.Contains(value!.Trim());
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

        private static bool IsUnsupportedPropertyValue(string value)
            => value.Contains("Unsupported Property", StringComparison.OrdinalIgnoreCase) &&
               value.Contains("ArgumentException", StringComparison.OrdinalIgnoreCase);

        private static bool ContainsAnyTerm(string value, IReadOnlyList<string> terms)
            => terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
