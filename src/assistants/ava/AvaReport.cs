using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;

namespace HeronWin.Ava;

internal sealed record AvaValidationReport(
    string RunId,
    string ScenarioName,
    string ValidationConfigName,
    string Profile,
    string ContinuationPolicy,
    IReadOnlyList<string> DefaultCheckpoints,
    string UxScenarioPath,
    string ValidationConfigPath,
    IReadOnlyList<AvaStepResult> Steps)
{
    public bool HasNotTestedFindings =>
        Steps.SelectMany(static step => step.Findings)
            .Any(static finding => string.Equals(finding.Status, AvaFindingStatus.NotTested, StringComparison.Ordinal));

    public bool HasBlockingFindings =>
        Steps.SelectMany(static step => step.Findings)
            .Any(static finding => AvaFindingStatus.IsBlocking(finding.Status));

    public IReadOnlyDictionary<string, int> CheckpointStatusCounts =>
        CountStatuses(Steps.SelectMany(static step => step.Checkpoints).Select(static checkpoint => checkpoint.Status));

    public IReadOnlyDictionary<string, int> FindingStatusCounts =>
        CountStatuses(Steps.SelectMany(static step => step.Findings).Select(static finding => finding.Status));

    private static IReadOnlyDictionary<string, int> CountStatuses(IEnumerable<string> statuses)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var status in statuses)
        {
            counts[status] = counts.TryGetValue(status, out var count) ? count + 1 : 1;
        }

        return counts;
    }
}

internal sealed record AvaStepResult(
    int Index,
    string StepId,
    string Name,
    string Command,
    string ContinuationPolicy,
    AvaStepEvidenceReference Evidence,
    IReadOnlyList<AvaCheckpointResult> Checkpoints,
    IReadOnlyList<AvaAccessibilityFinding> Findings);

internal sealed record AvaCheckpointResult(
    string Timing,
    string Status,
    string Summary);

internal sealed record AvaAccessibilityFinding
{
    private readonly string? exportIdOverride;
    private readonly string? triageCategoryOverride;
    private readonly string? evidenceSummaryOverride;

    public AvaAccessibilityFinding(
        string id,
        string status,
        string checkpoint,
        string summary,
        string? profileId = null,
        string? ruleId = null,
        string? sourceStandard = null,
        string? evidenceReference = null,
        string? stepId = null,
        string? toolName = null,
        string? nodeReference = null,
        string? exportId = null,
        string? triageCategory = null,
        string? evidenceSummary = null)
    {
        Id = id;
        Status = status;
        Checkpoint = checkpoint;
        Summary = summary;
        ProfileId = profileId;
        RuleId = ruleId;
        SourceStandard = sourceStandard;
        EvidenceReference = evidenceReference;
        StepId = stepId;
        ToolName = toolName;
        NodeReference = nodeReference;
        exportIdOverride = string.IsNullOrWhiteSpace(exportId) ? null : exportId;
        triageCategoryOverride = string.IsNullOrWhiteSpace(triageCategory) ? null : triageCategory;
        evidenceSummaryOverride = string.IsNullOrWhiteSpace(evidenceSummary) ? null : evidenceSummary;
    }

    public string Id { get; init; }

    public string ExportId => exportIdOverride ?? AvaFindingExport.CreateExportId(this);

    public string Status { get; init; }

    public string TriageCategory => triageCategoryOverride ?? AvaTriageCategory.FromFinding(this);

    public string Checkpoint { get; init; }

    public string Summary { get; init; }

    public string? EvidenceSummary => evidenceSummaryOverride ?? AvaFindingExport.CreateEvidenceSummary(this);

    public string? ProfileId { get; init; }

    public string? RuleId { get; init; }

    public string? SourceStandard { get; init; }

    public string? EvidenceReference { get; init; }

    public string? StepId { get; init; }

    public string? ToolName { get; init; }

    public string? NodeReference { get; init; }
}

