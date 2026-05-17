using System.Text;
using System.Text.Json;

namespace HeronWin.Ava;

internal static class AvaReportRegenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static AvaReportWriteResult Regenerate(string reportPathOrKeyword, string workingDirectory)
    {
        var reportJsonPath = ResolveReportJsonPath(reportPathOrKeyword, workingDirectory);
        var outputDirectory = Path.GetDirectoryName(reportJsonPath)
                              ?? throw new InvalidOperationException($"Could not resolve report directory for: {reportJsonPath}");
        var sourceReport = AvaReportWriter.ReadJson(reportJsonPath);
        var regeneratedReport = Regenerate(sourceReport, outputDirectory);

        return AvaReportWriter.Write(regeneratedReport, outputDirectory);
    }

    internal static AvaValidationReport Regenerate(AvaValidationReport sourceReport, string outputDirectory)
        => sourceReport with
        {
            Steps = sourceReport.Steps
                .Select(step => RegenerateStep(sourceReport, outputDirectory, step))
                .ToArray(),
        };

    private static AvaStepResult RegenerateStep(
        AvaValidationReport sourceReport,
        string outputDirectory,
        AvaStepResult step)
    {
        var records = LoadEvidenceRecords(outputDirectory, step);
        var validationCheckpoint = step.Checkpoints.LastOrDefault()?.Timing ?? AvaCheckpointTiming.After;
        var deterministicFindings = AvaDeterministicValidators.Validate(new AvaDeterministicValidationContext(
            step.Index,
            step.StepId,
            sourceReport.Profile,
            validationCheckpoint,
            step.Evidence,
            records));
        var preservedExecutionFindings = step.Findings
            .Where(static finding => finding.Id.StartsWith("AVA-EXECUTION-FAILED-", StringComparison.Ordinal))
            .ToArray();
        var findings = deterministicFindings
            .Concat(preservedExecutionFindings)
            .ToArray();

        return step with
        {
            Checkpoints = RecalculateCheckpoints(step.Checkpoints, validationCheckpoint, findings),
            Findings = findings,
        };
    }

    private static IReadOnlyList<AvaCheckpointResult> RecalculateCheckpoints(
        IReadOnlyList<AvaCheckpointResult> sourceCheckpoints,
        string validationCheckpoint,
        IReadOnlyList<AvaAccessibilityFinding> findings)
        => sourceCheckpoints
            .Select(checkpoint =>
            {
                var checkpointFindings = findings
                    .Where(finding => string.Equals(finding.Checkpoint, checkpoint.Timing, StringComparison.Ordinal))
                    .ToArray();
                if (checkpointFindings.Length == 0 &&
                    !string.Equals(checkpoint.Timing, validationCheckpoint, StringComparison.Ordinal))
                {
                    return new AvaCheckpointResult(
                        checkpoint.Timing,
                        AvaFindingStatus.NotTested,
                        "Deterministic validators run after scenario steps; this checkpoint was not evaluated.");
                }

                var status = AvaFindingStatus.Aggregate(checkpointFindings.Select(static finding => finding.Status));
                var summary = status == AvaFindingStatus.Pass
                    ? "Deterministic accessibility validators completed without findings for this checkpoint."
                    : $"Deterministic accessibility validators produced {checkpointFindings.Length} finding(s) for this checkpoint.";

                return new AvaCheckpointResult(
                    checkpoint.Timing,
                    status,
                    summary);
            })
            .ToArray();

    private static IReadOnlyList<AvaEvidenceRecord> LoadEvidenceRecords(
        string outputDirectory,
        AvaStepResult step)
    {
        var manifestPath = ResolvePath(outputDirectory, step.Evidence.ManifestPath);
        if (!File.Exists(manifestPath))
        {
            return
            [
                AvaEvidenceRecord.Missing(
                    $"Saved evidence manifest was not found: {step.Evidence.ManifestPath}")
            ];
        }

        var manifest = JsonSerializer.Deserialize<AvaEvidenceManifest>(
                           File.ReadAllText(manifestPath, Encoding.UTF8),
                           JsonOptions)
                       ?? throw new InvalidOperationException($"Saved evidence manifest was empty or invalid: {manifestPath}");
        var manifestDirectory = Path.GetDirectoryName(manifestPath)
                                ?? throw new InvalidOperationException($"Could not resolve evidence directory for: {manifestPath}");

        return manifest.Entries
            .Select(entry => LoadEvidenceRecord(manifestDirectory, entry))
            .ToArray();
    }

    private static AvaEvidenceRecord LoadEvidenceRecord(
        string manifestDirectory,
        AvaEvidenceManifestEntry entry)
    {
        var rawOutput = string.IsNullOrWhiteSpace(entry.RawOutputPath)
            ? null
            : ReadOptionalRawOutput(manifestDirectory, entry.RawOutputPath);

        return new AvaEvidenceRecord(
            entry.ToolName,
            entry.Status,
            entry.McpCallId,
            rawOutput,
            entry.Summary,
            entry.Error);
    }

    private static string? ReadOptionalRawOutput(string manifestDirectory, string rawOutputPath)
    {
        var fullPath = ResolvePath(manifestDirectory, rawOutputPath);
        return File.Exists(fullPath)
            ? File.ReadAllText(fullPath, Encoding.UTF8)
            : null;
    }

    private static string ResolveReportJsonPath(string reportPathOrKeyword, string workingDirectory)
    {
        if (string.Equals(reportPathOrKeyword, "latest", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveLatestReportJsonPath(workingDirectory);
        }

        var path = Path.GetFullPath(reportPathOrKeyword, workingDirectory);
        if (Directory.Exists(path))
        {
            path = Path.Combine(path, "report.json");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"AVA report JSON was not found: {path}", path);
        }

        return path;
    }

    private static string ResolveLatestReportJsonPath(string workingDirectory)
    {
        var avaArtifactsDirectory = Path.Combine(Path.GetFullPath(workingDirectory), "artifacts", "ava");
        if (!Directory.Exists(avaArtifactsDirectory))
        {
            throw new FileNotFoundException($"AVA artifacts directory was not found: {avaArtifactsDirectory}", avaArtifactsDirectory);
        }

        return Directory
                   .EnumerateFiles(avaArtifactsDirectory, "report.json", SearchOption.AllDirectories)
                   .Select(path => new FileInfo(path))
                   .OrderByDescending(file => file.LastWriteTimeUtc)
                   .FirstOrDefault()
                   ?.FullName
               ?? throw new FileNotFoundException($"No AVA report.json files were found under: {avaArtifactsDirectory}");
    }

    private static string ResolvePath(string baseDirectory, string path)
    {
        var normalizedPath = path.Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(normalizedPath)
            ? normalizedPath
            : Path.GetFullPath(normalizedPath, baseDirectory);
    }
}
