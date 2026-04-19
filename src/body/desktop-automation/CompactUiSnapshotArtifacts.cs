using System.Text.Json;
using System.Text.RegularExpressions;

namespace HeronWin.Body.DesktopAutomation;

internal sealed class CompactArtifactRenderSummary
{
    public required string JsonlPath { get; init; }

    public required string OutputDirectory { get; init; }

    public required string ManifestPath { get; init; }

    public required IReadOnlyList<CompactArtifactRenderResult> RenderedArtifacts { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }
}

internal sealed class CompactArtifactRenderResult
{
    public required long ScreenshotSequence { get; init; }

    public required string ScreenshotPath { get; init; }

    public required long SnapshotSequence { get; init; }

    public required string SnapshotTool { get; init; }

    public required string WindowHandle { get; init; }

    public required string WindowTitle { get; init; }

    public required string CompactImagePath { get; init; }

    public required string CompactJsonPath { get; init; }

    public required CompactSourceStats SourceStats { get; init; }
}

internal static class CompactUiSnapshotArtifactRenderer
{
    private static readonly Regex HandleFromImagePathPattern = new("-(0x[0-9A-Fa-f]+)\\.png$", RegexOptions.Compiled);

    internal static CompactArtifactRenderSummary RenderWindowArtifactsFromJsonl(
        string jsonlPath,
        string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(jsonlPath))
        {
            throw new InvalidOperationException("jsonlPath is required.");
        }

        var resolvedJsonlPath = Path.GetFullPath(jsonlPath);
        if (!File.Exists(resolvedJsonlPath))
        {
            throw new FileNotFoundException("Could not find the requested JSONL log file.", resolvedJsonlPath);
        }

        var resolvedOutputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(resolvedOutputDirectory);

        var latestWindowSnapshots = new Dictionary<string, LoggedWindowSnapshot>(StringComparer.OrdinalIgnoreCase);
        var renderedArtifacts = new List<CompactArtifactRenderResult>();
        var warnings = new List<string>();

        foreach (var line in File.ReadLines(resolvedJsonlPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var eventDocument = JsonDocument.Parse(line);
            var eventRoot = eventDocument.RootElement;
            var category = GetStringProperty(eventRoot, "category");
            var sequence = GetInt64Property(eventRoot, "sequence");

            if (!TryGetProperty(eventRoot, "data", out var dataElement))
            {
                continue;
            }

            if (string.Equals(category, "mcp.call.complete.full", StringComparison.Ordinal))
            {
                var toolName = GetToolName(dataElement) ?? string.Empty;
                if (!string.Equals(toolName, "describe_window", StringComparison.Ordinal))
                {
                    continue;
                }

                var snapshotText = GetStringProperty(dataElement, "text");
                if (string.IsNullOrWhiteSpace(snapshotText))
                {
                    warnings.Add($"Skipped describe_window event {sequence} because it did not include full text.");
                    continue;
                }

                using var snapshotDocument = JsonDocument.Parse(snapshotText);
                if (!TryParseWindowTreeResult(snapshotDocument.RootElement, out var window, out var elementTree))
                {
                    warnings.Add($"Skipped describe_window event {sequence} because its payload could not be parsed.");
                    continue;
                }

                latestWindowSnapshots[window.Handle] = new LoggedWindowSnapshot(sequence, toolName, window, elementTree);
                continue;
            }

            if (!string.Equals(category, "mcp.call.complete", StringComparison.Ordinal) ||
                !string.Equals(GetToolName(dataElement), "capture_window_screenshot", StringComparison.Ordinal))
            {
                continue;
            }

            var screenshotPath = GetFirstImagePath(dataElement);
            if (string.IsNullOrWhiteSpace(screenshotPath))
            {
                warnings.Add($"Skipped screenshot event {sequence} because it did not include an image path.");
                continue;
            }

            screenshotPath = Path.GetFullPath(screenshotPath);
            if (!File.Exists(screenshotPath))
            {
                warnings.Add($"Skipped screenshot event {sequence} because its image was missing: {screenshotPath}");
                continue;
            }

            var windowHandle = ResolveWindowHandleForScreenshot(dataElement, screenshotPath);
            if (string.IsNullOrWhiteSpace(windowHandle))
            {
                warnings.Add($"Skipped screenshot event {sequence} because its window handle could not be determined.");
                continue;
            }

            if (!latestWindowSnapshots.TryGetValue(windowHandle, out var loggedSnapshot))
            {
                warnings.Add(
                    $"Skipped screenshot event {sequence} for {windowHandle} because no earlier full describe_window payload was found.");
                continue;
            }

            var response = CompactUiSnapshotBuilder.BuildWindowResponse(
                loggedSnapshot.Window,
                loggedSnapshot.ElementTree,
                includeImage: true,
                debugArtifactDirectory: resolvedOutputDirectory);
            if (response.RenderedImage is null)
            {
                warnings.Add($"Skipped screenshot event {sequence} because compact rendering did not produce an image.");
                continue;
            }

            var screenshotBaseName = Path.GetFileNameWithoutExtension(screenshotPath);
            var compactImagePath = Path.Combine(resolvedOutputDirectory, $"{screenshotBaseName}.compact.png");
            File.Copy(response.RenderedImage.ImagePath, compactImagePath, overwrite: true);

            var outputResponse = new CompactSnapshotResponse
            {
                Window = response.Window,
                SourceStats = response.SourceStats,
                CompactTree = response.CompactTree,
                RenderedImage = new CompactRenderedImage
                {
                    ImagePath = compactImagePath,
                    ImageFormat = response.RenderedImage.ImageFormat,
                    ImageSize = response.RenderedImage.ImageSize,
                },
            };

            var compactJsonPath = Path.Combine(resolvedOutputDirectory, $"{screenshotBaseName}.compact.json");
            File.WriteAllText(compactJsonPath, CompactUiSnapshotJson.Serialize(outputResponse));

            renderedArtifacts.Add(new CompactArtifactRenderResult
            {
                ScreenshotSequence = sequence,
                ScreenshotPath = screenshotPath,
                SnapshotSequence = loggedSnapshot.Sequence,
                SnapshotTool = loggedSnapshot.ToolName,
                WindowHandle = loggedSnapshot.Window.Handle,
                WindowTitle = loggedSnapshot.Window.Title,
                CompactImagePath = compactImagePath,
                CompactJsonPath = compactJsonPath,
                SourceStats = outputResponse.SourceStats,
            });
        }

        var manifestPath = Path.Combine(resolvedOutputDirectory, "compact-artifacts.manifest.json");
        var summary = new CompactArtifactRenderSummary
        {
            JsonlPath = resolvedJsonlPath,
            OutputDirectory = resolvedOutputDirectory,
            ManifestPath = manifestPath,
            RenderedArtifacts = renderedArtifacts,
            Warnings = warnings,
        };

        File.WriteAllText(manifestPath, CompactUiSnapshotJson.Serialize(summary));
        return summary;
    }

