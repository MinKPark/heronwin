using System.Text.Json;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace HeronWin.Brain;

internal sealed class McpClientManager : IAsyncDisposable
{
    private const int MaxVisionImageDimension = 1280;
    private const long VisionJpegQuality = 75L;
    private const int DefaultToolCallTimeoutMs = 20_000;

    private readonly Dictionary<string, McpClient> _clients = new();
    private readonly Dictionary<string, McpServerConfig> _serverConfigsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _toolNamesByServer = new(StringComparer.Ordinal);
    private readonly Func<CancellationToken, Task<IReadOnlyList<ToolDefinition>>>? _listAllToolsOverride;
    private readonly TimeSpan _toolCallTimeout;
    private IReadOnlyList<ToolDefinition> _cachedToolDefinitions = [];
    private bool _hasCachedToolDefinitions;
    private string _envBaseDir = Directory.GetCurrentDirectory();

    public McpClientManager()
        : this(null, null)
    {
    }

    internal McpClientManager(
        Func<CancellationToken, Task<IReadOnlyList<ToolDefinition>>>? listAllToolsOverride,
        TimeSpan? toolCallTimeoutOverride)
    {
        _listAllToolsOverride = listAllToolsOverride;
        _toolCallTimeout = toolCallTimeoutOverride ?? GetConfiguredToolCallTimeout();
    }

