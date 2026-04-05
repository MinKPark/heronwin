using HeronWin.HerFace;
using System.Threading.Channels;

Console.OutputEncoding = System.Text.Encoding.UTF8;

HerfaceConsoleOptions consoleOptions;
try
{
    consoleOptions = HerfaceConsoleMode.Parse(args);
}
catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException)
{
    Console.Error.WriteLine($"x  {ex.Message}");
    Environment.ExitCode = 1;
    return;
}

if (consoleOptions.ShowHelp)
{
    HerfaceConsoleMode.PrintHelp();
    return;
}

var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};

var config = AppConfig.Load();
ArtifactCleanup.CleanupPreviousRunArtifacts(AppContext.BaseDirectory, Environment.ProcessPath);
DebugTrace.Configure(config.EnableDebugTrace || config.DebugAudioPlayback || consoleOptions.RequiresDebugTrace);
Display.Banner();
var httpClientSetup = HerfaceHttpClientFactory.Create();
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
        ["launchMode"] = consoleOptions.IsScripted ? "scripted" : "voice",
        ["scriptedScenarioPath"] = consoleOptions.ScenarioFilePath,
        ["scriptedCommands"] = consoleOptions.Commands.ToArray(),
        ["debugTraceEnabled"] = DebugTrace.IsEnabled,
        ["llmProvider"] = config.LlmProvider.ToString(),
        ["openAiModel"] = config.OpenAiModel,
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
        ["debugAudioPlayback"] = config.DebugAudioPlayback,
        ["debugVoiceDirectory"] = DebugTrace.IsEnabled
            ? ArtifactCleanup.GetDebugVoiceRecordingDirectory(AppContext.BaseDirectory)
            : null,
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
    $"mode={(consoleOptions.IsScripted ? "scripted" : "voice")}, llmProvider={config.LlmProvider}, openAiModel={config.OpenAiModel}, anthropicModel={config.AnthropicModel}, whisperModel={config.WhisperModel}, voiceLanguages={DebugTrace.SerializeObject(config.VoiceLanguages.Select(static language => language.DisplayName).ToArray())}, wakeWord={DebugTrace.SerializeObject(config.WakeWord)}, agentDefinitionPath={config.AgentDefinitionPath}, agentCoreDefinitionPath={config.AgentPrompts.CoreDefinitionPath ?? "(none)"}, agentSkills={config.AgentPrompts.Skills.Count}, mcpServers={config.McpServers.Count}, debugTrace={DebugTrace.IsEnabled}, debugAudioPlayback={config.DebugAudioPlayback}");

if (consoleOptions.IsScripted)
{
    var scriptedExitCode = await RunScriptedModeAsync(cancellationSource.Token);
    Display.Info("Shutting down...");
    DebugTrace.WriteEvent("session.shutdown", "Application shutdown completed.");
    if (!DebugTrace.IsEnabled)
    {
        ArtifactCleanup.CleanupCurrentRunArtifacts(DebugTrace.LogFilePath, DebugTrace.JsonLogFilePath, AppContext.BaseDirectory);
    }

    PrintDebugLogPathIfEnabled();
    Environment.ExitCode = scriptedExitCode;
    return;
}

ILlmClient llmClient;
IAudioTranscriber? audioTranscriber;
ISpeechSynthesizer? speechSynthesizer;
try
{
    llmClient = LlmFactory.CreateLlmClient(config, httpClient);
    audioTranscriber = LlmFactory.CreateAudioTranscriber(config, httpClient);
    speechSynthesizer = LlmFactory.CreateSpeechSynthesizer(config, httpClient);
}
catch (Exception ex)
{
    Display.Error(ex.Message);
    PrintDebugLogPathIfEnabled();
    return;
}

