using Xunit;

namespace HeronWin.HerFace.Tests;

public sealed class AudioRecorderTests
{
    [Fact]
    public void FilterSpeakerLeak_RemovesStrongSpeakerBleed_WithLagSearch()
    {
        var renderChunk = BuildPseudoSpeech(sampleCount: 1600, seed: 11, amplitude: 0.65);
        var microphoneChunk = Scale(renderChunk, 0.28);
        var lagSamples = AudioRecorder.SampleRate * 120 / 1000;
        var renderHistory = BuildRenderHistory(renderChunk, lagSamples, leadInSeed: 31);

        var analysis = AudioRecorder.FilterSpeakerLeak(microphoneChunk, renderHistory);

        Assert.True(analysis.CanUseFilteredAudio);
        Assert.True(analysis.IsLikelySpeakerOnly);
        Assert.NotNull(analysis.FilteredSamples);
        Assert.Equal(lagSamples, analysis.BestLagSamples);
        Assert.InRange(analysis.ExplainedRatio, 0.98, 1.001);
        Assert.True(analysis.FilteredRms < analysis.OriginalRms * 0.1);
    }

    [Fact]
    public void FilterSpeakerLeak_DoesNotSuppress_UnrelatedUserSpeech()
    {
        var renderChunk = BuildPseudoSpeech(sampleCount: 1600, seed: 17, amplitude: 0.65);
        var microphoneChunk = BuildPseudoSpeech(sampleCount: 1600, seed: 29, amplitude: 0.45);
        var renderHistory = BuildRenderHistory(renderChunk, lagSamples: 0, leadInSeed: 47);

        var analysis = AudioRecorder.FilterSpeakerLeak(microphoneChunk, renderHistory);

        Assert.False(analysis.CanUseFilteredAudio);
        Assert.False(analysis.IsLikelySpeakerOnly);
        Assert.Null(analysis.FilteredSamples);
        Assert.InRange(analysis.OriginalRms, 0.08, 0.5);
        Assert.True(analysis.ExplainedRatio < 0.35);
    }

    [Fact]
    public void FilterSpeakerLeak_PreservesUserSpeech_WhenSpeakerAndUserOverlap()
    {
        var renderChunk = BuildPseudoSpeech(sampleCount: 1600, seed: 7, amplitude: 0.65);
        var userChunk = BuildPseudoSpeech(sampleCount: 1600, seed: 53, amplitude: 0.30);
        var microphoneChunk = Mix(Scale(renderChunk, 0.38), userChunk);
        var lagSamples = AudioRecorder.SampleRate * 60 / 1000;
        var renderHistory = BuildRenderHistory(renderChunk, lagSamples, leadInSeed: 71);

        var analysis = AudioRecorder.FilterSpeakerLeak(microphoneChunk, renderHistory);

        Assert.True(analysis.CanUseFilteredAudio);
        Assert.False(analysis.IsLikelySpeakerOnly);
        Assert.NotNull(analysis.FilteredSamples);
        Assert.Equal(lagSamples, analysis.BestLagSamples);
        Assert.True(analysis.FilteredRms < analysis.OriginalRms);

        var originalDistance = ComputeDifferenceRms(microphoneChunk, userChunk);
        var filteredDistance = ComputeDifferenceRms(analysis.FilteredSamples!, userChunk);
        Assert.True(filteredDistance < originalDistance * 0.55);
    }

    [Fact]
    public void FilterSpeakerLeak_MarksSpeakerOnlyAudio_EvenWithSimpleRoomEcho()
    {
        var renderChunk = BuildPseudoSpeech(sampleCount: 1600, seed: 83, amplitude: 0.65);
        var primaryPath = Scale(renderChunk, 0.22);
        var echoPath = DelayAndScale(renderChunk, delaySamples: 80, factor: 0.10);
        var microphoneChunk = Mix(primaryPath, echoPath);
        var lagSamples = AudioRecorder.SampleRate * 90 / 1000;
        var renderHistory = BuildRenderHistory(renderChunk, lagSamples, leadInSeed: 97);

        var analysis = AudioRecorder.FilterSpeakerLeak(microphoneChunk, renderHistory);

        Assert.True(analysis.CanUseFilteredAudio);
        Assert.True(analysis.IsLikelySpeakerOnly);
        Assert.NotNull(analysis.FilteredSamples);
        Assert.InRange(analysis.ResidualEnergyRatio, 0.01, 0.45);
    }

