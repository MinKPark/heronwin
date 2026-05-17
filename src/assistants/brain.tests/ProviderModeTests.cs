using Xunit;

namespace HeronWin.Brain.Tests;

public sealed class ProviderModeTests
{
    [Theory]
    [InlineData("openai-codex")]
    [InlineData("chatgpt-subscription")]
    [InlineData("chatgpt")]
    [InlineData("chatgpt-web")]
    public void NormalizeProvider_MapsCodexAliases(string rawProvider)
    {
        var actual = AppConfig.NormalizeProvider(rawProvider);

        Assert.Equal(LlmProviderId.OpenAiCodex, actual);
    }

    [Fact]
    public void Resolve_OpenAiCodexProvider_IsTextOnlyAndScriptable()
    {
        var provider = LlmProviderCatalog.Resolve(LlmProviderId.OpenAiCodex);

        Assert.Equal(LlmProviderId.OpenAiCodex, provider.Id);
        Assert.Equal(BrainInteractiveMode.Text, provider.Capabilities.DefaultInteractiveMode);
        Assert.Contains(BrainInteractiveMode.Text, provider.Capabilities.SupportedInteractiveModes);
        Assert.DoesNotContain(BrainInteractiveMode.Voice, provider.Capabilities.SupportedInteractiveModes);
        Assert.True(provider.Capabilities.SupportsScriptedMode);
        Assert.False(provider.Capabilities.SupportsRuntimeModeSwitch);
    }

    [Fact]
    public void LlmReasoningEfforts_NormalizesSupportedValues()
    {
        Assert.Equal("low", LlmReasoningEfforts.Normalize(" LOW "));
        Assert.Equal("medium", LlmReasoningEfforts.Normalize("medium"));
        Assert.Equal("high", LlmReasoningEfforts.Normalize("high"));
        Assert.Equal("xhigh", LlmReasoningEfforts.Normalize("extra-high"));
        Assert.Null(LlmReasoningEfforts.Normalize(""));
    }

    [Fact]
    public void LlmReasoningEfforts_RejectsUnsupportedValues()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => LlmReasoningEfforts.Normalize("maximum"));

        Assert.Contains("Invalid reasoning effort", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadLlmRoleConfigs_ParsesPrefixlessAssistantLocalRoleVariables()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["DRIVER_MODEL"] = "gpt-5.4",
            ["DRIVER_REASONING_EFFORT"] = "medium",
            ["EVALUATOR_MODEL"] = "gpt-5.5",
            ["EVALUATOR_REASONING_EFFORT"] = "high",
            ["REPORTER_REASONING_EFFORT"] = "x-high",
        };

        var configs = AppConfig.LoadLlmRoleConfigs(key => values.GetValueOrDefault(key));

        var driver = Assert.Single(configs, config => config.Role == LlmRole.AvaDriver);
        Assert.Equal("gpt-5.4", driver.ModelOverride);
        Assert.Equal("medium", driver.ReasoningEffort);

        var evaluator = Assert.Single(configs, config => config.Role == LlmRole.AvaEvaluator);
        Assert.Equal("gpt-5.5", evaluator.ModelOverride);
        Assert.Equal("high", evaluator.ReasoningEffort);

        var reporter = Assert.Single(configs, config => config.Role == LlmRole.AvaReporter);
        Assert.Null(reporter.ModelOverride);
        Assert.Equal("xhigh", reporter.ReasoningEffort);
    }

    [Fact]
    public void GetLlmRoleConfig_FallsBackToSharedReasoningEffort()
    {
        var config = CreateConfig(
            LlmProviderId.OpenAiApi,
            llmReasoningEffort: "medium",
            roleConfigs: [new LlmRoleConfig(LlmRole.AvaDriver, "gpt-5.4", null)]);

        var roleConfig = config.GetLlmRoleConfig(LlmRole.AvaDriver);

        Assert.Equal("gpt-5.4", roleConfig.ModelOverride);
        Assert.Equal("medium", roleConfig.ReasoningEffort);
    }

    [Fact]
    public void ProviderCreateClient_UsesAvaDriverRoleModelAndReasoning()
    {
        var config = CreateConfig(
            LlmProviderId.OpenAiCodex,
            openAiCodexModel: "gpt-5.5",
            roleConfigs: [new LlmRoleConfig(LlmRole.AvaDriver, "spark", "high")]);
        var provider = LlmProviderCatalog.Resolve(LlmProviderId.OpenAiCodex);

        using var httpClient = new HttpClient();
        var client = provider.CreateClient(config, httpClient, LlmRole.AvaDriver);

        Assert.Contains(OpenAiCodexModels.SparkModelName, client.DisplayName, StringComparison.Ordinal);
        Assert.Contains("reasoning=high", client.DisplayName, StringComparison.Ordinal);
        Assert.Equal(OpenAiCodexModels.SparkModelName, client.ModelProfile.ModelName);
    }

    [Theory]
    [InlineData("/mode:text", 1)]
    [InlineData("switch to text mode", 1)]
    [InlineData("/mode:voice", 0)]
    [InlineData("to voice mode", 0)]
    public void InteractiveCommands_ParsesModeSwitches(string input, int expectedMode)
    {
        var actual = BrainInteractiveCommands.TryParseModeSwitch(input, out var parsedMode);

        Assert.True(actual);
        Assert.Equal((BrainInteractiveMode)expectedMode, parsedMode);
    }

    [Theory]
    [InlineData("/exit")]
    [InlineData("quit")]
    public void InteractiveCommands_RecognizesExitCommands(string input)
    {
        Assert.True(BrainInteractiveCommands.IsExitCommand(input));
    }

    [Theory]
    [InlineData("/reset")]
    [InlineData("reset")]
    public void InteractiveCommands_RecognizesResetCommands(string input)
    {
        Assert.True(BrainInteractiveCommands.IsResetCommand(input));
    }

    private static AppConfig CreateConfig(
        LlmProviderId providerId,
        string openAiModel = "gpt-5.4-mini",
        string openAiCodexModel = "",
        string anthropicModel = "claude-3-5-sonnet-20241022",
        string? llmReasoningEffort = null,
        IReadOnlyList<LlmRoleConfig>? roleConfigs = null)
        => new(
            AssistantId: "ava",
            LlmProvider: providerId,
            AgentDefinitionPath: "agent.md",
            AgentDefinition: "agent",
            AgentPrompts: new AgentPromptCatalog("agent.md", "agent", null, string.Empty, []),
            DebugAudioPlayback: false,
            EnableDebugTrace: false,
            OpenAiApiKey: "test-key",
            OpenAiModel: openAiModel,
            OpenAiCodexCommand: "codex",
            OpenAiCodexModel: openAiCodexModel,
            LlmTemperature: 0,
            LlmReasoningEffort: llmReasoningEffort,
            LlmRoleConfigs: roleConfigs ?? [],
            TtsModel: "tts",
            TtsVoice: "voice",
            TtsInstructions: string.Empty,
            AnthropicApiKey: "test-key",
            AnthropicModel: anthropicModel,
            WhisperModel: "whisper",
            VoiceLanguages: [],
            MaxRecordMs: 10_000,
            ActiveIdleTimeoutMs: 10_000,
            PostActionUiSettleDelayMs: 0,
            MaxContextTokens: 128_000,
            WakeWord: "hello",
            McpServers: []);
}
