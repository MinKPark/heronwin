namespace HeronWin.HerFace;

internal static class ArtifactCleanup
{
    internal static string GetLogsDirectory(string baseDirectory)
        => DebugTrace.BuildLogsDirectory(baseDirectory);

    internal static string GetEyesAndHandsScreenshotDirectory(string baseDirectory)
        => GetLogsDirectory(baseDirectory);

    internal static string GetDebugVoiceRecordingDirectory(string baseDirectory)
        => GetLogsDirectory(baseDirectory);

    internal static void CleanupPreviousRunArtifacts(string baseDirectory, string? processPath)
    {
        var debugLogPath = DebugTrace.BuildLogFilePath(baseDirectory, processPath);
        var debugJsonLogPath = DebugTrace.BuildJsonLogFilePath(baseDirectory, processPath);
        TryDeleteFile(debugLogPath);
        TryDeleteFile(debugJsonLogPath);
        TryDeleteDirectory(GetLogsDirectory(baseDirectory));
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

        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            TryDeleteDirectory(GetLogsDirectory(baseDirectory));
        }
    }

    internal static string SaveDebugVoiceRecording(string baseDirectory, RecordingResult recording)
    {
        var logsDirectory = GetLogsDirectory(baseDirectory);
        Directory.CreateDirectory(logsDirectory);
        var fileName = $"voice-{recording.StartedAt:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.wav";
        var destinationPath = Path.Combine(logsDirectory, fileName);
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
