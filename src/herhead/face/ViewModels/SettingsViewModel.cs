using HeronWin.Face.Models;
using HeronWin.Face.Services;
using System.IO;
using System.Windows.Input;

namespace HeronWin.Face.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly FaceAppSettings _faceSettings;
    private readonly FaceSettingsService _faceSettingsService;
    private string _envFilePath;
    private string _pipeName;
    private string _llmProvider = "openai-api";
    private string _openAiApiKey = string.Empty;
    private string _openAiModel = "gpt-5.2-chat-latest";
    private string _anthropicApiKey = string.Empty;
    private string _anthropicModel = "claude-3-5-sonnet-20241022";
    private string _whisperModel = "whisper-1";
    private string _ttsVoice = "marin";
    private string _wakeWord = "Hello there";
    private string _voiceLanguages = "American English, Korean";
    private string _maxRecordMs = "30000";
    private bool _debugTrace;
    private bool _debugAudioPlayback;
    private string _mcpServersJson = "[]";
    private string _rawEnvPreview = string.Empty;
    private string _saveMessage = "Save writes both the local face settings file and the selected brain .env file.";

    public SettingsViewModel(FaceAppSettings faceSettings, FaceSettingsService faceSettingsService)
    {
        _faceSettings = faceSettings;
        _faceSettingsService = faceSettingsService;
        _envFilePath = faceSettings.EnvFilePath;
        _pipeName = faceSettings.PipeName;
        SaveCommand = new RelayCommand(async () => await SaveAsync());
        ReloadEnvCommand = new RelayCommand(LoadEnv);
        LoadEnv();
    }

    public ICommand SaveCommand { get; }

    public ICommand ReloadEnvCommand { get; }

    public string EnvFilePath
    {
        get => _envFilePath;
        set => SetProperty(ref _envFilePath, value);
    }

    public string PipeName
    {
        get => _pipeName;
        set => SetProperty(ref _pipeName, value);
    }

    public string LlmProvider
    {
        get => _llmProvider;
        set => SetProperty(ref _llmProvider, value);
    }

    public string OpenAiApiKey
    {
        get => _openAiApiKey;
        set => SetProperty(ref _openAiApiKey, value);
    }

    public string OpenAiModel
    {
        get => _openAiModel;
        set => SetProperty(ref _openAiModel, value);
    }

    public string AnthropicApiKey
    {
        get => _anthropicApiKey;
        set => SetProperty(ref _anthropicApiKey, value);
    }

    public string AnthropicModel
    {
        get => _anthropicModel;
        set => SetProperty(ref _anthropicModel, value);
    }

    public string WhisperModel
    {
        get => _whisperModel;
        set => SetProperty(ref _whisperModel, value);
    }

    public string TtsVoice
    {
        get => _ttsVoice;
        set => SetProperty(ref _ttsVoice, value);
    }

    public string WakeWord
    {
        get => _wakeWord;
        set => SetProperty(ref _wakeWord, value);
    }

    public string VoiceLanguages
    {
        get => _voiceLanguages;
        set => SetProperty(ref _voiceLanguages, value);
    }

    public string MaxRecordMs
    {
        get => _maxRecordMs;
        set => SetProperty(ref _maxRecordMs, value);
    }

    public bool DebugTrace
    {
        get => _debugTrace;
        set => SetProperty(ref _debugTrace, value);
    }

    public bool DebugAudioPlayback
    {
        get => _debugAudioPlayback;
        set => SetProperty(ref _debugAudioPlayback, value);
    }

    public string McpServersJson
    {
        get => _mcpServersJson;
        set => SetProperty(ref _mcpServersJson, value);
    }

    public string RawEnvPreview
    {
        get => _rawEnvPreview;
        private set => SetProperty(ref _rawEnvPreview, value);
    }

    public string SaveMessage
    {
        get => _saveMessage;
        private set => SetProperty(ref _saveMessage, value);
    }

    public void LoadEnv()
    {
        var document = EnvFileDocument.Load(EnvFilePath);
        LlmProvider = document.GetValue("LLM_PROVIDER", "openai-api");
        OpenAiApiKey = document.GetValue("OPENAI_API_KEY");
        OpenAiModel = document.GetValue("OPENAI_MODEL", "gpt-5.2-chat-latest");
        AnthropicApiKey = document.GetValue("ANTHROPIC_API_KEY");
        AnthropicModel = document.GetValue("ANTHROPIC_MODEL", "claude-3-5-sonnet-20241022");
        WhisperModel = document.GetValue("WHISPER_MODEL", "whisper-1");
        TtsVoice = document.GetValue("TTS_VOICE", "marin");
        WakeWord = document.GetValue("WAKE_WORD", "Hello there");
        VoiceLanguages = document.GetValue("VOICE_LANGUAGES", "American English, Korean");
        MaxRecordMs = document.GetValue("MAX_RECORD_MS", "30000");
        DebugTrace = ParseBool(document.GetValue("DEBUG_TRACE"));
        DebugAudioPlayback = ParseBool(document.GetValue("DEBUG_AUDIO_PLAYBACK"));
        McpServersJson = document.GetValue("MCP_SERVERS", "[]");
        RawEnvPreview = document.Render();
        SaveMessage = File.Exists(EnvFilePath)
            ? "Loaded the selected .env file."
            : "No .env file exists yet. Save will create it.";
    }

    public async Task SaveAsync()
    {
        var document = EnvFileDocument.Load(EnvFilePath);
        document.SetValue("LLM_PROVIDER", LlmProvider);
        document.SetValue("OPENAI_API_KEY", OpenAiApiKey);
        document.SetValue("OPENAI_MODEL", OpenAiModel);
        document.SetValue("ANTHROPIC_API_KEY", AnthropicApiKey);
        document.SetValue("ANTHROPIC_MODEL", AnthropicModel);
        document.SetValue("WHISPER_MODEL", WhisperModel);
        document.SetValue("TTS_VOICE", TtsVoice);
        document.SetValue("WAKE_WORD", WakeWord);
        document.SetValue("VOICE_LANGUAGES", VoiceLanguages);
        document.SetValue("MAX_RECORD_MS", MaxRecordMs);
        document.SetValue("DEBUG_TRACE", DebugTrace ? "true" : "false");
        document.SetValue("DEBUG_AUDIO_PLAYBACK", DebugAudioPlayback ? "true" : "false");
        document.SetValue("MCP_SERVERS", McpServersJson);
        document.SetValue("FACE_PIPE_ENABLED", "true");
        document.SetValue("FACE_PIPE_NAME", PipeName);

        await document.SaveAsync(EnvFilePath);

        _faceSettings.EnvFilePath = EnvFilePath;
        _faceSettings.PipeName = PipeName;
        await _faceSettingsService.SaveAsync(_faceSettings);

        RawEnvPreview = document.Render();
        SaveMessage = $"Saved face settings and {EnvFilePath}. Restart brain if it is already running.";
    }

    private static bool ParseBool(string value)
    {
        return value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";
    }
}