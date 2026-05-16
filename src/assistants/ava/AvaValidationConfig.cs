using HeronWin.Brain;

namespace HeronWin.Ava;

internal sealed record AvaValidationConfig(
    string Name,
    string Profile,
    string ContinuationPolicy,
    IReadOnlyList<string> Checkpoints,
    string? WindowHandle,
    IReadOnlyList<AvaValidationStepConfig> Steps)
{
    public const string DefaultProfile = AvaProfileIds.FederalWindowsUiaMin;
    public static IReadOnlyList<string> DefaultCheckpoints { get; } = [AvaCheckpointTiming.After];

    public AvaEffectiveStepValidationConfig ResolveStep(int stepIndex)
    {
        var step = stepIndex >= 0 && stepIndex < Steps.Count
            ? Steps[stepIndex]
            : null;

        return new AvaEffectiveStepValidationConfig(
            step?.Name,
            string.IsNullOrWhiteSpace(step?.ContinuationPolicy)
                ? ContinuationPolicy
                : step.ContinuationPolicy,
            step?.Checkpoints.Count > 0
                ? step.Checkpoints
                : Checkpoints,
            string.IsNullOrWhiteSpace(step?.WindowHandle)
                ? WindowHandle
                : step.WindowHandle);
    }
}

internal sealed record AvaValidationStepConfig(
    string? Name,
    string? ContinuationPolicy,
    IReadOnlyList<string> Checkpoints,
    string? WindowHandle);

internal sealed record AvaEffectiveStepValidationConfig(
    string? Name,
    string ContinuationPolicy,
    IReadOnlyList<string> Checkpoints,
    string? WindowHandle);

internal static class AvaContinuationPolicy
{
    public const string ContinueAndReport = "continue-and-report";
}

internal static class AvaCheckpointTiming
{
    public const string After = "after";
}

internal static class AvaValidationConfigLoader
{
    public static AvaValidationConfig LoadFromFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Validation config file was not found.", fullPath);
        }

        return Parse(File.ReadAllText(fullPath), Path.GetFileName(fullPath));
    }

    internal static AvaValidationConfig Parse(string yaml, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            throw new InvalidOperationException("Validation config YAML was empty.");
        }

        if (BrainYamlParser.Parse(yaml) is not BrainYamlMapping mapping)
        {
            throw new InvalidOperationException("Validation config YAML must be a mapping.");
        }

        var checkpoints = ReadStringArray(mapping, "checkpoints");
        if (checkpoints.Count == 0)
        {
            checkpoints = AvaValidationConfig.DefaultCheckpoints;
        }

        var steps = mapping.TryGetValue("steps", out var stepsNode) && stepsNode is BrainYamlSequence stepsSequence
            ? stepsSequence.Items.Select(ParseStep).ToArray()
            : [];

        return new AvaValidationConfig(
            GetString(mapping, "name") ?? Path.GetFileNameWithoutExtension(sourceName),
            GetString(mapping, "profile") ?? AvaValidationConfig.DefaultProfile,
            GetString(mapping, "continuationPolicy") ?? AvaContinuationPolicy.ContinueAndReport,
            checkpoints,
            GetString(mapping, "windowHandle"),
            steps);
    }

    private static AvaValidationStepConfig ParseStep(BrainYamlNode node)
    {
        if (node is not BrainYamlMapping mapping)
        {
            throw new InvalidOperationException("Each validation config step must be a YAML mapping.");
        }

        return new AvaValidationStepConfig(
            GetString(mapping, "name"),
            GetString(mapping, "continuationPolicy"),
            ReadStringArray(mapping, "checkpoints"),
            GetString(mapping, "windowHandle"));
    }

    private static string? GetString(BrainYamlMapping mapping, string propertyName)
    {
        if (!mapping.TryGetValue(propertyName, out var node) ||
            node is not BrainYamlScalar scalar ||
            string.IsNullOrWhiteSpace(scalar.Value))
        {
            return null;
        }

        return scalar.Value.Trim();
    }

    private static IReadOnlyList<string> ReadStringArray(BrainYamlMapping mapping, string propertyName)
    {
        if (!mapping.TryGetValue(propertyName, out var node) ||
            node is not BrainYamlSequence sequence)
        {
            return [];
        }

        return sequence.Items
            .OfType<BrainYamlScalar>()
            .Select(static item => item.Value.Trim())
            .Where(static item => item.Length > 0)
            .ToArray();
    }
}
