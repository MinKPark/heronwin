namespace HeronWin.Brain;

internal static class BrainInteractiveCommands
{
    public static bool IsExitCommand(string text)
        => Normalize(text) is "/exit" or "exit" or "quit";

    public static bool IsResetCommand(string text)
        => Normalize(text) is "/reset" or "reset";

    public static bool TryParseModeSwitch(string text, out BrainInteractiveMode targetMode)
    {
        targetMode = BrainInteractiveMode.Text;
        switch (Normalize(text))
        {
            case "/mode:text":
            case "to text mode":
            case "switch to text mode":
            case "text mode":
                targetMode = BrainInteractiveMode.Text;
                return true;

            case "/mode:voice":
            case "to voice mode":
            case "switch to voice mode":
            case "voice mode":
                targetMode = BrainInteractiveMode.Voice;
                return true;

            default:
                return false;
        }
    }

    private static string Normalize(string text)
        => text.Trim().ToLowerInvariant();
}
