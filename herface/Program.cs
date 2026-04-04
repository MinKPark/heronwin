using HeronWin.HerFace;
using System.Text.RegularExpressions;
using System.Threading.Channels;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    cancellationSource.Cancel();
};

var config = AppConfig.Load();
ArtifactCleanup.CleanupPreviousRunArtifacts(AppContext.BaseDirectory, Environment.ProcessPath);
DebugTrace.Configure(config.DebugAudioPlayback);
Display.Banner();
using var httpClient = new HttpClient();
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
        ["llmProvider"] = config.LlmProvider.ToString(),
        ["openAiModel"] = config.OpenAiModel,
        ["anthropicModel"] = config.AnthropicModel,
        ["whisperModel"] = config.WhisperModel,
        ["ttsModel"] = config.TtsModel,
        ["ttsVoice"] = config.TtsVoice,
        ["wakeWord"] = config.WakeWord,
        ["maxContextTokens"] = config.MaxContextTokens,
        ["debugAudioPlayback"] = config.DebugAudioPlayback,
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
    $"llmProvider={config.LlmProvider}, openAiModel={config.OpenAiModel}, anthropicModel={config.AnthropicModel}, whisperModel={config.WhisperModel}, wakeWord={DebugTrace.SerializeObject(config.WakeWord)}, agentDefinitionPath={config.AgentDefinitionPath}, agentCoreDefinitionPath={config.AgentPrompts.CoreDefinitionPath ?? "(none)"}, agentSkills={config.AgentPrompts.Skills.Count}, mcpServers={config.McpServers.Count}, debugAudioPlayback={config.DebugAudioPlayback}");

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
Display.Info($"Microphone: {AudioDevices.DescribeDefaultMicrophone()}");
Display.Info($"Speaker: {AudioDevices.DescribeDefaultSpeaker()}");
Display.Info($"Mic capture: {AudioRecorder.DescribeRecordingFormat()}");
if (DebugTrace.IsEnabled && !string.IsNullOrWhiteSpace(DebugTrace.LogFilePath))
{
    Display.Info($"Debug trace: {DebugTrace.LogFilePath}");
    if (!string.IsNullOrWhiteSpace(DebugTrace.JsonLogFilePath))
    {
        Display.Info($"Debug trace JSONL: {DebugTrace.JsonLogFilePath}");
    }
}
if (speechSynthesizer is not null)
{
    Display.Info($"Voice output: {speechSynthesizer.DisplayName}");
}
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

            var tools = await mcpManager.ListAllToolsAsync(cancellationSource.Token);
            var composedPrompt = AgentPromptComposer.Compose(config.AgentPrompts, queuedTurn.Text, tools);
            DebugTrace.WriteEvent(
                "agent.prompt.composed",
                $"turn={queuedTurn.TurnId}, source={composedPrompt.SourceDescription}, fallback={composedPrompt.UsesFallbackDefinition}, skills={string.Join(", ", composedPrompt.ActiveSkills.Select(skill => skill.Key).DefaultIfEmpty("(none)"))}");

            await ContextManager.EnsureCapacityAsync(
                history,
                queuedTurn.Text,
                composedPrompt.SystemPrompt,
                config.MaxContextTokens,
                llmClient,
                cancellationSource.Token);

            var reply = await AgentRunner.RunTurnAsync(
                queuedTurn.TurnId,
                queuedTurn.Text,
                history,
                tools,
                composedPrompt,
                llmClient,
                mcpManager,
                cancellationSource.Token,
                intermediateStepNarrator: speechSynthesizer is null
                    ? null
                    : async (stepText, cancellationToken) =>
                    {
                        await PlayAudioOutputAsync(() => SpeakAsync(speechSynthesizer, stepText, cancellationToken));
                    });
            history.Add(new AgentMessage.User(queuedTurn.Text));
            history.Add(new AgentMessage.Assistant(reply.RawText));
            Display.ContextUsage(
                ContextManager.EstimateTokens(history, composedPrompt.SystemPrompt),
                config.MaxContextTokens);
            DebugTrace.WriteEvent(
                "agent.turn.complete",
                $"turn={queuedTurn.TurnId}, spoken={DebugTrace.Preview(reply.SpokenText, 300)}, log={DebugTrace.Preview(reply.LogText, 600)}");
            if (!string.IsNullOrWhiteSpace(reply.SpokenText))
            {
                try
                {
                    await PlayAudioOutputAsync(() => SpeakAsync(speechSynthesizer, reply.SpokenText, cancellationSource.Token));
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

        Display.Transcribing();
        if (config.DebugAudioPlayback)
        {
            Display.Info("Debug: replaying the captured WAV while it is being sent for transcription.");
        }

        var transcriptionTask = audioTranscriber.TranscribeAudioAsync(recording.FilePath, cancellationSource.Token);
        Task playbackTask = config.DebugAudioPlayback
            ? AudioPlayback.PlayWavFileAsync(recording.FilePath)
            : Task.CompletedTask;
        await Task.WhenAll(transcriptionTask, playbackTask.ContinueWith(_ => { }, TaskScheduler.Default));
        userText = await transcriptionTask;
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

    if (ShouldExitApp(userText))
    {
        DebugTrace.WriteEvent("session.exit_phrase_detected", $"text={DebugTrace.Preview(userText, 300)}");
        cancellationSource.Cancel();
        break;
    }

    if (!isActive)
    {
        if (!ContainsWakeWord(userText, config.WakeWord))
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
    ArtifactCleanup.CleanupCurrentRunArtifacts(DebugTrace.LogFilePath, DebugTrace.JsonLogFilePath);
}
PrintDebugLogPathIfEnabled();
return;

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

static bool ContainsWakeWord(string text, string wakeWord)
{
    if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(wakeWord))
    {
        return false;
    }

    return Regex.IsMatch(text, $@"\b{Regex.Escape(wakeWord)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}

static bool ShouldExitApp(string text)
{
    if (string.IsNullOrWhiteSpace(text))
    {
        return false;
    }

    var normalized = Regex.Replace(text, @"[^\p{L}\p{N}\s-]", " ")
        .ToLowerInvariant()
        .Trim();

    return normalized == "bye"
           || normalized == "bye-bye"
           || normalized == "bye bye"
           || normalized.EndsWith(" bye", StringComparison.Ordinal)
           || normalized.EndsWith(" bye-bye", StringComparison.Ordinal)
           || normalized.EndsWith(" bye bye", StringComparison.Ordinal);
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
    }
}
