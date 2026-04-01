namespace HeronWin.HerFace;

internal static class ArtifactCleanup
{
    private static readonly string EyesAndHandsScreenshotDirectory = Path.Combine(
        Path.GetTempPath(),
        "heronwin",
        "eyesandhands");

    internal static string GetEyesAndHandsScreenshotDirectory() => EyesAndHandsScreenshotDirectory;

    internal static void CleanupPreviousRunArtifacts(string baseDirectory, string? processPath)
    {
        var debugLogPath = DebugTrace.BuildLogFilePath(baseDirectory, processPath);
        TryDeleteFile(debugLogPath);
        TryDeleteDirectory(EyesAndHandsScreenshotDirectory);
    }

    internal static void CleanupCurrentRunArtifacts(string? debugLogPath)
    {
        if (!string.IsNullOrWhiteSpace(debugLogPath))
        {
            TryDeleteFile(debugLogPath);
        }

        TryDeleteDirectory(EyesAndHandsScreenshotDirectory);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }
}
