using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeronWin.Brain;

namespace HeronWin.Ava;

internal static class AvaEvidenceStatus
{
    public const string Captured = "captured";
    public const string Missing = "missing";
    public const string Error = "error";
}

internal sealed record AvaEvidenceRecord(
    string ToolName,
    string Status,
    long? McpCallId,
    string? RawOutput,
    string? Summary,
    string? Error)
{
    public static AvaEvidenceRecord Missing(string summary)
        => new(
            "ava.evidence",
            AvaEvidenceStatus.Missing,
            null,
            null,
            summary,
            null);

    public static AvaEvidenceRecord ErrorResult(string toolName, string error, string? rawOutput = null, long? mcpCallId = null)
        => new(
            toolName,
            AvaEvidenceStatus.Error,
            mcpCallId,
            rawOutput,
            null,
            error);
}

internal sealed record AvaEvidenceManifest(
    int Version,
    string RunId,
    string StepId,
    int StepIndex,
    string StepName,
    string? WindowHandle,
    IReadOnlyList<AvaEvidenceManifestEntry> Entries);

internal sealed record AvaEvidenceManifestEntry(
    int Sequence,
    string ToolName,
    long? McpCallId,
    string Status,
    string? RawOutputPath,
    string? Summary,
    string? Error);

internal sealed record AvaStepEvidenceReference(
    string StepId,
    string ManifestPath,
    string Status,
    int EntryCount);

internal sealed record AvaEvidenceBundleWriteRequest(
    string RunId,
    string OutputDirectory,
    string StepId,
    int StepIndex,
    string StepName,
    string? WindowHandle,
    IReadOnlyList<AvaEvidenceRecord> Records);

internal sealed class AvaEvidenceBundleWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AvaStepEvidenceReference WriteStepEvidence(AvaEvidenceBundleWriteRequest request)
    {
        var outputDirectory = Path.GetFullPath(request.OutputDirectory);
        var stepDirectory = Path.Combine(outputDirectory, "evidence", request.StepId);
        Directory.CreateDirectory(stepDirectory);

        var entries = new List<AvaEvidenceManifestEntry>();
        for (var index = 0; index < request.Records.Count; index++)
        {
            var record = request.Records[index];
            var rawOutputPath = WriteRawOutputIfPresent(stepDirectory, index + 1, record);

            entries.Add(new AvaEvidenceManifestEntry(
                index + 1,
                record.ToolName,
                record.McpCallId,
                record.Status,
                rawOutputPath,
                record.Summary,
                record.Error));
        }

        var manifest = new AvaEvidenceManifest(
            1,
            request.RunId,
            request.StepId,
            request.StepIndex,
            request.StepName,
            request.WindowHandle,
            entries);

        var manifestPath = Path.Combine(stepDirectory, "manifest.json");
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(manifest, JsonOptions) + Environment.NewLine,
            Encoding.UTF8);

        return new AvaStepEvidenceReference(
            request.StepId,
            ToRelativeReportPath(outputDirectory, manifestPath),
            ResolveManifestStatus(entries),
            entries.Count);
    }

    private static string? WriteRawOutputIfPresent(string stepDirectory, int sequence, AvaEvidenceRecord record)
    {
        if (string.IsNullOrEmpty(record.RawOutput))
        {
            return null;
        }

        var fileName = $"{sequence:000}-{SanitizeFileNameSegment(record.ToolName)}.raw.txt";
        var rawPath = Path.Combine(stepDirectory, fileName);
        File.WriteAllText(rawPath, record.RawOutput, Encoding.UTF8);
        return fileName;
    }

    private static string ResolveManifestStatus(IReadOnlyList<AvaEvidenceManifestEntry> entries)
    {
        if (entries.Any(static entry => entry.Status == AvaEvidenceStatus.Captured))
        {
            return AvaEvidenceStatus.Captured;
        }

        if (entries.Any(static entry => entry.Status == AvaEvidenceStatus.Error))
        {
            return AvaEvidenceStatus.Error;
        }

        return AvaEvidenceStatus.Missing;
    }

    private static string ToRelativeReportPath(string outputDirectory, string path)
        => Path.GetRelativePath(outputDirectory, path).Replace('\\', '/');

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
}

internal sealed record AvaEvidenceCollectionRequest(
    string RunId,
    string StepId,
    int StepIndex,
    string StepName,
    string Command,
    string WindowHandle);

internal interface IAvaEvidenceCollector
{
    Task<IReadOnlyList<AvaEvidenceRecord>> CollectAsync(
        AvaEvidenceCollectionRequest request,
        CancellationToken cancellationToken);
}

internal sealed class AvaMcpEvidenceCollector(McpClientManager mcpClientManager) : IAvaEvidenceCollector
{
    private static readonly string[] ToolNames =
    [
        "describe_window",
        "describe_window_focus",
        "capture_window_screenshot"
    ];

    public async Task<IReadOnlyList<AvaEvidenceRecord>> CollectAsync(
        AvaEvidenceCollectionRequest request,
        CancellationToken cancellationToken)
    {
        var records = new List<AvaEvidenceRecord>();
        foreach (var toolName in ToolNames)
        {
            records.Add(await CollectToolEvidenceAsync(toolName, request.WindowHandle, cancellationToken));
        }

        return records;
    }

    private async Task<AvaEvidenceRecord> CollectToolEvidenceAsync(
        string toolName,
        string windowHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await mcpClientManager.CallToolAsync(
                toolName,
                CreateToolArguments(toolName, windowHandle),
                cancellationToken);

            return new AvaEvidenceRecord(
                toolName,
                outcome.IsError ? AvaEvidenceStatus.Error : AvaEvidenceStatus.Captured,
                outcome.McpCallId,
                outcome.Text,
                outcome.IsError ? null : Summarize(outcome.Text),
                outcome.IsError ? Summarize(outcome.Text) : null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return AvaEvidenceRecord.ErrorResult(
                toolName,
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static IReadOnlyDictionary<string, object?> CreateToolArguments(string toolName, string windowHandle)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["windowHandle"] = windowHandle
        };

        if (toolName is "describe_window" or "describe_window_focus")
        {
            arguments["includeImage"] = true;
            arguments["debugMode"] = DebugTrace.IsEnabled;
        }

        return arguments;
    }

    private static string Summarize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Tool returned no text.";
        }

        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var firstLine = normalized
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        var summary = string.IsNullOrWhiteSpace(firstLine) ? normalized.Trim() : firstLine;
        return summary.Length <= 240 ? summary : summary[..240];
    }
}