internal static class AvaTriageCategory
{
    public const string AutomatedFailure = "automated-failure";
    public const string NeedsHumanReview = "needs-human-review";
    public const string NotTested = "not-tested";
    public const string EvidenceGap = "evidence-gap";

    public static string FromFinding(AvaAccessibilityFinding finding)
    {
        if (string.Equals(finding.Status, AvaFindingStatus.Fail, StringComparison.Ordinal))
        {
            return AutomatedFailure;
        }

        if (string.Equals(finding.Status, AvaFindingStatus.NeedsReview, StringComparison.Ordinal))
        {
            return NeedsHumanReview;
        }

        if (string.Equals(finding.Status, AvaFindingStatus.NotTested, StringComparison.Ordinal))
        {
            return IsEvidenceGap(finding.Id) ? EvidenceGap : NotTested;
        }

        return NeedsHumanReview;
    }

    private static bool IsEvidenceGap(string findingId)
        => findingId.Contains("EVIDENCE", StringComparison.Ordinal) ||
            findingId.Contains("TREE", StringComparison.Ordinal);
}

internal static class AvaFindingExport
{
    public static string CreateExportId(AvaAccessibilityFinding finding)
    {
        var material = string.Join('\n',
        [
            $"profile:{finding.ProfileId ?? string.Empty}",
            $"rule:{finding.RuleId ?? string.Empty}",
            $"finding:{finding.Id}",
            $"step:{finding.StepId ?? string.Empty}",
            $"checkpoint:{finding.Checkpoint}",
            $"node:{finding.NodeReference ?? string.Empty}",
            $"evidence:{finding.EvidenceReference ?? string.Empty}",
            $"tool:{finding.ToolName ?? string.Empty}"
        ]);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return $"ava-{Convert.ToHexString(bytes, 0, 12).ToLowerInvariant()}";
    }

    public static string? CreateEvidenceSummary(AvaAccessibilityFinding finding)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(finding.EvidenceReference))
        {
            parts.Add($"manifest: {finding.EvidenceReference}");
        }

        if (!string.IsNullOrWhiteSpace(finding.ToolName))
        {
            parts.Add($"tool: {finding.ToolName}");
        }

        if (!string.IsNullOrWhiteSpace(finding.NodeReference))
        {
            parts.Add($"node: {finding.NodeReference}");
        }

        return parts.Count == 0 ? null : string.Join("; ", parts);
    }
}

internal static class AvaFindingStatus
{
    public const string Pass = "pass";
    public const string Fail = "fail";
    public const string NeedsReview = "needs-review";
    public const string NotTested = "not-tested";

    public static bool IsBlocking(string status)
        => string.Equals(status, Fail, StringComparison.Ordinal) ||
            string.Equals(status, NeedsReview, StringComparison.Ordinal) ||
            string.Equals(status, NotTested, StringComparison.Ordinal);

    public static string Aggregate(IEnumerable<string> statuses)
    {
        var sawNotTested = false;
        var sawNeedsReview = false;

        foreach (var status in statuses)
        {
            if (string.Equals(status, Fail, StringComparison.Ordinal))
            {
                return Fail;
            }

            if (string.Equals(status, NeedsReview, StringComparison.Ordinal))
            {
                sawNeedsReview = true;
            }
            else if (string.Equals(status, NotTested, StringComparison.Ordinal))
            {
                sawNotTested = true;
            }
        }

        if (sawNeedsReview)
        {
            return NeedsReview;
        }

        return sawNotTested ? NotTested : Pass;
    }
}

