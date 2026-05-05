using Xunit;

namespace HeronWin.Tools.DesktopAutomation.Tests;

public sealed class KeyboardInvocationNavigationTests
{
    [Fact]
    public void BuildKeyboardInvocationKeyPreference_DefaultsToTabFirstFromRoot()
    {
        var actual = WindowAutomation.BuildKeyboardInvocationKeyPreference("root", "2/1");

        Assert.Equal(["Tab", "Right", "Down", "Left", "Up", "Shift+Tab"], actual);
    }

    [Fact]
    public void BuildKeyboardInvocationKeyPreference_PrefersForwardNavigationForLaterSibling()
    {
        var actual = WindowAutomation.BuildKeyboardInvocationKeyPreference("2/1", "2/4");

        Assert.Equal(["Right", "Down", "Tab", "Left", "Up", "Shift+Tab"], actual);
    }

    [Fact]
    public void BuildKeyboardInvocationKeyPreference_PrefersBackwardNavigationForEarlierSibling()
    {
        var actual = WindowAutomation.BuildKeyboardInvocationKeyPreference("2/4", "2/1");

        Assert.Equal(["Left", "Up", "Shift+Tab", "Right", "Down", "Tab"], actual);
    }

    [Fact]
    public void BuildKeyboardInvocationKeyPreference_PrefersDescendingIntoTargetSubtree()
    {
        var actual = WindowAutomation.BuildKeyboardInvocationKeyPreference("2", "2/1/3");

        Assert.Equal(["Right", "Down", "Tab", "Left", "Up", "Shift+Tab"], actual);
    }
}
