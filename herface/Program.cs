using HeronWin.HerFace;
using System.Text.RegularExpressions;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Display.Banner();

var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    cancellationSource.Cancel();
};

var config = AppConfig.Load();
using var httpClient = new HttpClient();
await using var mcpManager = new McpClientManager();

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
    return;
}

Display.Info($"LLM: {llmClient.DisplayName}");
Display.Info($"Microphone: {AudioDevices.DescribeDefaultMicrophone()}");
Display.Info($"Speaker: {AudioDevices.DescribeDefaultSpeaker()}");
Display.Info($"Mic capture: {AudioRecorder.DescribeRecordingFormat()}");
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

while (!cancellationSource.IsCancellationRequested)
{
    var userText = string.Empty;
    RecordingResult? recording = null;
    int? maxWaitForSpeechMs = isActive ? config.ActiveIdleTimeoutMs : null;

    try
    {
        Display.Prompt(isActive ? "\n🎤  Listening... " : $"\n🎤  Waiting for {config.WakeWord}... ");
        recording = await AudioRecorder.RecordAsync(config.MaxRecordMs, maxWaitForSpeechMs, cancellationSource.Token);
        Console.WriteLine();

        if (recording is null)
        {
            if (isActive)
            {
                await AudioPlayback.PlayWindDownCueAsync();
                isActive = false;
                Display.Info($"Back to standby. Say \"{config.WakeWord}\" when you want me again.");
            }

            continue;
        }

        if (isActive)
        {
            await AudioPlayback.PlayRecordingStopCueAsync();
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
        CleanupRecording(recording);
        continue;
    }

    CleanupRecording(recording);

    if (string.IsNullOrWhiteSpace(userText))
    {
        continue;
    }

    Display.Transcript(userText);

    if (ShouldExitApp(userText))
    {
        break;
    }

    if (!isActive)
    {
        if (!ContainsWakeWord(userText, config.WakeWord))
        {
            continue;
        }

        isActive = true;
        const string wakeResponse = "what's up?";
        Display.AssistantMessage(wakeResponse);
        try
        {
            await SpeakAsync(speechSynthesizer, wakeResponse, cancellationSource.Token);
        }
        catch (Exception ex)
        {
            Display.Warn($"Wake response speech failed: {ex.Message}");
        }
        continue;
    }

    try
    {
        var reply = await AgentRunner.RunTurnAsync(userText, history, llmClient, mcpManager, cancellationSource.Token);
        history.Add(new AgentMessage.User(userText));
        history.Add(new AgentMessage.Assistant(reply));
        try
        {
            await SpeakAsync(speechSynthesizer, reply, cancellationSource.Token);
        }
        catch (Exception ex)
        {
            Display.Warn($"Reply speech failed: {ex.Message}");
        }
    }
    catch (Exception ex)
    {
        Display.Error($"Agent error: {ex.Message}");
    }
}

Display.Info("Shutting down...");
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
