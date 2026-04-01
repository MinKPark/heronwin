namespace HeronWin.HerBody.EyesAndHands;

internal static class ConsoleMode
{
    internal static async Task<bool> TryRunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return false;
        }

        if (args.Any(arg => string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase)))
        {
            PrintHelp();
            return true;
        }

        var jsonMode = args.Any(arg => string.Equals(arg, "--selftest-json", StringComparison.OrdinalIgnoreCase));
        var selfTestMode = jsonMode || args.Any(arg => string.Equals(arg, "--selftest", StringComparison.OrdinalIgnoreCase));
        if (!selfTestMode)
        {
            return false;
        }

        using var executor = new UiAutomationExecutor();
        var selectionState = new WindowSelectionState();
        var result = await executor.RunAsync(() => WindowAutomation.ListWindows(selectionState));

        if (jsonMode)
        {
            Console.WriteLine(WindowAutomation.Serialize(result));
            return true;
        }

        DebugTrace.WriteConsoleLine("eyesandhands self-test");
        DebugTrace.WriteConsoleLine($"Visible windows: {result.Windows.Count}");

        if (!string.IsNullOrWhiteSpace(result.SelectedWindowHandle))
        {
            DebugTrace.WriteConsoleLine($"Selected window: {result.SelectedWindowHandle}");
        }

        if (result.Windows.Count == 0)
        {
            DebugTrace.WriteConsoleLine("No visible titled windows were detected from this session.");
            DebugTrace.WriteConsoleLine("If you are running from a non-interactive shell, launch this from your desktop session.");
            return true;
        }

        DebugTrace.WriteConsoleLine(string.Empty);
        foreach (var window in result.Windows)
        {
            var selectedMarker = window.IsSelected ? "*" : " ";
            DebugTrace.WriteConsoleLine(
                $"{selectedMarker} {window.Handle}  pid={window.ProcessId}  class={window.ClassName}  title={window.Title}");
        }

        return true;
    }

    private static void PrintHelp()
    {
        DebugTrace.WriteConsoleLine("eyesandhands");
        DebugTrace.WriteConsoleLine("Usage:");
        DebugTrace.WriteConsoleLine("  eyesandhands.exe                  Start the MCP stdio server");
        DebugTrace.WriteConsoleLine("  eyesandhands.exe --debug          Enable timestamped diagnostic output on stderr");
        DebugTrace.WriteConsoleLine("  eyesandhands.exe --selftest       Print a human-readable visible window list");
        DebugTrace.WriteConsoleLine("  eyesandhands.exe --selftest-json  Print the same result as JSON");
    }
}
