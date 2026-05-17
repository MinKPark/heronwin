namespace HeronWin.Ava;

internal static class AvaSensitiveValueRedactor
{
    private const string RedactedValue = "[redacted]";
    private const int MinimumSensitiveValueLength = 4;

    public static string? Redact(string? text)
        => Redact(text, GetEnvironmentValues());

    internal static string? Redact(
        string? text,
        IEnumerable<KeyValuePair<string, string?>> environmentValues)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var redacted = text;
        foreach (var (key, value) in environmentValues)
        {
            if (!IsSensitiveKey(key) ||
                string.IsNullOrWhiteSpace(value) ||
                value.Length < MinimumSensitiveValueLength)
            {
                continue;
            }

            redacted = redacted.Replace(value, RedactedValue, StringComparison.Ordinal);
        }

        return redacted;
    }

    private static IEnumerable<KeyValuePair<string, string?>> GetEnvironmentValues()
    {
        foreach (var key in Environment.GetEnvironmentVariables().Keys)
        {
            if (key?.ToString() is not { Length: > 0 } name)
            {
                continue;
            }

            yield return new KeyValuePair<string, string?>(
                name,
                Environment.GetEnvironmentVariable(name));
        }
    }

    private static bool IsSensitiveKey(string key)
        => key.Contains("KEY", StringComparison.OrdinalIgnoreCase) ||
           key.Contains("PIN", StringComparison.OrdinalIgnoreCase) ||
           key.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) ||
           key.Contains("SECRET", StringComparison.OrdinalIgnoreCase) ||
           key.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase) ||
           key.Contains("PASSCODE", StringComparison.OrdinalIgnoreCase) ||
           key.Contains("CREDENTIAL", StringComparison.OrdinalIgnoreCase);
}
