namespace HeronWin.Ava;

internal sealed record AvaRuleMetadata(
    string ProfileId,
    string RuleId,
    string SourceStandard);

internal static class AvaProfileIds
{
    public const string FederalWindowsUiaMin = "federal-windows-uia-min";
    public const string FederalWebMin = "federal-web-min";
}

internal static class AvaProfileCatalog
{
    public static AvaRuleMetadata? ResolveRule(string profileId, string findingId)
    {
        var normalizedProfileId = string.IsNullOrWhiteSpace(profileId)
            ? AvaValidationConfig.DefaultProfile
            : profileId.Trim();

        return (normalizedProfileId switch
        {
            AvaProfileIds.FederalWebMin => ResolveFederalWebRule(findingId),
            AvaProfileIds.FederalWindowsUiaMin => ResolveFederalWindowsUiaRule(findingId),
            _ => null
        }) is { } rule
            ? rule with { ProfileId = normalizedProfileId }
            : null;
    }

    private static AvaRuleMetadata? ResolveFederalWebRule(string findingId)
    {
        if (findingId.StartsWith("AVA-NAME-MISSING-", StringComparison.Ordinal))
        {
            return Rule("WEB-WCAG-4.1.2-NAME", "WCAG 2.0 SC 4.1.2 Name, Role, Value");
        }

        if (findingId.StartsWith("AVA-ROLE-MISSING-", StringComparison.Ordinal))
        {
            return Rule("WEB-WCAG-4.1.2-ROLE", "WCAG 2.0 SC 4.1.2 Name, Role, Value");
        }

        if (findingId.StartsWith("AVA-ACTION-MISSING-", StringComparison.Ordinal))
        {
            return Rule("WEB-WCAG-2.1.1-ACTIONABILITY", "WCAG 2.0 SC 2.1.1 Keyboard");
        }

        if (findingId.StartsWith("AVA-FOCUS-MISSING-", StringComparison.Ordinal))
        {
            return Rule("WEB-WCAG-2.4.3-FOCUS-EVIDENCE", "WCAG 2.0 SC 2.4.3 Focus Order");
        }

        return ResolveSharedEvidenceRule(findingId, "WEB-UIA-EVIDENCE", "UI Automation evidence for federal-web-min");
    }

    private static AvaRuleMetadata? ResolveFederalWindowsUiaRule(string findingId)
    {
        if (findingId.StartsWith("AVA-NAME-MISSING-", StringComparison.Ordinal))
        {
            return Rule("UIA-NAME-PROPERTY", "UI Automation Name property");
        }

        if (findingId.StartsWith("AVA-ROLE-MISSING-", StringComparison.Ordinal))
        {
            return Rule("UIA-CONTROL-TYPE", "UI Automation ControlType property");
        }

        if (findingId.StartsWith("AVA-ACTION-MISSING-", StringComparison.Ordinal))
        {
            return Rule("UIA-CONTROL-PATTERN", "UI Automation control patterns");
        }

        if (findingId.StartsWith("AVA-FOCUS-MISSING-", StringComparison.Ordinal))
        {
            return Rule("UIA-KEYBOARD-FOCUS-EVIDENCE", "UI Automation keyboard focus evidence");
        }

        return ResolveSharedEvidenceRule(findingId, "UIA-EVIDENCE", "UI Automation deterministic evidence");
    }

    private static AvaRuleMetadata? ResolveSharedEvidenceRule(
        string findingId,
        string rulePrefix,
        string sourceStandard)
    {
        if (findingId.StartsWith("AVA-EVIDENCE-MISSING-", StringComparison.Ordinal) ||
            findingId.StartsWith("AVA-TREE-EVIDENCE-MISSING-", StringComparison.Ordinal))
        {
            return Rule($"{rulePrefix}-MISSING", sourceStandard);
        }

        if (findingId.StartsWith("AVA-EVIDENCE-ERROR-", StringComparison.Ordinal))
        {
            return Rule($"{rulePrefix}-ERROR", sourceStandard);
        }

        if (findingId.StartsWith("AVA-TREE-PARSE-", StringComparison.Ordinal))
        {
            return Rule($"{rulePrefix}-TREE-PARSE", sourceStandard);
        }

        if (findingId.StartsWith("AVA-TREE-MISSING-", StringComparison.Ordinal))
        {
            return Rule($"{rulePrefix}-TREE-MISSING", sourceStandard);
        }

        return null;
    }

    private static AvaRuleMetadata Rule(string ruleId, string sourceStandard)
        => new(string.Empty, ruleId, sourceStandard);
}
