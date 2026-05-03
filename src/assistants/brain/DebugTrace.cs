using System.Text;
using System.Text.Json;

namespace HeronWin.Brain;

internal static class DebugTrace
{
    private static readonly object SyncRoot = new();
    private static volatile bool _isEnabled;
    private static string? _logFilePath;
    private static string? _jsonLogFilePath;
    private static string? _sessionId;
    private static long _sequenceNumber;

    internal static bool IsEnabled => _isEnabled;

    internal static string? LogFilePath => _logFilePath;

    internal static string? JsonLogFilePath => _jsonLogFilePath;

    internal static string SessionId => _sessionId ?? "(uninitialized)";

    internal static void Configure(bool isEnabled)
    {
        _isEnabled = isEnabled;
        _sequenceNumber = 0;
        _logFilePath = null;
        _jsonLogFilePath = null;
        _sessionId = Guid.NewGuid().ToString("n");

        if (!_isEnabled)
        {
            return;
        }

        var logFilePath = BuildLogFilePath(AppContext.BaseDirectory, Environment.ProcessPath);
        var jsonLogFilePath = BuildJsonLogFilePath(AppContext.BaseDirectory, Environment.ProcessPath);
        Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
        File.WriteAllText(logFilePath, string.Empty, Encoding.UTF8);
        File.WriteAllText(jsonLogFilePath, string.Empty, Encoding.UTF8);
        _logFilePath = logFilePath;
        _jsonLogFilePath = jsonLogFilePath;

        WriteStructuredEvent(
            "session.trace_ready",
            new Dictionary<string, object?>
            {
                ["pid"] = Environment.ProcessId,
                ["process"] = Environment.ProcessPath ?? "(unknown)",
                ["cwd"] = Directory.GetCurrentDirectory(),
                ["baseDir"] = AppContext.BaseDirectory,
                ["textLogPath"] = logFilePath,
                ["jsonLogPath"] = jsonLogFilePath,
            });
    }

    internal static string BuildLogsDirectory(string baseDirectory)
        => Path.Combine(baseDirectory, "logs");

    internal static string BuildLogFilePath(string baseDirectory, string? processPath)
    {
        var executableName = Path.GetFileNameWithoutExtension(processPath);
        if (string.IsNullOrWhiteSpace(executableName))
        {
            executableName = "brain";
        }

        return Path.Combine(
            BuildLogsDirectory(baseDirectory),
            $"{executableName.ToLowerInvariant()}.debug.log");
    }

    internal static string BuildJsonLogFilePath(string baseDirectory, string? processPath)
    {
        var executableName = Path.GetFileNameWithoutExtension(processPath);
        if (string.IsNullOrWhiteSpace(executableName))
        {
            executableName = "brain";
        }

        return Path.Combine(
            BuildLogsDirectory(baseDirectory),
            $"{executableName.ToLowerInvariant()}.debug.jsonl");
    }

    internal static string FormatTimestampedLine(string message, DateTimeOffset timestamp, long sequenceNumber)
        => $"[{timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] #{sequenceNumber:D5} {message}";

    internal static void WriteEvent(string category, string message)
    {
        if (!_isEnabled)
        {
            return;
        }

        WriteEntryCore(
            [$"{category}: {message}"],
            category,
            new Dictionary<string, object?> { ["message"] = message });
    }

    internal static void WriteStructuredEvent(string category, object payload)
    {
        if (!_isEnabled)
        {
            return;
        }

        WriteEntryCore(
            [$"{category}: {SerializeObject(payload, maxLength: 1400)}"],
            category,
            payload);
    }

    internal static void WriteBlock(string category, IEnumerable<string> lines)
    {
        if (!_isEnabled)
        {
            return;
        }

        var materialized = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
        if (materialized.Count == 0)
        {
            WriteEntryCore(
                [$"{category}: (empty)"],
                category,
                new Dictionary<string, object?> { ["lines"] = Array.Empty<string>() });
            return;
        }

        var textLines = new List<string>(materialized.Count + 1)
        {
            $"{category}:"
        };

        foreach (var line in materialized)
        {
            textLines.Add($"  {line}");
        }

        WriteEntryCore(
            textLines,
            category,
            new Dictionary<string, object?> { ["lines"] = materialized });
    }

