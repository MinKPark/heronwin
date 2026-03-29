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

        Console.WriteLine("eyesandhands self-test");
        Console.WriteLine($"Visible windows: {result.Windows.Count}");

        if (!string.IsNullOrWhiteSpace(result.SelectedWindowHandle))
        {
            Console.WriteLine($"Selected window: {result.SelectedWindowHandle}");
        }

        if (result.Windows.Count == 0)
        {
            Console.WriteLine("No visible titled windows were detected from this session.");
            Console.WriteLine("If you are running from a non-interactive shell, launch this from your desktop session.");
            return true;
        }

        Console.WriteLine();
        foreach (var window in result.Windows)
        {
            var selectedMarker = window.IsSelected ? "*" : " ";
            Console.WriteLine(
                $"{selectedMarker} {window.Handle}  pid={window.ProcessId}  class={window.ClassName}  title={window.Title}");
        }

        return true;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("eyesandhands");
        Console.WriteLine("Usage:");
        Console.WriteLine("  eyesandhands.exe                  Start the MCP stdio server");
        Console.WriteLine("  eyesandhands.exe --selftest       Print a human-readable visible window list");
        Console.WriteLine("  eyesandhands.exe --selftest-json  Print the same result as JSON");
    }
}
