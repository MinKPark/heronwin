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

        var jsonPath = Path.Combine(fullOutputDirectory, "report.json");
        var markdownPath = Path.Combine(fullOutputDirectory, "report.md");

        File.WriteAllText(jsonPath, ToJson(report), Encoding.UTF8);
        File.WriteAllText(markdownPath, ToMarkdown(report, fullOutputDirectory), Encoding.UTF8);

        return new AvaReportWriteResult(markdownPath, jsonPath);
    }

    public static string ToJson(AvaValidationReport report)
        => JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine;

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
            AppendFindingsTable(builder, step.Findings, outputDirectory);
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

        var imagePath = TryExtractScreenshotImagePath(File.ReadAllText(rawOutputPath, Encoding.UTF8));
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        var sourceImagePath = ResolvePath(manifestDirectory, imagePath);
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
            $"{step.StepId} {checkpointLabel} {toolLabel}");
    }

    private static string? TryExtractScreenshotImagePath(string rawOutput)
    {
        try
        {
            using var document = JsonDocument.Parse(rawOutput);
            var root = document.RootElement;
            if (TryGetStringProperty(root, "imagePath", out var imagePath) ||
                TryGetStringProperty(root, "screenshotPath", out imagePath))
            {
                return imagePath;
            }

            if (TryGetProperty(root, "screenshot", out var screenshot) &&
                (TryGetStringProperty(screenshot, "filePath", out imagePath) ||
                 TryGetStringProperty(screenshot, "imagePath", out imagePath) ||
                 TryGetStringProperty(screenshot, "path", out imagePath)))
            {
                return imagePath;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
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
        string AltText);

    private static void AppendFindingsTable(
        StringBuilder builder,
        IReadOnlyList<AvaAccessibilityFinding> findings,
        string? outputDirectory)
    {
        builder.AppendLine();
        builder.AppendLine("#### Findings");
        builder.AppendLine();

        if (findings.Count == 0)
        {
            builder.AppendLine("_No findings._");
            return;
        }

        var automatedFailures = findings
            .Where(static finding => string.Equals(
                finding.TriageCategory,
                AvaTriageCategory.AutomatedFailure,
                StringComparison.Ordinal))
            .ToArray();
        var humanReviewNeeded = findings
            .Where(static finding => string.Equals(
                finding.TriageCategory,
                AvaTriageCategory.NeedsHumanReview,
                StringComparison.Ordinal))
            .ToArray();
        var evidenceGaps = findings
            .Where(static finding => string.Equals(
                finding.TriageCategory,
                AvaTriageCategory.EvidenceGap,
                StringComparison.Ordinal))
            .ToArray();
        var notTestedFindings = findings
            .Where(static finding => string.Equals(
                finding.TriageCategory,
                AvaTriageCategory.NotTested,
                StringComparison.Ordinal))
            .ToArray();
        var otherFindings = findings
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
            outputDirectory);
        AppendFindingCategoryTable(
            builder,
            "Human Review Needed",
            humanReviewNeeded,
            "_No human review findings._",
            outputDirectory);
        if (evidenceGaps.Length > 0)
        {
            AppendFindingCategoryTable(
                builder,
                "Evidence Gaps",
                evidenceGaps,
                "_No evidence gaps._",
                outputDirectory);
        }

        if (notTestedFindings.Length > 0)
        {
            AppendFindingCategoryTable(
                builder,
                "Not Tested",
                notTestedFindings,
                "_No not-tested findings._",
                outputDirectory);
        }

        if (otherFindings.Length > 0)
        {
            AppendFindingCategoryTable(
                builder,
                "Other Findings",
                otherFindings,
                "_No other findings._",
                outputDirectory);
        }
    }

    private static void AppendFindingCategoryTable(
        StringBuilder builder,
        string heading,
        IReadOnlyList<AvaAccessibilityFinding> findings,
        string emptyMessage,
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
            builder.Append(EvidenceLinkCell(finding.EvidenceReference));
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

    private static string EvidenceLinkCell(string? value)
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