    internal static void WriteTextBlock(string category, IEnumerable<string> headerLines, string text)
    {
        if (!_isEnabled)
        {
            return;
        }

        var materializedHeaders = headerLines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        var textLines = new List<string>
        {
            $"{category}:"
        };

        foreach (var line in materializedHeaders)
        {
            textLines.Add($"  {line}");
        }

        if (string.IsNullOrEmpty(text))
        {
            textLines.Add("  (empty)");
        }
        else
        {
            var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            foreach (var line in normalized.Split('\n'))
            {
                textLines.Add($"  {line}");
            }
        }

        WriteEntryCore(
            textLines,
            category,
            new Dictionary<string, object?>
            {
                ["headers"] = materializedHeaders,
                ["text"] = text,
            });
    }

    internal static void WriteLlmRequest(
        long turnId,
        int attempt,
        string providerName,
        IReadOnlyList<AgentMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        string? systemPrompt,
        string? promptSource)
    {
        if (!_isEnabled)
        {
            return;
        }

        var promptTokenEstimate = ContextManager.EstimateTokens(
            messages,
            systemPrompt ?? string.Empty);
        var lines = new List<string>
        {
            $"turn={turnId}",
            $"attempt={attempt}",
            $"provider={providerName}",
            $"messages={messages.Count}",
            $"tools={tools.Count}",
            $"promptTokenEstimate={promptTokenEstimate}"
        };

        if (!string.IsNullOrWhiteSpace(promptSource))
        {
            lines.Add($"promptSource={promptSource}");
        }

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            lines.Add($"systemPromptChars={systemPrompt.Length}");
            lines.Add($"systemPrompt={Preview(systemPrompt, 900)}");
        }

        var toolNames = tools.Select(tool => tool.Name).ToArray();
        if (toolNames.Length > 0)
        {
            lines.Add($"toolNames={string.Join(", ", toolNames)}");
        }

        var describedMessages = new List<string>(messages.Count);
        for (var index = 0; index < messages.Count; index++)
        {
            var description = DescribeMessage(messages[index]);
            lines.Add($"message[{index}] {description}");
            describedMessages.Add(description);
        }

