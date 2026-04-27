using HeronWin.Brain;

Console.OutputEncoding = System.Text.Encoding.UTF8;

BrainConsoleOptions consoleOptions;
try
{
    consoleOptions = BrainConsoleMode.Parse(args);
}
catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException)
{
    Console.Error.WriteLine($"x  {ex.Message}");
    Environment.ExitCode = 1;
    return;
}

if (consoleOptions.ShowHelp)
{
    BrainConsoleMode.PrintHelp();
    return;
}

if (consoleOptions.IsTraceReport)
{
    try
    {
        Console.WriteLine(BrainTraceReporter.GenerateMarkdown(consoleOptions.TraceReportPath!));
    }
    catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException)
    {
        Console.Error.WriteLine($"x  {ex.Message}");
        Environment.ExitCode = 1;
    }

    return;
}

var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};

var config = AppConfig.Load();
var provider = LlmProviderCatalog.Resolve(config);
var initialInteractiveMode = provider.Capabilities.DefaultInteractiveMode;

FaceBridge.Initialize(config);
ArtifactCleanup.CleanupPreviousRunArtifacts(AppContext.BaseDirectory, Environment.ProcessPath);
DebugTrace.Configure(config.EnableDebugTrace || config.DebugAudioPlayback || consoleOptions.RequiresDebugTrace);
Display.Banner();
var httpClientSetup = BrainHttpClientFactory.Create();
using var httpClient = httpClientSetup.Client;
await using var mcpManager = new McpClientManager();

DebugTrace.WriteStructuredEvent(
    "session.start",
    new Dictionary<string, object?>
    {
        ["pid"] = Environment.ProcessId,
        ["process"] = Environment.ProcessPath ?? "(unknown)",
        ["cwd"] = Directory.GetCurrentDirectory(),
        ["baseDir"] = AppContext.BaseDirectory,
        ["sessionId"] = DebugTrace.SessionId,
        ["launchMode"] = consoleOptions.IsScripted ? "scripted" : initialInteractiveMode.ToString().ToLowerInvariant(),
        ["scriptedScenarioPath"] = consoleOptions.ScenarioFilePath,
        ["scriptedCommands"] = consoleOptions.Commands.ToArray(),
        ["debugTraceEnabled"] = DebugTrace.IsEnabled,
        ["llmProvider"] = config.LlmProvider.ToString(),
        ["openAiModel"] = config.OpenAiModel,
        ["openAiCodexModel"] = config.OpenAiCodexModel,
        ["anthropicModel"] = config.AnthropicModel,
        ["whisperModel"] = config.WhisperModel,
        ["voiceLanguages"] = config.VoiceLanguages.Select(static language => new Dictionary<string, object?>
        {
            ["displayName"] = language.DisplayName,
            ["openAiLanguageCode"] = language.OpenAiLanguageCode,
        }).ToArray(),
        ["ttsModel"] = config.TtsModel,
        ["ttsVoice"] = config.TtsVoice,
        ["wakeWord"] = config.WakeWord,
        ["maxContextTokens"] = config.MaxContextTokens,
        ["postActionUiSettleDelayMs"] = config.PostActionUiSettleDelayMs,
        ["debugAudioPlayback"] = config.DebugAudioPlayback,
        ["logsDirectory"] = DebugTrace.BuildLogsDirectory(AppContext.BaseDirectory),
        ["providerCapabilities"] = new Dictionary<string, object?>
        {
            ["supportedInteractiveModes"] = provider.Capabilities.SupportedInteractiveModes
                .Select(static mode => mode.ToString())
                .ToArray(),
            ["defaultInteractiveMode"] = provider.Capabilities.DefaultInteractiveMode.ToString(),
            ["supportsScriptedMode"] = provider.Capabilities.SupportsScriptedMode,
            ["supportsRuntimeModeSwitch"] = provider.Capabilities.SupportsRuntimeModeSwitch,
            ["supportsVisionInputs"] = provider.Capabilities.SupportsVisionInputs,
            ["supportsToolCalls"] = provider.Capabilities.SupportsToolCalls,
        },
        ["mcpServers"] = config.McpServers.Select(server => new Dictionary<string, object?>
        {
            ["name"] = server.Name,
            ["command"] = server.Command,
            ["args"] = server.Args ?? [],
            ["envKeys"] = server.Env?.Keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase).ToArray() ?? [],
        }).ToArray(),
        ["displayTopology"] = DisplayTopology.Capture(),
        ["textLogPath"] = DebugTrace.LogFilePath,
        ["jsonLogPath"] = DebugTrace.JsonLogFilePath,
    });