    [Fact]
    public void ShouldTreatChunkAsSpeech_ReturnsFalse_ForSpeakerDominantResidualAudio()
    {
        var renderChunk = BuildPseudoSpeech(sampleCount: 1600, seed: 101, amplitude: 0.65);
        var primaryPath = Scale(renderChunk, 0.23);
        var echoPath = DelayAndScale(renderChunk, delaySamples: 70, factor: 0.12);
        var lateEchoPath = DelayAndScale(renderChunk, delaySamples: 155, factor: 0.07);
        var microphoneChunk = Mix(primaryPath, Mix(echoPath, lateEchoPath));
        var lagSamples = AudioRecorder.SampleRate * 95 / 1000;
        var renderHistory = BuildRenderHistory(renderChunk, lagSamples, leadInSeed: 131);

        var analysis = AudioRecorder.FilterSpeakerLeak(microphoneChunk, renderHistory);
        var (peak, rms) = ComputeLevels(analysis.FilteredSamples!);

        Assert.True(analysis.CanUseFilteredAudio);
        Assert.True(analysis.IsLikelySpeakerDominant);
        Assert.False(AudioRecorder.ShouldTreatChunkAsSpeech(peak, rms, analysis));
    }

    [Fact]
    public void ShouldTreatChunkAsSpeech_ReturnsTrue_ForUserOverlapAfterFiltering()
    {
        var renderChunk = BuildPseudoSpeech(sampleCount: 1600, seed: 7, amplitude: 0.65);
        var userChunk = BuildPseudoSpeech(sampleCount: 1600, seed: 53, amplitude: 0.30);
        var microphoneChunk = Mix(Scale(renderChunk, 0.38), userChunk);
        var lagSamples = AudioRecorder.SampleRate * 60 / 1000;
        var renderHistory = BuildRenderHistory(renderChunk, lagSamples, leadInSeed: 71);

        var analysis = AudioRecorder.FilterSpeakerLeak(microphoneChunk, renderHistory);
        var effectiveChunk = analysis.FilteredSamples ?? microphoneChunk;
        var (peak, rms) = ComputeLevels(effectiveChunk);

        Assert.True(analysis.CanUseFilteredAudio);
        Assert.False(analysis.IsLikelySpeakerOnly);
        Assert.True(AudioRecorder.ShouldTreatChunkAsSpeech(peak, rms, analysis));
    }

    private static short[] BuildPseudoSpeech(int sampleCount, int seed, double amplitude)
    {
        var random = new Random(seed);
        var samples = new short[sampleCount];
        double state = 0;

        for (var index = 0; index < sampleCount; index += 1)
        {
            state = state * 0.86 + ((random.NextDouble() * 2) - 1) * 0.22;
            var normalized = Math.Clamp(state * amplitude, -0.95, 0.95);
            samples[index] = ToPcm16(normalized);
        }

        return samples;
    }

    private static float[] BuildRenderHistory(short[] latestChunk, int lagSamples, int leadInSeed)
    {
        var leadIn = BuildPseudoSpeech(sampleCount: 800, seed: leadInSeed, amplitude: 0.25);
        var history = new float[leadIn.Length + latestChunk.Length + lagSamples];
        CopyNormalized(leadIn, history, 0);
        CopyNormalized(latestChunk, history, leadIn.Length);
        return history;
    }

    private static short[] Scale(short[] samples, double factor)
    {
        var scaled = new short[samples.Length];
        for (var index = 0; index < samples.Length; index += 1)
        {
            scaled[index] = ToPcm16(samples[index] / 32768d * factor);
        }

        return scaled;
    }

    private static short[] Mix(short[] left, short[] right)
    {
        var length = Math.Min(left.Length, right.Length);
        var mixed = new short[length];
        for (var index = 0; index < length; index += 1)
        {
            var normalized = left[index] / 32768d + right[index] / 32768d;
            mixed[index] = ToPcm16(normalized);
        }

        return mixed;
    }

    private static short[] DelayAndScale(short[] samples, int delaySamples, double factor)
    {
        var delayed = new short[samples.Length];
        for (var index = delaySamples; index < samples.Length; index += 1)
        {
            delayed[index] = ToPcm16(samples[index - delaySamples] / 32768d * factor);
        }

        return delayed;
    }

    private static double ComputeDifferenceRms(short[] left, short[] right)
    {
        var length = Math.Min(left.Length, right.Length);
        if (length == 0)
        {
            return 0;
        }

        double energy = 0;
        for (var index = 0; index < length; index += 1)
        {
            var difference = left[index] / 32768d - right[index] / 32768d;
            energy += difference * difference;
        }

        return Math.Sqrt(energy / length);
    }

    private static (double Peak, double Rms) ComputeLevels(short[] samples)
    {
        if (samples.Length == 0)
        {
            return (0, 0);
        }

        var peak = 0d;
        double energy = 0;
        foreach (var sample in samples)
        {
            var normalized = sample / 32768d;
            peak = Math.Max(peak, Math.Abs(normalized));
            energy += normalized * normalized;
        }

        return (peak, Math.Sqrt(energy / samples.Length));
    }

    private static void CopyNormalized(short[] source, float[] destination, int destinationOffset)
    {
        for (var index = 0; index < source.Length; index += 1)
        {
            destination[destinationOffset + index] = source[index] / 32768f;
        }
    }

    private static short ToPcm16(double normalizedSample)
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
}
