using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace HeronWin.HerFace;

internal sealed class McpClientManager : IAsyncDisposable
{
    private readonly Dictionary<string, McpClient> _clients = new();

    public async Task ConnectAsync(IReadOnlyList<McpServerConfig> servers, CancellationToken cancellationToken)
    {
        var envBaseDir = Environment.GetEnvironmentVariable("HERFACE_ENV_DIR")
                         ?? Directory.GetCurrentDirectory();

        foreach (var server in servers)
        {
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
        }
    }

    public async Task<IReadOnlyList<ToolDefinition>> ListAllToolsAsync(CancellationToken cancellationToken)
    {
        var result = new List<ToolDefinition>();

        foreach (var client in _clients.Values)
        {
            var tools = await client.ListToolsAsync();
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

    public async Task<string> CallToolAsync(string toolName, object args, CancellationToken cancellationToken)
    {
        var dictionaryArgs = args as IReadOnlyDictionary<string, object?> ??
                             JsonSerializer.Deserialize<Dictionary<string, object?>>(
                                 JsonSerializer.Serialize(args, JsonSerializerOptionsCache.Default),
                                 JsonSerializerOptionsCache.Default) ??
                             new Dictionary<string, object?>();

        foreach (var client in _clients.Values)
        {
            var tools = await client.ListToolsAsync();
            if (!tools.Any(tool => string.Equals(tool.Name, toolName, StringComparison.Ordinal)))
            {
                continue;
            }

            var result = await client.CallToolAsync(toolName, dictionaryArgs, cancellationToken: cancellationToken);
            var textBlocks = result.Content.OfType<TextContentBlock>().Select(block => block.Text);
            return string.Join('\n', textBlocks);
        }

        throw new InvalidOperationException($"Tool \"{toolName}\" not found on any connected MCP server.");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients.Values)
        {
            await client.DisposeAsync();
        }

        _clients.Clear();
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
