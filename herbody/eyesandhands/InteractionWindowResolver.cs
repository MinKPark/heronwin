namespace HeronWin.HerBody.EyesAndHands;

internal static class InteractionWindowResolver
{
    internal static nint ResolveWindowForInteraction(
        WindowSelectionState selectionState,
        Func<nint, bool> isWindow,
        Action<nint> focusWindow,
        Func<nint> getActiveWindowHandle)
    {
        var selectedHandle = selectionState.GetSelectedHandle();
        if (selectedHandle.HasValue)
        {
            if (isWindow(selectedHandle.Value))
            {
                focusWindow(selectedHandle.Value);
                return selectedHandle.Value;
            }

            selectionState.Clear();
        }

        return getActiveWindowHandle();
    }
}
