namespace HeronWin.Brain;

internal sealed record BrainConsoleOptions(
    bool ShowHelp,
    string? ScenarioFilePath,
    string? TraceReportPath)
{
    public bool IsScripted =>
        !ShowHelp &&
        !string.IsNullOrWhiteSpace(ScenarioFilePath);

    public bool IsTraceReport =>
        !ShowHelp &&
        !string.IsNullOrWhiteSpace(TraceReportPath);

    public bool RequiresDebugTrace => IsScripted;
}

internal static class BrainConsoleMode
{
    public static BrainConsoleOptions Parse(string[] args)
        => ParseTars(args);

    public static BrainConsoleOptions ParseTars(string[] args)
        => Parse(args, allowScenario: true);

    public static BrainConsoleOptions ParseCursor(string[] args)
        => Parse(args, allowScenario: false);

    private static BrainConsoleOptions Parse(string[] args, bool allowScenario)
    {
        if (args.Length == 0)
        {
            return new BrainConsoleOptions(false, null, null);
        }

        var showHelp = false;
        string? scenarioFilePath = null;
        string? traceReportPath = null;

        for (var index = 0; index < args.Length; index += 1)
        {
            var arg = args[index];
            switch (arg.ToLowerInvariant())
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    break;

                case "--scenario":
                    if (!allowScenario)
                    {
                        throw new InvalidOperationException(
                            "--scenario is supported by tars. Use cursor with no arguments for interactive mode.");
                    }

                    if (!string.IsNullOrWhiteSpace(scenarioFilePath))
                    {
                        throw new InvalidOperationException("Only one --scenario path can be provided.");
                    }

                    scenarioFilePath = Path.GetFullPath(RequireValue(args, ref index, arg));
                    break;

                case "--trace-report":
                    if (!string.IsNullOrWhiteSpace(traceReportPath))
                    {
                        throw new InvalidOperationException("Only one --trace-report path can be provided.");
                    }

                    traceReportPath = Path.GetFullPath(RequireValue(args, ref index, arg));
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unknown argument \"{arg}\". Use --help to see supported options.");
            }
        }

        if (!string.IsNullOrWhiteSpace(traceReportPath) &&
            !string.IsNullOrWhiteSpace(scenarioFilePath))
        {
            throw new InvalidOperationException(
                "Use either scenario execution (--scenario) or --trace-report, not both together.");
        }

        return new BrainConsoleOptions(showHelp, scenarioFilePath, traceReportPath);
    }

    public static void PrintHelp()
        => PrintTarsHelp();

    public static void PrintTarsHelp()
    {
        Console.WriteLine("tars");
        Console.WriteLine("Usage:");
        Console.WriteLine("  tars.exe --scenario .\\scenario.yml     Run a YAML scenario with log-based assertions");
        Console.WriteLine("  tars.exe --trace-report .\\tars.debug.jsonl");
        Console.WriteLine("                                           Print a markdown latency report for a saved JSONL trace");
        Console.WriteLine("  tars.exe --help                         Show this help");
        Console.WriteLine();
        Console.WriteLine("Scenario notes:");
        Console.WriteLine("  One-step scripted work should be represented as a one-command scenario file.");
        Console.WriteLine("  Scenario mode bypasses microphone capture and voice playback.");
        Console.WriteLine("  It enables debug trace logging automatically so each turn can be judged from the JSONL log.");
        Console.WriteLine();
        Console.WriteLine("Tip:");
        Console.WriteLine("  Set DEBUG_TRACE=1 if you also want persistent debug logs outside scenario runs.");
    }

    public static void PrintCursorHelp()
    {
        Console.WriteLine("cursor");
        Console.WriteLine("Usage:");
        Console.WriteLine("  cursor.exe                              Start the provider's default interactive mode");
        Console.WriteLine("  cursor.exe --trace-report .\\cursor.debug.jsonl");
        Console.WriteLine("                                           Print a markdown latency report for a saved JSONL trace");
        Console.WriteLine("  cursor.exe --help                       Show this help");
        Console.WriteLine();
        Console.WriteLine("Interactive mode notes:");
        Console.WriteLine("  openai-api starts in voice mode and supports /mode:text and /mode:voice.");
        Console.WriteLine("  openai-codex starts in text mode and uses your local Codex / ChatGPT sign-in.");
        Console.WriteLine("Tip:");
        Console.WriteLine("  Set DEBUG_TRACE=1 if you also want persistent debug logs in normal voice mode.");
    }

    private static string RequireValue(string[] args, ref int index, string flag)
    {
        if (index + 1 >= args.Length)
        {
            throw new InvalidOperationException($"Argument {flag} requires a value.");
        }

        index += 1;
        var value = args[index].Trim();
        if (value.Length == 0)
        {
            throw new InvalidOperationException($"Argument {flag} requires a non-empty value.");
        }

        return value;
    }
}
