using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.WebSockets;
using System.Buffers;
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
    public IReadOnlyList<AvaEvidenceArtifact> Artifacts { get; init; } = [];

    public static AvaEvidenceRecord Missing(string summary)
        => Missing("ava.evidence", summary);

    public static AvaEvidenceRecord Missing(string toolName, string summary)
        => new(
            string.IsNullOrWhiteSpace(toolName) ? "ava.evidence" : toolName,
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

internal sealed record AvaEvidenceArtifact(
    string Kind,
    string ContentType,
    string Content,
    string? Label = null);

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
    string? Error)
{
    public IReadOnlyList<AvaEvidenceArtifactReference> Artifacts { get; init; } = [];
}

internal sealed record AvaEvidenceArtifactReference(
    string Kind,
    string Path,
    string ContentType,
    string? Label);

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
            var sequence = index + 1;
            var rawOutputPath = WriteRawOutputIfPresent(stepDirectory, sequence, record);
            var artifactReferences = WriteArtifacts(stepDirectory, sequence, record);

            entries.Add(new AvaEvidenceManifestEntry(
                sequence,
                record.ToolName,
                record.McpCallId,
                record.Status,
                rawOutputPath,
                record.Summary,
                record.Error)
            {
                Artifacts = artifactReferences,
            });
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

    private static IReadOnlyList<AvaEvidenceArtifactReference> WriteArtifacts(
        string stepDirectory,
        int sequence,
        AvaEvidenceRecord record)
    {
        if (record.Artifacts.Count == 0)
        {
            return [];
        }

        var references = new List<AvaEvidenceArtifactReference>();
        foreach (var artifact in record.Artifacts)
        {
            var relativePath = BuildArtifactRelativePath(sequence, artifact);
            var fullPath = Path.Combine(stepDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)
                                      ?? throw new InvalidOperationException($"Could not resolve artifact directory for: {fullPath}"));
            File.WriteAllText(fullPath, artifact.Content, Encoding.UTF8);
            references.Add(new AvaEvidenceArtifactReference(
                artifact.Kind,
                relativePath,
                artifact.ContentType,
                artifact.Label));
        }

        return references;
    }

    private static string BuildArtifactRelativePath(int sequence, AvaEvidenceArtifact artifact)
        => artifact.Kind.Trim().ToLowerInvariant() switch
        {
            "html" => $"web/{sequence:000}-page.html",
            "dom-snapshot" => $"web/{sequence:000}-dom-snapshot.json",
            "accessibility-tree" => $"web/{sequence:000}-accessibility-tree.json",
            _ => $"artifacts/{sequence:000}-{SanitizeFileNameSegment(artifact.Kind)}.txt",
        };

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

internal sealed class AvaCompositeEvidenceCollector(
    IAvaEvidenceCollector primaryCollector,
    AvaCdpEvidenceCollector webEvidenceCollector) : IAvaEvidenceCollector
{
    public async Task<IReadOnlyList<AvaEvidenceRecord>> CollectAsync(
        AvaEvidenceCollectionRequest request,
        CancellationToken cancellationToken)
    {
        var records = (await primaryCollector.CollectAsync(request, cancellationToken)).ToList();
        var webRecord = await webEvidenceCollector.TryCollectAsync(request, records, cancellationToken);
        if (webRecord is not null)
        {
            records.Add(webRecord);
        }

        return records;
    }
}

