using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace HeronWin.HerFace;

internal sealed record RecordingResult(
    string FilePath,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    double WallClockDurationMs,
    double WaveDurationMs,
    double DurationDeltaMs,
    long PcmDataBytes
);

internal static class AudioDevices
{
    public static string DescribeDefaultMicrophone()
        => DescribeDefaultDevice(DataFlow.Capture, Role.Console, "No default microphone found");

    public static string DescribeDefaultSpeaker()
        => DescribeDefaultDevice(DataFlow.Render, Role.Console, "No default speaker found");

    private static string DescribeDefaultDevice(DataFlow flow, Role role, string fallback)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(flow, role);
            return $"{device.FriendlyName} [{device.DataFlow}, {device.State}]";
        }
        catch
        {
            return fallback;
        }
    }
}

internal static class AudioRecorder
{
    public const int SampleRate = 16_000;
    public const int ChannelCount = 1;
    public const int BitsPerSample = 16;
    private const int BufferMilliseconds = 100;
    private const int PreRollMilliseconds = 2_500;
    private const int SilenceGraceMs = 1_000;
    private const int MinSpeechCaptureMs = 350;
    private const int ConsecutiveSpeechBuffers = 2;
    private const double SpeechPeakThreshold = 0.03;
    private const double SpeechRmsThreshold = 0.009;

    public static string DescribeRecordingFormat()
        => $"{SampleRate} Hz, {ChannelCount} channel, {BitsPerSample}-bit PCM";

    public static Task<RecordingResult?> RecordAsync(int maxDurationMs, int? maxWaitForSpeechMs, CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"herface-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.wav");
        var completion = new TaskCompletionSource<RecordingResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var waveIn = new WaveInEvent
        {
            BufferMilliseconds = BufferMilliseconds,
            DeviceNumber = 0,
            NumberOfBuffers = 3,
            WaveFormat = new WaveFormat(SampleRate, BitsPerSample, ChannelCount),
        };
        var writer = new WaveFileWriter(tempPath, waveIn.WaveFormat);

        DateTimeOffset? startedAt = null;
        DateTimeOffset? firstSpeechAt = null;
        DateTimeOffset? lastSpeechAt = null;
        DateTimeOffset? endedAt = null;
        long pcmDataBytes = 0;
        var speechStarted = false;
        var consecutiveSpeechBuffers = 0;
        var consecutiveSilenceBuffers = 0;
        var stopRequested = 0;
        var silenceBuffersToStop = (int)Math.Ceiling((double)SilenceGraceMs / BufferMilliseconds);
        var preRollBytesLimit = waveIn.WaveFormat.AverageBytesPerSecond * PreRollMilliseconds / 1000;
        var preRollBytes = 0;
        var preRollBuffers = new Queue<BufferedAudioChunk>();

        using var cancellationRegistration = cancellationToken.Register(() => RequestStop());
        using var waitForSpeechTimer = maxWaitForSpeechMs is int waitMs
            ? new Timer(_ =>
            {
                if (!speechStarted)
                {
                    RequestStop();
                }
            }, null, waitMs, Timeout.Infinite)
            : null;

