using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeronWin.Brain;

namespace HeronWin.Ava;

internal sealed record AvaCompactTreeEvaluationRequest(
    string RunId,
    string WindowHandle,
    string OutputDirectory,
    bool RunVisionVerdict,
    bool DebugMode);

internal sealed record AvaCompactTreeEvaluationResult(
    string RunId,
    string OutputDirectory,
    string ReportPath,
    string VerdictPath,
    AvaCompactTreeEvaluationReport Report)
{
    public bool HasErrors =>
        Report.ToolRecords.Any(static record => string.Equals(record.Status, AvaEvidenceStatus.Error, StringComparison.Ordinal)) ||
        string.Equals(Report.Verdict.Status, AvaCompactTreeVisionVerdictStatus.Error, StringComparison.Ordinal);
}

internal sealed record AvaCompactTreeEvaluationReport(
    int Version,
    string RunId,
    string WindowHandle,
    DateTimeOffset CreatedAt,
    bool VisionVerdictRequested,
    string VerdictPath,
    IReadOnlyList<AvaCompactTreeEvaluationToolRecord> ToolRecords,
    AvaCompactTreeVisionVerdict Verdict);

internal sealed record AvaCompactTreeEvaluationToolRecord(
    int Sequence,
    string ToolName,
    long? McpCallId,
    string Status,
    string RawOutputPath,
    string? Summary,
    string? Error,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<AvaCompactTreeEvaluationArtifact> Artifacts);

internal sealed record AvaCompactTreeEvaluationArtifact(
    string Kind,
    string ToolName,
    string? SourcePath,
    string Path,
    string ContentType,
    string Label);

internal sealed record AvaCompactTreeVisionVerdict(
    string Status,
    bool? SamePrimaryScreen,
    bool? SameRecognizableTaskOrState,
    bool? SameKeyText,
    bool? SameKeyActionableControls,
    IReadOnlyList<string> MissingCriticalElements,
    IReadOnlyList<string> HallucinatedElements,
    bool? OverallMatch,
    string? Confidence,
    string? Notes,
    string? RawResponse,
    string? Error)
{
    public static AvaCompactTreeVisionVerdict NotRequested()
        => new(
            AvaCompactTreeVisionVerdictStatus.NotRequested,
            null,
            null,
            null,
            null,
            [],
            [],
            null,
            null,
            "Vision verdict was not requested. Artifacts were collected for manual review.",
            null,
            null);

    public static AvaCompactTreeVisionVerdict ErrorResult(string error, string? rawResponse = null)
        => new(
            AvaCompactTreeVisionVerdictStatus.Error,
            null,
            null,
            null,
            null,
            [],
            [],
            null,
            null,
            null,
            rawResponse,
            error);
}

internal static class AvaCompactTreeVisionVerdictStatus
{
    public const string Captured = "captured";
    public const string NotRequested = "not-requested";
    public const string Error = "error";
}

internal static class AvaCompactTreeEvaluationRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string[] ToolNames =
    [
        "describe_window",
        "describe_window_focus",
        "capture_window_screenshot"
    ];

    public static async Task<AvaCompactTreeEvaluationResult> RunAsync(
        AvaCompactTreeEvaluationRequest request,
        McpClientManager mcpClientManager,
        ILlmClient? evaluatorClient,
        CancellationToken cancellationToken)
    {
        var outputDirectory = Path.GetFullPath(request.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var records = new List<AvaCompactTreeEvaluationToolRecord>();
        for (var index = 0; index < ToolNames.Length; index += 1)
        {
            records.Add(await CollectToolRecordAsync(
                index + 1,
                ToolNames[index],
                request,
                mcpClientManager,
                outputDirectory,
                cancellationToken));
        }

        var verdict = request.RunVisionVerdict
            ? await BuildVisionVerdictAsync(records, evaluatorClient, outputDirectory, cancellationToken)
            : AvaCompactTreeVisionVerdict.NotRequested();

        var verdictPath = Path.Combine(outputDirectory, "verdict.json");
        await File.WriteAllTextAsync(
            verdictPath,
            JsonSerializer.Serialize(verdict, JsonOptions) + Environment.NewLine,
            Encoding.UTF8,
            cancellationToken);

        var report = new AvaCompactTreeEvaluationReport(
            1,
            request.RunId,
            request.WindowHandle,
            DateTimeOffset.UtcNow,
            request.RunVisionVerdict,
            ToRelativeReportPath(outputDirectory, verdictPath),
            records,
            verdict);

        var reportPath = Path.Combine(outputDirectory, "compact-tree-evaluation.json");
        await File.WriteAllTextAsync(
            reportPath,
            JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine,
            Encoding.UTF8,
            cancellationToken);

        return new AvaCompactTreeEvaluationResult(
            request.RunId,
            outputDirectory,
            reportPath,
            verdictPath,
            report);
    }

    private static async Task<AvaCompactTreeEvaluationToolRecord> CollectToolRecordAsync(
        int sequence,
        string toolName,
        AvaCompactTreeEvaluationRequest request,
        McpClientManager mcpClientManager,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await mcpClientManager.CallToolAsync(
                toolName,
                CreateToolArguments(toolName, request.WindowHandle, request.DebugMode),
                cancellationToken);
            var rawOutputPath = await WriteRawOutputAsync(
                outputDirectory,
                sequence,
                toolName,
                outcome.Text,
                cancellationToken);
            var artifactResult = CopyArtifacts(
                outputDirectory,
                sequence,
                toolName,
                outcome.Text);
            var status = outcome.IsError ? AvaEvidenceStatus.Error : AvaEvidenceStatus.Captured;

            return new AvaCompactTreeEvaluationToolRecord(
                sequence,
                toolName,
                outcome.McpCallId,
                status,
                ToRelativeReportPath(outputDirectory, rawOutputPath),
                outcome.IsError ? null : Summarize(outcome.Text),
                outcome.IsError ? Summarize(outcome.Text) : null,
                artifactResult.Warnings,
                artifactResult.Artifacts);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var rawOutputPath = await WriteRawOutputAsync(
                outputDirectory,
                sequence,
                toolName,
                $"{ex.GetType().Name}: {ex.Message}",
                cancellationToken);

            return new AvaCompactTreeEvaluationToolRecord(
                sequence,
                toolName,
                null,
                AvaEvidenceStatus.Error,
                ToRelativeReportPath(outputDirectory, rawOutputPath),
                null,
                $"{ex.GetType().Name}: {ex.Message}",
                [],
                []);
        }
    }

    private static IReadOnlyDictionary<string, object?> CreateToolArguments(
        string toolName,
        string windowHandle,
        bool debugMode)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["windowHandle"] = windowHandle
        };

        if (toolName is "describe_window" or "describe_window_focus")
        {
            arguments["includeImage"] = true;
            arguments["debugMode"] = debugMode;
        }

        return arguments;
    }

    private static async Task<string> WriteRawOutputAsync(
        string outputDirectory,
        int sequence,
        string toolName,
        string rawOutput,
        CancellationToken cancellationToken)
    {
        var fileExtension = LooksLikeJson(rawOutput) ? ".json" : ".raw.txt";
        var rawOutputPath = Path.Combine(
            outputDirectory,
            $"{sequence:000}-{SanitizeFileNameSegment(toolName)}{fileExtension}");
        await File.WriteAllTextAsync(rawOutputPath, rawOutput, Encoding.UTF8, cancellationToken);
        return rawOutputPath;
    }

    private static bool LooksLikeJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            using var _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ArtifactCopyResult CopyArtifacts(
        string outputDirectory,
        int sequence,
        string toolName,
        string rawOutput)
    {
        var warnings = new List<string>();
        var artifacts = new List<AvaCompactTreeEvaluationArtifact>();
        foreach (var candidate in ExtractArtifactCandidates(toolName, rawOutput))
        {
            if (string.IsNullOrWhiteSpace(candidate.SourcePath))
            {
                continue;
            }

            var sourcePath = Path.GetFullPath(candidate.SourcePath);
            if (!File.Exists(sourcePath))
            {
                warnings.Add($"Artifact source did not exist for {toolName}: {sourcePath}");
                continue;
            }

            var extension = Path.GetExtension(sourcePath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".png";
            }

            var relativePath = Path.Combine(
                    "artifacts",
                    $"{sequence:000}-{SanitizeFileNameSegment(toolName)}-{candidate.Kind}{extension.ToLowerInvariant()}")
                .Replace('\\', '/');
            var targetPath = Path.Combine(outputDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                if (!string.Equals(sourcePath, Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(sourcePath, targetPath, overwrite: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                warnings.Add($"Artifact copy failed for {toolName}: {sourcePath} -> {targetPath}: {ex.Message}");
                continue;
            }

            artifacts.Add(new AvaCompactTreeEvaluationArtifact(
                candidate.Kind,
                toolName,
                sourcePath,
                relativePath,
                ResolveContentType(targetPath),
                candidate.Label));
        }

        return new ArtifactCopyResult(warnings, artifacts);
    }

    private static IReadOnlyList<ArtifactCandidate> ExtractArtifactCandidates(string toolName, string rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(rawOutput);
            var root = document.RootElement;
            var candidates = new List<ArtifactCandidate>();

            if (toolName == "describe_window")
            {
                AddCandidate(candidates, root, ["renderedImage", "imagePath"], "compact-window-render", "Compact window render");
                AddCandidate(candidates, root, ["debugEvidence", "screenshot", "filePath"], "debug-window-screenshot", "Debug window screenshot");
                AddCandidate(candidates, root, ["debugEvidence", "screenshot", "imagePath"], "debug-window-screenshot", "Debug window screenshot");
            }
            else if (toolName == "describe_window_focus")
            {
                AddCandidate(candidates, root, ["renderedImage", "imagePath"], "compact-focus-render", "Compact focus render");
                AddCandidate(candidates, root, ["debugEvidence", "screenshot", "filePath"], "debug-focus-screenshot", "Debug focus screenshot");
                AddCandidate(candidates, root, ["debugEvidence", "screenshot", "imagePath"], "debug-focus-screenshot", "Debug focus screenshot");
            }
            else if (toolName == "capture_window_screenshot")
            {
                AddCandidate(candidates, root, ["imagePath"], "real-screenshot", "Real window screenshot");
                AddCandidate(candidates, root, ["screenshotPath"], "real-screenshot", "Real window screenshot");
                AddCandidate(candidates, root, ["screenshot", "filePath"], "real-screenshot", "Real window screenshot");
                AddCandidate(candidates, root, ["screenshot", "imagePath"], "real-screenshot", "Real window screenshot");
            }

            return candidates
                .GroupBy(static candidate => $"{candidate.Kind}:{candidate.SourcePath}", StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void AddCandidate(
        List<ArtifactCandidate> candidates,
        JsonElement root,
        IReadOnlyList<string> propertyPath,
        string kind,
        string label)
    {
        if (TryGetNestedStringProperty(root, propertyPath, out var path))
        {
            candidates.Add(new ArtifactCandidate(kind, path, label));
        }
    }

    private static async Task<AvaCompactTreeVisionVerdict> BuildVisionVerdictAsync(
        IReadOnlyList<AvaCompactTreeEvaluationToolRecord> records,
        ILlmClient? evaluatorClient,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        if (evaluatorClient is null)
        {
            return AvaCompactTreeVisionVerdict.ErrorResult(
                "Vision verdict was requested, but no evaluator LLM client was provided.");
        }

        var realScreenshot = FindArtifact(records, "real-screenshot");
        var compactRender = FindArtifact(records, "compact-window-render");
        if (realScreenshot is null || compactRender is null)
        {
            return AvaCompactTreeVisionVerdict.ErrorResult(
                "Vision verdict requires both a real screenshot and a compact window render artifact.");
        }

        try
        {
            var images = new[]
            {
                LoadToolImage(ResolveOutputPath(outputDirectory, realScreenshot.Path), "high"),
                LoadToolImage(ResolveOutputPath(outputDirectory, compactRender.Path), "high")
            };
            var prompt = BuildVisionVerdictPrompt(records, outputDirectory);
            var result = await evaluatorClient.ChatAsync(
                [new AgentMessage.VisualContext(prompt, images)],
                [],
                "You evaluate whether a compact UI-tree render preserves the recognizable state of a real application screenshot. Return strict JSON only.",
                cancellationToken);
            var rawResponse = result.Text ?? string.Empty;
            if (!TryParseVisionVerdict(rawResponse, out var verdict, out var parseError))
            {
                return AvaCompactTreeVisionVerdict.ErrorResult(parseError, rawResponse);
            }

            return verdict with { RawResponse = rawResponse };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return AvaCompactTreeVisionVerdict.ErrorResult($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string BuildVisionVerdictPrompt(
        IReadOnlyList<AvaCompactTreeEvaluationToolRecord> records,
        string outputDirectory)
    {
        var sourceStats = TryExtractSourceStats(
            records.FirstOrDefault(static record => record.ToolName == "describe_window"),
            outputDirectory);
        var builder = new StringBuilder();
        builder.AppendLine("Compare image 1, the real screenshot, against image 2, the rendered compact UI tree.");
        builder.AppendLine("Treat the real screenshot as source of truth. The compact render only needs recognition-level fidelity, not pixel fidelity.");
        if (!string.IsNullOrWhiteSpace(sourceStats))
        {
            builder.AppendLine();
            builder.AppendLine("Compact source stats:");
            builder.AppendLine(sourceStats);
        }

        builder.AppendLine();
        builder.AppendLine("Return strict JSON with these fields:");
        builder.AppendLine("""
{
  "samePrimaryScreen": true,
  "sameRecognizableTaskOrState": true,
  "sameKeyText": true,
  "sameKeyActionableControls": true,
  "missingCriticalElements": [],
  "hallucinatedElements": [],
  "overallMatch": true,
  "confidence": "high",
  "notes": "short diagnostic note"
}
""");
        return builder.ToString();
    }

    private static string? TryExtractSourceStats(
        AvaCompactTreeEvaluationToolRecord? record,
        string outputDirectory)
    {
        if (record is null)
        {
            return null;
        }

        try
        {
            var rawPath = ResolveOutputPath(outputDirectory, record.RawOutputPath);
            if (!File.Exists(rawPath))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(rawPath, Encoding.UTF8));
            return TryGetProperty(document.RootElement, "sourceStats", out var sourceStats)
                ? sourceStats.GetRawText()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static ToolImage LoadToolImage(string path, string detail)
    {
        var fullPath = Path.GetFullPath(path);
        var contentType = ResolveContentType(fullPath);
        var image = new ToolImage(contentType, Convert.ToBase64String(File.ReadAllBytes(fullPath)), detail);
        return McpClientManager.OptimizeToolImageForVision(image);
    }

    private static AvaCompactTreeEvaluationArtifact? FindArtifact(
        IReadOnlyList<AvaCompactTreeEvaluationToolRecord> records,
        string kind)
        => records
            .SelectMany(static record => record.Artifacts)
            .FirstOrDefault(artifact => string.Equals(artifact.Kind, kind, StringComparison.Ordinal));

    private static bool TryParseVisionVerdict(
        string rawResponse,
        out AvaCompactTreeVisionVerdict verdict,
        out string error)
    {
        verdict = null!;
        error = string.Empty;
        var json = ExtractJsonObject(rawResponse);
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Vision verdict response did not contain a JSON object.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            verdict = new AvaCompactTreeVisionVerdict(
                AvaCompactTreeVisionVerdictStatus.Captured,
                TryGetBooleanProperty(root, "samePrimaryScreen"),
                TryGetBooleanProperty(root, "sameRecognizableTaskOrState"),
                TryGetBooleanProperty(root, "sameKeyText"),
                TryGetBooleanProperty(root, "sameKeyActionableControls"),
                TryGetStringArrayProperty(root, "missingCriticalElements"),
                TryGetStringArrayProperty(root, "hallucinatedElements"),
                TryGetBooleanProperty(root, "overallMatch"),
                TryGetScalarTextProperty(root, "confidence"),
                TryGetScalarTextProperty(root, "notes"),
                rawResponse,
                null);
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Vision verdict JSON could not be parsed: {ex.Message}";
            return false;
        }
    }

    private static string? ExtractJsonObject(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return null;
        }

        var trimmed = rawResponse.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
            {
                trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
            }
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        return start >= 0 && end > start
            ? trimmed[start..(end + 1)]
            : null;
    }

    private static bool? TryGetBooleanProperty(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static string? TryGetScalarTextProperty(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static IReadOnlyList<string> TryGetStringArrayProperty(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var property))
        {
            return [];
        }

        if (property.ValueKind == JsonValueKind.Array)
        {
            return property
                .EnumerateArray()
                .Select(static item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText())
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(static item => item!)
                .ToArray();
        }

        return property.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.GetString())
            ? [property.GetString()!]
            : [];
    }

    private static bool TryGetNestedStringProperty(
        JsonElement root,
        IReadOnlyList<string> propertyPath,
        out string value)
    {
        value = string.Empty;
        var current = root;
        foreach (var propertyName in propertyPath)
        {
            if (!TryGetProperty(current, propertyName, out current))
            {
                return false;
            }
        }

        if (current.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = current.GetString() ?? string.Empty;
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

    private static string ResolveOutputPath(string outputDirectory, string relativePath)
        => Path.Combine(outputDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string ResolveContentType(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".json" => "application/json",
            ".html" or ".htm" => "text/html",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };

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

        var sanitized = builder.ToString().Trim('-', '.', ' ');
        return string.IsNullOrWhiteSpace(sanitized) ? "artifact" : sanitized;
    }

    private sealed record ArtifactCandidate(string Kind, string SourcePath, string Label);

    private sealed record ArtifactCopyResult(
        IReadOnlyList<string> Warnings,
        IReadOnlyList<AvaCompactTreeEvaluationArtifact> Artifacts);
}
