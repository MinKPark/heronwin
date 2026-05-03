using System.Text.Json;

namespace HeronWin.Brain;

internal sealed record McpServerConfig(
    string Name,
    string Command,
    string[]? Args,
    Dictionary<string, string>? Env
);

internal enum LlmProviderId
{
    OpenAiApi,
    OpenAiCodex,
    ClaudeApi,
}

internal sealed record AppConfig(
    string AssistantId,
    LlmProviderId LlmProvider,
    string AgentDefinitionPath,
    string AgentDefinition,
    AgentPromptCatalog AgentPrompts,
    bool DebugAudioPlayback,
    bool EnableDebugTrace,
    string OpenAiApiKey,
    string OpenAiModel,
    string OpenAiCodexCommand,
    string OpenAiCodexModel,
    double LlmTemperature,
    string TtsModel,
    string TtsVoice,
    string TtsInstructions,
    string AnthropicApiKey,
    string AnthropicModel,
    string WhisperModel,
    IReadOnlyList<VoiceLanguagePreference> VoiceLanguages,
    int MaxRecordMs,
    int ActiveIdleTimeoutMs,
    int PostActionUiSettleDelayMs,
    int MaxContextTokens,
    string WakeWord,
    IReadOnlyList<McpServerConfig> McpServers
)
{
    public static AppConfig Load(string assistantId = "cursor")
    {
        var normalizedAssistantId = NormalizeAssistantId(assistantId);
        DotEnvLoader.Load(normalizedAssistantId);

        var rawProvider = Environment.GetEnvironmentVariable("LLM_PROVIDER") ?? "openai-api";
        var provider = NormalizeProvider(rawProvider);
        if (LooksLikeDeprecatedCodexAlias(rawProvider))
        {
            Console.WriteLine(
                "Warning: LLM_PROVIDER aliases \"chatgpt\" and \"chatgpt-web\" now map to the experimental \"openai-codex\" provider.");
        }

        var agentPrompts = AgentPromptLoader.Load(normalizedAssistantId);
        return new AppConfig(
            normalizedAssistantId,
            provider,
            agentPrompts.FallbackDefinitionPath,
            agentPrompts.FallbackDefinition,
            agentPrompts,
            ParseBoolean(Environment.GetEnvironmentVariable("DEBUG_AUDIO_PLAYBACK"), fallback: false),
            ParseBoolean(Environment.GetEnvironmentVariable("DEBUG_TRACE"), fallback: false),
            Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty,
            Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-5.4-mini",
            Environment.GetEnvironmentVariable("OPENAI_CODEX_COMMAND") ?? "codex",
            Environment.GetEnvironmentVariable("OPENAI_CODEX_MODEL") ?? string.Empty,
            ParseDouble(Environment.GetEnvironmentVariable("LLM_TEMPERATURE"), 0),
            Environment.GetEnvironmentVariable("TTS_MODEL") ?? "gpt-4o-mini-tts",
            Environment.GetEnvironmentVariable("TTS_VOICE") ?? "marin",
            Environment.GetEnvironmentVariable("TTS_INSTRUCTIONS")
                ?? "Speak like a mature, casual woman with a warm, grounded presence. Keep the tone natural, relaxed, and supportive, with soft confidence and gentle pacing. Avoid sounding bubbly, theatrical, or overly formal.",
            Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? string.Empty,
            Environment.GetEnvironmentVariable("ANTHROPIC_MODEL") ?? "claude-3-5-sonnet-20241022",
            Environment.GetEnvironmentVariable("WHISPER_MODEL") ?? "whisper-1",
            VoiceLanguagePreferences.Parse(Environment.GetEnvironmentVariable("VOICE_LANGUAGES")),
            ParseInt(Environment.GetEnvironmentVariable("MAX_RECORD_MS"), 30_000),
            ParseInt(Environment.GetEnvironmentVariable("ACTIVE_IDLE_TIMEOUT_MS"), 60_000),
            Math.Max(0, ParseInt(Environment.GetEnvironmentVariable("POST_ACTION_UI_SETTLE_DELAY_MS"), 1_000)),
            ParseInt(Environment.GetEnvironmentVariable("MAX_CONTEXT_TOKENS"), 128_000),
            Environment.GetEnvironmentVariable("WAKE_WORD") ?? "Hello there",
            LoadMcpServers(Environment.GetEnvironmentVariable("MCP_SERVERS"))
        );
    }

    private static IReadOnlyList<McpServerConfig> LoadMcpServers(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "[]")
        {
            return [];
        }

        try
        {
            var value = JsonSerializer.Deserialize<List<McpServerConfig>>(raw, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            var envBaseDir = Environment.GetEnvironmentVariable("HERONWIN_ENV_DIR")
                             ?? Environment.GetEnvironmentVariable("BRAIN_ENV_DIR")
                             ?? Directory.GetCurrentDirectory();
            return (value ?? [])
                .Select(server => server with
                {
                    Command = ResolveMaybeRelativePath(server.Command, envBaseDir),
                    Args = server.Args?.Select(arg => ResolveMaybeRelativePath(arg, envBaseDir)).ToArray()
                })
                .ToList();
        }
        catch
        {
            Console.WriteLine("Warning: MCP_SERVERS is not valid JSON, ignoring.");
            return [];
        }
    }

    internal static LlmProviderId NormalizeProvider(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "openai" or "openai-api" => LlmProviderId.OpenAiApi,
            "openai-codex" or "chatgpt-subscription" or "gpt" or "chatgpt" or "chatgpt-web" => LlmProviderId.OpenAiCodex,
            "claude" or "claude-api" => LlmProviderId.ClaudeApi,
            _ => throw new InvalidOperationException(
                $"Invalid LLM_PROVIDER \"{value}\". Must be \"openai-api\", \"openai-codex\", or \"claude-api\".")
        };

    private static bool LooksLikeDeprecatedCodexAlias(string value)
        => value.Trim().ToLowerInvariant() is "gpt" or "chatgpt" or "chatgpt-web";

    private static string NormalizeAssistantId(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "tars" => "tars",
            "cursor" => "cursor",
            _ => "cursor"
        };

    private static bool ParseBoolean(string? value, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => throw new InvalidOperationException($"Invalid boolean value \"{value}\".")
        };
    }

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, out var parsed) ? parsed : fallback;

    private static double ParseDouble(string? value, double fallback)
        => double.TryParse(value, out var parsed) ? parsed : fallback;

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

