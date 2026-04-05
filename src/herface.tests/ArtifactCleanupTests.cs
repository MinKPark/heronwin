using Xunit;

namespace HeronWin.HerFace.Tests;

public sealed class ArtifactCleanupTests
{
    [Fact]
    public void GetLogsDirectory_PointsToLogsFolderBesideExecutable()
    {
        var actual = ArtifactCleanup.GetLogsDirectory(@"C:\temp\herface\");

        Assert.Equal(
            Path.Combine(@"C:\temp\herface\", "logs"),
            actual);
    }

    [Fact]
    public void GetEyesAndHandsScreenshotDirectory_PointsToLogsFolder()
    {
        var actual = ArtifactCleanup.GetEyesAndHandsScreenshotDirectory(@"C:\temp\herface\");

        Assert.Equal(Path.Combine(@"C:\temp\herface\", "logs"), actual);
    }

    [Fact]
    public void GetDebugVoiceRecordingDirectory_PointsToLogsFolder()
    {
        var actual = ArtifactCleanup.GetDebugVoiceRecordingDirectory(@"C:\temp\herface\");

        Assert.Equal(Path.Combine(@"C:\temp\herface\", "logs"), actual);
    }

    [Fact]
    public void CleanupPreviousRunArtifacts_DeletesDebugVoiceDirectory()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "herface-artifact-tests", Guid.NewGuid().ToString("n"));
        var debugVoiceDirectory = ArtifactCleanup.GetDebugVoiceRecordingDirectory(baseDirectory);
        Directory.CreateDirectory(debugVoiceDirectory);
        File.WriteAllText(Path.Combine(debugVoiceDirectory, "voice-test.wav"), "test");

        try
        {
            ArtifactCleanup.CleanupPreviousRunArtifacts(baseDirectory, processPath: null);

            Assert.False(Directory.Exists(debugVoiceDirectory));
        }
        finally
        {
            if (Directory.Exists(baseDirectory))
            {
                Directory.Delete(baseDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void SaveDebugVoiceRecording_CopiesRecordingIntoDebugVoiceDirectory()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "herface-artifact-tests", Guid.NewGuid().ToString("n"));
        var sourceDirectory = Path.Combine(baseDirectory, "source");
        Directory.CreateDirectory(sourceDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "input.wav");
        File.WriteAllText(sourcePath, "wav-data");
        var recording = new RecordingResult(
            sourcePath,
            new DateTimeOffset(2026, 4, 5, 12, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 5, 12, 30, 1, TimeSpan.Zero),
            1000,
            950,
            -50,
            32000);

        try
        {
            var savedPath = ArtifactCleanup.SaveDebugVoiceRecording(baseDirectory, recording);

            Assert.True(File.Exists(savedPath));
            Assert.StartsWith(ArtifactCleanup.GetDebugVoiceRecordingDirectory(baseDirectory), savedPath, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("wav-data", File.ReadAllText(savedPath));
        }
        finally
        {
            if (Directory.Exists(baseDirectory))
            {
                Directory.Delete(baseDirectory, recursive: true);
            }
        }
    }
}
