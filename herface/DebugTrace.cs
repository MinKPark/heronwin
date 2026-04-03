using System.Text;
using System.Text.Json;

namespace HeronWin.HerFace;

internal static class DebugTrace
{
    private static readonly object SyncRoot = new();
    private static volatile bool _isEnabled;
    private static string? _logFilePath;
    private static long _sequenceNumber;

    internal static bool IsEnabled => _isEnabled;

    internal static string? LogFilePath => _logFilePath;

    internal static void Configure(bool isEnabled)
    {
        _isEnabled = isEnabled;
        _sequenceNumber = 0;
        _logFilePath = null;

        if (!_isEnabled)
        {
            return;
        }

        var logFilePath = BuildLogFilePath(AppContext.BaseDirectory, Environment.ProcessPath);
        Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
        File.WriteAllText(logFilePath, string.Empty, Encoding.UTF8);
        _logFilePath = logFilePath;

        WriteEvent(
            "session.start",
            $"pid={Environment.ProcessId}, process={Environment.ProcessPath ?? "(unknown)"}, cwd={Directory.GetCurrentDirectory()}, baseDir={AppContext.BaseDirectory}");
    }

    internal static string BuildLogFilePath(string baseDirectory, string? processPath)
    {
        var executableName = Path.GetFileNameWithoutExtension(processPath);
        if (string.IsNullOrWhiteSpace(executableName))
        {
            executableName = "herface";
        }

        return Path.Combine(baseDirectory, $"{executableName.ToLowerInvariant()}.debug.log");
    }

    internal static string FormatTimestampedLine(string message, DateTimeOffset timestamp, long sequenceNumber)
        => $"[{timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] #{sequenceNumber:D5} {message}";

    internal static void WriteEvent(string category, string message)
    {
        if (!_isEnabled)
        {
            return;
        }

        WriteLineCore($"{category}: {message}");
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
            WriteLineCore($"{category}: (empty)");
            return;
        }

        WriteLineCore($"{category}:");
        foreach (var line in materialized)
        {
            WriteLineCore($"  {line}");
        }
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

        WriteLineCore($"{category}:");
        foreach (var line in materializedHeaders)
        {
            WriteLineCore($"  {line}");
        }

        if (string.IsNullOrEmpty(text))
        {
            WriteLineCore("  (empty)");
            return;
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        foreach (var line in normalized.Split('\n'))
        {
            WriteLineCore($"  {line}");
        }
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

        var lines = new List<string>
        {
            $"turn={turnId}",
            $"attempt={attempt}",
            $"provider={providerName}",
            $"messages={messages.Count}",
            $"tools={tools.Count}"
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

        if (tools.Count > 0)
        {
            lines.Add($"toolNames={string.Join(", ", tools.Select(tool => tool.Name))}");
        }

        for (var index = 0; index < messages.Count; index++)
        {
            lines.Add($"message[{index}] {DescribeMessage(messages[index])}");
        }

        WriteBlock("llm.request", lines);
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

        foreach (var toolCall in result.ToolCalls)
        {
            lines.Add(
                $"toolCall id={toolCall.Id}, name={toolCall.Name}, arguments={Preview(toolCall.Arguments, 600)}");
        }

        WriteBlock("llm.response", lines);
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

    private static void WriteLineCore(string message)
    {
        var logFilePath = _logFilePath;
        if (!_isEnabled || string.IsNullOrWhiteSpace(logFilePath))
        {
            return;
        }

        var sequenceNumber = Interlocked.Increment(ref _sequenceNumber);
        var formatted = FormatTimestampedLine(message, DateTimeOffset.Now, sequenceNumber);

        lock (SyncRoot)
        {
            File.AppendAllText(logFilePath, formatted + Environment.NewLine, Encoding.UTF8);
        }
    }
}
