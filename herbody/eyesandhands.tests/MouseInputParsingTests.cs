using Xunit;

namespace HeronWin.HerBody.EyesAndHands.Tests;

public sealed class MouseInputParsingTests
{
    [Theory]
    [InlineData("left", "left")]
    [InlineData("Left", "left")]
    [InlineData("primary", "left")]
    [InlineData("right", "right")]
    [InlineData("Right", "right")]
    [InlineData("secondary", "right")]
    public void NormalizeMouseButton_CanonicalizesSupportedButtons(string mouseButton, string expected)
    {
        var actual = WindowAutomation.NormalizeMouseButton(mouseButton);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void NormalizeMouseButton_RejectsUnsupportedButtons()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => WindowAutomation.NormalizeMouseButton("middle"));

        Assert.Contains("Use left or right", exception.Message);
    }
}