    private static bool TryParseWindowTreeResult(
        JsonElement root,
        out WindowDescriptor window,
        out UiElementSnapshot elementTree)
    {
        window = null!;
        elementTree = null!;

        if (!TryGetProperty(root, "Window", out var windowElement) ||
            !TryGetProperty(root, "ElementTree", out var treeElement))
        {
            return false;
        }

        if (!TryParseWindowDescriptor(windowElement, out window) ||
            !TryParseUiElementSnapshot(treeElement, out elementTree))
        {
            return false;
        }

        return true;
    }

    private static bool TryParseWindowDescriptor(JsonElement element, out WindowDescriptor window)
    {
        window = null!;
        if (!TryGetString(element, "Handle", out var handle) ||
            !TryGetString(element, "Title", out var title) ||
            !TryGetString(element, "ClassName", out var className) ||
            !TryGetInt32(element, "ProcessId", out var processId) ||
            !TryGetProperty(element, "Bounds", out var boundsElement) ||
            !TryParseWindowBounds(boundsElement, out var bounds))
        {
            return false;
        }

        window = new WindowDescriptor(handle, title, className, processId, bounds);
        return true;
    }

    private static bool TryParseWindowBounds(JsonElement element, out WindowBounds bounds)
    {
        bounds = null!;
        if (!TryGetInt32(element, "Left", out var left) ||
            !TryGetInt32(element, "Top", out var top) ||
            !TryGetInt32(element, "Width", out var width) ||
            !TryGetInt32(element, "Height", out var height))
        {
            return false;
        }

        bounds = new WindowBounds(left, top, width, height);
        return true;
    }

    private static bool TryParseElementBounds(JsonElement element, out ElementBounds bounds)
    {
        bounds = null!;
        if (!TryGetDouble(element, "Left", out var left) ||
            !TryGetDouble(element, "Top", out var top) ||
            !TryGetDouble(element, "Width", out var width) ||
            !TryGetDouble(element, "Height", out var height))
        {
            return false;
        }

        bounds = new ElementBounds(left, top, width, height);
        return true;
    }

    private static bool TryParseUiElementSnapshot(JsonElement element, out UiElementSnapshot snapshot)
    {
        snapshot = null!;
        if (!TryGetString(element, "Path", out var path) ||
            !TryGetString(element, "UiPath", out var uiPath) ||
            !TryGetString(element, "ControlType", out var controlType))
        {
            return false;
        }

        var children = new List<UiElementSnapshot>();
        if (TryGetProperty(element, "Children", out var childrenElement) && childrenElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var childElement in childrenElement.EnumerateArray())
            {
                if (!TryParseUiElementSnapshot(childElement, out var childSnapshot))
                {
                    return false;
                }

                children.Add(childSnapshot);
            }
        }

