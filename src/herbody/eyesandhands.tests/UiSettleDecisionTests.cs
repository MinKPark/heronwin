using System.Windows.Automation;
using Xunit;

namespace HeronWin.HerBody.EyesAndHands.Tests;

public sealed class UiSettleDecisionTests
{
    [Fact]
    public void IsUiChangeSettled_ReturnsTrue_WhenWindowIsGone()
    {
        var now = DateTime.UtcNow;

        var settled = WindowAutomation.IsUiChangeSettled(
            windowAvailable: false,
            interactionState: WindowInteractionState.Running,
            utcNow: now,
            lastObservedChangeUtc: now,
            quietPeriod: TimeSpan.FromMilliseconds(200));

        Assert.True(settled);
    }

    [Fact]
    public void IsUiChangeSettled_ReturnsTrue_WhenWindowIsReady()
    {
        var now = DateTime.UtcNow;

        var settled = WindowAutomation.IsUiChangeSettled(
            windowAvailable: true,
            interactionState: WindowInteractionState.ReadyForUserInteraction,
            utcNow: now,
            lastObservedChangeUtc: now - TimeSpan.FromMilliseconds(250),
            quietPeriod: TimeSpan.FromMilliseconds(200));

        Assert.True(settled);
    }

    [Fact]
    public void IsUiChangeSettled_ReturnsFalse_WhenWindowIsStillRunning()
    {
        var now = DateTime.UtcNow;

        var settled = WindowAutomation.IsUiChangeSettled(
            windowAvailable: true,
            interactionState: WindowInteractionState.Running,
            utcNow: now,
            lastObservedChangeUtc: null,
            quietPeriod: TimeSpan.FromMilliseconds(200));

        Assert.False(settled);
    }

    [Fact]
    public void IsUiChangeSettled_ReturnsFalse_WhenStateIsIndefinite()
    {
        var now = DateTime.UtcNow;

        var settled = WindowAutomation.IsUiChangeSettled(
            windowAvailable: true,
            interactionState: null,
            utcNow: now,
            lastObservedChangeUtc: now - TimeSpan.FromMilliseconds(100),
            quietPeriod: TimeSpan.FromMilliseconds(200));

        Assert.False(settled);
    }

    [Fact]
    public void IsUiChangeSettled_ReturnsTrue_WhenStateIsIndefiniteButQuietPeriodElapsed()
    {
        var now = DateTime.UtcNow;

        var settled = WindowAutomation.IsUiChangeSettled(
            windowAvailable: true,
            interactionState: null,
            utcNow: now,
            lastObservedChangeUtc: now - TimeSpan.FromMilliseconds(250),
            quietPeriod: TimeSpan.FromMilliseconds(200));

        Assert.True(settled);
    }

    [Fact]
    public void IsUiChangeSettled_ReturnsTrue_WhenStateIsIndefiniteAndNoChangesWereObserved()
    {
        var now = DateTime.UtcNow;

        var settled = WindowAutomation.IsUiChangeSettled(
            windowAvailable: true,
            interactionState: null,
            utcNow: now,
            lastObservedChangeUtc: null,
            quietPeriod: TimeSpan.FromMilliseconds(200));

        Assert.True(settled);
    }

    [Fact]
    public void IsUiChangeSettled_TreatsBlockedByModalWindowAsSettled()
    {
        var now = DateTime.UtcNow;

        var settled = WindowAutomation.IsUiChangeSettled(
            windowAvailable: true,
            interactionState: WindowInteractionState.BlockedByModalWindow,
            utcNow: now,
            lastObservedChangeUtc: now - TimeSpan.FromMilliseconds(250),
            quietPeriod: TimeSpan.FromMilliseconds(200));

        Assert.True(settled);
    }
}