DebugTrace.WriteEvent(
    "config.loaded",
    $"mode={(consoleOptions.IsScripted ? "scripted" : initialInteractiveMode.ToString().ToLowerInvariant())}, llmProvider={config.LlmProvider}, openAiModel={config.OpenAiModel}, openAiCodexModel={config.OpenAiCodexModel}, anthropicModel={config.AnthropicModel}, whisperModel={config.WhisperModel}, voiceLanguages={DebugTrace.SerializeObject(config.VoiceLanguages.Select(static language => language.DisplayName).ToArray())}, wakeWord={DebugTrace.SerializeObject(config.WakeWord)}, postActionUiSettleDelayMs={config.PostActionUiSettleDelayMs}, agentDefinitionPath={config.AgentDefinitionPath}, agentCoreDefinitionPath={config.AgentPrompts.CoreDefinitionPath ?? "(none)"}, agentSkills={config.AgentPrompts.Skills.Count}, mcpServers={config.McpServers.Count}, debugTrace={DebugTrace.IsEnabled}, debugAudioPlayback={config.DebugAudioPlayback}");

if (consoleOptions.IsScripted)
{
    var scriptedExitCode = await RunScriptedModeAsync(cancellationSource.Token);
    await ShutdownAsync();
    Environment.ExitCode = scriptedExitCode;
    return;
}

ILlmClient llmClient;
try
{
    provider.ValidateConfiguration(config);
    llmClient = provider.CreateClient(config, httpClient);
}
catch (Exception ex)
{
    Display.Error(ex.Message);
    await ShutdownAsync();
    Environment.ExitCode = 1;
    return;
}

Display.Info($"LLM: {llmClient.DisplayName}");
Display.Info($"Interactive mode: {initialInteractiveMode}");
if (httpClientSetup.BypassedBrokenLoopbackProxy)
{
    Display.Warn(
        $"Ignoring broken loopback proxy setting for outbound API calls: {httpClientSetup.BypassedProxyValue}");
}
if (DebugTrace.IsEnabled && !string.IsNullOrWhiteSpace(DebugTrace.LogFilePath))
{
    Display.Info($"Debug trace: {DebugTrace.LogFilePath}");
    if (!string.IsNullOrWhiteSpace(DebugTrace.JsonLogFilePath))
    {
        Display.Info($"Debug trace JSONL: {DebugTrace.JsonLogFilePath}");
    }

    Display.Info($"Debug artifacts: {DebugTrace.BuildLogsDirectory(AppContext.BaseDirectory)}");
}

await ConnectMcpServersAsync(cancellationSource.Token);

var history = new List<AgentMessage>();
var desktopSession = new DesktopSessionContext();
var currentConfig = config;
var currentProvider = provider;
var activeMode = initialInteractiveMode;
long nextTurnId = 0;

if (activeMode == BrainInteractiveMode.Voice &&
    !TryCreateVoiceServices(currentProvider, currentConfig, out var startupAudioTranscriber, out var startupSpeechSynthesizer, out var startupVoiceError))
{
    Display.Error(startupVoiceError);
    await ShutdownAsync();
    Environment.ExitCode = 1;
    return;
}

while (!cancellationSource.IsCancellationRequested)
{
    switch (activeMode)
    {
        case BrainInteractiveMode.Text:
        {
            var loopResult = await RunTextModeAsync(cancellationSource.Token);
            if (loopResult.ExitRequested)
            {
                cancellationSource.Cancel();
            }
            else if (loopResult.NextMode is { } nextMode)
            {
                activeMode = nextMode;
            }

            break;
        }

        case BrainInteractiveMode.Voice:
        {
            if (!TryCreateVoiceServices(currentProvider, currentConfig, out var audioTranscriber, out var speechSynthesizer, out var voiceError))
            {
                Display.Error(voiceError);
                cancellationSource.Cancel();
                break;
            }

            var loopResult = await RunVoiceModeAsync(audioTranscriber, speechSynthesizer, cancellationSource.Token);
            if (loopResult.ExitRequested)
            {
                cancellationSource.Cancel();
            }
            else if (loopResult.NextMode is { } nextMode)
            {
                activeMode = nextMode;
            }

            break;
        }
    }
}

await ShutdownAsync();
return;

