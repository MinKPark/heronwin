using NAudio.Wave;

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

internal static class AudioRecorder
{
    public const int SampleRate = 16_000;
    public const int ChannelCount = 1;
    public const int BitsPerSample = 16;
    private const int SilenceGraceMs = 700;
    private const int MinSpeechCaptureMs = 350;
    private const double SilenceThreshold = 0.01;

    public static string DescribeRecordingFormat()
        => $"{SampleRate} Hz, {ChannelCount} channel, {BitsPerSample}-bit PCM";

    public static Task<RecordingResult> RecordAsync(int maxDurationMs, CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"herface-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.wav");
        var completion = new TaskCompletionSource<RecordingResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var waveIn = new WaveInEvent
        {
            BufferMilliseconds = 100,
            DeviceNumber = 0,
            NumberOfBuffers = 3,
            WaveFormat = new WaveFormat(SampleRate, BitsPerSample, ChannelCount),
        };
        var writer = new WaveFileWriter(tempPath, waveIn.WaveFormat);

        var startedAt = DateTimeOffset.UtcNow;
        DateTimeOffset? firstSpeechAt = null;
        DateTimeOffset? lastSpeechAt = null;
        DateTimeOffset? endedAt = null;
        long pcmDataBytes = 0;
        var stopRequested = 0;

        using var cancellationRegistration = cancellationToken.Register(RequestStop);
        using var maxDurationTimer = new Timer(_ => RequestStop(), null, maxDurationMs, Timeout.Infinite);
        using var silenceTimer = new Timer(_ => RequestStop(), null, Timeout.Infinite, Timeout.Infinite);

        waveIn.DataAvailable += (_, eventArgs) =>
        {
            writer.Write(eventArgs.Buffer, 0, eventArgs.BytesRecorded);
            writer.Flush();
            pcmDataBytes += eventArgs.BytesRecorded;

            var now = DateTimeOffset.UtcNow;
            if (ContainsSpeech(eventArgs.Buffer, eventArgs.BytesRecorded))
            {
                firstSpeechAt ??= now;
                lastSpeechAt = now;
                silenceTimer.Change(Timeout.Infinite, Timeout.Infinite);
                return;
            }

            if (firstSpeechAt is null || lastSpeechAt is null)
            {
                return;
            }

            var speechStopAt = Max(
                lastSpeechAt.Value.AddMilliseconds(SilenceGraceMs),
                firstSpeechAt.Value.AddMilliseconds(MinSpeechCaptureMs));
            var delay = speechStopAt - now;
            silenceTimer.Change(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
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

                var finalEndedAt = endedAt ?? DateTimeOffset.UtcNow;
                var wallClockDurationMs = (finalEndedAt - startedAt).TotalMilliseconds;
                var waveDurationMs = waveIn.WaveFormat.AverageBytesPerSecond == 0
                    ? 0
                    : (double)pcmDataBytes / waveIn.WaveFormat.AverageBytesPerSecond * 1000;

                completion.TrySetResult(new RecordingResult(
                    tempPath,
                    startedAt,
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

        void RequestStop()
        {
            if (Interlocked.Exchange(ref stopRequested, 1) != 0)
            {
                return;
            }

            endedAt = DateTimeOffset.UtcNow;
            waveIn.StopRecording();
        }
    }

    private static bool ContainsSpeech(byte[] buffer, int bytesRecorded)
    {
        if (bytesRecorded < 2)
        {
            return false;
        }

        var peak = 0;
        for (var i = 0; i + 1 < bytesRecorded; i += 2)
        {
            var sample = BitConverter.ToInt16(buffer, i);
            peak = Math.Max(peak, Math.Abs(sample));
        }

        return peak / 32767d >= SilenceThreshold;
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right)
        => left >= right ? left : right;
}

internal static class AudioPlayback
{
    private const float QuietBeepAmplitude = 0.08f;

    private static readonly Lazy<Task<string>> StartCuePath = new(() => EnsureCueFileAsync(
        "herface-recording-start.wav",
        BuildTonePcmBuffer(880, 100)));

    private static readonly Lazy<Task<string>> StopCuePath = new(() => EnsureCueFileAsync(
        "herface-recording-stop.wav",
        [.. BuildTonePcmBuffer(660, 80), .. BuildSilencePcmBuffer(70), .. BuildTonePcmBuffer(880, 100)]));

    public static async Task PlayRecordingStartCueAsync()
        => await PlayWavFileAsync(await StartCuePath.Value);

    public static async Task PlayRecordingStopCueAsync()
        => await PlayWavFileAsync(await StopCuePath.Value);

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

    private static byte[] BuildTonePcmBuffer(int frequencyHz, int durationMs)
    {
        var sampleCount = Math.Max(1, AudioRecorder.SampleRate * durationMs / 1000);
        var bytes = new byte[sampleCount * 2];
        var amplitude = (short)(short.MaxValue * QuietBeepAmplitude);

        for (var index = 0; index < sampleCount; index += 1)
        {
            var angle = 2 * Math.PI * frequencyHz * index / AudioRecorder.SampleRate;
            var sample = (short)Math.Round(Math.Sin(angle) * amplitude);
            BitConverter.GetBytes(sample).CopyTo(bytes, index * 2);
        }

        return bytes;
    }

    private static byte[] BuildSilencePcmBuffer(int durationMs)
    {
        var sampleCount = Math.Max(1, AudioRecorder.SampleRate * durationMs / 1000);
        return new byte[sampleCount * 2];
    }
}