        WriteEntryCore(
            BuildBlockLines("llm.request", lines),
            "llm.request",
            new Dictionary<string, object?>
            {
                ["turn"] = turnId,
                ["attempt"] = attempt,
                ["provider"] = providerName,
                ["messageCount"] = messages.Count,
                ["messages"] = describedMessages,
                ["toolCount"] = tools.Count,
                ["toolNames"] = toolNames,
                ["promptTokenEstimate"] = promptTokenEstimate,
                ["promptSource"] = promptSource,
                ["systemPromptChars"] = string.IsNullOrWhiteSpace(systemPrompt) ? 0 : systemPrompt.Length,
                ["systemPromptPreview"] = string.IsNullOrWhiteSpace(systemPrompt) ? null : Preview(systemPrompt, 900),
            });
    }

    internal static void WriteLlmResponse(
        long turnId,
        int attempt,
        string providerName,
        ChatResult result)
    {
        if (!_isEnabled)
        {
            return;
        }

        var lines = new List<string>
        {
            $"turn={turnId}",
            $"attempt={attempt}",
            $"provider={providerName}",
            $"text={Preview(result.Text, 1200)}",
            $"toolCalls={result.ToolCalls.Count}"
        };

        var toolCalls = new List<Dictionary<string, object?>>(result.ToolCalls.Count);
        foreach (var toolCall in result.ToolCalls)
        {
            var toolCallData = new Dictionary<string, object?>
            {
                ["id"] = toolCall.Id,
                ["name"] = toolCall.Name,
                ["argumentsPreview"] = PreviewToolArguments(toolCall.Name, toolCall.Arguments, 600),
            };

            lines.Add(
                $"toolCall id={toolCall.Id}, name={toolCall.Name}, arguments={PreviewToolArguments(toolCall.Name, toolCall.Arguments, 600)}");
            toolCalls.Add(toolCallData);
        }

        WriteEntryCore(
            BuildBlockLines("llm.response", lines),
            "llm.response",
            new Dictionary<string, object?>
            {
                ["turn"] = turnId,
                ["attempt"] = attempt,
                ["provider"] = providerName,
                ["textPreview"] = Preview(result.Text, 1200),
                ["toolCalls"] = toolCalls,
            });
    }

    internal static string PreviewToolArguments(string toolName, string? argumentsJson, int maxLength = 600)
    {
        if (!string.Equals(toolName, "type_window_text", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(argumentsJson))
        {
            return Preview(argumentsJson, maxLength);
        }

        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("text", out var textElement) ||
                textElement.ValueKind != JsonValueKind.String)
            {
                return Preview(argumentsJson, maxLength);
            }

            var value = textElement.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                return Preview(argumentsJson, maxLength);
            }

            var trimmed = value.Trim();
            if (trimmed.Length < 2 ||
                trimmed.Length > 12 ||
                trimmed.Any(char.IsWhiteSpace) ||
                trimmed.Any(character => !char.IsLetterOrDigit(character)))
            {
                return Preview(argumentsJson, maxLength);
            }

            return Preview("""{"text":"[type_window_text redacted]"}""", maxLength);
        }
        catch
        {
            return Preview(argumentsJson, maxLength);
        }
    }

    internal static string Preview(string? text, int maxLength = 400)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(empty)";
        }

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        if (normalized.Length <= maxLength)
        {
            return normalized.Replace('\n', ' ');
        }

        return $"{normalized[..maxLength].Replace('\n', ' ')}... [{normalized.Length} chars]";
    }

    internal static string SerializeObject(object? value, int maxLength = 800)
    {
        if (value is null)
        {
            return "null";
        }

        try
        {
            return Preview(JsonSerializer.Serialize(value, JsonSerializerOptionsCache.Default), maxLength);
        }
        catch
        {
            return Preview(value.ToString(), maxLength);
        }
    }

    private static IReadOnlyList<string> BuildBlockLines(string category, IEnumerable<string> lines)
    {
        var blockLines = new List<string> { $"{category}:" };
        foreach (var line in lines)
        {
            blockLines.Add($"  {line}");
        }

        return blockLines;
    }

    private static string DescribeMessage(AgentMessage message)
        => message switch
        {
            AgentMessage.User user =>
                $"role=user text={Preview(user.Content, 500)}",
            AgentMessage.Summary summary =>
                $"role=summary text={Preview(summary.Content, 500)}",
            AgentMessage.VisualContext visual =>
                $"role=user_visual text={Preview(visual.Content, 400)}, images={visual.Images.Count}",
            AgentMessage.Assistant assistant when assistant.ToolCalls is { Count: > 0 } =>
                $"role=assistant text={Preview(assistant.Content, 400)}, toolCalls={string.Join(", ", assistant.ToolCalls.Select(call => call.Name))}",
            AgentMessage.Assistant assistant =>
                $"role=assistant text={Preview(assistant.Content, 500)}",
            AgentMessage.ToolResult toolResult =>
                $"role=tool_result name={toolResult.ToolName}, text={Preview(toolResult.Content, 500)}, images={toolResult.Images?.Count ?? 0}",
            _ => $"role={message.Role}"
        };

    private static void WriteEntryCore(
        IReadOnlyList<string> textLines,
        string category,
        object? payload)
    {
        var logFilePath = _logFilePath;
        var jsonLogFilePath = _jsonLogFilePath;
        if (!_isEnabled || string.IsNullOrWhiteSpace(logFilePath) || string.IsNullOrWhiteSpace(jsonLogFilePath))
        {
            return;
        }

        var timestamp = DateTimeOffset.Now;
        var sequenceNumber = Interlocked.Increment(ref _sequenceNumber);
        var formattedLines = textLines
            .Select(line => FormatTimestampedLine(line, timestamp, sequenceNumber))
            .ToArray();
        var jsonLine = SerializeJsonEnvelope(category, payload, timestamp, sequenceNumber);

        lock (SyncRoot)
        {
            File.AppendAllLines(logFilePath, formattedLines, Encoding.UTF8);
            File.AppendAllText(jsonLogFilePath, jsonLine + Environment.NewLine, Encoding.UTF8);
        }
    }

    private static string SerializeJsonEnvelope(
        string category,
        object? payload,
        DateTimeOffset timestamp,
        long sequenceNumber)
    {
        var envelope = new Dictionary<string, object?>
        {
            ["timestamp"] = timestamp,
            ["sequence"] = sequenceNumber,
            ["sessionId"] = _sessionId,
            ["category"] = category,
            ["data"] = payload,
        };

        try
        {
            return JsonSerializer.Serialize(envelope, JsonSerializerOptionsCache.Default);
        }
        catch
        {
            envelope["data"] = payload?.ToString();
            envelope["serializationFallback"] = true;
            return JsonSerializer.Serialize(envelope, JsonSerializerOptionsCache.Default);
        }
    }
}
