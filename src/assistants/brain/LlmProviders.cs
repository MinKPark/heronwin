namespace HeronWin.Brain;

internal enum BrainInteractiveMode
{
    Voice,
    Text
}

internal sealed record LlmProviderCapabilities(
    IReadOnlySet<BrainInteractiveMode> SupportedInteractiveModes,
    BrainInteractiveMode DefaultInteractiveMode,
    bool SupportsScriptedMode,
    bool SupportsRuntimeModeSwitch,
    bool SupportsVisionInputs,
    bool SupportsToolCalls);

internal interface ILlmProvider
{
    LlmProviderId Id { get; }
    string DisplayName { get; }
    LlmProviderCapabilities Capabilities { get; }
    void ValidateConfiguration(AppConfig config);
    ILlmClient CreateClient(AppConfig config, HttpClient httpClient);
    IAudioTranscriber? CreateAudioTranscriber(AppConfig config, HttpClient httpClient);
    ISpeechSynthesizer? CreateSpeechSynthesizer(AppConfig config, HttpClient httpClient);
}

internal static class LlmProviderCatalog
{
    private static readonly ILlmProvider[] Providers =
    [
        new OpenAiApiProvider(),
        new OpenAiCodexProvider(),
        new ClaudeApiProvider()
    ];

    public static ILlmProvider Resolve(LlmProviderId providerId)
        => Providers.FirstOrDefault(provider => provider.Id == providerId)
           ?? throw new InvalidOperationException($"Unsupported LLM provider: {providerId}.");

    public static ILlmProvider Resolve(AppConfig config) => Resolve(config.LlmProvider);
}

internal sealed class OpenAiApiProvider : ILlmProvider
{
    private static readonly HashSet<BrainInteractiveMode> SupportedModes =
    [
        BrainInteractiveMode.Voice,
        BrainInteractiveMode.Text
    ];

    public LlmProviderId Id => LlmProviderId.OpenAiApi;
    public string DisplayName => "OpenAI API";
    public LlmProviderCapabilities Capabilities { get; } = new(
        SupportedModes,
        BrainInteractiveMode.Voice,
        SupportsScriptedMode: true,
        SupportsRuntimeModeSwitch: true,
        SupportsVisionInputs: true,
        SupportsToolCalls: true);

    public void ValidateConfiguration(AppConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.OpenAiApiKey))
        {
            throw new InvalidOperationException(
                "OPENAI_API_KEY is not set. OpenAI API mode requires an API key.");
        }
    }

    public ILlmClient CreateClient(AppConfig config, HttpClient httpClient)
        => new OpenAiApiClient(
            httpClient,
            config.OpenAiApiKey,
            config.OpenAiModel,
            config.LlmTemperature);

    public IAudioTranscriber? CreateAudioTranscriber(AppConfig config, HttpClient httpClient)
        => string.IsNullOrWhiteSpace(config.OpenAiApiKey)
            ? null
            : new OpenAiWhisperTranscriber(
                httpClient,
                config.OpenAiApiKey,
                config.WhisperModel,
                config.VoiceLanguages);

    public ISpeechSynthesizer? CreateSpeechSynthesizer(AppConfig config, HttpClient httpClient)
        => string.IsNullOrWhiteSpace(config.OpenAiApiKey)
            ? null
            : new OpenAiSpeechSynthesizer(
                httpClient,
                config.OpenAiApiKey,
                config.TtsModel,
                config.TtsVoice,
                config.TtsInstructions);
}

internal sealed class OpenAiCodexProvider : ILlmProvider
{
    private static readonly HashSet<BrainInteractiveMode> SupportedModes =
    [
        BrainInteractiveMode.Text
    ];

    public LlmProviderId Id => LlmProviderId.OpenAiCodex;
    public string DisplayName => "ChatGPT / Codex sign-in";
    public LlmProviderCapabilities Capabilities { get; } = new(
        SupportedModes,
        BrainInteractiveMode.Text,
        SupportsScriptedMode: true,
        SupportsRuntimeModeSwitch: false,
        SupportsVisionInputs: true,
        SupportsToolCalls: true);

    public void ValidateConfiguration(AppConfig config)
    {
        var status = OpenAiCodexCliSupport.GetLoginStatus(config.OpenAiCodexCommand, Directory.GetCurrentDirectory());
        if (!status.IsAvailable)
        {
            throw new InvalidOperationException(status.Message);
        }

        if (!status.IsLoggedIn)
        {
            throw new InvalidOperationException(
                $"{status.Message} Run \"{config.OpenAiCodexCommand} login\" and sign in with ChatGPT / Codex before selecting openai-codex.");
        }
    }

    public ILlmClient CreateClient(AppConfig config, HttpClient httpClient)
        => new OpenAiCodexCliClient(
            config.OpenAiCodexCommand,
            config.OpenAiCodexModel);

    public IAudioTranscriber? CreateAudioTranscriber(AppConfig config, HttpClient httpClient) => null;

    public ISpeechSynthesizer? CreateSpeechSynthesizer(AppConfig config, HttpClient httpClient) => null;
}

internal sealed class ClaudeApiProvider : ILlmProvider
{
    private static readonly HashSet<BrainInteractiveMode> SupportedModes =
    [
        BrainInteractiveMode.Voice,
        BrainInteractiveMode.Text
    ];

    public LlmProviderId Id => LlmProviderId.ClaudeApi;
    public string DisplayName => "Claude API";
    public LlmProviderCapabilities Capabilities { get; } = new(
        SupportedModes,
        BrainInteractiveMode.Voice,
        SupportsScriptedMode: true,
        SupportsRuntimeModeSwitch: true,
        SupportsVisionInputs: true,
        SupportsToolCalls: true);

    public void ValidateConfiguration(AppConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.AnthropicApiKey))
        {
            throw new InvalidOperationException(
                "ANTHROPIC_API_KEY is not set. Claude API mode requires an API key.");
        }
    }

    public ILlmClient CreateClient(AppConfig config, HttpClient httpClient)
        => new ClaudeApiClient(
            httpClient,
            config.AnthropicApiKey,
            config.AnthropicModel,
            config.LlmTemperature);

    public IAudioTranscriber? CreateAudioTranscriber(AppConfig config, HttpClient httpClient)
        => string.IsNullOrWhiteSpace(config.OpenAiApiKey)
            ? null
            : new OpenAiWhisperTranscriber(
                httpClient,
                config.OpenAiApiKey,
                config.WhisperModel,
                config.VoiceLanguages);

    public ISpeechSynthesizer? CreateSpeechSynthesizer(AppConfig config, HttpClient httpClient)
        => string.IsNullOrWhiteSpace(config.OpenAiApiKey)
            ? null
            : new OpenAiSpeechSynthesizer(
                httpClient,
                config.OpenAiApiKey,
                config.TtsModel,
                config.TtsVoice,
                config.TtsInstructions);
}
