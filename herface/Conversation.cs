using System.Text.Json;
using System.Text;

namespace HeronWin.HerFace;

internal sealed record ToolDefinition(string Name, string Description, JsonElement Parameters);

internal sealed record ToolCallRequest(string Id, string Name, string Arguments);

internal sealed record ToolImage(string MimeType, string Base64Data);

internal sealed record ToolCallOutcome(string Text, IReadOnlyList<ToolImage> Images);

internal sealed record AgentReply(string LogText, string SpokenText, string RawText);

internal abstract record AgentMessage(string Role)
{
    public sealed record User(string Content) : AgentMessage("user");

    public sealed record Summary(string Content) : AgentMessage("summary");

    public sealed record VisualContext(string Content, IReadOnlyList<ToolImage> Images) : AgentMessage("user_visual");

    public sealed record Assistant(string? Content, IReadOnlyList<ToolCallRequest>? ToolCalls = null)
        : AgentMessage("assistant");

    public sealed record ToolResult(
        string ToolCallId,
        string ToolName,
        string Content,
        IReadOnlyList<ToolImage>? Images = null)
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
    public static async Task<AgentReply> RunTurnAsync(
        string userText,
        List<AgentMessage> history,
        AppConfig config,
        ILlmClient llmClient,
        McpClientManager mcpManager,
        CancellationToken cancellationToken)
    {
        var tools = await mcpManager.ListAllToolsAsync(cancellationToken);
        var messages = history.Concat([new AgentMessage.User(userText)]).ToList();
        Display.UserMessage(userText);
        var usedAnyTools = false;
        var performedDesktopAction = false;

        while (true)
        {
            var result = await llmClient.ChatAsync(messages, tools, cancellationToken);
            if (result.ToolCalls.Count == 0)
            {
                var responseText = result.Text ?? """{"say":"","log":"(no response)"}""";
                var parsedReply = AssistantResponseParser.Parse(responseText);
                if (NeedsRepair(responseText, parsedReply, usedAnyTools, performedDesktopAction))
                {
                    var repairedReply = await llmClient.ChatAsync(
                        [
                            ..messages,
                            new AgentMessage.Assistant(responseText),
                            new AgentMessage.User(BuildRepairInstruction(performedDesktopAction))
                        ],
                        [],
                        cancellationToken);
                    if (repairedReply.ToolCalls.Count == 0 && !string.IsNullOrWhiteSpace(repairedReply.Text))
                    {
                        responseText = repairedReply.Text;
                        parsedReply = AssistantResponseParser.Parse(responseText);
                    }
                }

                Display.AssistantReply(parsedReply.SpokenText, parsedReply.LogText);

                messages.Add(new AgentMessage.Assistant(responseText));
                return parsedReply with { RawText = responseText };
            }

            messages.Add(new AgentMessage.Assistant(result.Text, result.ToolCalls));
            var followUpEvidence = new List<AgentMessage>();

            foreach (var toolCall in result.ToolCalls)
            {
                usedAnyTools = true;
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

                ToolCallOutcome toolOutput;
                try
                {
                    toolOutput = await mcpManager.CallToolAsync(toolCall.Name, parsedArgs, cancellationToken);
                }
                catch (Exception ex)
                {
                    toolOutput = new ToolCallOutcome($"Error: {ex.Message}", []);
                }

                Display.ToolResult(toolCall.Name, toolOutput.Text, toolOutput.Images.Count);
                messages.Add(new AgentMessage.ToolResult(toolCall.Id, toolCall.Name, toolOutput.Text, toolOutput.Images));
                if (toolOutput.Images.Count > 0)
                {
                    followUpEvidence.Add(new AgentMessage.VisualContext(
                        $"Supplemental screenshot output from tool \"{toolCall.Name}\". Treat these images as the source of truth for what is visibly on screen before answering.",
                        toolOutput.Images));
                }

                if (IsDesktopActionTool(toolCall.Name))
                {
                    performedDesktopAction = true;
                    try
                    {
                        var parsedArgsDictionary = parsedArgs as IReadOnlyDictionary<string, object?> ?? new Dictionary<string, object?>();
                        await Task.Delay(GetPostActionDelayMs(toolCall.Name, config), cancellationToken);

                        if (toolCall.Name == "launch_app_via_taskbar_search")
                        {
                            var appName = TryGetStringArgument(parsedArgsDictionary, "appName");
                            if (!string.IsNullOrWhiteSpace(appName))
                            {
                                try
                                {
                                    var selectResult = await mcpManager.CallToolAsync(
                                        "select_window",
                                        new Dictionary<string, object?> { ["titleContains"] = appName },
                                        cancellationToken);
                                    Display.ToolResult("select_window", selectResult.Text, selectResult.Images.Count);
                                    followUpEvidence.Add(new AgentMessage.User(
                                        $"Internal window re-selection after launching \"{appName}\":\n{selectResult.Text}"));
                                    if (selectResult.Images.Count > 0)
                                    {
                                        followUpEvidence.Add(new AgentMessage.VisualContext(
                                            "Internal screenshot evidence after re-selecting the launched app window. Treat the screenshot as authoritative for the current visible screen.",
                                            selectResult.Images));
                                    }
                                }
                                catch (Exception ex)
                                {
                                    followUpEvidence.Add(new AgentMessage.User(
                                        $"Attempt to re-select the launched app window by title \"{appName}\" was unavailable: {ex.Message}"));
                                }
                            }
                        }

                        var postActionSnapshot = await mcpManager.CallToolAsync(
                            "describe_selected_window",
                            new Dictionary<string, object?> { ["maxDepth"] = 4 },
                            cancellationToken);
                        Display.ToolResult("describe_selected_window", postActionSnapshot.Text, postActionSnapshot.Images.Count);
                        followUpEvidence.Add(new AgentMessage.User(
                            $"Post-action visible UI snapshot after tool \"{toolCall.Name}\":\n{postActionSnapshot.Text}\nUse this UI Automation tree first. If it is too sparse or ambiguous to describe the visible screen confidently, call capture_selected_window_screenshot before answering."));
                        if (postActionSnapshot.Images.Count > 0)
                        {
                            followUpEvidence.Add(new AgentMessage.VisualContext(
                                "Post-action visual evidence for the current selected window. Use it only if you actually have image content available.",
                                postActionSnapshot.Images));
                        }
                    }
                    catch (Exception ex)
                    {
                        followUpEvidence.Add(new AgentMessage.User(
                            $"Post-action UI snapshot was unavailable after tool \"{toolCall.Name}\": {ex.Message}"));
                    }
                }
            }

            messages.AddRange(followUpEvidence);
        }
    }

