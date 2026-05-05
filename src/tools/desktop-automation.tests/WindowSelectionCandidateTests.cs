using Xunit;

namespace HeronWin.Body.DesktopAutomation.Tests;

public sealed class WindowSelectionCandidateTests
{
    [Fact]
    public void IsInteractiveSelectionTarget_ReturnsTrue_ForVisibleTitledAppWindow()
    {
        var result = WindowAutomation.IsInteractiveSelectionTarget(
            title: "Untitled - Notepad",
            className: "Notepad",
            isVisible: true,
            isExcludedHandle: false);

        Assert.True(result);
    }

    [Fact]
    public void IsInteractiveSelectionTarget_ReturnsFalse_ForSearchSurface()
    {
        var result = WindowAutomation.IsInteractiveSelectionTarget(
            title: "Windows Input Experience",
            className: "Windows.UI.Core.CoreWindow",
            isVisible: true,
            isExcludedHandle: false);

        Assert.False(result);
    }

    [Fact]
    public void IsInteractiveSelectionTarget_ReturnsFalse_ForShellWindow()
    {
        var result = WindowAutomation.IsInteractiveSelectionTarget(
            title: "",
            className: "Shell_TrayWnd",
            isVisible: true,
            isExcludedHandle: false);

        Assert.False(result);
    }

    [Fact]
    public void GetInteractiveSelectionTargetRejectionReason_ReturnsExcludedHandle()
    {
        var reason = WindowAutomation.GetInteractiveSelectionTargetRejectionReason(
            title: "Netflix",
            className: "Chrome_WidgetWin_1",
            isVisible: true,
            isExcludedHandle: true);

        Assert.Equal("excluded_handle", reason);
    }

    [Fact]
    public void GetInteractiveSelectionTargetRejectionReason_ReturnsNull_ForValidInteractiveWindow()
    {
        var reason = WindowAutomation.GetInteractiveSelectionTargetRejectionReason(
            title: "Netflix - Microsoft Edge",
            className: "Chrome_WidgetWin_1",
            isVisible: true,
            isExcludedHandle: false);

        Assert.Null(reason);
    }
}