internal sealed class AvaCdpEvidenceCollector(HttpClient? httpClient = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

    public async Task<AvaEvidenceRecord?> TryCollectAsync(
        AvaEvidenceCollectionRequest request,
        IReadOnlyList<AvaEvidenceRecord> existingRecords,
        CancellationToken cancellationToken)
    {
        if (!ContainsWebContent(existingRecords))
        {
            return null;
        }

        var endpoint = ResolveEndpoint();
        IReadOnlyList<CdpTarget> targets;
        try
        {
            targets = await ListTargetsAsync(endpoint, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return AvaEvidenceRecord.Missing(
                "web_dom_snapshot",
                $"CDP endpoint was not available at {endpoint}; web/W3C validation skipped.");
        }

        var target = SelectTarget(targets, existingRecords);
        if (target is null || string.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl))
        {
            return AvaEvidenceRecord.Missing(
                "web_dom_snapshot",
                $"CDP endpoint was available at {endpoint}, but no matching page target was found; web/W3C validation skipped.");
        }

        try
        {
            var capture = await CaptureTargetAsync(target, cancellationToken);
            var rawOutput = JsonSerializer.Serialize(
                new
                {
                    endpoint,
                    target = new
                    {
                        target.Id,
                        target.Type,
                        target.Title,
                        target.Url,
                    },
                    capture.DomSnapshot,
                    capture.AccessibilityTree,
                    htmlArtifact = "html",
                },
                JsonOptions);

            return new AvaEvidenceRecord(
                "web_dom_snapshot",
                AvaEvidenceStatus.Captured,
                null,
                rawOutput,
                $"Captured CDP DOM, accessibility tree, and HTML for {target.Title ?? target.Url ?? "page"}.",
                null)
            {
                Artifacts =
                [
                    new AvaEvidenceArtifact(
                        "html",
                        "text/html",
                        capture.Html,
                        "html"),
                ],
            };
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or JsonException or InvalidOperationException or TaskCanceledException)
        {
            return AvaEvidenceRecord.ErrorResult(
                "web_dom_snapshot",
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string ResolveEndpoint()
    {
        var endpoint = Environment.GetEnvironmentVariable("AVA_CDP_ENDPOINT");
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            return endpoint.Trim().TrimEnd('/');
        }

        var port = Environment.GetEnvironmentVariable("AVA_CDP_PORT");
        return int.TryParse(port, out var parsedPort) && parsedPort > 0
            ? $"http://127.0.0.1:{parsedPort}"
            : "http://127.0.0.1:9222";
    }

    private async Task<IReadOnlyList<CdpTarget>> ListTargetsAsync(
        string endpoint,
        CancellationToken cancellationToken)
    {
        var json = await httpClient.GetStringAsync($"{endpoint}/json/list", cancellationToken);
        return JsonSerializer.Deserialize<CdpTarget[]>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? [];
    }

    private static bool ContainsWebContent(IReadOnlyList<AvaEvidenceRecord> records)
        => records
            .Where(static record => string.Equals(record.Status, AvaEvidenceStatus.Captured, StringComparison.Ordinal))
            .Select(static record => record.RawOutput)
            .Any(rawOutput => ContainsWebContent(rawOutput));

    private static bool ContainsWebContent(string? rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            return false;
        }

        return rawOutput.Contains("\"RootWebArea\"", StringComparison.OrdinalIgnoreCase) ||
            rawOutput.Contains("\"controlType\":\"Document\"", StringComparison.OrdinalIgnoreCase) ||
            rawOutput.Contains("\"controlType\": \"Document\"", StringComparison.OrdinalIgnoreCase);
    }

    private static CdpTarget? SelectTarget(IReadOnlyList<CdpTarget> targets, IReadOnlyList<AvaEvidenceRecord> records)
    {
        var pageTargets = targets
            .Where(static target => string.Equals(target.Type, "page", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl))
            .ToArray();
        if (pageTargets.Length == 0)
        {
            return null;
        }

        var windowTitle = ExtractWindowTitle(records);
        if (!string.IsNullOrWhiteSpace(windowTitle))
        {
            var normalizedWindowTitle = NormalizeTitle(windowTitle);
            var matchingTarget = pageTargets.FirstOrDefault(target =>
            {
                var title = NormalizeTitle(target.Title);
                return title.Length > 0 &&
                    (normalizedWindowTitle.Contains(title, StringComparison.OrdinalIgnoreCase) ||
                     title.Contains(normalizedWindowTitle, StringComparison.OrdinalIgnoreCase));
            });
            if (matchingTarget is not null)
            {
                return matchingTarget;
            }
        }

        return pageTargets.FirstOrDefault(static target =>
                   !string.IsNullOrWhiteSpace(target.Url) &&
                   target.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
               ?? pageTargets[0];
    }

    private static string? ExtractWindowTitle(IReadOnlyList<AvaEvidenceRecord> records)
    {
        foreach (var record in records)
        {
            if (string.IsNullOrWhiteSpace(record.RawOutput))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(record.RawOutput);
                if (TryGetProperty(document.RootElement, "window", out var window) &&
                    TryGetProperty(window, "title", out var title) &&
                    title.ValueKind == JsonValueKind.String)
                {
                    return title.GetString();
                }
            }
            catch (JsonException)
            {
            }
        }

        return null;
    }

    private static string NormalizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value
            .Replace(" - Microsoft Edge", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" - Personal", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" - Google Chrome", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" - Chromium", string.Empty, StringComparison.OrdinalIgnoreCase);
        return string.Join(
            " ",
            normalized.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
    }

    private static async Task<CdpCapture> CaptureTargetAsync(
        CdpTarget target,
        CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(target.WebSocketDebuggerUrl!), cancellationToken);
        var session = new CdpSession(socket);
        var htmlResponse = await session.SendAsync(
            "Runtime.evaluate",
            new
            {
                expression = "document.documentElement.outerHTML",
                returnByValue = true
            },
            cancellationToken);
        var domSnapshot = await session.SendAsync(
            "DOMSnapshot.captureSnapshot",
            new
            {
                computedStyles = Array.Empty<string>(),
                includeDOMRects = true,
                includePaintOrder = true,
            },
            cancellationToken);
        var accessibilityTree = await session.SendAsync(
            "Accessibility.getFullAXTree",
            new { },
            cancellationToken);

        var html = ExtractRuntimeStringValue(htmlResponse)
                   ?? throw new InvalidOperationException("CDP Runtime.evaluate did not return document HTML.");
        return new CdpCapture(html, domSnapshot, accessibilityTree);
    }

    private static string? ExtractRuntimeStringValue(JsonElement response)
    {
        if (TryGetProperty(response, "result", out var result) &&
            TryGetProperty(result, "result", out var runtimeResult) &&
            TryGetProperty(runtimeResult, "value", out var value) &&
            value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
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

    private sealed record CdpTarget(
        string? Id,
        string? Type,
        string? Title,
        string? Url,
        string? WebSocketDebuggerUrl);

    private sealed record CdpCapture(
        string Html,
        JsonElement DomSnapshot,
        JsonElement AccessibilityTree);

    private sealed class CdpSession(ClientWebSocket socket)
    {
        private int nextId;

        public async Task<JsonElement> SendAsync(
            string method,
            object parameters,
            CancellationToken cancellationToken)
        {
            var id = Interlocked.Increment(ref nextId);
            var payload = JsonSerializer.Serialize(new
            {
                id,
                method,
                @params = parameters,
            });
            var bytes = Encoding.UTF8.GetBytes(payload);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);

            while (true)
            {
                using var responseDocument = JsonDocument.Parse(await ReceiveTextAsync(cancellationToken));
                var root = responseDocument.RootElement;
                if (!TryGetProperty(root, "id", out var responseId) ||
                    responseId.ValueKind != JsonValueKind.Number ||
                    !responseId.TryGetInt32(out var parsedId) ||
                    parsedId != id)
                {
                    continue;
                }

                if (TryGetProperty(root, "error", out var error))
                {
                    throw new InvalidOperationException($"CDP command {method} failed: {error}");
                }

                return root.Clone();
            }
        }

        private async Task<string> ReceiveTextAsync(CancellationToken cancellationToken)
        {
            using var stream = new MemoryStream();
            var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            try
            {
                while (true)
                {
                    var result = await socket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        throw new WebSocketException("CDP websocket closed unexpectedly.");
                    }

                    stream.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage)
                    {
                        return Encoding.UTF8.GetString(stream.ToArray());
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}
