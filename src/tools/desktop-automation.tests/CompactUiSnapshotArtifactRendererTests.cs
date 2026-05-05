using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using Xunit;

namespace HeronWin.Tools.DesktopAutomation.Tests;

public sealed class CompactUiSnapshotArtifactRendererTests
{
    [Fact]
    public void RenderWindowArtifactsFromJsonl_CreatesCompactImageAndJsonForScreenshot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "heronwin-compact-artifact-tests", Guid.NewGuid().ToString("N"));
        var outputDirectory = Path.Combine(tempRoot, "output");
        var jsonlPath = Path.Combine(tempRoot, "brain.debug.jsonl");
        var screenshotPath = Path.Combine(
            tempRoot,
            "20260419-000143729-Netflix_-_Personal_-_Microsoft_Edge-0x00ABCDEF.png");

        Directory.CreateDirectory(tempRoot);
        using (var bitmap = new Bitmap(32, 24))
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Black);
            bitmap.Save(screenshotPath, ImageFormat.Png);
        }

        var rawSnapshot = new
        {
            Window = new
            {
                Handle = "0x00ABCDEF",
                Title = "Netflix - Personal - Microsoft Edge",
                ClassName = "Chrome_WidgetWin_1",
                ProcessId = 12345,
                Bounds = new
                {
                    Left = 0,
                    Top = 0,
                    Width = 800,
                    Height = 600,
                },
            },
            MaxDepth = (int?)null,
            FullDepth = true,
            ElementTree = new
            {
                Path = "root",
                UiPath = "root",
                Name = "Netflix - Personal - Microsoft Edge",
                ControlType = "Window",
                ClassName = "Chrome_WidgetWin_1",
                IsEnabled = true,
                HasKeyboardFocus = true,
                Bounds = new
                {
                    Left = 0,
                    Top = 0,
                    Width = 800,
                    Height = 600,
                },
                Children = new object[]
                {
                    new
                    {
                        Path = "1/0",
                        UiPath = "1/0",
                        Name = "Who's watching?",
                        ControlType = "Text",
                        Bounds = new
                        {
                            Left = 120,
                            Top = 80,
                            Width = 240,
                            Height = 32,
                        },
                    },
                    new
                    {
                        Path = "1/1",
                        UiPath = "1/1",
                        Name = "Min",
                        ControlType = "Hyperlink",
                        IsKeyboardFocusable = true,
                        AvailableActions = new[] { "focus", "invoke" },
                        Bounds = new
                        {
                            Left = 120,
                            Top = 150,
                            Width = 180,
                            Height = 44,
                        },
                    },
                },
            },
        };

        var fullDescribeEvent = new
        {
            timestamp = "2026-04-18T17:01:43.0000000-07:00",
            sequence = 127,
            category = "mcp.call.complete.full",
            data = new
            {
                headers = new[]
                {
                    "server=cognition",
                    "tool=describe_window",
                    "elapsedMs=123",
                    "isError=False",
                },
                text = JsonSerializer.Serialize(rawSnapshot, new JsonSerializerOptions { WriteIndented = true }),
            },
        };

        var screenshotEvent = new
        {
            timestamp = "2026-04-18T17:01:43.5000000-07:00",
            sequence = 134,
            category = "mcp.call.complete",
            data = new
            {
                server = "cognition",
                tool = "capture_window_screenshot",
                imagePaths = new[] { screenshotPath },
                textPreview = JsonSerializer.Serialize(new
                {
                    Window = new
                    {
                        Handle = "0x00ABCDEF",
                    },
                    ImagePath = screenshotPath,
                }),
            },
        };

        File.WriteAllLines(
            jsonlPath,
            [
                JsonSerializer.Serialize(fullDescribeEvent),
                JsonSerializer.Serialize(screenshotEvent),
            ]);

        try
        {
            var summary = CompactUiSnapshotArtifactRenderer.RenderWindowArtifactsFromJsonl(jsonlPath, outputDirectory);

            var artifact = Assert.Single(summary.RenderedArtifacts);
            Assert.Empty(summary.Warnings);
            Assert.Equal("0x00ABCDEF", artifact.WindowHandle);
            Assert.Equal(127, artifact.SnapshotSequence);
            Assert.Equal(134, artifact.ScreenshotSequence);
            Assert.True(File.Exists(artifact.CompactImagePath));
            Assert.True(File.Exists(artifact.CompactJsonPath));
            Assert.True(File.Exists(summary.ManifestPath));

            var compactJson = File.ReadAllText(artifact.CompactJsonPath);
            Assert.Contains("\"compactTree\"", compactJson, StringComparison.Ordinal);
            Assert.Contains("\"llmTree\"", compactJson, StringComparison.Ordinal);
            Assert.Contains("Min", compactJson, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
