using Xunit;

namespace HeronWin.HerBody.EyesAndHands.Tests;

public sealed class InteractionWindowResolverTests
{
    [Fact]
    public void ResolveWindowForInteraction_UsesSelectedWindowAndRefocusesIt_WhenSelectionIsValid()
    {
        var selectionState = new WindowSelectionState();
        selectionState.SetSelectedHandle((nint)42);
        nint? focusedHandle = null;

        var resolvedHandle = InteractionWindowResolver.ResolveWindowForInteraction(
            selectionState,
            handle => handle == (nint)42,
            handle => focusedHandle = handle,
            () => (nint)99);

        Assert.Equal((nint)42, resolvedHandle);
        Assert.Equal((nint)42, selectionState.GetSelectedHandle());
        Assert.Equal((nint)42, focusedHandle);
    }

    [Fact]
    public void ResolveWindowForInteraction_ClearsStaleSelectionAndFallsBackToForegroundWindow()
    {
        var selectionState = new WindowSelectionState();
        selectionState.SetSelectedHandle((nint)42);
        var focusCalls = 0;

        var resolvedHandle = InteractionWindowResolver.ResolveWindowForInteraction(
            selectionState,
            _ => false,
            _ => focusCalls++,
            () => (nint)99);

        Assert.Equal((nint)99, resolvedHandle);
        Assert.Null(selectionState.GetSelectedHandle());
        Assert.Equal(0, focusCalls);
    }

    [Fact]
    public void ResolveWindowForInteraction_UsesForegroundWindow_WhenNoSelectionExists()
    {
        var selectionState = new WindowSelectionState();
        var focusCalls = 0;

        var resolvedHandle = InteractionWindowResolver.ResolveWindowForInteraction(
            selectionState,
            _ => true,
            _ => focusCalls++,
            () => (nint)77);

        Assert.Equal((nint)77, resolvedHandle);
        Assert.Null(selectionState.GetSelectedHandle());
        Assert.Equal(0, focusCalls);
    }
}