    public async Task ConnectAsync(IReadOnlyList<McpServerConfig> servers, CancellationToken cancellationToken)
    {
        foreach (var client in _clients.Values)
        {
            await client.DisposeAsync();
        }

        _clients.Clear();
        _serverConfigsByName.Clear();
        _toolNamesByServer.Clear();
        _cachedToolDefinitions = [];
        _hasCachedToolDefinitions = false;
        _envBaseDir = Environment.GetEnvironmentVariable("BRAIN_ENV_DIR")
                      ?? Directory.GetCurrentDirectory();

        foreach (var server in servers)
        {
            _serverConfigsByName[server.Name] = server;
            await ConnectServerAsync(server, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ToolDefinition>> ListAllToolsAsync(CancellationToken cancellationToken)
    {
        if (_hasCachedToolDefinitions)
        {
            DebugTrace.WriteEvent(
                "mcp.tools.cached",
                $"count={_cachedToolDefinitions.Count}, servers={_clients.Count}");
            return _cachedToolDefinitions;
        }

        if (_listAllToolsOverride is not null)
        {
            _cachedToolDefinitions = await _listAllToolsOverride(cancellationToken);
            _hasCachedToolDefinitions = true;
            return _cachedToolDefinitions;
        }

        var result = new List<ToolDefinition>();

        foreach (var (serverName, client) in _clients.ToArray())
        {
            var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
            var toolNames = new HashSet<string>(tools.Select(tool => tool.Name), StringComparer.Ordinal);
            _toolNamesByServer[serverName] = toolNames;
            DebugTrace.WriteEvent(
                "mcp.tools.listed",
                $"server={serverName}, tools={string.Join(", ", tools.Select(tool => tool.Name).DefaultIfEmpty("(none)"))}");
            foreach (var tool in tools)
            {
                result.Add(new ToolDefinition(
                    tool.Name,
                    tool.Description ?? string.Empty,
                    ExtractParameters(tool)));
            }
        }

        _cachedToolDefinitions = result.ToArray();
        _hasCachedToolDefinitions = true;
        return _cachedToolDefinitions;
    }

    public async Task<ToolCallOutcome> CallToolAsync(string toolName, object args, CancellationToken cancellationToken)
    {
        var dictionaryArgs = args as IReadOnlyDictionary<string, object?> ??
                             JsonSerializer.Deserialize<Dictionary<string, object?>>(
                                 JsonSerializer.Serialize(args, JsonSerializerOptionsCache.Default),
                                 JsonSerializerOptionsCache.Default) ??
                             new Dictionary<string, object?>();

        foreach (var (serverName, client) in _clients.ToArray())
        {
            if (!_toolNamesByServer.TryGetValue(serverName, out var toolNames) ||
                !toolNames.Contains(toolName))
            {
                continue;
            }

            var stopwatch = Stopwatch.StartNew();
            DebugTrace.WriteStructuredEvent(
                "mcp.call.start",
                new Dictionary<string, object?>
                {
                    ["server"] = serverName,
                    ["tool"] = toolName,
                    ["arguments"] = dictionaryArgs,
                });

            try
            {
                CallToolResult result;
                try
                {
                    result = await RunWithTimeoutAsync<CallToolResult>(
                        innerCancellationToken => client.CallToolAsync(
                            toolName,
                            dictionaryArgs,
                            cancellationToken: innerCancellationToken).AsTask(),
                        _toolCallTimeout,
                        cancellationToken);
                }
                catch (TimeoutException ex)
                {
                    DebugTrace.WriteStructuredEvent(
                        "mcp.call.timeout",
                        new Dictionary<string, object?>
                        {
                            ["server"] = serverName,
                            ["tool"] = toolName,
                            ["arguments"] = dictionaryArgs,
                            ["elapsedMs"] = (int)Math.Round(stopwatch.Elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero),
                            ["timeoutMs"] = (int)Math.Round(_toolCallTimeout.TotalMilliseconds, MidpointRounding.AwayFromZero),
                        });

                    await RecoverServerAfterTimeoutAsync(serverName, cancellationToken);

                    throw new TimeoutException(
                        $"Tool \"{toolName}\" on server \"{serverName}\" timed out after {(int)Math.Round(_toolCallTimeout.TotalMilliseconds, MidpointRounding.AwayFromZero)} ms.",
                        ex);
                }

                var structuredContentPreview = TrySerializeStructuredContent(result.StructuredContent);
                var text = BuildToolText(result, structuredContentPreview);
                var images = ExtractImages(result.Content, text);
                var imagePaths = ExtractImageFilePathsFromJsonText(text);
                var contentBlockTypes = result.Content
                    .Select(block => block.GetType().Name)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static name => name, StringComparer.Ordinal)
                    .ToArray();

                DebugTrace.WriteStructuredEvent(
                    "mcp.call.complete",
                    new Dictionary<string, object?>
                    {
                        ["server"] = serverName,
                        ["tool"] = toolName,
                        ["elapsedMs"] = (int)Math.Round(stopwatch.Elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero),
                        ["isError"] = result.IsError,
                        ["contentBlockTypes"] = contentBlockTypes,
                        ["structuredContentPreview"] = structuredContentPreview,
                        ["imageCount"] = images.Count,
                        ["imagePaths"] = imagePaths,
                        ["textPreview"] = DebugTrace.Preview(text, 1000),
                    });

                if (ShouldLogFullToolPayload(toolName, text) || result.IsError == true)
                {
                    DebugTrace.WriteTextBlock(
                        "mcp.call.complete.full",
                        [
                            $"server={serverName}",
                            $"tool={toolName}",
                            $"elapsedMs={(int)Math.Round(stopwatch.Elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero)}",
                            $"isError={result.IsError == true}",
                            $"contentBlockTypes={string.Join(", ", contentBlockTypes.DefaultIfEmpty("(none)"))}",
                            $"structuredContent={structuredContentPreview ?? "(none)"}",
                            $"images={images.Count}"
                        ],
                        text);
                }

                return new ToolCallOutcome(text, images, result.IsError == true);
            }
            catch (McpProtocolException ex)
            {
                LogToolCallFailure(serverName, toolName, dictionaryArgs, stopwatch.Elapsed, ex);
                throw;
            }
            catch (McpException ex)
            {
                LogToolCallFailure(serverName, toolName, dictionaryArgs, stopwatch.Elapsed, ex);
                throw;
            }
            catch (Exception ex)
            {
                LogToolCallFailure(serverName, toolName, dictionaryArgs, stopwatch.Elapsed, ex);
                throw;
            }
        }

        DebugTrace.WriteEvent("mcp.call.missing_tool", $"tool={toolName}");
        throw new InvalidOperationException($"Tool \"{toolName}\" not found on any connected MCP server.");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients.Values)
        {
            await client.DisposeAsync();
        }

        _clients.Clear();
        _serverConfigsByName.Clear();
        _toolNamesByServer.Clear();
        _cachedToolDefinitions = [];
        _hasCachedToolDefinitions = false;
    }

    internal static async Task<T> RunWithTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return await operation(cancellationToken);
        }

        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var operationTask = operation(timeoutCancellation.Token);
        if (operationTask.IsCompleted)
        {
            return await operationTask;
        }

