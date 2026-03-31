using HeronWin.HerFace;

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
try
{
    llmClient = LlmFactory.CreateLlmClient(config, httpClient);
    audioTranscriber = LlmFactory.CreateAudioTranscriber(config, httpClient);
}
catch (Exception ex)
{
    Display.Error(ex.Message);
    return;
}

Display.Info($"LLM: {llmClient.DisplayName}");
Display.Info($"Mic capture: {AudioRecorder.DescribeRecordingFormat()}");
if (config.DebugAudioPlayback)
{
    Display.Info("Debug audio playback is enabled; each captured recording will replay during transcription.");
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
Display.Info("Type your message and press Enter, or just press Enter to use the microphone.");
Display.Info("Type \"exit\" or press Ctrl+C to quit.");
Display.Separator();

var history = new List<AgentMessage>();

while (!cancellationSource.IsCancellationRequested)
{
    Display.Prompt("\n🎤  Press Enter to speak (or type your message): ");
    var line = Console.ReadLine();
    if (line is null)
    {
        break;
    }

    var trimmed = line.Trim();
    if (string.Equals(trimmed, "exit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    var userText = trimmed;
    RecordingResult? recording = null;

    if (string.IsNullOrWhiteSpace(userText))
    {
        Display.Recording();
        if (audioTranscriber is null)
        {
            Display.Warn("Voice transcription requires OPENAI_API_KEY for Whisper. Please type your message instead.");
            continue;
        }

        try
        {
            await AudioPlayback.PlayRecordingStartCueAsync();
            recording = await AudioRecorder.RecordAsync(config.MaxRecordMs, cancellationSource.Token);
            await AudioPlayback.PlayRecordingStopCueAsync();

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
        catch (Exception ex)
        {
            Display.Error($"Recording/transcription failed: {ex.Message}");
            CleanupRecording(recording);
            continue;
        }

        CleanupRecording(recording);

        if (string.IsNullOrWhiteSpace(userText))
        {
            Display.Warn("No speech detected. Please try again.");
            continue;
        }
    }

    try
    {
        var reply = await AgentRunner.RunTurnAsync(userText, history, llmClient, mcpManager, cancellationSource.Token);
        history.Add(new AgentMessage.User(userText));
        history.Add(new AgentMessage.Assistant(reply));
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
