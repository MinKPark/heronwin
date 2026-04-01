using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace HeronWin.HerFace;

internal sealed class McpClientManager : IAsyncDisposable
{
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

            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = server.Name,
                Command = ResolveMaybeRelativePath(server.Command, envBaseDir),
                Arguments = server.Args?.Select(arg => ResolveMaybeRelativePath(arg, envBaseDir)).ToArray() ?? [],
                EnvironmentVariables = environment.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (string?)kvp.Value)
            });

            var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
            _clients[server.Name] = client;
            _toolNamesByServer[server.Name] = new HashSet<string>(StringComparer.Ordinal);
            DebugTrace.WriteEvent("mcp.connect.complete", $"server={server.Name}");
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

            DebugTrace.WriteBlock(
                "mcp.call.start",
                [
                    $"server={serverName}",
                    $"tool={toolName}",
                    $"arguments={DebugTrace.SerializeObject(dictionaryArgs)}"
                ]);
            var result = await client.CallToolAsync(toolName, dictionaryArgs, cancellationToken: cancellationToken);
            var textBlocks = result.Content.OfType<TextContentBlock>().Select(block => block.Text);
            var text = string.Join('\n', textBlocks);
            var images = ExtractImages(result.Content, text);
            var imagePaths = ExtractImageFilePathsFromJsonText(text);
            var logLines = new List<string>
            {
                $"server={serverName}",
                $"tool={toolName}",
                $"images={images.Count}",
                $"result={DebugTrace.Preview(text, 1000)}"
            };
            if (imagePaths.Count > 0)
            {
                logLines.Add($"imagePaths={string.Join(", ", imagePaths)}");
            }

            DebugTrace.WriteBlock(
                "mcp.call.complete",
                logLines);
            if (ShouldLogFullToolPayload(toolName, text))
            {
                DebugTrace.WriteTextBlock(
                    "mcp.call.complete.full",
                    [
                        $"server={serverName}",
                        $"tool={toolName}",
                        $"images={images.Count}"
                    ],
                    text);
            }
            return new ToolCallOutcome(text, images);
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
                    images.Add(new ToolImage(mimeType, base64Data));
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
        image = new ToolImage(mimeType, Convert.ToBase64String(bytes));
        return true;
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