Display.Info($"LLM: {llmClient.DisplayName}");
if (httpClientSetup.BypassedBrokenLoopbackProxy)
{
    Display.Warn(
        $"Ignoring broken loopback proxy setting for outbound API calls: {httpClientSetup.BypassedProxyValue}");
}
Display.Info($"Microphone: {AudioDevices.DescribeDefaultMicrophone()}");
Display.Info($"Speaker: {AudioDevices.DescribeDefaultSpeaker()}");
Display.Info($"Mic capture: {AudioRecorder.DescribeRecordingFormat()}");
Display.Info($"Voice languages: {string.Join(", ", config.VoiceLanguages.Select(static language => language.DisplayName))}");
if (DebugTrace.IsEnabled && !string.IsNullOrWhiteSpace(DebugTrace.LogFilePath))
{
    Display.Info($"Debug trace: {DebugTrace.LogFilePath}");
    if (!string.IsNullOrWhiteSpace(DebugTrace.JsonLogFilePath))
    {
        Display.Info($"Debug trace JSONL: {DebugTrace.JsonLogFilePath}");
    }

    Display.Info($"Debug voice WAVs: {ArtifactCleanup.GetDebugVoiceRecordingDirectory(AppContext.BaseDirectory)}");
}
if (speechSynthesizer is not null)
{
    Display.Info($"Voice output: {speechSynthesizer.DisplayName}");
}
Display.Info($"Voice input: {audioTranscriber?.DisplayName ?? "(unavailable)"}");
if (config.DebugAudioPlayback)
{
    Display.Info("Debug audio playback is enabled; each captured recording will replay during transcription.");
}
if (audioTranscriber is null)
{
    Display.Error("Voice mode requires OPENAI_API_KEY for Whisper transcription.");
    PrintDebugLogPathIfEnabled();
    return;
}

