using Xunit;

namespace HeronWin.Tools.DesktopAutomation.Tests;

public sealed class TaskbarActivationModeTests
{
    [Fact]
    public void ResolveTaskbarActivationMode_PrefersInvoke_WhenAvailable()
    {
        var mode = WindowAutomation.ResolveTaskbarActivationMode(
            canInvoke: true,
            canSelect: true,
            canToggle: true,
            isKeyboardFocusable: true);

        Assert.Equal(TaskbarActivationMode.Invoke, mode);
    }

    [Fact]
    public void ResolveTaskbarActivationMode_FallsBackToFocusAndPressEnter_ForKeyboardFocusableButtons()
    {
        var mode = WindowAutomation.ResolveTaskbarActivationMode(
            canInvoke: false,
            canSelect: false,
            canToggle: false,
            isKeyboardFocusable: true);

        Assert.Equal(TaskbarActivationMode.FocusAndPressEnter, mode);
    }

    [Fact]
    public void ResolveTaskbarActivationMode_FallsBackToFocusOnly_WhenButtonCannotTakeKeyboardInput()
    {
        var mode = WindowAutomation.ResolveTaskbarActivationMode(
            canInvoke: false,
            canSelect: false,
            canToggle: false,
            isKeyboardFocusable: false);

        Assert.Equal(TaskbarActivationMode.FocusOnly, mode);
    }
}
