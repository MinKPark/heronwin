using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
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
    IReadOnlyList<AvaAccessibilityFinding> Findings,
    AvaCommandExecutionResult? Execution = null);

internal sealed record AvaCheckpointResult(
    string Timing,
    string Status,
    string Summary);

internal sealed record AvaElementBounds(
    double Left,
    double Top,
    double Width,
    double Height);

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
        string? nodeTrace = null,
        string? automationId = null,
        string? ariaProperties = null,
        AvaElementBounds? elementBounds = null,
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
        NodeTrace = string.IsNullOrWhiteSpace(nodeTrace) ? null : nodeTrace;
        AutomationId = string.IsNullOrWhiteSpace(automationId) ? null : automationId;
        AriaProperties = string.IsNullOrWhiteSpace(ariaProperties) ? null : ariaProperties;
        ElementBounds = elementBounds;
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

    public string? NodeTrace { get; init; }

    public string? AutomationId { get; init; }

    public string? AriaProperties { get; init; }

    public AvaElementBounds? ElementBounds { get; init; }
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

        if (!string.IsNullOrWhiteSpace(finding.NodeTrace))
        {
            parts.Add($"trace: {finding.NodeTrace}");
        }

        if (!string.IsNullOrWhiteSpace(finding.AutomationId))
        {
            parts.Add($"automationId: {finding.AutomationId}");
        }

        if (!string.IsNullOrWhiteSpace(finding.AriaProperties))
        {
            parts.Add($"aria: {finding.AriaProperties}");
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
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static AvaReportWriteResult Write(AvaValidationReport report, string outputDirectory)
    {
        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullOutputDirectory);
        var reportForOutput = DeduplicateFindings(report);

        var jsonPath = Path.Combine(fullOutputDirectory, "report.json");
        var markdownPath = Path.Combine(fullOutputDirectory, "report.md");

        File.WriteAllText(jsonPath, ToJson(reportForOutput), Encoding.UTF8);
        File.WriteAllText(markdownPath, ToMarkdown(reportForOutput, fullOutputDirectory), Encoding.UTF8);

        return new AvaReportWriteResult(markdownPath, jsonPath);
    }

    public static string ToJson(AvaValidationReport report)
        => JsonSerializer.Serialize(DeduplicateFindings(report), JsonOptions) + Environment.NewLine;

    public static AvaValidationReport ReadJson(string jsonPath)
    {
        var fullPath = Path.GetFullPath(jsonPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"AVA report JSON was not found: {fullPath}", fullPath);
        }

        return JsonSerializer.Deserialize<AvaValidationReport>(
                   File.ReadAllText(fullPath, Encoding.UTF8),
                   JsonOptions)
               ?? throw new InvalidOperationException($"AVA report JSON was empty or invalid: {fullPath}");
    }

    public static string ToMarkdown(AvaValidationReport report)
        => ToMarkdown(report, outputDirectory: null);

    private static string ToMarkdown(AvaValidationReport report, string? outputDirectory)
    {
        report = DeduplicateFindings(report);
        ClearHighlightDirectories(outputDirectory, report);
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
        AppendSummaryTables(builder, report);
        builder.AppendLine();
        builder.AppendLine("## Steps");

        foreach (var step in report.Steps)
        {
            builder.AppendLine();
            builder.AppendLine($"### {step.Index}. {step.Name}");
            builder.AppendLine();
            builder.AppendLine($"- Command: `{step.Command}`");
            builder.AppendLine($"- Continuation policy: `{step.ContinuationPolicy}`");
            if (step.Execution is not null)
            {
                builder.AppendLine($"- Execution: `{step.Execution.Status}` - {step.Execution.Summary}");
                builder.AppendLine($"- Execution tool calls: `{step.Execution.ToolCallCount}` (`{step.Execution.ToolErrorCount}` errors)");
            }

            builder.AppendLine($"- Checkpoints: `{string.Join(", ", step.Checkpoints.Select(static checkpoint => checkpoint.Timing))}`");
            builder.AppendLine($"- Evidence: `{step.Evidence.ManifestPath}` (`{step.Evidence.Status}`, {step.Evidence.EntryCount} entries)");

            AppendStepScreenshots(builder, step, outputDirectory);
            AppendStepWebEvidence(builder, step, outputDirectory);
            AppendFindingsTable(builder, step, outputDirectory);
        }

        return builder.ToString();
    }

    private static void AppendSummaryTables(StringBuilder builder, AvaValidationReport report)
    {
        var stepStatusCounts = CountStepStatuses(report);

        builder.AppendLine("### Steps");
        builder.AppendLine();
        builder.AppendLine("| Total | Pass | Fail | Needs Review | Not Tested |");
        builder.AppendLine("| --- | --- | --- | --- | --- |");
        builder.Append("| ");
        builder.Append(report.Steps.Count);
        builder.Append(" | ");
        builder.Append(GetStatusCount(stepStatusCounts, AvaFindingStatus.Pass));
        builder.Append(" | ");
        builder.Append(GetStatusCount(stepStatusCounts, AvaFindingStatus.Fail));
        builder.Append(" | ");
        builder.Append(GetStatusCount(stepStatusCounts, AvaFindingStatus.NeedsReview));
        builder.Append(" | ");
        builder.Append(GetStatusCount(stepStatusCounts, AvaFindingStatus.NotTested));
        builder.AppendLine(" |");
        builder.AppendLine();

        AppendFindingsSummaryTable(builder, report);
    }

    private static void AppendFindingsSummaryTable(StringBuilder builder, AvaValidationReport report)
    {
        builder.AppendLine("### Findings");
        builder.AppendLine();
        builder.AppendLine("| Step | Total | Fail | Needs Review | Not Tested |");
        builder.AppendLine("| --- | --- | --- | --- | --- |");

        foreach (var step in report.Steps)
        {
            AppendFindingSummaryRow(builder, CodeCell(step.StepId), CountFindingStatuses(step.Findings), step.Findings.Count);
        }

        AppendFindingSummaryRow(
            builder,
            "**Total**",
            report.FindingStatusCounts,
            report.Steps.Sum(static step => step.Findings.Count));
    }

    private static void AppendFindingSummaryRow(
        StringBuilder builder,
        string label,
        IReadOnlyDictionary<string, int> statusCounts,
        int total)
    {
        builder.Append("| ");
        builder.Append(label);
        builder.Append(" | ");
        builder.Append(total);
        builder.Append(" | ");
        builder.Append(GetStatusCount(statusCounts, AvaFindingStatus.Fail));
        builder.Append(" | ");
        builder.Append(GetStatusCount(statusCounts, AvaFindingStatus.NeedsReview));
        builder.Append(" | ");
        builder.Append(GetStatusCount(statusCounts, AvaFindingStatus.NotTested));
        builder.AppendLine(" |");
    }

    private static IReadOnlyDictionary<string, int> CountFindingStatuses(IReadOnlyList<AvaAccessibilityFinding> findings)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var finding in findings)
        {
            counts[finding.Status] = counts.TryGetValue(finding.Status, out var count) ? count + 1 : 1;
        }

        return counts;
    }

    private static IReadOnlyDictionary<string, int> CountStepStatuses(AvaValidationReport report)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var step in report.Steps)
        {
            var status = AvaFindingStatus.Aggregate(step.Checkpoints.Select(static checkpoint => checkpoint.Status));
            counts[status] = counts.TryGetValue(status, out var count) ? count + 1 : 1;
        }

        return counts;
    }

    private static AvaValidationReport DeduplicateFindings(AvaValidationReport report)
    {
        var seen = new List<AvaAccessibilityFinding>();
        var steps = report.Steps
            .Select(step =>
            {
                var uniqueFindings = new List<AvaAccessibilityFinding>();
                foreach (var finding in step.Findings)
                {
                    if (seen.Any(seenFinding => AreDuplicateFindings(seenFinding, finding)))
                    {
                        continue;
                    }

                    seen.Add(finding);
                    uniqueFindings.Add(finding);
                }

                return step with
                {
                    Findings = uniqueFindings,
                    Checkpoints = RecalculateCheckpointsForUniqueFindings(step.Checkpoints, uniqueFindings),
                };
            })
            .ToArray();

        return report with { Steps = steps };
    }

    private static IReadOnlyList<AvaCheckpointResult> RecalculateCheckpointsForUniqueFindings(
        IReadOnlyList<AvaCheckpointResult> checkpoints,
        IReadOnlyList<AvaAccessibilityFinding> findings)
        => checkpoints
            .Select(checkpoint =>
            {
                var checkpointFindings = findings
                    .Where(finding => string.Equals(finding.Checkpoint, checkpoint.Timing, StringComparison.Ordinal))
                    .ToArray();
                if (checkpointFindings.Length == 0)
                {
                    if (string.Equals(checkpoint.Status, AvaFindingStatus.Pass, StringComparison.Ordinal) ||
                        string.Equals(checkpoint.Status, AvaFindingStatus.NotTested, StringComparison.Ordinal))
                    {
                        return checkpoint;
                    }

                    return new AvaCheckpointResult(
                        checkpoint.Timing,
                        AvaFindingStatus.Pass,
                        "No unique accessibility findings remain for this checkpoint after duplicate suppression.");
                }

                var status = AvaFindingStatus.Aggregate(checkpointFindings.Select(static finding => finding.Status));
                var summary = status == AvaFindingStatus.Pass
                    ? "Deterministic accessibility validators completed without unique findings for this checkpoint."
                    : $"Deterministic accessibility validators produced {checkpointFindings.Length} unique finding(s) for this checkpoint.";

                return new AvaCheckpointResult(checkpoint.Timing, status, summary);
            })
            .ToArray();

    private static bool AreDuplicateFindings(AvaAccessibilityFinding first, AvaAccessibilityFinding second)
    {
        if (!string.Equals(NormalizeSignaturePart(first.Status), NormalizeSignaturePart(second.Status), StringComparison.Ordinal) ||
            !string.Equals(NormalizeSignaturePart(first.ProfileId), NormalizeSignaturePart(second.ProfileId), StringComparison.Ordinal) ||
            !string.Equals(NormalizeSignaturePart(first.RuleId), NormalizeSignaturePart(second.RuleId), StringComparison.Ordinal) ||
            !string.Equals(NormalizeSignaturePart(FindingDisplayId(first.Id)), NormalizeSignaturePart(FindingDisplayId(second.Id)), StringComparison.Ordinal) ||
            !string.Equals(NormalizeSignaturePart(first.Summary), NormalizeSignaturePart(second.Summary), StringComparison.Ordinal) ||
            !string.Equals(NormalizeSignaturePart(first.AriaProperties), NormalizeSignaturePart(second.AriaProperties), StringComparison.Ordinal))
        {
            return false;
        }

        var firstAutomationId = NormalizeSignaturePart(first.AutomationId);
        var secondAutomationId = NormalizeSignaturePart(second.AutomationId);
        if ((firstAutomationId.Length > 0 || secondAutomationId.Length > 0) &&
            !string.Equals(firstAutomationId, secondAutomationId, StringComparison.Ordinal))
        {
            return false;
        }

        var sameBounds = AreEquivalentBounds(first.ElementBounds, second.ElementBounds);
        var samePath = HaveRelatedElementPaths(first.NodeTrace, second.NodeTrace);
        if (sameBounds && samePath)
        {
            return true;
        }

        if (firstAutomationId.Length > 0 && sameBounds)
        {
            return true;
        }

        return samePath && (first.ElementBounds is null || second.ElementBounds is null);
    }

    private static bool AreEquivalentBounds(AvaElementBounds? first, AvaElementBounds? second)
        => first is not null &&
            second is not null &&
            string.Equals(NormalizeBounds(first), NormalizeBounds(second), StringComparison.Ordinal);

    private static bool HaveRelatedElementPaths(string? first, string? second)
    {
        var firstSegments = NormalizeElementPathSegments(first);
        var secondSegments = NormalizeElementPathSegments(second);
        if (firstSegments.Count == 0 || secondSegments.Count == 0)
        {
            return false;
        }

        return HasTrailingSegmentMatch(firstSegments, secondSegments, 3) ||
            HasTrailingSegmentMatch(firstSegments, secondSegments, 2);
    }

    private static IReadOnlyList<string> NormalizeElementPathSegments(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value
                .Split(" / ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static segment => !string.Equals(segment, "...", StringComparison.Ordinal))
                .Select(NormalizeSignaturePart)
                .Where(static segment => segment.Length > 0)
                .ToArray();

    private static bool HasTrailingSegmentMatch(
        IReadOnlyList<string> firstSegments,
        IReadOnlyList<string> secondSegments,
        int segmentCount)
    {
        if (firstSegments.Count < segmentCount || secondSegments.Count < segmentCount)
        {
            return false;
        }

        for (var index = 1; index <= segmentCount; index++)
        {
            if (!string.Equals(
                    firstSegments[^index],
                    secondSegments[^index],
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeSignaturePart(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Join(
                " ",
                value.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                .Trim()
                .ToLowerInvariant();

    private static string NormalizeBounds(AvaElementBounds? bounds)
        => bounds is null
            ? string.Empty
            : string.Join(
                ":",
                Math.Round(bounds.Left).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Math.Round(bounds.Top).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Math.Round(bounds.Width).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Math.Round(bounds.Height).ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static int GetStatusCount(IReadOnlyDictionary<string, int> counts, string status)
        => counts.TryGetValue(status, out var count) ? count : 0;

    private static void AppendStepScreenshots(
        StringBuilder builder,
        AvaStepResult step,
        string? outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return;
        }

        var screenshots = LoadStepScreenshots(outputDirectory, step);
        if (screenshots.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("#### Screenshots");
        builder.AppendLine();

        foreach (var screenshot in screenshots)
        {
            builder.AppendLine($"**{screenshot.Label}**");
            builder.AppendLine();
            builder.AppendLine($"![{EscapeImageAltText(screenshot.AltText)}]({EscapeLinkDestination(screenshot.ReportPath)})");
            builder.AppendLine();
        }
    }

    private static void AppendStepWebEvidence(
        StringBuilder builder,
        AvaStepResult step,
        string? outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return;
        }

        var webEvidence = LoadStepWebEvidence(outputDirectory, step);
        if (webEvidence.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("#### Web Evidence");
        builder.AppendLine();

        foreach (var evidence in webEvidence)
        {
            if (string.Equals(evidence.Status, AvaEvidenceStatus.Captured, StringComparison.Ordinal) &&
                evidence.HtmlLinks.Count > 0)
            {
                var links = string.Join(
                    ", ",
                    evidence.HtmlLinks.Select(link => $"[{EscapeLinkText(link.Label)}]({EscapeLinkDestination(link.ReportPath)})"));
                builder.AppendLine($"- {TextCell(evidence.Label)}: {links}");
            }
            else
            {
                builder.AppendLine($"- {TextCell(evidence.Label)}: {TextCell(evidence.Summary ?? evidence.Status)}");
            }
        }
    }

    private static IReadOnlyList<StepWebEvidence> LoadStepWebEvidence(
        string outputDirectory,
        AvaStepResult step)
    {
        var manifestPath = ResolvePath(outputDirectory, step.Evidence.ManifestPath);
        if (!File.Exists(manifestPath))
        {
            return [];
        }

        AvaEvidenceManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<AvaEvidenceManifest>(
                File.ReadAllText(manifestPath, Encoding.UTF8),
                JsonOptions);
        }
        catch (JsonException)
        {
            return [];
        }

        if (manifest is null)
        {
            return [];
        }

        var manifestDirectory = Path.GetDirectoryName(manifestPath);
        if (string.IsNullOrWhiteSpace(manifestDirectory))
        {
            return [];
        }

        return manifest.Entries
            .Where(static entry => string.Equals(entry.ToolName, "web_dom_snapshot", StringComparison.Ordinal))
            .Select(entry => LoadStepWebEvidenceEntry(outputDirectory, manifestDirectory, entry))
            .Where(static entry => entry is not null)
            .Select(static entry => entry!)
            .ToArray();
    }

    private static StepWebEvidence? LoadStepWebEvidenceEntry(
        string outputDirectory,
        string manifestDirectory,
        AvaEvidenceManifestEntry entry)
    {
        var htmlLinks = entry.Artifacts
            .Where(static artifact => string.Equals(artifact.Kind, "html", StringComparison.OrdinalIgnoreCase))
            .Select(artifact =>
            {
                var fullPath = ResolvePath(manifestDirectory, artifact.Path);
                if (!File.Exists(fullPath))
                {
                    return null;
                }

                return new StepWebEvidenceLink(
                    string.IsNullOrWhiteSpace(artifact.Label) ? "html" : artifact.Label!,
                    ToRelativeReportPath(outputDirectory, fullPath));
            })
            .Where(static link => link is not null)
            .Select(static link => link!)
            .ToArray();

        if (htmlLinks.Length == 0 &&
            string.Equals(entry.Status, AvaEvidenceStatus.Captured, StringComparison.Ordinal))
        {
            return null;
        }

        return new StepWebEvidence(
            HumanizeIdentifier(entry.ToolName),
            entry.Status,
            entry.Summary ?? entry.Error,
            htmlLinks);
    }

    private static IReadOnlyList<StepScreenshot> LoadStepScreenshots(
        string outputDirectory,
        AvaStepResult step)
    {
        var manifestPath = ResolvePath(outputDirectory, step.Evidence.ManifestPath);
        if (!File.Exists(manifestPath))
        {
            return [];
        }

        AvaEvidenceManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<AvaEvidenceManifest>(
                File.ReadAllText(manifestPath, Encoding.UTF8),
                JsonOptions);
        }
        catch (JsonException)
        {
            return [];
        }

        if (manifest is null)
        {
            return [];
        }

        var manifestDirectory = Path.GetDirectoryName(manifestPath);
        if (string.IsNullOrWhiteSpace(manifestDirectory))
        {
            return [];
        }

        var allScreenshots = new List<StepScreenshot>();
        var dedicatedScreenshots = new List<StepScreenshot>();
        foreach (var entry in manifest.Entries)
        {
            var screenshot = TryLoadStepScreenshot(outputDirectory, manifestDirectory, step, entry);
            if (screenshot is null)
            {
                continue;
            }

            allScreenshots.Add(screenshot);
            if (string.Equals(entry.ToolName, "capture_window_screenshot", StringComparison.Ordinal))
            {
                dedicatedScreenshots.Add(screenshot);
            }
        }

        return dedicatedScreenshots.Count > 0 ? dedicatedScreenshots : allScreenshots;
    }

    private static StepScreenshot? TryLoadStepScreenshot(
        string outputDirectory,
        string manifestDirectory,
        AvaStepResult step,
        AvaEvidenceManifestEntry entry)
    {
        if (!string.Equals(entry.Status, AvaEvidenceStatus.Captured, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(entry.RawOutputPath))
        {
            return null;
        }

        var rawOutputPath = ResolvePath(manifestDirectory, entry.RawOutputPath);
        if (!File.Exists(rawOutputPath))
        {
            return null;
        }

        var screenshotInfo = TryExtractScreenshotInfo(File.ReadAllText(rawOutputPath, Encoding.UTF8));
        if (screenshotInfo is null || string.IsNullOrWhiteSpace(screenshotInfo.ImagePath))
        {
            return null;
        }

        var sourceImagePath = ResolvePath(manifestDirectory, screenshotInfo.ImagePath);
        if (!File.Exists(sourceImagePath))
        {
            return null;
        }

        var screenshotDirectory = Path.Combine(manifestDirectory, "screenshots");
        var extension = Path.GetExtension(sourceImagePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".png";
        }

        var targetPath = Path.Combine(
            screenshotDirectory,
            $"{entry.Sequence:000}-{SanitizeFileNameSegment(entry.ToolName)}{extension.ToLowerInvariant()}");

        try
        {
            Directory.CreateDirectory(screenshotDirectory);
            if (!string.Equals(
                    Path.GetFullPath(sourceImagePath),
                    Path.GetFullPath(targetPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourceImagePath, targetPath, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }

        var checkpointLabel = FormatCheckpointTiming(step.Checkpoints.LastOrDefault()?.Timing);
        var toolLabel = HumanizeIdentifier(entry.ToolName);
        return new StepScreenshot(
            $"{checkpointLabel} - {toolLabel}",
            ToRelativeReportPath(outputDirectory, targetPath),
            $"{step.StepId} {checkpointLabel} {toolLabel}",
            targetPath,
            screenshotInfo.WindowBounds,
            screenshotInfo.ImageSize);
    }

    private static ScreenshotInfo? TryExtractScreenshotInfo(string rawOutput)
    {
        try
        {
            using var document = JsonDocument.Parse(rawOutput);
            var root = document.RootElement;
            string? imagePath = null;
            _ = TryGetStringProperty(root, "imagePath", out imagePath) ||
                TryGetStringProperty(root, "screenshotPath", out imagePath);

            if (TryGetProperty(root, "screenshot", out var screenshot))
            {
                _ = TryGetStringProperty(screenshot, "filePath", out imagePath) ||
                    TryGetStringProperty(screenshot, "imagePath", out imagePath) ||
                    TryGetStringProperty(screenshot, "path", out imagePath);
            }

            if (string.IsNullOrWhiteSpace(imagePath))
            {
                return null;
            }

            var windowBounds = TryExtractWindowBounds(root);
            var imageSize = TryExtractImageSize(root);
            return new ScreenshotInfo(imagePath, windowBounds, imageSize);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AvaElementBounds? TryExtractWindowBounds(JsonElement root)
    {
        if (TryGetProperty(root, "window", out var window) &&
            TryGetProperty(window, "bounds", out var bounds) &&
            TryGetBounds(bounds, out var parsedBounds))
        {
            return parsedBounds;
        }

        return TryGetProperty(root, "bounds", out bounds) &&
               TryGetBounds(bounds, out parsedBounds)
            ? parsedBounds
            : null;
    }

    private static ScreenshotImageSize? TryExtractImageSize(JsonElement root)
    {
        if (TryGetProperty(root, "imageSize", out var imageSize) &&
            TryGetImageSize(imageSize, out var parsedSize))
        {
            return parsedSize;
        }

        if (TryGetProperty(root, "screenshot", out var screenshot) &&
            TryGetProperty(screenshot, "imageSize", out imageSize) &&
            TryGetImageSize(imageSize, out parsedSize))
        {
            return parsedSize;
        }

        return null;
    }

    private static bool TryGetBounds(JsonElement element, out AvaElementBounds bounds)
    {
        bounds = null!;
        if (element.ValueKind != JsonValueKind.Object ||
            !TryGetDoubleProperty(element, "left", out var left) ||
            !TryGetDoubleProperty(element, "top", out var top) ||
            !TryGetDoubleProperty(element, "width", out var width) ||
            !TryGetDoubleProperty(element, "height", out var height) ||
            width <= 0 ||
            height <= 0)
        {
            return false;
        }

        bounds = new AvaElementBounds(left, top, width, height);
        return true;
    }

    private static bool TryGetImageSize(JsonElement element, out ScreenshotImageSize imageSize)
    {
        imageSize = null!;
        if (element.ValueKind != JsonValueKind.Object ||
            !TryGetDoubleProperty(element, "width", out var width) ||
            !TryGetDoubleProperty(element, "height", out var height) ||
            width <= 0 ||
            height <= 0)
        {
            return false;
        }

        imageSize = new ScreenshotImageSize(width, height);
        return true;
    }

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

        return property.ValueKind == JsonValueKind.String &&
               double.TryParse(
                   property.GetString(),
                   System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out value);
    }

    private static bool TryGetStringProperty(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (!TryGetProperty(element, propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return !string.IsNullOrWhiteSpace(value);
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

    private static string FormatCheckpointTiming(string? timing)
        => timing switch
        {
            "before" => "Before checkpoint",
            "during" => "During checkpoint",
            "after" or null or "" => "After checkpoint",
            _ => $"{HumanizeIdentifier(timing)} checkpoint"
        };

    private static string HumanizeIdentifier(string value)
        => string.Join(
            " ",
            value.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static token => token.Length == 0
                    ? token
                    : string.Concat(char.ToUpperInvariant(token[0]), token[1..].ToLowerInvariant())));

    private static string ToRelativeReportPath(string outputDirectory, string path)
        => Path.GetRelativePath(outputDirectory, path).Replace('\\', '/');

    private static string ResolvePath(string baseDirectory, string path)
    {
        var normalizedPath = path.Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(normalizedPath)
            ? normalizedPath
            : Path.GetFullPath(normalizedPath, baseDirectory);
    }

    private static string SanitizeFileNameSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(invalid.Contains(ch) ? '-' : ch);
        }

        return builder.ToString();
    }

    private sealed record StepScreenshot(
        string Label,
        string ReportPath,
        string AltText,
        string FullPath,
        AvaElementBounds? WindowBounds,
        ScreenshotImageSize? ImageSize);

    private sealed record ScreenshotInfo(
        string ImagePath,
        AvaElementBounds? WindowBounds,
        ScreenshotImageSize? ImageSize);

    private sealed record ScreenshotImageSize(
        double Width,
        double Height);

    private sealed record StepWebEvidence(
        string Label,
        string Status,
        string? Summary,
        IReadOnlyList<StepWebEvidenceLink> HtmlLinks);

    private sealed record StepWebEvidenceLink(
        string Label,
        string ReportPath);

    private static void AppendFindingsTable(
        StringBuilder builder,
        AvaStepResult step,
        string? outputDirectory)
    {
        builder.AppendLine();
        builder.AppendLine("#### Findings");
        builder.AppendLine();

        if (step.Findings.Count == 0)
        {
            builder.AppendLine("_No findings._");
            return;
        }

        var automatedFailures = step.Findings
            .Where(static finding => string.Equals(
                finding.TriageCategory,
                AvaTriageCategory.AutomatedFailure,
                StringComparison.Ordinal))
            .ToArray();
        var humanReviewNeeded = step.Findings
            .Where(static finding => string.Equals(
                finding.TriageCategory,
                AvaTriageCategory.NeedsHumanReview,
                StringComparison.Ordinal))
            .ToArray();
        var evidenceGaps = step.Findings
            .Where(static finding => string.Equals(
                finding.TriageCategory,
                AvaTriageCategory.EvidenceGap,
                StringComparison.Ordinal))
            .ToArray();
        var notTestedFindings = step.Findings
            .Where(static finding => string.Equals(
                finding.TriageCategory,
                AvaTriageCategory.NotTested,
                StringComparison.Ordinal))
            .ToArray();
        var otherFindings = step.Findings
            .Where(static finding =>
                !string.Equals(finding.TriageCategory, AvaTriageCategory.AutomatedFailure, StringComparison.Ordinal) &&
                !string.Equals(finding.TriageCategory, AvaTriageCategory.NeedsHumanReview, StringComparison.Ordinal) &&
                !string.Equals(finding.TriageCategory, AvaTriageCategory.EvidenceGap, StringComparison.Ordinal) &&
                !string.Equals(finding.TriageCategory, AvaTriageCategory.NotTested, StringComparison.Ordinal))
            .ToArray();

        AppendFindingCategoryTable(
            builder,
            "Automated Failures",
            automatedFailures,
            "_No automated failures._",
            step,
            outputDirectory);
        AppendFindingCategoryTable(
            builder,
            "Human Review Needed",
            humanReviewNeeded,
            "_No human review findings._",
            step,
            outputDirectory);
        if (evidenceGaps.Length > 0)
        {
            AppendFindingCategoryTable(
                builder,
                "Evidence Gaps",
                evidenceGaps,
                "_No evidence gaps._",
                step,
                outputDirectory);
        }

        if (notTestedFindings.Length > 0)
        {
            AppendFindingCategoryTable(
                builder,
                "Not Tested",
                notTestedFindings,
                "_No not-tested findings._",
                step,
                outputDirectory);
        }

        if (otherFindings.Length > 0)
        {
            AppendFindingCategoryTable(
                builder,
                "Other Findings",
                otherFindings,
                "_No other findings._",
                step,
                outputDirectory);
        }
    }

    private static void AppendFindingCategoryTable(
        StringBuilder builder,
        string heading,
        IReadOnlyList<AvaAccessibilityFinding> findings,
        string emptyMessage,
        AvaStepResult step,
        string? outputDirectory)
    {
        builder.AppendLine($"##### {heading} ({findings.Count})");
        builder.AppendLine();
        if (findings.Count == 0)
        {
            builder.AppendLine(emptyMessage);
            builder.AppendLine();
            return;
        }

        builder.AppendLine("| Finding | Source | Checkpoint | Summary | Rule | Evidence | Element Path | Automation ID | ARIA |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");

        foreach (var finding in findings)
        {
            builder.Append("| ");
            builder.Append(CodeCell(FindingDisplayId(finding.Id)));
            builder.Append(" | ");
            builder.Append(CodeCell(FindingSource(finding)));
            builder.Append(" | ");
            builder.Append(CodeCell(finding.Checkpoint));
            builder.Append(" | ");
            builder.Append(TextCell(finding.Summary));
            builder.Append(" | ");
            builder.Append(RuleCell(finding, outputDirectory));
            builder.Append(" | ");
            builder.Append(EvidenceLinkCell(finding, step, outputDirectory));
            builder.Append(" | ");
            builder.Append(CodeCell(finding.NodeTrace));
            builder.Append(" | ");
            builder.Append(CodeCell(finding.AutomationId));
            builder.Append(" | ");
            builder.Append(CodeCell(finding.AriaProperties));
            builder.AppendLine(" |");
        }

        builder.AppendLine();
    }

    private static string FindingDisplayId(string findingId)
    {
        var tokens = findingId.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var stepTokenIndex = Array.FindIndex(tokens, IsOrdinalToken);
        if (stepTokenIndex <= 0)
        {
            return findingId;
        }

        return string.Join('-', tokens.Take(stepTokenIndex));
    }

    private static string? FindingSource(AvaAccessibilityFinding finding)
        => string.IsNullOrWhiteSpace(finding.ToolName)
            ? SourceFromFindingId(finding.Id)
            : NormalizeSourceToken(finding.ToolName);

    private static string? SourceFromFindingId(string findingId)
    {
        var tokens = findingId.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var stepTokenIndex = Array.FindIndex(tokens, IsOrdinalToken);
        if (stepTokenIndex < 0 || stepTokenIndex == tokens.Length - 1)
        {
            return null;
        }

        var sourceTokens = tokens
            .Skip(stepTokenIndex + 1)
            .ToArray();
        if (sourceTokens.Length > 0 && IsOrdinalToken(sourceTokens[^1]))
        {
            sourceTokens = sourceTokens[..^1];
        }

        return sourceTokens.Length == 0 ? null : string.Join('-', sourceTokens);
    }

    private static bool IsOrdinalToken(string token)
        => token.Length == 3 && token.All(char.IsDigit);

    private static string NormalizeSourceToken(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasSeparator = false;

        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
                previousWasSeparator = false;
                continue;
            }

            if (!previousWasSeparator)
            {
                builder.Append('-');
                previousWasSeparator = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static string RuleCell(AvaAccessibilityFinding finding, string? outputDirectory)
    {
        var ruleIdentifier = RuleIdentifier(finding);
        if (string.IsNullOrWhiteSpace(ruleIdentifier))
        {
            return string.Empty;
        }

        return $"[{EscapeLinkText(ruleIdentifier)}]({EscapeLinkDestination(RuleDocumentPath(ruleIdentifier, outputDirectory))})";
    }

    private static string? RuleIdentifier(AvaAccessibilityFinding finding)
    {
        if (string.IsNullOrWhiteSpace(finding.RuleId))
        {
            return string.IsNullOrWhiteSpace(finding.ProfileId)
                ? null
                : finding.ProfileId.Trim().ToLowerInvariant();
        }

        var normalizedRuleId = finding.RuleId.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(finding.ProfileId)
            ? normalizedRuleId
            : $"{finding.ProfileId.Trim().ToLowerInvariant()}-{normalizedRuleId}";
    }

    private static string RuleDocumentPath(string ruleIdentifier, string? outputDirectory)
    {
        var relativeDocsPath = $"docs/ava/rules/{ruleIdentifier}.md";
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return relativeDocsPath;
        }

        var repoRoot = FindRepositoryRoot(outputDirectory);
        if (repoRoot is null)
        {
            return relativeDocsPath;
        }

        return ToRelativeReportPath(
            outputDirectory,
            Path.Combine(repoRoot, "docs", "ava", "rules", $"{ruleIdentifier}.md"));
    }

    private static string? FindRepositoryRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "docs", "README.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string CodeCell(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : $"`{EscapeTableCell(value).Replace('`', '\'')}`";

    private static string TextCell(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : EscapeTableCell(value);

    private static string EvidenceLinkCell(
        AvaAccessibilityFinding finding,
        AvaStepResult step,
        string? outputDirectory)
    {
        var links = new List<string>();
        var manifestLink = EvidenceManifestLink(finding.EvidenceReference);
        if (!string.IsNullOrWhiteSpace(manifestLink))
        {
            links.Add(manifestLink);
        }

        if (string.Equals(finding.ToolName, "web_dom_snapshot", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(outputDirectory))
        {
            foreach (var htmlLink in LoadStepWebEvidence(outputDirectory, step)
                         .SelectMany(static evidence => evidence.HtmlLinks))
            {
                links.Add($"[{EscapeLinkText(htmlLink.Label)}]({EscapeLinkDestination(htmlLink.ReportPath)})");
            }
        }

        var highlightedScreenshotPath = TryCreateHighlightedScreenshot(outputDirectory, step, finding);
        if (!string.IsNullOrWhiteSpace(highlightedScreenshotPath))
        {
            links.Add($"[picture]({EscapeLinkDestination(highlightedScreenshotPath)})");
        }

        return string.Join("<br>", links);
    }

    private static void ClearHighlightDirectories(string? outputDirectory, AvaValidationReport report)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return;
        }

        foreach (var step in report.Steps)
        {
            var manifestPath = ResolvePath(outputDirectory, step.Evidence.ManifestPath);
            var manifestDirectory = Path.GetDirectoryName(manifestPath);
            if (string.IsNullOrWhiteSpace(manifestDirectory))
            {
                continue;
            }

            var highlightDirectory = Path.Combine(manifestDirectory, "highlights");
            if (!Directory.Exists(highlightDirectory))
            {
                continue;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(highlightDirectory, "*.png"))
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static string EvidenceManifestLink(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var destination = value.Trim().Replace('\\', '/');
        var label = Path.GetFileName(destination);
        if (string.IsNullOrWhiteSpace(label))
        {
            label = destination;
        }

        return $"[{EscapeLinkText(label)}]({EscapeLinkDestination(destination)})";
    }

    private static string? TryCreateHighlightedScreenshot(
        string? outputDirectory,
        AvaStepResult step,
        AvaAccessibilityFinding finding)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory) || finding.ElementBounds is null)
        {
            return null;
        }

        var screenshot = LoadStepScreenshots(outputDirectory, step).FirstOrDefault();
        if (screenshot is null || screenshot.WindowBounds is null || !File.Exists(screenshot.FullPath))
        {
            return null;
        }

        var manifestPath = ResolvePath(outputDirectory, step.Evidence.ManifestPath);
        var manifestDirectory = Path.GetDirectoryName(manifestPath);
        if (string.IsNullOrWhiteSpace(manifestDirectory))
        {
            return null;
        }

        var highlightDirectory = Path.Combine(manifestDirectory, "highlights");
        var targetPath = Path.Combine(highlightDirectory, $"{finding.ExportId}.png");

        try
        {
            Directory.CreateDirectory(highlightDirectory);
            using var bitmap = new Bitmap(screenshot.FullPath);
            if (!TryMapBoundsToImage(finding.ElementBounds, screenshot.WindowBounds, bitmap.Width, bitmap.Height, out var rectangle))
            {
                return null;
            }

            using var graphics = Graphics.FromImage(bitmap);
            using var outerPen = new Pen(Color.Black, 8);
            using var innerPen = new Pen(Color.FromArgb(255, 255, 0), 4);
            graphics.DrawRectangle(outerPen, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
            graphics.DrawRectangle(innerPen, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
            bitmap.Save(targetPath, ImageFormat.Png);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or ExternalException)
        {
            return null;
        }

        return ToRelativeReportPath(outputDirectory, targetPath);
    }

    private static bool TryMapBoundsToImage(
        AvaElementBounds elementBounds,
        AvaElementBounds windowBounds,
        int imageWidth,
        int imageHeight,
        out RectangleF rectangle)
    {
        rectangle = RectangleF.Empty;
        if (windowBounds.Width <= 0 ||
            windowBounds.Height <= 0 ||
            imageWidth <= 0 ||
            imageHeight <= 0)
        {
            return false;
        }

        var scaleX = imageWidth / windowBounds.Width;
        var scaleY = imageHeight / windowBounds.Height;
        var x = (float)((elementBounds.Left - windowBounds.Left) * scaleX);
        var y = (float)((elementBounds.Top - windowBounds.Top) * scaleY);
        var width = (float)(elementBounds.Width * scaleX);
        var height = (float)(elementBounds.Height * scaleY);

        var imageRectangle = new RectangleF(0, 0, imageWidth - 1, imageHeight - 1);
        var mapped = RectangleF.Intersect(new RectangleF(x, y, width, height), imageRectangle);
        if (mapped.Width <= 0 || mapped.Height <= 0)
        {
            return false;
        }

        const float minimumVisibleSize = 12;
        if (mapped.Width < minimumVisibleSize)
        {
            var center = mapped.Left + mapped.Width / 2;
            mapped.X = center - minimumVisibleSize / 2;
            mapped.Width = minimumVisibleSize;
        }

        if (mapped.Height < minimumVisibleSize)
        {
            var center = mapped.Top + mapped.Height / 2;
            mapped.Y = center - minimumVisibleSize / 2;
            mapped.Height = minimumVisibleSize;
        }

        mapped = RectangleF.Intersect(mapped, imageRectangle);
        if (mapped.Width <= 0 || mapped.Height <= 0)
        {
            return false;
        }

        rectangle = mapped;
        return true;
    }

    private static string EscapeLinkText(string value)
        => EscapeTableCell(value)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);

    private static string EscapeImageAltText(string value)
        => value
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal)
            .Trim();

    private static string EscapeLinkDestination(string value)
        => value
            .Replace(" ", "%20", StringComparison.Ordinal)
            .Replace("(", "%28", StringComparison.Ordinal)
            .Replace(")", "%29", StringComparison.Ordinal)
            .Replace("|", "%7C", StringComparison.Ordinal);

    private static string EscapeTableCell(string value)
        => value
            .Trim()
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\n", "<br>", StringComparison.Ordinal);

}

internal sealed record AvaReportWriteResult(
    string MarkdownPath,
    string JsonPath);