    private static bool IsDesktopActionTool(string toolName)
        => toolName is "launch_app_via_taskbar_search"
            or "select_taskbar_app"
            or "select_window"
            or "click_selected_window_element"
            or "focus_selected_window_element"
            or "invoke_main_menu_item"
            or "invoke_context_menu_item"
            or "send_input_to_window";

    private static int GetPostActionDelayMs(string toolName, AppConfig config)
        => toolName switch
        {
            "launch_app_via_taskbar_search" => Math.Max(0, config.LaunchAppPostActionDelayMs),
            "invoke_main_menu_item" or "invoke_context_menu_item" => Math.Max(0, config.InvokePostActionDelayMs),
            _ => 300
        };

    private static bool NeedsRepair(string rawText, AgentReply reply, bool usedAnyTools, bool performedDesktopAction)
    {
        var isStructured = AssistantResponseParser.IsStructuredJson(rawText);
        if (!isStructured)
        {
            return true;
        }

        if (performedDesktopAction && string.IsNullOrWhiteSpace(reply.SpokenText))
        {
            return true;
        }

        return usedAnyTools && string.IsNullOrWhiteSpace(reply.LogText);
    }

    private static string BuildRepairInstruction(bool performedDesktopAction)
        => performedDesktopAction
            ? "Rewrite your previous answer as strict JSON only: {\"say\":\"...\",\"log\":\"...\"}. Use the post-action UI Automation tree first. If the current evidence is too sparse or ambiguous to describe the visible screen confidently, do not guess. In say, include the action outcome, the current visible screen state if it is supported by evidence, and 2 or 3 likely next actions. In log, include the fuller evidence-based description and briefly note any uncertainty."
            : "Rewrite your previous answer as strict JSON only: {\"say\":\"...\",\"log\":\"...\"}. Keep say short and spoken-friendly. Put fuller detail in log.";

