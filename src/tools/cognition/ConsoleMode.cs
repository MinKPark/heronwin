using HeronWin.Body.DesktopAutomation;

namespace HeronWin.Body.Cognition;

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

        var renderJsonlPath = GetOptionValue(args, "--render-compact-from-jsonl");
        if (!string.IsNullOrWhiteSpace(renderJsonlPath))
        {
            var outputDirectory = GetOptionValue(args, "--output-dir") ??
                                  Path.Combine(
                                      Path.GetDirectoryName(Path.GetFullPath(renderJsonlPath))!,
                                      "manual-compact-validation");
            var summary = CompactUiSnapshotArtifactRenderer.RenderWindowArtifactsFromJsonl(
                renderJsonlPath,
                outputDirectory);
            Console.WriteLine(CompactUiSnapshotJson.Serialize(summary));
            return true;
        }

        var jsonMode = args.Any(arg => string.Equals(arg, "--selftest-json", StringComparison.OrdinalIgnoreCase));
        var selfTestMode = jsonMode || args.Any(arg => string.Equals(arg, "--selftest", StringComparison.OrdinalIgnoreCase));
        if (!selfTestMode)
        {
            return false;
        }

        using var executor = new UiAutomationExecutor();
        var result = await executor.RunAsync(() => WindowAutomation.ListWindows(new WindowSelectionState()));

        if (jsonMode)
        {
            Console.WriteLine(WindowAutomation.Serialize(result));
            return true;
        }

        DebugTrace.WriteConsoleLine("cognition self-test");
        DebugTrace.WriteConsoleLine($"Visible windows: {result.Windows.Count}");

        if (result.Windows.Count == 0)
        {
            DebugTrace.WriteConsoleLine("No visible titled windows were detected from this session.");
            DebugTrace.WriteConsoleLine("If you are running from a non-interactive shell, launch this from your desktop session.");
            return true;
        }

        DebugTrace.WriteConsoleLine(string.Empty);
        foreach (var window in result.Windows)
        {
            DebugTrace.WriteConsoleLine(
                $"  {window.Handle}  pid={window.ProcessId}  class={window.ClassName}  title={window.Title}");
        }

        return true;
    }

    private static void PrintHelp()
    {
        DebugTrace.WriteConsoleLine("cognition");
        DebugTrace.WriteConsoleLine("Usage:");
        DebugTrace.WriteConsoleLine("  cognition.exe                  Start the MCP stdio server");
        DebugTrace.WriteConsoleLine("  cognition.exe --debug          Enable timestamped diagnostic output on stderr");
        DebugTrace.WriteConsoleLine("  cognition.exe --selftest       Print a human-readable visible window list");
        DebugTrace.WriteConsoleLine("  cognition.exe --selftest-json  Print the same result as JSON");
        DebugTrace.WriteConsoleLine("  cognition.exe --render-compact-from-jsonl <path> [--output-dir <path>]");
    }

    private static string? GetOptionValue(string[] args, string optionName)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], optionName, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

}
