using Xunit;

namespace HeronWin.Brain.Tests;

public sealed class SpeechGateTests
{
    [Fact]
    public void ContainsWakeWord_ReturnsTrue_ForExactWakePhrase()
    {
        var actual = SpeechGate.ContainsWakeWord("Hello there", "Hello there");

        Assert.True(actual);
    }

    [Fact]
    public void ContainsWakeWord_ReturnsTrue_ForWakePhraseInsideLongerTranscript()
    {
        var actual = SpeechGate.ContainsWakeWord("Hello there can you open Netflix", "Hello there");

        Assert.True(actual);
    }

    [Fact]
    public void ContainsWakeWord_ReturnsTrue_ForCloseWhisperVariant()
    {
        var actual = SpeechGate.ContainsWakeWord("Hello, beer.", "Hello there");

        Assert.True(actual);
    }

    [Fact]
    public void ContainsWakeWord_ReturnsFalse_ForUnrelatedTranscript()
    {
        var actual = SpeechGate.ContainsWakeWord("Let's go to Netflix website.", "Hello there");

        Assert.False(actual);
    }

    [Fact]
    public void ShouldExitApp_ReturnsTrue_ForByeByeVariant()
    {
        var actual = SpeechGate.ShouldExitApp("bye-bye");

        Assert.True(actual);
    }

    [Fact]
    public void ShouldExitApp_ReturnsFalse_ForNonExitPhrase()
    {
        var actual = SpeechGate.ShouldExitApp("goodbye for now");

        Assert.False(actual);
    }
}
