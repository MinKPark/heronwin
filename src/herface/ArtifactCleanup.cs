namespace HeronWin.HerFace;

internal static class ArtifactCleanup
{
    private static readonly string EyesAndHandsScreenshotDirectory = Path.Combine(
        Path.GetTempPath(),
        "heronwin",
        "eyesandhands");

    internal static string GetEyesAndHandsScreenshotDirectory() => EyesAndHandsScreenshotDirectory;

    internal static string GetDebugVoiceRecordingDirectory(string baseDirectory)
        => Path.Combine(baseDirectory, "debug-voice");

    internal static void CleanupPreviousRunArtifacts(string baseDirectory, string? processPath)
    {
        var debugLogPath = DebugTrace.BuildLogFilePath(baseDirectory, processPath);
        var debugJsonLogPath = DebugTrace.BuildJsonLogFilePath(baseDirectory, processPath);
        TryDeleteFile(debugLogPath);
        TryDeleteFile(debugJsonLogPath);
        TryDeleteDirectory(EyesAndHandsScreenshotDirectory);
        TryDeleteDirectory(GetDebugVoiceRecordingDirectory(baseDirectory));
    }

    internal static void CleanupCurrentRunArtifacts(string? debugLogPath, string? debugJsonLogPath, string? baseDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(debugLogPath))
        {
            TryDeleteFile(debugLogPath);
        }

        if (!string.IsNullOrWhiteSpace(debugJsonLogPath))
        {
            TryDeleteFile(debugJsonLogPath);
        }

        TryDeleteDirectory(EyesAndHandsScreenshotDirectory);
        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            TryDeleteDirectory(GetDebugVoiceRecordingDirectory(baseDirectory));
        }
    }

    internal static string SaveDebugVoiceRecording(string baseDirectory, RecordingResult recording)
    {
        var debugVoiceDirectory = GetDebugVoiceRecordingDirectory(baseDirectory);
        Directory.CreateDirectory(debugVoiceDirectory);
        var fileName = $"voice-{recording.StartedAt:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.wav";
        var destinationPath = Path.Combine(debugVoiceDirectory, fileName);
        File.Copy(recording.FilePath, destinationPath, overwrite: false);
        return destinationPath;
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
