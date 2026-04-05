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
    private const int SpeakerReferenceHistoryMs = 3_000;
    private const int SpeakerLagSearchMs = 750;
    private const int SpeakerLagSearchStepMs = 5;
    private const int SpeakerReferenceHistorySamples = SampleRate * SpeakerReferenceHistoryMs / 1000;
    private const int SpeakerLagSearchSamples = SampleRate * SpeakerLagSearchMs / 1000;
    private const int SpeakerLagSearchStepSamples = SampleRate * SpeakerLagSearchStepMs / 1000;
    private const double SpeakerReferenceRmsThreshold = 0.0035;
    private const double SpeakerLeakCandidateExplainedRatioThreshold = 0.12;
    private const double SpeakerLeakDominantExplainedRatioThreshold = 0.24;
    private const double SpeakerLeakResidualEnergyRatioThreshold = 0.97;
    private const double SpeakerLeakDominantResidualEnergyRatioThreshold = 0.65;
    private const double SpeakerLeakDominantResidualRmsThreshold = 0.02;
    private const double SpeakerLeakScaleLimit = 4.0;

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
        var speakerReference = SpeakerReferenceCapture.TryStart();

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
        var speakerFilteredBufferCount = 0;
        var speakerDominantBufferCount = 0;
        var maxSpeakerExplainedRatio = 0d;

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
            var levels = AnalyzeAudioLevels(eventArgs.Buffer, eventArgs.BytesRecorded);
            var chunk = BufferedAudioChunk.Create(eventArgs.Buffer, eventArgs.BytesRecorded, chunkStartedAt, chunkEndedAt);
            var speakerLeakAnalysis = FilterSpeakerLeak(eventArgs.Buffer, eventArgs.BytesRecorded, speakerReference);
            if (speakerLeakAnalysis.ExplainedRatio > maxSpeakerExplainedRatio)
            {
                maxSpeakerExplainedRatio = speakerLeakAnalysis.ExplainedRatio;
            }

            if (speakerLeakAnalysis.CanUseFilteredAudio && speakerLeakAnalysis.FilteredSamples is not null)
            {
                var filteredBytes = EncodePcm16Samples(speakerLeakAnalysis.FilteredSamples);
                chunk = new BufferedAudioChunk(filteredBytes, filteredBytes.Length, chunkStartedAt, chunkEndedAt);
                levels = AnalyzeAudioLevels(filteredBytes, filteredBytes.Length);
                speakerFilteredBufferCount += 1;
            }

            var looksLikeSpeech = levels.Peak >= SpeechPeakThreshold || levels.Rms >= SpeechRmsThreshold;
            if (speakerLeakAnalysis.IsLikelySpeakerOnly)
            {
                looksLikeSpeech = false;
                speakerDominantBufferCount += 1;
            }

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
                speakerReference?.Dispose();
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

                if (speakerFilteredBufferCount > 0)
                {
                    DebugTrace.WriteEvent(
                        "audio.speaker_filter",
                        $"filteredBuffers={speakerFilteredBufferCount}, speakerOnlyBuffers={speakerDominantBufferCount}, maxExplainedRatio={maxSpeakerExplainedRatio:F2}, speechStarted={speechStarted}");
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

        try
        {
            waveIn.StartRecording();
        }
        catch
        {
            speakerReference?.Dispose();
            writer.Dispose();
            waveIn.Dispose();
            throw;
        }

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

    internal static SpeakerLeakAnalysis FilterSpeakerLeak(short[] microphoneSamples, float[] renderReferenceHistory)
    {
        if (microphoneSamples.Length == 0)
        {
            return SpeakerLeakAnalysis.None();
        }

        var microphoneNormalized = new float[microphoneSamples.Length];
        double micEnergy = 0;
        for (var i = 0; i < microphoneSamples.Length; i += 1)
        {
            var normalized = microphoneSamples[i] / 32768f;
            microphoneNormalized[i] = normalized;
            micEnergy += normalized * normalized;
        }

        if (micEnergy <= 0 || renderReferenceHistory.Length < microphoneSamples.Length)
        {
            return SpeakerLeakAnalysis.None(Math.Sqrt(micEnergy / Math.Max(1, microphoneSamples.Length)));
        }

        var minimumReferenceEnergy = microphoneSamples.Length * SpeakerReferenceRmsThreshold * SpeakerReferenceRmsThreshold;
        var maxLagSamples = Math.Min(
            SpeakerLagSearchSamples,
            renderReferenceHistory.Length - microphoneSamples.Length);

        var bestExplainedRatio = 0d;
        var bestDot = 0d;
        var bestReferenceEnergy = 0d;
        var bestLagSamples = -1;
        var bestStart = -1;

        EvaluateLag(0);
        for (var lag = SpeakerLagSearchStepSamples; lag <= maxLagSamples; lag += SpeakerLagSearchStepSamples)
        {
            EvaluateLag(lag);
        }

        if (bestLagSamples != maxLagSamples)
        {
            EvaluateLag(maxLagSamples);
        }

        if (bestStart < 0 || bestReferenceEnergy <= minimumReferenceEnergy)
        {
            return SpeakerLeakAnalysis.None(Math.Sqrt(micEnergy / microphoneSamples.Length));
        }

        var scale = Math.Clamp(bestDot / bestReferenceEnergy, -SpeakerLeakScaleLimit, SpeakerLeakScaleLimit);
        var filteredSamples = new short[microphoneSamples.Length];
        double filteredEnergy = 0;
        for (var i = 0; i < microphoneSamples.Length; i += 1)
        {
            var residual = microphoneNormalized[i] - scale * renderReferenceHistory[bestStart + i];
            filteredEnergy += residual * residual;
            filteredSamples[i] = ConvertToPcm16(residual);
        }

        var originalRms = Math.Sqrt(micEnergy / microphoneSamples.Length);
        var filteredRms = Math.Sqrt(filteredEnergy / microphoneSamples.Length);
        var referenceRms = Math.Sqrt(bestReferenceEnergy / microphoneSamples.Length);
        var residualEnergyRatio = micEnergy <= 0 ? 1d : filteredEnergy / micEnergy;
        var correlation = bestDot / Math.Sqrt(micEnergy * bestReferenceEnergy);
        var canUseFilteredAudio =
            bestExplainedRatio >= SpeakerLeakCandidateExplainedRatioThreshold
            && residualEnergyRatio <= SpeakerLeakResidualEnergyRatioThreshold;
        var isLikelySpeakerOnly =
            canUseFilteredAudio
            && bestExplainedRatio >= SpeakerLeakDominantExplainedRatioThreshold
            && residualEnergyRatio <= SpeakerLeakDominantResidualEnergyRatioThreshold
            && filteredRms <= SpeakerLeakDominantResidualRmsThreshold;

        return new SpeakerLeakAnalysis(
            canUseFilteredAudio,
            isLikelySpeakerOnly,
            canUseFilteredAudio ? filteredSamples : null,
            originalRms,
            filteredRms,
            referenceRms,
            correlation,
            bestExplainedRatio,
            bestLagSamples,
            scale,
            residualEnergyRatio);

        void EvaluateLag(int lagSamples)
        {
            if (lagSamples < 0)
            {
                return;
            }

            var start = renderReferenceHistory.Length - microphoneSamples.Length - lagSamples;
            if (start < 0)
            {
                return;
            }

            double dot = 0;
            double referenceEnergy = 0;
            for (var i = 0; i < microphoneSamples.Length; i += 1)
            {
                var referenceSample = renderReferenceHistory[start + i];
                dot += microphoneNormalized[i] * referenceSample;
                referenceEnergy += referenceSample * referenceSample;
            }

            if (referenceEnergy <= minimumReferenceEnergy)
            {
                return;
            }

            var explainedRatio = dot * dot / (micEnergy * referenceEnergy);
            if (explainedRatio <= bestExplainedRatio)
            {
                return;
            }

            bestExplainedRatio = explainedRatio;
            bestDot = dot;
            bestReferenceEnergy = referenceEnergy;
            bestLagSamples = lagSamples;
            bestStart = start;
        }
    }

    private static SpeakerLeakAnalysis FilterSpeakerLeak(
        byte[] microphoneBuffer,
        int bytesRecorded,
        SpeakerReferenceCapture? speakerReference)
    {
        if (speakerReference is null || bytesRecorded < 2)
        {
            return SpeakerLeakAnalysis.None();
        }

        var microphoneSamples = DecodePcm16Samples(microphoneBuffer, bytesRecorded);
        if (microphoneSamples.Length == 0)
        {
            return SpeakerLeakAnalysis.None();
        }

        var renderHistory = speakerReference.CopyRecentSamples(microphoneSamples.Length + SpeakerLagSearchSamples);
        return FilterSpeakerLeak(microphoneSamples, renderHistory);
    }

    private static short[] DecodePcm16Samples(byte[] buffer, int bytesRecorded)
    {
        if (bytesRecorded < 2)
        {
            return [];
        }

        var sampleCount = bytesRecorded / 2;
        var samples = new short[sampleCount];
        Buffer.BlockCopy(buffer, 0, samples, 0, sampleCount * 2);
        return samples;
    }

    private static byte[] EncodePcm16Samples(short[] samples)
    {
        var buffer = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, buffer, 0, buffer.Length);
        return buffer;
    }

    private static short ConvertToPcm16(double normalizedSample)
    {
        var clamped = Math.Clamp(normalizedSample, -1d, 1d);
        if (clamped <= -1d)
        {
            return short.MinValue;
        }

        if (clamped >= 1d)
        {
            return short.MaxValue;
        }

        return (short)Math.Round(clamped * short.MaxValue);
    }

    private static bool IsFloatFormat(WaveFormat waveFormat)
    {
        if (waveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            return true;
        }

        if (waveFormat.Encoding != WaveFormatEncoding.Extensible)
        {
            return false;
        }

        var subFormat = waveFormat.GetType().GetProperty("SubFormat")?.GetValue(waveFormat);
        return subFormat is Guid guid && guid == new Guid("00000003-0000-0010-8000-00aa00389b71");
    }

    private static float[] DecodeRenderSamples(byte[] buffer, int bytesRecorded, WaveFormat waveFormat)
    {
        if (bytesRecorded <= 0 || waveFormat.BlockAlign <= 0)
        {
            return [];
        }

        var channelCount = Math.Max(1, waveFormat.Channels);
        var bytesPerSample = Math.Max(1, waveFormat.BlockAlign / channelCount);
        var frameCount = bytesRecorded / waveFormat.BlockAlign;
        if (frameCount <= 0)
        {
            return [];
        }

        var isFloat = IsFloatFormat(waveFormat);
        var monoSamples = new float[frameCount];
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex += 1)
        {
            var frameOffset = frameIndex * waveFormat.BlockAlign;
            double sampleSum = 0;
            for (var channelIndex = 0; channelIndex < channelCount; channelIndex += 1)
            {
                var sampleOffset = frameOffset + channelIndex * bytesPerSample;
                sampleSum += ReadNormalizedSample(buffer, sampleOffset, waveFormat.BitsPerSample, bytesPerSample, isFloat);
            }

            monoSamples[frameIndex] = (float)(sampleSum / channelCount);
        }

        if (waveFormat.SampleRate == SampleRate)
        {
            return monoSamples;
        }

        var resampledCount = Math.Max(1, (int)Math.Round((double)monoSamples.Length * SampleRate / waveFormat.SampleRate));
        var resampled = new float[resampledCount];
        if (monoSamples.Length == 1)
        {
            Array.Fill(resampled, monoSamples[0]);
            return resampled;
        }

        for (var index = 0; index < resampledCount; index += 1)
        {
            var sourceIndex = resampledCount == 1
                ? 0
                : (double)index * (monoSamples.Length - 1) / (resampledCount - 1);
            var leftIndex = (int)sourceIndex;
            var rightIndex = Math.Min(leftIndex + 1, monoSamples.Length - 1);
            var blend = sourceIndex - leftIndex;
            resampled[index] = (float)(monoSamples[leftIndex] + (monoSamples[rightIndex] - monoSamples[leftIndex]) * blend);
        }

        return resampled;
    }

    private static double ReadNormalizedSample(
        byte[] buffer,
        int offset,
        int bitsPerSample,
        int bytesPerSample,
        bool isFloat)
    {
        if (isFloat)
        {
            return bitsPerSample switch
            {
                32 when offset + 4 <= buffer.Length => Math.Clamp(BitConverter.ToSingle(buffer, offset), -1f, 1f),
                64 when offset + 8 <= buffer.Length => Math.Clamp(BitConverter.ToDouble(buffer, offset), -1d, 1d),
                _ => 0d,
            };
        }

        return bitsPerSample switch
        {
            8 when offset + 1 <= buffer.Length => (buffer[offset] - 128) / 128d,
            16 when offset + 2 <= buffer.Length => BitConverter.ToInt16(buffer, offset) / 32768d,
            24 when offset + 3 <= buffer.Length => ReadInt24(buffer, offset) / 8388608d,
            32 when offset + 4 <= buffer.Length && bytesPerSample >= 4 => BitConverter.ToInt32(buffer, offset) / 2147483648d,
            _ => 0d,
        };
    }

    private static int ReadInt24(byte[] buffer, int offset)
    {
        var value = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
        if ((value & 0x800000) != 0)
        {
            value |= unchecked((int)0xFF000000);
        }

        return value;
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
    internal readonly record struct SpeakerLeakAnalysis(
        bool CanUseFilteredAudio,
        bool IsLikelySpeakerOnly,
        short[]? FilteredSamples,
        double OriginalRms,
        double FilteredRms,
        double ReferenceRms,
        double Correlation,
        double ExplainedRatio,
        int BestLagSamples,
        double Scale,
        double ResidualEnergyRatio)
    {
        public static SpeakerLeakAnalysis None(double originalRms = 0)
            => new(false, false, null, originalRms, originalRms, 0, 0, 0, -1, 0, 1);
    }

    private readonly record struct BufferedAudioChunk(byte[] Bytes, int BytesRecorded, DateTimeOffset StartedAt, DateTimeOffset EndedAt)
    {
        public static BufferedAudioChunk Create(byte[] sourceBuffer, int bytesRecorded, DateTimeOffset startedAt, DateTimeOffset endedAt)
        {
            var copy = new byte[bytesRecorded];
            Buffer.BlockCopy(sourceBuffer, 0, copy, 0, bytesRecorded);
            return new BufferedAudioChunk(copy, bytesRecorded, startedAt, endedAt);
        }
    }

    private sealed class SpeakerReferenceCapture : IDisposable
    {
        private readonly object _syncRoot = new();
        private readonly WasapiLoopbackCapture _capture;
        private readonly float[] _history = new float[SpeakerReferenceHistorySamples];
        private int _nextWriteIndex;
        private int _storedSamples;
        private int _disposed;

        private SpeakerReferenceCapture()
        {
            _capture = new WasapiLoopbackCapture();
            try
            {
                _capture.DataAvailable += OnDataAvailable;
                _capture.StartRecording();
            }
            catch
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.Dispose();
                throw;
            }
        }

        public static SpeakerReferenceCapture? TryStart()
        {
            try
            {
                return new SpeakerReferenceCapture();
            }
            catch (Exception ex)
            {
                DebugTrace.WriteEvent(
                    "audio.speaker_reference",
                    $"status=unavailable, error={DebugTrace.Preview(ex.Message, 300)}");
                return null;
            }
        }

        public float[] CopyRecentSamples(int sampleCount)
        {
            lock (_syncRoot)
            {
                var copyCount = Math.Min(sampleCount, _storedSamples);
                if (copyCount <= 0)
                {
                    return [];
                }

                var snapshot = new float[copyCount];
                var start = (_nextWriteIndex - copyCount + _history.Length) % _history.Length;
                var firstCopyCount = Math.Min(copyCount, _history.Length - start);
                Array.Copy(_history, start, snapshot, 0, firstCopyCount);
                if (firstCopyCount < copyCount)
                {
                    Array.Copy(_history, 0, snapshot, firstCopyCount, copyCount - firstCopyCount);
                }

                return snapshot;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _capture.DataAvailable -= OnDataAvailable;
            try
            {
                _capture.StopRecording();
            }
            catch
            {
                // Best effort cleanup.
            }

            _capture.Dispose();
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            var samples = DecodeRenderSamples(eventArgs.Buffer, eventArgs.BytesRecorded, _capture.WaveFormat);
            if (samples.Length == 0)
            {
                return;
            }

            lock (_syncRoot)
            {
                for (var index = 0; index < samples.Length; index += 1)
                {
                    _history[_nextWriteIndex] = samples[index];
                    _nextWriteIndex = (_nextWriteIndex + 1) % _history.Length;
                    if (_storedSamples < _history.Length)
                    {
                        _storedSamples += 1;
                    }
                }
            }
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