async Task<int> RunScriptedModeAsync(CancellationToken cancellationToken)
{
    if (!provider.Capabilities.SupportsScriptedMode)
    {
        Display.Error($"{provider.DisplayName} does not support scripted mode.");
        return 1;
    }

    ILlmClient scriptedLlmClient;
    try
    {
        provider.ValidateConfiguration(config);
        scriptedLlmClient = provider.CreateClient(config, httpClient);
    }
    catch (Exception ex)
    {
        Display.Error(ex.Message);
        return 1;
    }

    Display.Info($"LLM: {scriptedLlmClient.DisplayName}");
    if (httpClientSetup.BypassedBrokenLoopbackProxy)
    {
        Display.Warn(
            $"Ignoring broken loopback proxy setting for outbound API calls: {httpClientSetup.BypassedProxyValue}");
    }
    if (DebugTrace.IsEnabled && !string.IsNullOrWhiteSpace(DebugTrace.LogFilePath))
    {
        Display.Info($"Debug trace: {DebugTrace.LogFilePath}");
        if (!string.IsNullOrWhiteSpace(DebugTrace.JsonLogFilePath))
        {
            Display.Info($"Debug trace JSONL: {DebugTrace.JsonLogFilePath}");
        }
    }

    await ConnectMcpServersAsync(cancellationToken);
    return await ScriptedConversationRunner.RunAsync(
        consoleOptions,
        config,
        scriptedLlmClient,
        mcpManager,
        cancellationToken);
}

async Task ConnectMcpServersAsync(CancellationToken cancellationToken)
{
    if (config.McpServers.Count > 0)
    {
        Display.Info($"Connecting to {config.McpServers.Count} MCP server(s)...");
        try
        {
            await mcpManager.ConnectAsync(config.McpServers, cancellationToken);
            var tools = await mcpManager.ListAllToolsAsync(cancellationToken);
            Display.Info($"MCP tools available: {string.Join(", ", tools.Select(tool => tool.Name).DefaultIfEmpty("(none)"))}");
        }
        catch (Exception ex)
        {
            Display.Warn($"MCP connection failed: {ex.Message}");
        }
    }
    else
    {
        Display.Info("No MCP servers configured. Running without tool support.");
    }
}

bool TryCreateVoiceServices(
    ILlmProvider activeProvider,
    AppConfig activeConfig,
    out IAudioTranscriber audioTranscriber,
    out ISpeechSynthesizer? speechSynthesizer,
    out string errorText)
{
    audioTranscriber = null!;
    speechSynthesizer = null;
    errorText = string.Empty;

    if (!activeProvider.Capabilities.SupportedInteractiveModes.Contains(BrainInteractiveMode.Voice))
    {
        errorText = $"{activeProvider.DisplayName} does not support voice mode.";
        return false;
    }

    audioTranscriber = activeProvider.CreateAudioTranscriber(activeConfig, httpClient)!;
    speechSynthesizer = activeProvider.CreateSpeechSynthesizer(activeConfig, httpClient);
    if (audioTranscriber is null)
    {
        errorText =
            $"Voice mode requires speech credentials. Set OPENAI_API_KEY so Whisper transcription can run for {activeProvider.DisplayName}.";
        return false;
    }

    return true;
}

bool TryHandleModeSwitch(
    BrainInteractiveMode currentMode,
    BrainInteractiveMode requestedMode,
    out InteractiveLoopResult loopResult)
{
    loopResult = new InteractiveLoopResult(false, null);

    if (requestedMode == currentMode)
    {
        Display.Info($"Already in {currentMode.ToString().ToLowerInvariant()} mode.");
        return true;
    }

    if (!currentProvider.Capabilities.SupportedInteractiveModes.Contains(requestedMode))
    {
        Display.Error($"{currentProvider.DisplayName} does not support {requestedMode.ToString().ToLowerInvariant()} mode.");
        return true;
    }

    if (!currentProvider.Capabilities.SupportsRuntimeModeSwitch)
    {
        Display.Error($"{currentProvider.DisplayName} does not support interactive mode switching at runtime.");
        return true;
    }

    if (requestedMode == BrainInteractiveMode.Voice &&
        !TryCreateVoiceServices(currentProvider, currentConfig, out _, out _, out var voiceError))
    {
        Display.Error(voiceError);
        return true;
    }

    Display.Info($"Switching to {requestedMode.ToString().ToLowerInvariant()} mode.");
    loopResult = new InteractiveLoopResult(false, requestedMode);
    return true;
}

