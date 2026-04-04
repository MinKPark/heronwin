namespace HeronWin.HerFace;

internal sealed record HerfaceConsoleOptions(
    bool ShowHelp,
    string? ScenarioFilePath,
    IReadOnlyList<string> Commands)
{
    public bool IsScripted =>
        !ShowHelp &&
        (!string.IsNullOrWhiteSpace(ScenarioFilePath) || Commands.Count > 0);

    public bool RequiresDebugTrace => IsScripted;
}

internal static class HerfaceConsoleMode
{
    public static HerfaceConsoleOptions Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new HerfaceConsoleOptions(false, null, []);
        }

        var showHelp = false;
        string? scenarioFilePath = null;
        var commands = new List<string>();

        for (var index = 0; index < args.Length; index += 1)
        {
            var arg = args[index];
            switch (arg.ToLowerInvariant())
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    break;

                case "--command":
                    commands.Add(RequireValue(args, ref index, arg));
                    break;

                case "--commands-file":
                    commands.AddRange(HerfaceCommandFileLoader.LoadFromFile(RequireValue(args, ref index, arg)));
                    break;

                case "--scenario":
                    if (!string.IsNullOrWhiteSpace(scenarioFilePath))
                    {
                        throw new InvalidOperationException("Only one --scenario path can be provided.");
                    }

                    scenarioFilePath = Path.GetFullPath(RequireValue(args, ref index, arg));
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unknown argument \"{arg}\". Use --help to see supported options.");
            }
        }

        if (!string.IsNullOrWhiteSpace(scenarioFilePath) && commands.Count > 0)
        {
            throw new InvalidOperationException(
                "Use either scripted commands (--command / --commands-file) or --scenario, not both together.");
        }

        return new HerfaceConsoleOptions(showHelp, scenarioFilePath, commands);
    }

    public static void PrintHelp()
    {
        Console.WriteLine("herface");
        Console.WriteLine("Usage:");
        Console.WriteLine("  herface.exe                              Start normal voice mode");
        Console.WriteLine("  herface.exe --command \"open netflix\"     Run one scripted command");
        Console.WriteLine("  herface.exe --command \"...\" --command \"...\"");
        Console.WriteLine("                                           Run multiple scripted commands");
        Console.WriteLine("  herface.exe --commands-file .\\steps.yml  Run scripted commands from a YAML file");
        Console.WriteLine("  herface.exe --scenario .\\scenario.yml    Run a YAML scenario with log-based assertions");
        Console.WriteLine("  herface.exe --help                        Show this help");
        Console.WriteLine();
        Console.WriteLine("Command file format:");
        Console.WriteLine("  YAML sequence of strings, or a mapping with a commands: sequence.");
        Console.WriteLine();
        Console.WriteLine("Scripted mode notes:");
        Console.WriteLine("  Scripted mode bypasses microphone capture and voice playback.");
        Console.WriteLine("  It still routes commands through the normal herface agent/tool pipeline.");
        Console.WriteLine("  It enables debug trace logging automatically so each turn can be judged from the JSONL log.");
        Console.WriteLine();
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
