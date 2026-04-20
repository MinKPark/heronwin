using Xunit;

namespace HeronWin.Brain.Tests;

public sealed class DebugTraceTests
{
    [Fact]
    public void BuildLogFilePath_UsesLogsSubfolderBesideExecutable()
    {
        var actual = DebugTrace.BuildLogFilePath(
            @"C:\apps\brain\bin\Debug\net10.0-windows\",
            @"C:\apps\brain\bin\Debug\net10.0-windows\brain.exe");

        Assert.Equal(
            @"C:\apps\brain\bin\Debug\net10.0-windows\logs\brain.debug.log",
            actual);
    }

    [Fact]
    public void BuildJsonLogFilePath_UsesLogsSubfolderBesideExecutable()
    {
        var actual = DebugTrace.BuildJsonLogFilePath(
            @"C:\apps\brain\bin\Debug\net10.0-windows\",
            @"C:\apps\brain\bin\Debug\net10.0-windows\brain.exe");

        Assert.Equal(
            @"C:\apps\brain\bin\Debug\net10.0-windows\logs\brain.debug.jsonl",
            actual);
    }

    [Fact]
    public void FormatTimestampedLine_IncludesSequenceNumber()
    {
        var actual = DebugTrace.FormatTimestampedLine(
            "sample",
            new DateTimeOffset(2026, 3, 31, 21, 15, 30, 123, TimeSpan.FromHours(-7)),
            sequenceNumber: 42);

        Assert.Equal("[2026-03-31 21:15:30.123 -07:00] #00042 sample", actual);
    }

    [Fact]
    public void Preview_FlattensWhitespaceAndTruncates()
    {
        var actual = DebugTrace.Preview("first line\r\nsecond line", maxLength: 10);

        Assert.Equal("first line... [22 chars]", actual);
    }

    [Fact]
    public void PreviewToolArguments_RedactsSensitiveTypeWindowText()
    {
        var actual = DebugTrace.PreviewToolArguments("type_window_text", """{"text":"3579"}""");

        Assert.Equal("""{"text":"[type_window_text redacted]"}""", actual);
    }

    [Fact]
    public void PreviewToolArguments_LeavesOtherToolArgumentsVisible()
    {
        var actual = DebugTrace.PreviewToolArguments("invoke_window_element", """{"elementPath":"1/2/3"}""");

        Assert.Equal("""{"elementPath":"1/2/3"}""", actual);
    }

    [Fact]
    public void ShouldLogFullToolPayload_ReturnsTrue_ForUiTreeTools()
    {
        Assert.True(McpClientManager.ShouldLogFullToolPayload("describe_window", "{ }"));
        Assert.True(McpClientManager.ShouldLogFullToolPayload("describe_window_focus", "{ }"));
    }

    [Fact]
    public void ShouldLogFullToolPayload_ReturnsFalse_ForNonTreeTools()
    {
        Assert.False(McpClientManager.ShouldLogFullToolPayload("invoke_window_element", "{ }"));
        Assert.False(McpClientManager.ShouldLogFullToolPayload("describe_window", ""));
    }

    [Fact]
    public void ExtractImageFilePathsFromJsonText_ReturnsReferencedImagePaths()
    {
        const string json = """
        {
          "ImagePath": "C:\\temp\\shot1.png",
          "Nested": {
            "screenshotPath": "C:\\temp\\shot2.png"
          }
        }
        """;

        var actual = McpClientManager.ExtractImageFilePathsFromJsonText(json);

        Assert.Equal(
            [@"C:\temp\shot1.png", @"C:\temp\shot2.png"],
            actual);
    }
}