async Task<InteractiveLoopResult> RunTextModeAsync(CancellationToken cancellationToken)
{
    Display.Separator();
    Display.Info("Text mode is ready. Type /exit to quit, /reset to clear history, or /mode:voice if your provider supports voice.");
    FaceBridge.PublishStatus("listening", "Text mode", "Waiting for a typed request.");

    while (!cancellationToken.IsCancellationRequested)
    {
        Display.Prompt("\n[text] ");
        var userText = Console.ReadLine();
        if (userText is null)
        {
            return new InteractiveLoopResult(true, null);
        }

        if (string.IsNullOrWhiteSpace(userText))
        {
            continue;
        }

        if (BrainInteractiveCommands.IsExitCommand(userText))
        {
            return new InteractiveLoopResult(true, null);
        }

        if (BrainInteractiveCommands.IsResetCommand(userText))
        {
            history.Clear();
            desktopSession = new DesktopSessionContext();
            Display.Info("Conversation history cleared.");
            FaceBridge.PublishStatus("listening", "Text mode", "Waiting for a typed request.");
            continue;
        }

        if (BrainInteractiveCommands.TryParseModeSwitch(userText, out var requestedMode) &&
            TryHandleModeSwitch(BrainInteractiveMode.Text, requestedMode, out var loopResult))
        {
            if (loopResult.NextMode is not null)
            {
                return loopResult;
            }

            continue;
        }

        var turnResult = await ProcessInteractiveTurnAsync(userText, "text", speakReplies: false, null, cancellationToken);
        if (turnResult.ExitRequested)
        {
            return turnResult;
        }
    }

    return new InteractiveLoopResult(true, null);
}

