using Xunit;

namespace HeronWin.HerFace.Tests;

public sealed class DebugTraceTests
{
    [Fact]
    public void BuildLogFilePath_UsesLogsSubfolderBesideExecutable()
    {
        var actual = DebugTrace.BuildLogFilePath(
            @"C:\apps\herface\bin\Debug\net10.0-windows\",
            @"C:\apps\herface\bin\Debug\net10.0-windows\herface.exe");

        Assert.Equal(
            @"C:\apps\herface\bin\Debug\net10.0-windows\logs\herface.debug.log",
            actual);
    }

    [Fact]
    public void BuildJsonLogFilePath_UsesLogsSubfolderBesideExecutable()
    {
        var actual = DebugTrace.BuildJsonLogFilePath(
            @"C:\apps\herface\bin\Debug\net10.0-windows\",
            @"C:\apps\herface\bin\Debug\net10.0-windows\herface.exe");

        Assert.Equal(
            @"C:\apps\herface\bin\Debug\net10.0-windows\logs\herface.debug.jsonl",
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
    public void ShouldLogFullToolPayload_ReturnsTrue_ForUiTreeTools()
    {
        Assert.True(McpClientManager.ShouldLogFullToolPayload("describe_selected_window", "{ }"));
        Assert.True(McpClientManager.ShouldLogFullToolPayload("describe_selected_window_focus", "{ }"));
    }

    [Fact]
    public void ShouldLogFullToolPayload_ReturnsFalse_ForNonTreeTools()
    {
        Assert.False(McpClientManager.ShouldLogFullToolPayload("invoke_selected_window_element", "{ }"));
        Assert.False(McpClientManager.ShouldLogFullToolPayload("describe_selected_window", ""));
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
