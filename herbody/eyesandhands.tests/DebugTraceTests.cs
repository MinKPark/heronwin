using Xunit;

namespace HeronWin.HerBody.EyesAndHands.Tests;

public sealed class DebugTraceTests
{
    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("yes")]
    [InlineData("on")]
    public void IsEnabledEnvironmentValue_ReturnsTrue_ForSupportedValues(string value)
    {
        Assert.True(DebugTrace.IsEnabledEnvironmentValue(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("off")]
    public void IsEnabledEnvironmentValue_ReturnsFalse_ForOtherValues(string? value)
    {
        Assert.False(DebugTrace.IsEnabledEnvironmentValue(value));
    }

    [Fact]
    public void ShouldEnable_ReturnsTrue_WhenDebugArgumentIsPresent()
    {
        var enabled = DebugTrace.ShouldEnable(["--debug"], environmentValue: null);

        Assert.True(enabled);
    }

    [Fact]
    public void FormatTimestampedLine_PrefixesTimestamp()
    {
        var formatted = DebugTrace.FormatTimestampedLine(
            "sample",
            new DateTimeOffset(2026, 3, 31, 9, 15, 30, 123, TimeSpan.FromHours(-7)));

        Assert.Equal("[2026-03-31 09:15:30.123 -07:00] sample", formatted);
    }
}