        ElementBounds? bounds = null;
        if (TryGetProperty(element, "Bounds", out var boundsElement) &&
            boundsElement.ValueKind == JsonValueKind.Object &&
            TryParseElementBounds(boundsElement, out var parsedBounds))
        {
            bounds = parsedBounds;
        }

        snapshot = new UiElementSnapshot(
            path,
            uiPath,
            GetStringProperty(element, "Name") ?? string.Empty,
            controlType,
            GetStringProperty(element, "AutomationId") ?? string.Empty,
            GetStringProperty(element, "ClassName") ?? string.Empty,
            GetBooleanProperty(element, "IsEnabled"),
            GetBooleanProperty(element, "IsOffscreen"),
            GetBooleanProperty(element, "HasKeyboardFocus"),
            GetBooleanProperty(element, "IsKeyboardFocusable"),
            GetBooleanProperty(element, "IsSelected"),
            GetStringArrayProperty(element, "AvailableActions"),
            bounds,
            children);
        return true;
    }

    private static string? ResolveWindowHandleForScreenshot(JsonElement dataElement, string screenshotPath)
    {
        var match = HandleFromImagePathPattern.Match(Path.GetFileName(screenshotPath));
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        var textPreview = GetStringProperty(dataElement, "textPreview");
        if (string.IsNullOrWhiteSpace(textPreview))
        {
            return null;
        }

        try
        {
            using var previewDocument = JsonDocument.Parse(textPreview);
            return TryGetProperty(previewDocument.RootElement, "Window", out var windowElement) &&
                   TryGetString(windowElement, "Handle", out var handle)
                ? handle
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetToolName(JsonElement dataElement)
    {
        var directTool = GetStringProperty(dataElement, "tool");
        if (!string.IsNullOrWhiteSpace(directTool))
        {
            return directTool;
        }

        if (!TryGetProperty(dataElement, "headers", out var headersElement) ||
            headersElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var headerElement in headersElement.EnumerateArray())
        {
            if (headerElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var header = headerElement.GetString();
            if (!string.IsNullOrWhiteSpace(header) &&
                header.StartsWith("tool=", StringComparison.Ordinal))
            {
                return header["tool=".Length..];
            }
        }

        return null;
    }

    private static string? GetFirstImagePath(JsonElement dataElement)
    {
        if (!TryGetProperty(dataElement, "imagePaths", out var imagePathsElement) ||
            imagePathsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var imagePathElement in imagePathsElement.EnumerateArray())
        {
            if (imagePathElement.ValueKind == JsonValueKind.String)
            {
                var imagePath = imagePathElement.GetString();
                if (!string.IsNullOrWhiteSpace(imagePath))
                {
                    return imagePath;
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<string> GetStringArrayProperty(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var arrayElement) ||
            arrayElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var values = new List<string>();
        foreach (var child in arrayElement.EnumerateArray())
        {
            if (child.ValueKind == JsonValueKind.String)
            {
                var value = child.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value);
                }
            }
        }

        return values;
    }

    private static bool GetBooleanProperty(JsonElement element, string propertyName)
        => TryGetProperty(element, propertyName, out var propertyElement) &&
           propertyElement.ValueKind == JsonValueKind.True;

    private static string? GetStringProperty(JsonElement element, string propertyName)
        => TryGetProperty(element, propertyName, out var propertyElement) &&
           propertyElement.ValueKind == JsonValueKind.String
            ? propertyElement.GetString()
            : null;

    private static long GetInt64Property(JsonElement element, string propertyName)
        => TryGetProperty(element, propertyName, out var propertyElement) &&
           propertyElement.ValueKind == JsonValueKind.Number &&
           propertyElement.TryGetInt64(out var value)
            ? value
            : 0;

    private static bool TryGetInt32(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        return TryGetProperty(element, propertyName, out var propertyElement) &&
               propertyElement.ValueKind == JsonValueKind.Number &&
               propertyElement.TryGetInt32(out value);
    }

    private static bool TryGetDouble(JsonElement element, string propertyName, out double value)
    {
        value = 0;
        return TryGetProperty(element, propertyName, out var propertyElement) &&
               propertyElement.ValueKind == JsonValueKind.Number &&
               propertyElement.TryGetDouble(out value);
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!TryGetProperty(element, propertyName, out var propertyElement) ||
            propertyElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = propertyElement.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private sealed record LoggedWindowSnapshot(
        long Sequence,
        string ToolName,
        WindowDescriptor Window,
        UiElementSnapshot ElementTree);
}
