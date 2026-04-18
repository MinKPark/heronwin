namespace HeronWin.Body.DesktopAutomation;

internal static class DebugTrace
{
    private const string DebugEnvironmentVariable = "BODY_WINDOWS_DEBUG";
    private static readonly object SyncRoot = new();
    private static volatile bool _isEnabled;

    internal static bool IsEnabled => _isEnabled;

    internal static void Configure(IEnumerable<string> args)
    {
        _isEnabled = ShouldEnable(args, Environment.GetEnvironmentVariable(DebugEnvironmentVariable));
    }

    internal static bool ShouldEnable(IEnumerable<string> args, string? environmentValue)
    {
        return args.Any(arg => string.Equals(arg, "--debug", StringComparison.OrdinalIgnoreCase)) ||
               IsEnabledEnvironmentValue(environmentValue);
    }

    internal static bool IsEnabledEnvironmentValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim() switch
        {
            "1" => true,
            "true" => true,
            "TRUE" => true,
            "yes" => true,
            "YES" => true,
            "on" => true,
            "ON" => true,
            _ => false,
        };
    }

    internal static string FormatTimestampedLine(string message, DateTimeOffset timestamp)
    {
        return $"[{timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] {message}";
    }

    internal static void WriteLine(string message)
    {
        if (!_isEnabled)
        {
            return;
        }

        WriteLineCore(Console.Error, message, includeTimestamp: true);
    }

    internal static void WriteConsoleLine(string message)
    {
        WriteLineCore(Console.Out, message, includeTimestamp: _isEnabled);
    }

    private static void WriteLineCore(TextWriter writer, string message, bool includeTimestamp)
    {
        lock (SyncRoot)
        {
            writer.WriteLine(includeTimestamp
                ? FormatTimestampedLine(message, DateTimeOffset.Now)
                : message);
            writer.Flush();
        }
    }
}