        waveIn.DataAvailable += (_, eventArgs) =>
        {
            var now = DateTimeOffset.UtcNow;
            var chunkDuration = TimeSpan.FromSeconds((double)eventArgs.BytesRecorded / waveIn.WaveFormat.AverageBytesPerSecond);
            var chunkEndedAt = now;
            var chunkStartedAt = chunkEndedAt - chunkDuration;
            var chunk = BufferedAudioChunk.Create(eventArgs.Buffer, eventArgs.BytesRecorded, chunkStartedAt, chunkEndedAt);
            var levels = AnalyzeAudioLevels(chunk.Bytes, chunk.BytesRecorded);
            var looksLikeSpeech = levels.Peak >= SpeechPeakThreshold || levels.Rms >= SpeechRmsThreshold;

            if (!speechStarted)
            {
                preRollBuffers.Enqueue(chunk);
                preRollBytes += chunk.BytesRecorded;
                while (preRollBytes > preRollBytesLimit && preRollBuffers.Count > 0)
                {
                    var trimmed = preRollBuffers.Dequeue();
                    preRollBytes -= trimmed.BytesRecorded;
                }

                if (looksLikeSpeech)
                {
                    consecutiveSpeechBuffers += 1;
                    if (consecutiveSpeechBuffers >= ConsecutiveSpeechBuffers)
                    {
                        speechStarted = true;
                        startedAt = preRollBuffers.Count > 0 ? preRollBuffers.Peek().StartedAt : chunk.StartedAt;
                        firstSpeechAt = chunk.EndedAt;
                        lastSpeechAt = chunk.EndedAt;
                        consecutiveSilenceBuffers = 0;

                        foreach (var bufferedChunk in preRollBuffers)
                        {
                            writer.Write(bufferedChunk.Bytes, 0, bufferedChunk.BytesRecorded);
                            pcmDataBytes += bufferedChunk.BytesRecorded;
                        }

                        writer.Flush();
                        preRollBuffers.Clear();
                        preRollBytes = 0;
                    }
                }
                else
                {
                    consecutiveSpeechBuffers = 0;
                }

                return;
            }

            writer.Write(chunk.Bytes, 0, chunk.BytesRecorded);
            writer.Flush();
            pcmDataBytes += chunk.BytesRecorded;

            if (looksLikeSpeech)
            {
                consecutiveSilenceBuffers = 0;
                lastSpeechAt = chunk.EndedAt;
            }
            else
            {
                consecutiveSilenceBuffers += 1;
            }

            if (startedAt is not null && (chunk.EndedAt - startedAt.Value).TotalMilliseconds >= maxDurationMs)
            {
                RequestStop(chunk.EndedAt);
                return;
            }

            if (firstSpeechAt is null || lastSpeechAt is null)
            {
                return;
            }

            var minStopAt = Max(
                lastSpeechAt.Value.AddMilliseconds(SilenceGraceMs),
                firstSpeechAt.Value.AddMilliseconds(MinSpeechCaptureMs));
            if (consecutiveSilenceBuffers >= silenceBuffersToStop && chunk.EndedAt >= minStopAt)
            {
                RequestStop(chunk.EndedAt);
            }
        };

        waveIn.RecordingStopped += (_, eventArgs) =>
        {
            try
            {
                writer.Flush();
                writer.Dispose();
                waveIn.Dispose();
                if (eventArgs.Exception is not null)
                {
                    completion.TrySetException(eventArgs.Exception);
                    return;
                }

                if (!speechStarted)
                {
                    TryDeleteFile(tempPath);
                    completion.TrySetResult(null);
                    return;
                }

                var finalEndedAt = endedAt ?? DateTimeOffset.UtcNow;
                var actualStartedAt = startedAt ?? finalEndedAt;
                var wallClockDurationMs = (finalEndedAt - actualStartedAt).TotalMilliseconds;
                var waveDurationMs = waveIn.WaveFormat.AverageBytesPerSecond == 0
                    ? 0
                    : (double)pcmDataBytes / waveIn.WaveFormat.AverageBytesPerSecond * 1000;

                completion.TrySetResult(new RecordingResult(
                    tempPath,
                    actualStartedAt,
                    finalEndedAt,
                    wallClockDurationMs,
                    waveDurationMs,
                    waveDurationMs - wallClockDurationMs,
                    pcmDataBytes));
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        };

        waveIn.StartRecording();
        return completion.Task;

        void RequestStop(DateTimeOffset? stopAt = null)
        {
            if (Interlocked.Exchange(ref stopRequested, 1) != 0)
            {
                return;
            }

            endedAt = stopAt ?? DateTimeOffset.UtcNow;
            waveIn.StopRecording();
        }
    }

