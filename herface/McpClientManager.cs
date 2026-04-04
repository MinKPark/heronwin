using System.Text.Json;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace HeronWin.HerFace;

internal sealed class McpClientManager : IAsyncDisposable
{
    private const int MaxVisionImageDimension = 1280;
    private const long VisionJpegQuality = 75L;

    private readonly Dictionary<string, McpClient> _clients = new();
    private readonly Dictionary<string, HashSet<string>> _toolNamesByServer = new(StringComparer.Ordinal);

    public async Task ConnectAsync(IReadOnlyList<McpServerConfig> servers, CancellationToken cancellationToken)
    {
        var envBaseDir = Environment.GetEnvironmentVariable("HERFACE_ENV_DIR")
                         ?? Directory.GetCurrentDirectory();

        foreach (var server in servers)
        {
            DebugTrace.WriteBlock(
                "mcp.connect.start",
                [
                    $"server={server.Name}",
                    $"command={ResolveMaybeRelativePath(server.Command, envBaseDir)}",
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
            var resolvedCommand = ResolveMaybeRelativePath(server.Command, envBaseDir);
            var resolvedArguments = server.Args?.Select(arg => ResolveMaybeRelativePath(arg, envBaseDir)).ToArray() ?? [];

            DebugTrace.WriteStructuredEvent(
                "mcp.connect.start",
                new Dictionary<string, object?>
                {
                    ["server"] = server.Name,
                    ["command"] = resolvedCommand,
                    ["arguments"] = resolvedArguments,
                    ["workingDirectory"] = envBaseDir,
                    ["serverEnvKeys"] = server.Env?.Keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase).ToArray() ?? [],
                    ["stderrCaptureEnabled"] = true,
                    ["eyesAndHandsDebugEnabled"] = IsEyesAndHandsServer(server) && DebugTrace.IsEnabled,
                });

            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = server.Name,
                Command = resolvedCommand,
                Arguments = resolvedArguments,
                WorkingDirectory = envBaseDir,
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
                    ["workingDirectory"] = envBaseDir,
                    ["command"] = resolvedCommand,
                    ["arguments"] = resolvedArguments,
                });
        }
    }

    public async Task<IReadOnlyList<ToolDefinition>> ListAllToolsAsync(CancellationToken cancellationToken)
    {
        var result = new List<ToolDefinition>();

        foreach (var (serverName, client) in _clients)
        {
            var tools = await client.ListToolsAsync();
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

        return result;
    }

    public async Task<ToolCallOutcome> CallToolAsync(string toolName, object args, CancellationToken cancellationToken)
    {
        var dictionaryArgs = args as IReadOnlyDictionary<string, object?> ??
                             JsonSerializer.Deserialize<Dictionary<string, object?>>(
                                 JsonSerializer.Serialize(args, JsonSerializerOptionsCache.Default),
                                 JsonSerializerOptionsCache.Default) ??
                             new Dictionary<string, object?>();

        foreach (var (serverName, client) in _clients)
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
                var result = await client.CallToolAsync(toolName, dictionaryArgs, cancellationToken: cancellationToken);
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
        _toolNamesByServer.Clear();
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

        return toolName is "describe_selected_window"
            or "describe_selected_window_focus";
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

    private static void ApplyServerDebugEnvironment(McpServerConfig server, Dictionary<string, string> environment)
    {
        environment["HERFACE_DEBUG_SESSION_ID"] = DebugTrace.SessionId;

        if (DebugTrace.IsEnabled && IsEyesAndHandsServer(server))
        {
            environment["EYESANDHANDS_DEBUG"] = "1";
        }
    }

    private static bool IsEyesAndHandsServer(McpServerConfig server)
    {
        if (server.Name.Contains("eyesandhands", StringComparison.OrdinalIgnoreCase) ||
            server.Command.Contains("eyesandhands", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return server.Args?.Any(arg => arg.Contains("eyesandhands", StringComparison.OrdinalIgnoreCase)) == true;
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
