using HeronWin.Body.DesktopAutomation;

namespace HeronWin.Body.Execution;

internal static class ConsoleMode
{
    internal static bool TryRun(string[] args)
    {
        if (!args.Any(arg => string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        DebugTrace.WriteConsoleLine("execution");
        DebugTrace.WriteConsoleLine("Usage:");
        DebugTrace.WriteConsoleLine("  execution.exe          Start the MCP stdio server");
        DebugTrace.WriteConsoleLine("  execution.exe --debug  Enable timestamped diagnostic output on stderr");
        return true;
    }
}