if (config.McpServers.Count > 0)
{
    Display.Info($"Connecting to {config.McpServers.Count} MCP server(s)...");
    try
    {
        await mcpManager.ConnectAsync(config.McpServers, cancellationSource.Token);
        var tools = await mcpManager.ListAllToolsAsync(cancellationSource.Token);
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

Display.Separator();
Display.Info($"Standby mode is listening for \"{config.WakeWord}\".");
Display.Info("After the wake phrase is heard, just speak naturally. Say \"bye\" or \"bye-bye\" to exit.");
Display.Info("If you go quiet for a minute, I will drift back to standby.");
Display.Separator();

var history = new List<AgentMessage>();
var isActive = false;
var audioOutputActive = 0;
var agentWorkActive = 0;
var queuedTurnCount = 0;
long nextTurnId = 0;
var userQueue = Channel.CreateUnbounded<(long TurnId, string Text)>(new UnboundedChannelOptions
{
    SingleReader = true,
    SingleWriter = true
});

var agentTask = Task.Run(async () =>
{
    await foreach (var queuedTurn in userQueue.Reader.ReadAllAsync(cancellationSource.Token))
    {
        Interlocked.Decrement(ref queuedTurnCount);
        Interlocked.Increment(ref agentWorkActive);
        try
        {
            DebugTrace.WriteEvent(
                "agent.turn.dequeue",
                $"turn={queuedTurn.TurnId}, historyMessages={history.Count}, queuedText={DebugTrace.Preview(queuedTurn.Text, 500)}");

            var processedTurn = await HerfaceTurnProcessor.ProcessAsync(
                queuedTurn.TurnId,
                queuedTurn.Text,
                history,
                config,
                llmClient,
                mcpManager,
                cancellationSource.Token,
                turnSource: "voice",
                intermediateStepNarrator: speechSynthesizer is null
                    ? null
                    : async (stepText, innerCancellationToken) =>
                    {
                        await PlayAudioOutputAsync(() => SpeakAsync(speechSynthesizer, stepText, innerCancellationToken));
                    });

            if (!string.IsNullOrWhiteSpace(processedTurn.Reply.SpokenText))
            {
                try
                {
                    await PlayAudioOutputAsync(
                        () => SpeakAsync(speechSynthesizer, processedTurn.Reply.SpokenText, cancellationSource.Token));
                }
                catch (Exception ex)
                {
                    Display.Warn($"Reply speech failed: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            break;
        }
        catch (Exception ex)
        {
            Display.Error($"Agent error: {ex.Message}");
        }
        finally
        {
            Interlocked.Decrement(ref agentWorkActive);
        }
    }
}, cancellationSource.Token);

while (!cancellationSource.IsCancellationRequested)
{
    var userText = string.Empty;
    RecordingResult? recording = null;
    int? maxWaitForSpeechMs = isActive ? config.ActiveIdleTimeoutMs : null;

    try
    {
        await WaitForTurnOutputToFinishAsync(cancellationSource.Token);
        Display.Info(isActive ? "Listening..." : $"Waiting for {config.WakeWord}...");
        if (isActive)
        {
            await PlayAudioOutputAsync(AudioPlayback.PlayRecordingStartCueAsync);
        }

        recording = await AudioRecorder.RecordAsync(config.MaxRecordMs, maxWaitForSpeechMs, cancellationSource.Token);

        if (recording is null)
        {
            if (isActive)
            {
                await PlayAudioOutputAsync(AudioPlayback.PlayWindDownCueAsync);
                isActive = false;
                Display.Info($"Back to standby. Say \"{config.WakeWord}\" when you want me again.");
            }

            continue;
        }

        if (isActive)
        {
            await PlayAudioOutputAsync(AudioPlayback.PlayRecordingStopCueAsync);
        }

        if (config.DebugAudioPlayback)
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
            config,
            cancellationSource.Token);
    }
    catch (OperationCanceledException)
    {
        CleanupRecording(recording);
        break;
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
        cancellationSource.Cancel();
        break;
    }

    if (!isActive)
    {
        if (!SpeechGate.ContainsWakeWord(userText, config.WakeWord))
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
            await PlayAudioOutputAsync(() => SpeakAsync(speechSynthesizer, wakeResponse, cancellationSource.Token));
        }
        catch (Exception ex)
        {
            Display.Warn($"Wake response speech failed: {ex.Message}");
        }
        continue;
    }

    try
    {
        var turnId = Interlocked.Increment(ref nextTurnId);
        Interlocked.Increment(ref queuedTurnCount);
        DebugTrace.WriteEvent("agent.turn.queued", $"turn={turnId}, text={DebugTrace.Preview(userText, 500)}");
        await userQueue.Writer.WriteAsync((turnId, userText), cancellationSource.Token);
    }
    catch (Exception ex)
    {
        Interlocked.Decrement(ref queuedTurnCount);
        Display.Error($"Queue error: {ex.Message}");
        DebugTrace.WriteEvent("agent.queue_error", $"error={DebugTrace.Preview(ex.ToString(), 800)}");
    }
}

userQueue.Writer.TryComplete();
try
{
    await agentTask;
}
catch (OperationCanceledException)
{
    // Normal shutdown.
}

Display.Info("Shutting down...");
DebugTrace.WriteEvent("session.shutdown", "Application shutdown completed.");
if (!DebugTrace.IsEnabled)
{
    ArtifactCleanup.CleanupCurrentRunArtifacts(DebugTrace.LogFilePath, DebugTrace.JsonLogFilePath, AppContext.BaseDirectory);
}
PrintDebugLogPathIfEnabled();
return;

async Task<int> RunScriptedModeAsync(CancellationToken cancellationToken)
{
    ILlmClient scriptedLlmClient;
    try
    {
        scriptedLlmClient = LlmFactory.CreateLlmClient(config, httpClient);
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

    return await ScriptedConversationRunner.RunAsync(
        consoleOptions,
        config,
        scriptedLlmClient,
        mcpManager,
        cancellationToken);
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

async Task PlayAudioOutputAsync(Func<Task> playback)
{
    Interlocked.Increment(ref audioOutputActive);
    try
    {
        await playback();
    }
    finally
    {
        Interlocked.Decrement(ref audioOutputActive);
    }
}

async Task WaitForTurnOutputToFinishAsync(CancellationToken cancellationToken)
{
    while ((Volatile.Read(ref audioOutputActive) > 0
            || Volatile.Read(ref agentWorkActive) > 0
            || Volatile.Read(ref queuedTurnCount) > 0)
           && !cancellationToken.IsCancellationRequested)
    {
        await Task.Delay(100, cancellationToken);
    }
}

static void PrintDebugLogPathIfEnabled()
{
    if (DebugTrace.IsEnabled && !string.IsNullOrWhiteSpace(DebugTrace.LogFilePath))
    {
        Display.Info($"Debug log saved to: {DebugTrace.LogFilePath}");
        if (!string.IsNullOrWhiteSpace(DebugTrace.JsonLogFilePath))
        {
            Display.Info($"Debug JSONL saved to: {DebugTrace.JsonLogFilePath}");
        }

        Display.Info($"Debug voice WAVs saved to: {ArtifactCleanup.GetDebugVoiceRecordingDirectory(AppContext.BaseDirectory)}");
    }
}