async Task<InteractiveLoopResult> RunVoiceModeAsync(
    IAudioTranscriber audioTranscriber,
    ISpeechSynthesizer? speechSynthesizer,
    CancellationToken cancellationToken)
{
    Display.Info($"Microphone: {AudioDevices.DescribeDefaultMicrophone()}");
    Display.Info($"Speaker: {AudioDevices.DescribeDefaultSpeaker()}");
    Display.Info($"Mic capture: {AudioRecorder.DescribeRecordingFormat()}");
    Display.Info($"Voice languages: {string.Join(", ", currentConfig.VoiceLanguages.Select(static language => language.DisplayName))}");
    if (speechSynthesizer is not null)
    {
        Display.Info($"Voice output: {speechSynthesizer.DisplayName}");
    }

    Display.Info($"Voice input: {audioTranscriber.DisplayName}");
    if (currentConfig.DebugAudioPlayback)
    {
        Display.Info("Debug audio playback is enabled; each captured recording will replay during transcription.");
    }

    Display.Separator();
    Display.Info($"Standby mode is listening for \"{currentConfig.WakeWord}\".");
    Display.Info("After the wake phrase is heard, just speak naturally. Say \"bye\" or \"bye-bye\" to exit.");
    Display.Info("If you go quiet for a minute, I will drift back to standby.");
    Display.Separator();
    FaceBridge.PublishStatus("standby", "Standby", $"Listening for \"{currentConfig.WakeWord}\".");

    var isActive = false;

    while (!cancellationToken.IsCancellationRequested)
    {
        var userText = string.Empty;
        RecordingResult? recording = null;
        int? maxWaitForSpeechMs = isActive ? currentConfig.ActiveIdleTimeoutMs : null;

        try
        {
            Display.Info(isActive ? "Listening..." : $"Waiting for {currentConfig.WakeWord}...");
            if (isActive)
            {
                await PlayAudioOutputAsync(AudioPlayback.PlayRecordingStartCueAsync);
            }

            recording = await AudioRecorder.RecordAsync(currentConfig.MaxRecordMs, maxWaitForSpeechMs, cancellationToken);

            if (recording is null)
            {
                if (isActive)
                {
                    await PlayAudioOutputAsync(AudioPlayback.PlayWindDownCueAsync);
                    isActive = false;
                    Display.Info($"Back to standby. Say \"{currentConfig.WakeWord}\" when you want me again.");
                }

                continue;
            }

            if (isActive)
            {
                await PlayAudioOutputAsync(AudioPlayback.PlayRecordingStopCueAsync);
            }

            if (currentConfig.DebugAudioPlayback)
            {
                Display.Info(
                    $"Debug recording window: {recording.StartedAt.LocalDateTime:HH:mm:ss.fff} -> {recording.EndedAt.LocalDateTime:HH:mm:ss.fff} ({recording.WallClockDurationMs:F0} ms wall-clock)");
                var deltaLabel = $"{(recording.DurationDeltaMs >= 0 ? "+" : string.Empty)}{recording.DurationDeltaMs:F0} ms";
                var comparison = Math.Abs(recording.DurationDeltaMs) <= 150 ? "matches closely" : "does not match closely";
                Display.Info(
                    $"Debug WAV span: {recording.WaveDurationMs:F0} ms from {recording.PcmDataBytes} PCM bytes; delta vs wall-clock: {deltaLabel} ({comparison})");
            }

            PersistDebugRecordingIfEnabled(recording);
            userText = await TranscribeRecordingAsync(
                audioTranscriber,
                recording,
                currentConfig,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            CleanupRecording(recording);
            return new InteractiveLoopResult(true, null);
        }
        catch (Exception ex)
        {
            Display.Error($"Recording/transcription failed: {ex.Message}");
            DebugTrace.WriteEvent("audio.error", $"stage=record-or-transcribe, error={DebugTrace.Preview(ex.ToString(), 800)}");
            CleanupRecording(recording);
            continue;
        }

        CleanupRecording(recording);

        if (string.IsNullOrWhiteSpace(userText))
        {
            continue;
        }

        Display.Transcript(userText);
        DebugTrace.WriteEvent("user.transcript", $"text={DebugTrace.Preview(userText, 500)}");

        if (SpeechGate.ShouldExitApp(userText))
        {
            DebugTrace.WriteEvent("session.exit_phrase_detected", $"text={DebugTrace.Preview(userText, 300)}");
            return new InteractiveLoopResult(true, null);
        }

        if (BrainInteractiveCommands.TryParseModeSwitch(userText, out var requestedMode) &&
            TryHandleModeSwitch(BrainInteractiveMode.Voice, requestedMode, out var switchResult))
        {
            if (switchResult.NextMode is not null)
            {
                return switchResult;
            }

            continue;
        }

        if (!isActive)
        {
            if (!SpeechGate.ContainsWakeWord(userText, currentConfig.WakeWord))
            {
                DebugTrace.WriteEvent(
                    "session.standby_ignored",
                    $"reason=no-wake-word, text={DebugTrace.Preview(userText, 300)}");
                continue;
            }

            isActive = true;
            DebugTrace.WriteEvent("session.wake_activated", $"text={DebugTrace.Preview(userText, 300)}");
            const string wakeResponse = "what's up?";
            Display.AssistantReply(wakeResponse, string.Empty);
            try
            {
                await PlayAudioOutputAsync(() => SpeakAsync(speechSynthesizer, wakeResponse, cancellationToken));
            }
            catch (Exception ex)
            {
                Display.Warn($"Wake response speech failed: {ex.Message}");
            }
            continue;
        }

        var turnResult = await ProcessInteractiveTurnAsync(userText, "voice", speakReplies: true, speechSynthesizer, cancellationToken);
        if (turnResult.ExitRequested || turnResult.NextMode is not null)
        {
            return turnResult;
        }
    }

    return new InteractiveLoopResult(true, null);
}

async Task<InteractiveLoopResult> ProcessInteractiveTurnAsync(
    string userText,
    string turnSource,
    bool speakReplies,
    ISpeechSynthesizer? speechSynthesizer,
    CancellationToken cancellationToken)
{
    try
    {
        var turnId = Interlocked.Increment(ref nextTurnId);
        var processedTurn = await BrainTurnProcessor.ProcessAsync(
            turnId,
            userText,
            history,
            desktopSession,
            currentConfig,
            llmClient,
            mcpManager,
            cancellationToken,
            turnSource,
            intermediateStepNarrator: speakReplies && speechSynthesizer is not null
                ? async (stepText, innerCancellationToken) =>
                {
                    await PlayAudioOutputAsync(() => SpeakAsync(speechSynthesizer, stepText, innerCancellationToken));
                }
                : null);

        if (processedTurn.UpdatedConfig is not null)
        {
            currentConfig = processedTurn.UpdatedConfig;
            currentProvider = LlmProviderCatalog.Resolve(currentConfig);
        }

        if (speakReplies && speechSynthesizer is not null && !string.IsNullOrWhiteSpace(processedTurn.Reply.SpokenText))
        {
            try
            {
                await PlayAudioOutputAsync(
                    () => SpeakAsync(speechSynthesizer, processedTurn.Reply.SpokenText, cancellationToken));
            }
            catch (Exception ex)
            {
                Display.Warn($"Reply speech failed: {ex.Message}");
            }
        }
    }
    catch (OperationCanceledException)
    {
        return new InteractiveLoopResult(true, null);
    }
    catch (Exception ex)
    {
        Display.Error($"Agent error: {ex.Message}");
    }

    return new InteractiveLoopResult(false, null);
}

static void CleanupRecording(RecordingResult? recording)
{
    if (recording is null)
    {
        return;
    }

    try
    {
        File.Delete(recording.FilePath);
    }
    catch
    {
        // Ignore cleanup failures.
    }
}

static async Task SpeakAsync(ISpeechSynthesizer? speechSynthesizer, string text, CancellationToken cancellationToken)
{
    if (speechSynthesizer is null || string.IsNullOrWhiteSpace(text))
    {
        return;
    }

    string? audioPath = null;
    try
    {
        audioPath = await speechSynthesizer.SynthesizeSpeechAsync(text, cancellationToken);
        await AudioPlayback.PlayWavFileAsync(audioPath);
    }
    finally
    {
        if (!string.IsNullOrWhiteSpace(audioPath))
        {
            try
            {
                File.Delete(audioPath);
            }
            catch
            {
                // Ignore cleanup failures.
            }
        }
    }
}

static async Task<string> TranscribeRecordingAsync(
    IAudioTranscriber audioTranscriber,
    RecordingResult recording,
    AppConfig config,
    CancellationToken cancellationToken)
{
    Display.Transcribing();
    if (config.DebugAudioPlayback)
    {
        Display.Info("Debug: replaying the captured WAV while it is being sent for transcription.");
    }

    var transcriptionTask = audioTranscriber.TranscribeAudioAsync(recording.FilePath, cancellationToken);
    Task playbackTask = config.DebugAudioPlayback
        ? AudioPlayback.PlayWavFileAsync(recording.FilePath)
        : Task.CompletedTask;
    await Task.WhenAll(transcriptionTask, playbackTask.ContinueWith(_ => { }, TaskScheduler.Default));
    return await transcriptionTask;
}

static void PersistDebugRecordingIfEnabled(RecordingResult recording)
{
    if (!DebugTrace.IsEnabled)
    {
        return;
    }

    try
    {
        var savedPath = ArtifactCleanup.SaveDebugVoiceRecording(AppContext.BaseDirectory, recording);
        DebugTrace.WriteStructuredEvent(
            "audio.debug_recording_saved",
            new Dictionary<string, object?>
            {
                ["sourcePath"] = recording.FilePath,
                ["savedPath"] = savedPath,
                ["startedAt"] = recording.StartedAt,
                ["endedAt"] = recording.EndedAt,
                ["waveDurationMs"] = recording.WaveDurationMs,
                ["pcmDataBytes"] = recording.PcmDataBytes,
            });
    }
    catch (Exception ex)
    {
        DebugTrace.WriteEvent(
            "audio.debug_recording_save_failed",
            $"sourcePath={recording.FilePath}, error={DebugTrace.Preview(ex.ToString(), 600)}");
    }
}

static Task PlayAudioOutputAsync(Func<Task> playback) => playback();

async Task ShutdownAsync()
{
    Display.Info("Shutting down...");
    await FaceBridge.ShutdownAsync();
    DebugTrace.WriteEvent("session.shutdown", "Application shutdown completed.");
    if (!DebugTrace.IsEnabled)
    {
        ArtifactCleanup.CleanupCurrentRunArtifacts(DebugTrace.LogFilePath, DebugTrace.JsonLogFilePath, AppContext.BaseDirectory);
    }

    if (DebugTrace.IsEnabled && !string.IsNullOrWhiteSpace(DebugTrace.LogFilePath))
    {
        Display.Info($"Debug log saved to: {DebugTrace.LogFilePath}");
        if (!string.IsNullOrWhiteSpace(DebugTrace.JsonLogFilePath))
        {
            Display.Info($"Debug JSONL saved to: {DebugTrace.JsonLogFilePath}");
        }

        Display.Info($"Debug artifacts saved under: {DebugTrace.BuildLogsDirectory(AppContext.BaseDirectory)}");
    }
}

internal sealed record InteractiveLoopResult(bool ExitRequested, BrainInteractiveMode? NextMode);