    private static string? TryGetStringArgument(IReadOnlyDictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string text => text,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            _ => value.ToString()
        };
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
    public static void Transcript(string text) => Console.WriteLine($"\n{Label("Heard")} {text}");

    public static void UserMessage(string text) => Console.WriteLine($"\n{Label("You")} {text}");
    public static void AssistantMessage(string text) => Console.WriteLine($"\n{Label("Assistant")} {text}");
    public static void AssistantReply(string sayText, string logText)
    {
        var normalizedSay = string.IsNullOrWhiteSpace(sayText) ? string.Empty : sayText.Trim();
        var normalizedLog = string.IsNullOrWhiteSpace(logText) ? string.Empty : logText.Trim();

        if (!string.IsNullOrWhiteSpace(normalizedSay))
        {
            Console.WriteLine($"\n{Label("Say")} {normalizedSay}");
        }

        if (!string.IsNullOrWhiteSpace(normalizedLog))
        {
            Console.WriteLine($"\n{Label("Log")} {normalizedLog}");
        }
    }

    public static void ContextUsage(int currentTokens, int maxTokens)
    {
        var ratio = maxTokens <= 0 ? 0 : currentTokens / (double)maxTokens * 100;
        Console.WriteLine($"i  Context: ~{currentTokens:N0} / {maxTokens:N0} tokens ({ratio:F1}%)");
    }

    public static void ContextCompressed(int currentTokens, int maxTokens)
    {
        var ratio = maxTokens <= 0 ? 0 : currentTokens / (double)maxTokens * 100;
        Console.WriteLine($"i  Context compressed: ~{currentTokens:N0} / {maxTokens:N0} tokens ({ratio:F1}%)");
    }

    public static void ToolCall(string toolName, string args)
    {
        Console.WriteLine($"\n{Label("Tool call")} {toolName}");
        if (args != "{}")
        {
            Console.WriteLine($"{new string(' ', LabelWidth + 3)}{args}");
        }
    }

    public static void ToolResult(string toolName, string result, int imageCount = 0)
    {
        var preview = result.Length > 200 ? $"{result[..200]}..." : result;
        Console.WriteLine($"{Label("Tool result")} ({toolName})");
        Console.WriteLine($"{new string(' ', LabelWidth + 3)}{preview}");
        if (imageCount > 0)
        {
            Console.WriteLine($"{new string(' ', LabelWidth + 3)}[{imageCount} screenshot attachment(s)]");
        }
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

internal static class AssistantResponseParser
{
    public static bool IsStructuredJson(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(rawText);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && (document.RootElement.TryGetProperty("say", out _)
                       || document.RootElement.TryGetProperty("log", out _));
        }
        catch
        {
            return false;
        }
    }

    public static AgentReply Parse(string rawText)
    {
        if (!string.IsNullOrWhiteSpace(rawText))
        {
            try
            {
                using var document = JsonDocument.Parse(rawText);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var say = document.RootElement.TryGetProperty("say", out var sayElement)
                        ? sayElement.GetString() ?? string.Empty
                        : string.Empty;
                    var log = document.RootElement.TryGetProperty("log", out var logElement)
                        ? logElement.GetString() ?? string.Empty
                        : string.Empty;

                    if (!string.IsNullOrWhiteSpace(say) || !string.IsNullOrWhiteSpace(log))
                    {
                        return new AgentReply(
                            string.IsNullOrWhiteSpace(log) ? say : log,
                            say,
                            rawText);
                    }
                }
            }
            catch
            {
                // Fall through to plain-text fallback.
            }
        }

        return new AgentReply(rawText, BuildSpeechFallback(rawText), rawText);
    }

    private static string BuildSpeechFallback(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return string.Empty;
        }

        var normalized = rawText
            .Replace("**", string.Empty)
            .Replace("`", string.Empty)
            .Replace("\r", " ")
            .Replace("\n", " ");

        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ").Trim();
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"^\s*[-*•]+\s*", string.Empty);

        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var sentenceEnd = normalized.IndexOfAny(['.', '!', '?']);
        var spoken = sentenceEnd >= 0
            ? normalized[..(sentenceEnd + 1)]
            : normalized;

        return spoken.Length > 180 ? $"{spoken[..177].Trim()}..." : spoken.Trim();
    }
}