internal static class DotEnvLoader
{
    public static void Load(string assistantId)
    {
        foreach (var candidate in EnumerateCandidatePaths(assistantId).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            foreach (var line in File.ReadAllLines(candidate))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                {
                    continue;
                }

                var separatorIndex = trimmed.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = trimmed[..separatorIndex].Trim();
                var value = trimmed[(separatorIndex + 1)..].Trim();
                if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
                {
                    value = value[1..^1];
                }

                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
                {
                    Environment.SetEnvironmentVariable(key, value);
                }
            }

            var envDirectory = Path.GetDirectoryName(candidate);
            Environment.SetEnvironmentVariable("HERONWIN_ENV_DIR", envDirectory);
            Environment.SetEnvironmentVariable("BRAIN_ENV_DIR", envDirectory);

            return;
        }
    }

    private static IEnumerable<string> EnumerateCandidatePaths(string assistantId)
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            yield return Path.Combine(current.FullName, ".env");
            yield return Path.Combine(current.FullName, "assistants", assistantId, ".env");
            yield return Path.Combine(current.FullName, "src", "assistants", assistantId, ".env");
            yield return Path.Combine(current.FullName, "assistants", ".env");
            yield return Path.Combine(current.FullName, "src", "assistants", ".env");
            yield return Path.Combine(current.FullName, "head", "brain", ".env");
            yield return Path.Combine(current.FullName, "src", "head", "brain", ".env");
            current = current.Parent;
        }
    }

}