        var timeoutTask = Task.Delay(timeout, cancellationToken);
        var completedTask = await Task.WhenAny(operationTask, timeoutTask);
        if (completedTask == operationTask)
        {
            return await operationTask;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        timeoutCancellation.Cancel();
        throw new TimeoutException(
            $"Operation timed out after {(int)Math.Round(timeout.TotalMilliseconds, MidpointRounding.AwayFromZero)} ms.");
    }

    private static JsonElement ExtractParameters(object tool)
    {
        var property = tool.GetType().GetProperty("JsonSchema")
                       ?? tool.GetType().GetProperty("InputSchema")
                       ?? tool.GetType().GetProperty("Schema");

        if (property?.GetValue(tool) is JsonElement jsonElement)
        {
            return jsonElement;
        }

        using var document = JsonDocument.Parse("""{"type":"object","properties":{}}""");
        return document.RootElement.Clone();
    }

    private static IReadOnlyList<ToolImage> ExtractImages(IEnumerable<object> contentBlocks, string toolText)
    {
        var images = new List<ToolImage>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var block in contentBlocks)
        {
            var type = block.GetType();
            var mimeType = type.GetProperty("MimeType")?.GetValue(block)?.ToString();
            var dataValue = type.GetProperty("Data")?.GetValue(block);
            if (string.IsNullOrWhiteSpace(mimeType) || dataValue is null)
            {
                continue;
            }

            var base64Data = dataValue switch
            {
                byte[] bytes => Convert.ToBase64String(bytes),
                string text => text,
                _ => string.Empty
            };

            if (!string.IsNullOrWhiteSpace(base64Data))
            {
                var key = $"{mimeType}:{base64Data.Length}";
                if (seenKeys.Add(key))
                {
                    images.Add(OptimizeToolImageForVision(new ToolImage(mimeType, base64Data)));
                }
            }
        }

        foreach (var image in ExtractImagesFromJsonText(toolText))
        {
            var key = $"{image.MimeType}:{image.Base64Data.Length}";
            if (seenKeys.Add(key))
            {
                images.Add(image);
            }
        }

