using HeronWin.Brain;
using Xunit;

namespace HeronWin.Cursor.Tests;

public sealed class CursorCliTests
{
    [Fact]
    public void ParseCursor_AllowsInteractiveDefault()
    {
        var options = BrainConsoleMode.ParseCursor([]);

        Assert.False(options.ShowHelp);
        Assert.False(options.IsTraceReport);
        Assert.False(options.IsScripted);
    }
}
