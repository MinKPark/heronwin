namespace HeronWin.Brain;

internal enum LlmRole
{
    Default,
    AvaDriver,
    AvaEvaluator,
    AvaReporter
}

internal sealed record LlmRoleConfig(
    LlmRole Role,
    string? ModelOverride,
    string? ReasoningEffort);

internal static class LlmReasoningEfforts
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
    public const string XHigh = "xhigh";

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            Low => Low,
            Medium => Medium,
            High => High,
            "x-high" or "extra-high" or "extra_high" or XHigh => XHigh,
            _ => throw new InvalidOperationException(
                $"Invalid reasoning effort \"{value}\". Must be \"low\", \"medium\", \"high\", or \"xhigh\".")
        };
    }
}