        return images;
    }

    internal static bool ShouldLogFullToolPayload(string toolName, string toolText)
    {
        if (string.IsNullOrWhiteSpace(toolText))
        {
            return false;
        }

        return toolName is "describe_window"
            or "describe_window_compact"
            or "describe_window_focus"
            or "describe_window_focus_compact";
    }

    internal static IReadOnlyList<string> ExtractImageFilePathsFromJsonText(string toolText)
    {
        if (string.IsNullOrWhiteSpace(toolText))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(toolText);
            var paths = new List<string>();
            CollectImageFilePaths(document.RootElement, paths);
            return paths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<ToolImage> ExtractImagesFromJsonText(string toolText)
    {
        if (string.IsNullOrWhiteSpace(toolText))
        {
            return [];
        }

        try
        {
            var images = new List<ToolImage>();
            foreach (var path in ExtractImageFilePathsFromJsonText(toolText))
            {
                if (TryLoadImageFile(path, out var image))
                {
                    images.Add(image);
                }
            }
            return images;
        }
        catch
        {
            return [];
        }
    }

    private static void CollectImageFilePaths(JsonElement element, List<string> paths)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String
                        && IsImagePathProperty(property.Name))
                    {
                        var path = property.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            paths.Add(path);
                        }
                    }
                    else
                    {
                        CollectImageFilePaths(property.Value, paths);
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectImageFilePaths(item, paths);
                }

                break;
        }
    }

    private static bool IsImagePathProperty(string propertyName)
        => propertyName.Equals("imagePath", StringComparison.OrdinalIgnoreCase)
           || propertyName.Equals("image_path", StringComparison.OrdinalIgnoreCase)
           || propertyName.Equals("screenshotPath", StringComparison.OrdinalIgnoreCase)
           || propertyName.Equals("screenshot_path", StringComparison.OrdinalIgnoreCase);

    private static bool TryLoadImageFile(string? path, out ToolImage image)
    {
        image = default!;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        var mimeType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(mimeType))
        {
            return false;
        }

        var bytes = File.ReadAllBytes(path);
        image = OptimizeToolImageForVision(new ToolImage(mimeType, Convert.ToBase64String(bytes)));
        return true;
    }

    internal static ToolImage OptimizeToolImageForVision(ToolImage image)
    {
        if (!IsResizableRasterMimeType(image.MimeType))
        {
            return image;
        }

        byte[] originalBytes;
        try
        {
            originalBytes = Convert.FromBase64String(image.Base64Data);
        }
        catch
        {
            return image;
        }

        try
        {
            using var inputStream = new MemoryStream(originalBytes, writable: false);
            using var bitmap = new Bitmap(inputStream);
            var longestSide = Math.Max(bitmap.Width, bitmap.Height);
            if (longestSide <= MaxVisionImageDimension)
            {
                return image;
            }

            var scale = (double)MaxVisionImageDimension / longestSide;
            var resizedWidth = Math.Max(1, (int)Math.Round(bitmap.Width * scale, MidpointRounding.AwayFromZero));
            var resizedHeight = Math.Max(1, (int)Math.Round(bitmap.Height * scale, MidpointRounding.AwayFromZero));

            using var resizedBitmap = new Bitmap(resizedWidth, resizedHeight);
            using (var graphics = Graphics.FromImage(resizedBitmap))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                graphics.DrawImage(bitmap, 0, 0, resizedWidth, resizedHeight);
            }

            using var outputStream = new MemoryStream();
            var jpegEncoder = ImageCodecInfo.GetImageEncoders()
                .FirstOrDefault(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
            if (jpegEncoder is null)
            {
                return image;
            }

            using var encoderParameters = new EncoderParameters(1);
            encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, VisionJpegQuality);
            resizedBitmap.Save(outputStream, jpegEncoder, encoderParameters);

            return new ToolImage("image/jpeg", Convert.ToBase64String(outputStream.ToArray()), image.Detail);
        }
        catch
        {
            return image;
        }
    }

    private static bool IsResizableRasterMimeType(string mimeType)
        => mimeType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
           || mimeType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)
           || mimeType.Equals("image/bmp", StringComparison.OrdinalIgnoreCase);

    private static string BuildToolText(CallToolResult result, string? structuredContentPreview)
    {
        var textBlocks = result.Content
            .OfType<TextContentBlock>()
            .Select(block => block.Text)
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .ToArray();
        if (textBlocks.Length > 0)
        {
            return string.Join('\n', textBlocks);
        }

        return structuredContentPreview ?? string.Empty;
    }

    private static string? TrySerializeStructuredContent(object? structuredContent)
    {
        if (structuredContent is null)
        {
            return null;
        }

        if (structuredContent is JsonElement jsonElement &&
            jsonElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        try
        {
            var serialized = JsonSerializer.Serialize(structuredContent, JsonSerializerOptionsCache.Default);
            return string.Equals(serialized, "null", StringComparison.Ordinal) ? null : serialized;
        }
        catch
        {
            return structuredContent.ToString();
        }
    }

    private static void LogToolCallFailure(
        string serverName,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        TimeSpan elapsed,
        Exception ex)
    {
        DebugTrace.WriteStructuredEvent(
            "mcp.call.failed",
            new Dictionary<string, object?>
            {
                ["server"] = serverName,
                ["tool"] = toolName,
                ["arguments"] = arguments,
                ["elapsedMs"] = (int)Math.Round(elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero),
                ["exceptionType"] = ex.GetType().FullName ?? ex.GetType().Name,
                ["message"] = ex.Message,
                ["mcpErrorCode"] = ex is McpProtocolException protocolException
                    ? protocolException.ErrorCode.ToString()
                    : null,
                ["innerExceptionType"] = ex.InnerException?.GetType().FullName,
                ["innerMessage"] = ex.InnerException?.Message,
                ["exceptionPreview"] = DebugTrace.Preview(ex.ToString(), 1400),
            });
    }

    private async Task ConnectServerAsync(McpServerConfig server, CancellationToken cancellationToken)
    {
        DebugTrace.WriteBlock(
            "mcp.connect.start",
            [
                $"server={server.Name}",
                $"command={ResolveMaybeRelativePath(server.Command, _envBaseDir)}",
                $"args={DebugTrace.SerializeObject(server.Args ?? [])}"
            ]);

        var environment = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => entry.Value?.ToString() ?? string.Empty);

        if (server.Env is not null)
        {
            foreach (var kvp in server.Env)
            {
                environment[kvp.Key] = kvp.Value;
            }
        }

        ApplyServerDebugEnvironment(server, environment);
        var resolvedCommand = ResolveMaybeRelativePath(server.Command, _envBaseDir);
        var resolvedArguments = server.Args?.Select(arg => ResolveMaybeRelativePath(arg, _envBaseDir)).ToArray() ?? [];

        DebugTrace.WriteStructuredEvent(
            "mcp.connect.start",
            new Dictionary<string, object?>
            {
                ["server"] = server.Name,
                ["command"] = resolvedCommand,
                ["arguments"] = resolvedArguments,
                ["workingDirectory"] = _envBaseDir,
                ["serverEnvKeys"] = server.Env?.Keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase).ToArray() ?? [],
                ["stderrCaptureEnabled"] = true,
                ["desktopAutomationDebugEnabled"] = IsDesktopAutomationServer(server) && DebugTrace.IsEnabled,
            });

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = server.Name,
            Command = resolvedCommand,
            Arguments = resolvedArguments,
            WorkingDirectory = _envBaseDir,
            EnvironmentVariables = environment.ToDictionary(
                kvp => kvp.Key,
                kvp => (string?)kvp.Value),
            StandardErrorLines = line =>
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    return;
                }

                DebugTrace.WriteStructuredEvent(
                    "mcp.stderr",
                    new Dictionary<string, object?>
                    {
                        ["server"] = server.Name,
                        ["line"] = line,
                    });
            }
        });

        var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
        _clients[server.Name] = client;
        _toolNamesByServer[server.Name] = new HashSet<string>(StringComparer.Ordinal);
        DebugTrace.WriteStructuredEvent(
            "mcp.connect.complete",
            new Dictionary<string, object?>
            {
                ["server"] = server.Name,
                ["workingDirectory"] = _envBaseDir,
                ["command"] = resolvedCommand,
                ["arguments"] = resolvedArguments,
            });
    }

    private async Task RecoverServerAfterTimeoutAsync(string serverName, CancellationToken cancellationToken)
    {
        if (!_serverConfigsByName.TryGetValue(serverName, out var server))
        {
            return;
        }

        var preservedToolNames = _toolNamesByServer.TryGetValue(serverName, out var toolNames)
            ? new HashSet<string>(toolNames, StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        DebugTrace.WriteStructuredEvent(
            "mcp.reconnect.start",
            new Dictionary<string, object?>
            {
                ["server"] = serverName,
                ["reason"] = "tool_call_timeout",
                ["knownToolCount"] = preservedToolNames.Count,
            });

        if (_clients.Remove(serverName, out var existingClient))
        {
            try
            {
                await existingClient.DisposeAsync();
            }
            catch (Exception ex)
            {
                DebugTrace.WriteStructuredEvent(
                    "mcp.reconnect.dispose_failed",
                    new Dictionary<string, object?>
                    {
                        ["server"] = serverName,
                        ["error"] = DebugTrace.Preview(ex.ToString(), 700),
                    });
            }
        }

        _toolNamesByServer[serverName] = preservedToolNames;
        _cachedToolDefinitions = [];
        _hasCachedToolDefinitions = false;

        try
        {
            await ConnectServerAsync(server, cancellationToken);
            if (preservedToolNames.Count > 0)
            {
                _toolNamesByServer[serverName] = preservedToolNames;
            }

            DebugTrace.WriteStructuredEvent(
                "mcp.reconnect.complete",
                new Dictionary<string, object?>
                {
                    ["server"] = serverName,
                    ["knownToolCount"] = _toolNamesByServer[serverName].Count,
                });
        }
        catch (Exception ex)
        {
            DebugTrace.WriteStructuredEvent(
                "mcp.reconnect.failed",
                new Dictionary<string, object?>
                {
                    ["server"] = serverName,
                    ["error"] = DebugTrace.Preview(ex.ToString(), 900),
                });
            throw;
        }
    }

    private static TimeSpan GetConfiguredToolCallTimeout()
    {
        var rawValue = Environment.GetEnvironmentVariable("MCP_TOOL_TIMEOUT_MS");
        if (!int.TryParse(rawValue, out var timeoutMs) || timeoutMs <= 0)
        {
            timeoutMs = DefaultToolCallTimeoutMs;
        }

        return TimeSpan.FromMilliseconds(timeoutMs);
    }

    private static void ApplyServerDebugEnvironment(McpServerConfig server, Dictionary<string, string> environment)
    {
        environment["BRAIN_DEBUG_SESSION_ID"] = DebugTrace.SessionId;

        if (IsDesktopAutomationServer(server))
        {
            environment["BRAIN_DEBUG_ARTIFACT_DIR"] = DebugTrace.BuildLogsDirectory(AppContext.BaseDirectory);
        }

        if (DebugTrace.IsEnabled && IsDesktopAutomationServer(server))
        {
            environment["BODY_WINDOWS_DEBUG"] = "1";
        }
    }

    private static bool IsDesktopAutomationServer(McpServerConfig server)
    {
        if (server.Name.Contains("cognition", StringComparison.OrdinalIgnoreCase) ||
            server.Name.Contains("execution", StringComparison.OrdinalIgnoreCase) ||
            server.Command.Contains("cognition", StringComparison.OrdinalIgnoreCase) ||
            server.Command.Contains("execution", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return server.Args?.Any(arg =>
            arg.Contains("cognition", StringComparison.OrdinalIgnoreCase) ||
            arg.Contains("execution", StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static string ResolveMaybeRelativePath(string value, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var looksLikeRelativePath = value.StartsWith(".\\", StringComparison.Ordinal)
                                    || value.StartsWith("./", StringComparison.Ordinal)
                                    || value.StartsWith("..\\", StringComparison.Ordinal)
                                    || value.StartsWith("../", StringComparison.Ordinal);
        if (!looksLikeRelativePath || Path.IsPathRooted(value))
        {
            return value;
        }

        return Path.GetFullPath(Path.Combine(baseDir, value));
    }
}
