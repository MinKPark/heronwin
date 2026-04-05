using Xunit;

namespace HeronWin.HerBody.EyesAndHands.Tests;

public sealed class WindowScreenshotNamingTests
{
    [Theory]
    [InlineData("Untitled - Notepad", "Untitled_-_Notepad")]
    [InlineData("File:/Open?*", "File__Open")]
    [InlineData("   ", "window")]
    public void SanitizeFileNameSegment_NormalizesWindowTitles(string input, string expected)
    {
        var actual = WindowAutomation.SanitizeFileNameSegment(input);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GetScreenshotDirectory_UsesConfiguredArtifactDirectory()
    {
        var actual = WindowAutomation.GetScreenshotDirectory(@"C:\apps\herface\bin\Debug\net10.0-windows\logs");

        Assert.Equal(@"C:\apps\herface\bin\Debug\net10.0-windows\logs", actual);
    }
}
