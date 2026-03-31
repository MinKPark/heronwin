using System.Text.Json;

namespace HeronWin.HerFace;

internal sealed record ToolDefinition(string Name, string Description, JsonElement Parameters);

internal sealed record ToolCallRequest(string Id, string Name, string Arguments);

internal abstract record AgentMessage(string Role)
{
    public sealed record User(string Content) : AgentMessage("user");

    public sealed record Assistant(string? Content, IReadOnlyList<ToolCallRequest>? ToolCalls = null)
        : AgentMessage("assistant");

    public sealed record ToolResult(string ToolCallId, string ToolName, string Content)
        : AgentMessage("tool_result");
}

internal sealed record ChatResult(string? Text, IReadOnlyList<ToolCallRequest> ToolCalls);

internal interface ILlmClient
{
    LlmProviderId ProviderId { get; }
    string DisplayName { get; }
    Task<ChatResult> ChatAsync(
        IReadOnlyList<AgentMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken);
}

internal interface IAudioTranscriber
{
    string DisplayName { get; }
    Task<string> TranscribeAudioAsync(string audioFilePath, CancellationToken cancellationToken);
}

internal static class AgentRunner
{
    public static async Task<string> RunTurnAsync(
        string userText,
        List<AgentMessage> history,
        ILlmClient llmClient,
        McpClientManager mcpManager,
        CancellationToken cancellationToken)
    {
        var tools = await mcpManager.ListAllToolsAsync(cancellationToken);
        var messages = history.Concat([new AgentMessage.User(userText)]).ToList();
        Display.UserMessage(userText);

        while (true)
        {
            var result = await llmClient.ChatAsync(messages, tools, cancellationToken);
            if (result.ToolCalls.Count == 0)
            {
                var responseText = result.Text ?? "(no response)";
                Display.AssistantMessage(responseText);
                messages.Add(new AgentMessage.Assistant(responseText));
                return responseText;
            }

            messages.Add(new AgentMessage.Assistant(result.Text, result.ToolCalls));

            foreach (var toolCall in result.ToolCalls)
            {
                object parsedArgs = new Dictionary<string, object?>();
                try
                {
                    parsedArgs = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                                     toolCall.Arguments,
                                     JsonSerializerOptionsCache.Default)
                                 ?? new Dictionary<string, object?>();
                }
                catch
                {
                    // Keep empty object.
                }

                Display.ToolCall(toolCall.Name, toolCall.Arguments);

                string toolOutput;
                try
                {
                    toolOutput = await mcpManager.CallToolAsync(toolCall.Name, parsedArgs, cancellationToken);
                }
                catch (Exception ex)
                {
                    toolOutput = $"Error: {ex.Message}";
                }

                Display.ToolResult(toolCall.Name, toolOutput);
                messages.Add(new AgentMessage.ToolResult(toolCall.Id, toolCall.Name, toolOutput));
            }
        }
    }
}

internal static class Display
{
    private const int LabelWidth = 12;

    public static void Banner()
    {
        Console.WriteLine();
        Console.WriteLine("  ╔══════════════════════════════╗");
        Console.WriteLine("  ║         H E R F A C E        ║");
        Console.WriteLine("  ║  AI voice agent — heronwin   ║");
        Console.WriteLine("  ╚══════════════════════════════╝");
        Console.WriteLine();
    }

    public static void Info(string text) => Console.WriteLine($"i  {text}");
    public static void Warn(string text) => Console.WriteLine($"!  {text}");
    public static void Error(string text) => Console.Error.WriteLine($"x  {text}");
    public static void Separator() => Console.WriteLine(new string('─', 60));
    public static void Prompt(string text) => Console.Write(text);
    public static void Recording() => Console.WriteLine("o  Recording... (stop on silence or timeout)");
    public static void Transcribing() => Console.WriteLine(".. Transcribing speech...");

    public static void UserMessage(string text) => Console.WriteLine($"\n{Label("You")} {text}");
    public static void AssistantMessage(string text) => Console.WriteLine($"\n{Label("Assistant")} {text}");

    public static void ToolCall(string toolName, string args)
    {
        Console.WriteLine($"\n{Label("Tool call")} {toolName}");
        if (args != "{}")
        {
            Console.WriteLine($"{new string(' ', LabelWidth + 3)}{args}");
        }
    }

    public static void ToolResult(string toolName, string result)
    {
        var preview = result.Length > 200 ? $"{result[..200]}..." : result;
        Console.WriteLine($"{Label("Tool result")} ({toolName})");
        Console.WriteLine($"{new string(' ', LabelWidth + 3)}{preview}");
    }

    private static string Label(string text) => $"[{text.PadRight(LabelWidth)}]";
}

internal static class JsonSerializerOptionsCache
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}