internal static class AvaReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static AvaReportWriteResult Write(AvaValidationReport report, string outputDirectory)
    {
        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullOutputDirectory);

        var jsonPath = Path.Combine(fullOutputDirectory, "report.json");
        var markdownPath = Path.Combine(fullOutputDirectory, "report.md");

        File.WriteAllText(jsonPath, ToJson(report), Encoding.UTF8);
        File.WriteAllText(markdownPath, ToMarkdown(report), Encoding.UTF8);

        return new AvaReportWriteResult(markdownPath, jsonPath);
    }

    public static string ToJson(AvaValidationReport report)
        => JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine;

    public static string ToMarkdown(AvaValidationReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# AVA Validation Report: {report.ScenarioName}");
        builder.AppendLine();
        builder.AppendLine($"- Run ID: `{report.RunId}`");
        builder.AppendLine($"- Validation config: `{report.ValidationConfigName}`");
        builder.AppendLine($"- Profile: `{report.Profile}`");
        builder.AppendLine($"- Continuation policy: `{report.ContinuationPolicy}`");
        builder.AppendLine($"- Default checkpoints: `{string.Join(", ", report.DefaultCheckpoints)}`");
        builder.AppendLine($"- UX scenario: `{report.UxScenarioPath}`");
        builder.AppendLine($"- Validation config path: `{report.ValidationConfigPath}`");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine($"- Steps: `{report.Steps.Count}`");
        builder.AppendLine($"- Findings: `{report.Steps.Sum(static step => step.Findings.Count)}`");
        builder.AppendLine($"- Checkpoint status counts: `{FormatStatusCounts(report.CheckpointStatusCounts)}`");
        builder.AppendLine($"- Finding status counts: `{FormatStatusCounts(report.FindingStatusCounts)}`");
        builder.AppendLine();
        builder.AppendLine("## Steps");

        foreach (var step in report.Steps)
        {
            builder.AppendLine();
            builder.AppendLine($"### {step.Index}. {step.Name}");
            builder.AppendLine();
            builder.AppendLine($"- Command: `{step.Command}`");
            builder.AppendLine($"- Continuation policy: `{step.ContinuationPolicy}`");
            builder.AppendLine($"- Checkpoints: `{string.Join(", ", step.Checkpoints.Select(static checkpoint => checkpoint.Timing))}`");
            builder.AppendLine($"- Evidence: `{step.Evidence.ManifestPath}` (`{step.Evidence.Status}`, {step.Evidence.EntryCount} entries)");

            foreach (var finding in step.Findings)
            {
                builder.Append($"- Finding `{finding.Id}`: `{finding.Status}` at `{finding.Checkpoint}` - {finding.Summary}");
                builder.Append($" Export ID: `{finding.ExportId}`");
                builder.Append($" Triage: `{finding.TriageCategory}`");
                if (!string.IsNullOrWhiteSpace(finding.ProfileId))
                {
                    builder.Append($" Profile: `{finding.ProfileId}`");
                }

                if (!string.IsNullOrWhiteSpace(finding.RuleId))
                {
                    builder.Append($" Rule: `{finding.RuleId}`");
                }

                if (!string.IsNullOrWhiteSpace(finding.SourceStandard))
                {
                    builder.Append($" Source: `{finding.SourceStandard}`");
                }

                if (!string.IsNullOrWhiteSpace(finding.EvidenceReference))
                {
                    builder.Append($" Evidence: `{finding.EvidenceReference}`");
                }

                if (!string.IsNullOrWhiteSpace(finding.EvidenceSummary))
                {
                    builder.Append($" Evidence summary: `{finding.EvidenceSummary}`");
                }

                if (!string.IsNullOrWhiteSpace(finding.ToolName))
                {
                    builder.Append($" Tool: `{finding.ToolName}`");
                }

                if (!string.IsNullOrWhiteSpace(finding.NodeReference))
                {
                    builder.Append($" Node: `{finding.NodeReference}`");
                }

                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private static string FormatStatusCounts(IReadOnlyDictionary<string, int> counts)
        => counts.Count == 0
            ? "none"
            : string.Join(", ", counts.Select(static pair => $"{pair.Key}: {pair.Value}"));
}

internal sealed record AvaReportWriteResult(
    string MarkdownPath,
    string JsonPath);