internal static class ContextManager
{
    private const double CompressionTriggerRatio = 0.7;
    private const int KeepRecentMessages = 8;

    public static int EstimateTokens(
        IReadOnlyList<AgentMessage> history,
        string agentDefinition,
        string? pendingUserText = null)
    {
        var total = EstimateTextTokens(agentDefinition) + 16;
        foreach (var message in history)
        {
            total += message switch
            {
                AgentMessage.User user => EstimateTextTokens(user.Content) + 8,
                AgentMessage.Summary summary => EstimateTextTokens(summary.Content) + 12,
                AgentMessage.VisualContext visual => EstimateTextTokens(visual.Content) + (visual.Images.Count * 512) + 16,
                AgentMessage.Assistant assistant => EstimateTextTokens(assistant.Content ?? string.Empty) + 8,
                AgentMessage.ToolResult toolResult => EstimateTextTokens(toolResult.Content) + 16,
                _ => 8
            };
        }

        if (!string.IsNullOrWhiteSpace(pendingUserText))
        {
            total += EstimateTextTokens(pendingUserText) + 8;
        }

        return total;
    }

    public static async Task EnsureCapacityAsync(
        List<AgentMessage> history,
        string pendingUserText,
        string agentDefinition,
        int maxContextTokens,
        ILlmClient llmClient,
        CancellationToken cancellationToken)
    {
        var currentTokens = EstimateTokens(history, agentDefinition, pendingUserText);
        Display.ContextUsage(currentTokens, maxContextTokens);

        if (maxContextTokens <= 0
            || currentTokens < maxContextTokens * CompressionTriggerRatio
            || history.Count <= KeepRecentMessages)
        {
            return;
        }

        var splitIndex = Math.Max(1, history.Count - KeepRecentMessages);
        var messagesToCompress = history.Take(splitIndex).ToList();
        var summaryText = await SummarizeAsync(messagesToCompress, llmClient, cancellationToken);

        history.RemoveRange(0, splitIndex);
        history.Insert(0, new AgentMessage.Summary(summaryText));

        var compressedTokens = EstimateTokens(history, agentDefinition, pendingUserText);
        Display.ContextCompressed(compressedTokens, maxContextTokens);
    }

    private static async Task<string> SummarizeAsync(
        IReadOnlyList<AgentMessage> messages,
        ILlmClient llmClient,
        CancellationToken cancellationToken)
    {
        var transcript = new StringBuilder();
        foreach (var message in messages)
        {
            switch (message)
            {
                case AgentMessage.User user:
                    transcript.AppendLine($"User: {user.Content}");
                    break;
                case AgentMessage.Summary summary:
                    transcript.AppendLine($"Prior summary: {summary.Content}");
                    break;
                case AgentMessage.Assistant assistant:
                    transcript.AppendLine($"Assistant: {assistant.Content}");
                    break;
            }
        }

        var summaryPrompt = """
Summarize the earlier conversation into a compact factual memory for future turns.
Keep only durable context:
- user preferences and instructions
- unresolved tasks or constraints
- important UI/app state that may still matter
- important factual findings from prior turns

Return plain text only, under 250 words, with short lines.
""";

        var result = await llmClient.ChatAsync(
            [new AgentMessage.User($"{summaryPrompt}\n\nConversation:\n{transcript}")],
            [],
            cancellationToken);

        var summaryText = result.Text ?? string.Empty;
        if (AssistantResponseParser.IsStructuredJson(summaryText))
        {
            var parsed = AssistantResponseParser.Parse(summaryText);
            summaryText = string.IsNullOrWhiteSpace(parsed.LogText) ? parsed.SpokenText : parsed.LogText;
        }

        summaryText = summaryText.Trim();
        return string.IsNullOrWhiteSpace(summaryText)
            ? "Earlier conversation was compressed, but no durable context was extracted."
            : summaryText;
    }

    private static int EstimateTextTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return Math.Max(1, (text.Length + 3) / 4);
    }
}
