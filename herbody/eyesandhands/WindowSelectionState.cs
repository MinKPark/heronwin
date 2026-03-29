namespace HeronWin.HerBody.EyesAndHands;

public sealed class WindowSelectionState
{
    private readonly object _sync = new();
    private nint? _selectedHandle;

    public nint? GetSelectedHandle()
    {
        lock (_sync)
        {
            return _selectedHandle;
        }
    }

    public void SetSelectedHandle(nint handle)
    {
        lock (_sync)
        {
            _selectedHandle = handle;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _selectedHandle = null;
        }
    }
}
