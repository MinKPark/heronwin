using Xunit;

namespace HeronWin.HerBody.EyesAndHands.Tests;

public sealed class ClickTargetHeuristicsTests
{
    [Fact]
    public void TryResolveVisibleClickArea_ClipsElementBoundsToWindow()
    {
        var elementBounds = new ElementBounds(0, 40, 1901, 3565);
        var windowBounds = new WindowBounds(0, 0, 1920, 1048);

        var resolved = WindowAutomation.TryResolveVisibleClickArea(
            elementBounds,
            windowBounds,
            out var visibleBounds);

        Assert.True(resolved);
        Assert.Equal(new ElementBounds(0, 40, 1901, 1008), visibleBounds);
    }

    [Fact]
    public void IsLikelyImpreciseContainerClickTarget_ReturnsTrue_ForLargeUnnamedGroup()
    {
        var result = WindowAutomation.IsLikelyImpreciseContainerClickTarget(
            "Group",
            "",
            "",
            isKeyboardFocusable: false,
            new ElementBounds(-1920, 40, 1901, 3565),
            new ElementBounds(-1920, 40, 1901, 1000),
            new WindowBounds(-1928, -8, 1936, 1048));

        Assert.True(result);
    }

    [Fact]
    public void IsLikelyImpreciseContainerClickTarget_ReturnsFalse_ForNamedButton()
    {
        var result = WindowAutomation.IsLikelyImpreciseContainerClickTarget(
            "Button",
            "Play",
            "play-button",
            isKeyboardFocusable: true,
            new ElementBounds(120, 280, 160, 48),
            new ElementBounds(120, 280, 160, 48),
            new WindowBounds(0, 0, 1920, 1048));

        Assert.False(result);
    }
}