    private static AudioLevels AnalyzeAudioLevels(byte[] buffer, int bytesRecorded)
    {
        if (bytesRecorded < 2)
        {
            return new AudioLevels(0, 0);
        }

        var peak = 0;
        double sumSquares = 0;
        var sampleCount = 0;
        for (var i = 0; i + 1 < bytesRecorded; i += 2)
        {
            var sample = BitConverter.ToInt16(buffer, i);
            var magnitude = sample == short.MinValue ? 32768 : Math.Abs((int)sample);
            peak = Math.Max(peak, magnitude);
            var normalized = sample / 32768d;
            sumSquares += normalized * normalized;
            sampleCount += 1;
        }

        var peakNormalized = peak / 32768d;
        var rmsNormalized = sampleCount == 0 ? 0 : Math.Sqrt(sumSquares / sampleCount);
        return new AudioLevels(peakNormalized, rmsNormalized);
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right)
        => left >= right ? left : right;

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    private readonly record struct AudioLevels(double Peak, double Rms);
    private readonly record struct BufferedAudioChunk(byte[] Bytes, int BytesRecorded, DateTimeOffset StartedAt, DateTimeOffset EndedAt)
    {
        public static BufferedAudioChunk Create(byte[] sourceBuffer, int bytesRecorded, DateTimeOffset startedAt, DateTimeOffset endedAt)
        {
            var copy = new byte[bytesRecorded];
            Buffer.BlockCopy(sourceBuffer, 0, copy, 0, bytesRecorded);
            return new BufferedAudioChunk(copy, bytesRecorded, startedAt, endedAt);
        }
    }
}

internal static class AudioPlayback
{
    private const float QuietBeepAmplitude = 0.08f;

    private static readonly Lazy<Task<string>> StartCuePath = new(() => EnsureCueFileAsync(
        "herface-recording-start.wav",
        BuildChimePcmBuffer(1046, 160)));

    private static readonly Lazy<Task<string>> StopCuePath = new(() => EnsureCueFileAsync(
        "herface-recording-stop.wav",
        BuildChimePcmBuffer(523, 220)));

    private static readonly Lazy<Task<string>> WindDownCuePath = new(() => EnsureCueFileAsync(
        "herface-wind-down.wav",
        BuildWindPcmBuffer(1100)));

    public static async Task PlayRecordingStartCueAsync()
        => await PlayWavFileAsync(await StartCuePath.Value);

    public static async Task PlayRecordingStopCueAsync()
        => await PlayWavFileAsync(await StopCuePath.Value);

    public static async Task PlayWindDownCueAsync()
        => await PlayWavFileAsync(await WindDownCuePath.Value);

    public static async Task PlayWavFileAsync(string filePath)
    {
        using var reader = new AudioFileReader(filePath);
        using var output = new WaveOutEvent();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, eventArgs) =>
        {
            if (eventArgs.Exception is not null)
            {
                completion.TrySetException(eventArgs.Exception);
                return;
            }

            completion.TrySetResult();
        };

        output.Init(reader);
        output.Play();
        await completion.Task;
    }

    private static async Task<string> EnsureCueFileAsync(string fileName, byte[] pcmBytes)
    {
        var path = Path.Combine(Path.GetTempPath(), fileName);
        await using var writer = new WaveFileWriter(path, new WaveFormat(AudioRecorder.SampleRate, AudioRecorder.BitsPerSample, AudioRecorder.ChannelCount));
        await writer.WriteAsync(pcmBytes, 0, pcmBytes.Length);
        return path;
    }

    private static byte[] BuildChimePcmBuffer(int frequencyHz, int durationMs)
    {
        var sampleCount = Math.Max(1, AudioRecorder.SampleRate * durationMs / 1000);
        var bytes = new byte[sampleCount * 2];
        var amplitude = (short)(short.MaxValue * QuietBeepAmplitude);

        for (var index = 0; index < sampleCount; index += 1)
        {
            var angle = 2 * Math.PI * frequencyHz * index / AudioRecorder.SampleRate;
            var decay = 1.0 - (double)index / sampleCount;
            var sample = (short)Math.Round(Math.Sin(angle) * amplitude * decay);
            BitConverter.GetBytes(sample).CopyTo(bytes, index * 2);
        }

        return bytes;
    }

    private static byte[] BuildWindPcmBuffer(int durationMs)
    {
        var sampleCount = Math.Max(1, AudioRecorder.SampleRate * durationMs / 1000);
        var bytes = new byte[sampleCount * 2];
        var random = new Random(17);
        double smoothed = 0;
        var amplitude = short.MaxValue * 0.035;

        for (var index = 0; index < sampleCount; index += 1)
        {
            var noise = random.NextDouble() * 2 - 1;
            smoothed = (smoothed * 0.985) + (noise * 0.015);
            var progress = (double)index / sampleCount;
            var envelope = Math.Sin(progress * Math.PI);
            var sample = (short)Math.Round(smoothed * amplitude * envelope);
            BitConverter.GetBytes(sample).CopyTo(bytes, index * 2);
        }

        return bytes;
    }
}
