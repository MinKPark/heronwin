using System.Drawing;
using Xunit;

namespace HeronWin.Body.DesktopAutomation.Tests;

public sealed class CompactUiSnapshotBuilderTests
{
    [Fact]
    public void BuildWindowResponse_PreservesMeaningfulNodesWithinBudget()
    {
        var filler = new string('x', 500);
        var response = CompactUiSnapshotBuilder.BuildWindowResponse(
            CreateWindowDescriptor(),
            Snapshot(
                "root",
                "root",
                "Window",
                name: "Netflix - Microsoft Edge",
                children:
                [
                    Snapshot(
                        "1/0",
                        "1/0",
                        "Edit",
                        name: "Address and search bar",
                        className: "OmniboxViewViews",
                        actions: ["focus", "set_value"],
                        bounds: Bounds(24, 16, 520, 34)),
                    Snapshot(
                        "1/1",
                        "1/1",
                        "Document",
                        name: "Netflix",
                        automationId: "RootWebArea",
                        children:
                        [
                            Snapshot(
                                "1/1/0",
                                "1/1/0",
                                "Text",
                                name: "Who's watching?",
                                bounds: Bounds(96, 120, 240, 34)),
                            Snapshot(
                                "1/1/1",
                                "1/1/1",
                                "Hyperlink",
                                name: "Boyfriend on Demand",
                                actions: ["focus", "invoke"],
                                bounds: Bounds(96, 180, 260, 44)),
                            Snapshot(
                                "1/1/2",
                                "1/1/2",
                                "Text",
                                name: filler)
                        ]),
                    Snapshot(
                        "1/2",
                        "1/2",
                        "Button",
                        name: "Close")
                ]),
            budgetHintChars: 1_400,
            includeImage: false);

        var json = CompactUiSnapshotJson.Serialize(response);

        Assert.Contains("\"compactTree\"", json, StringComparison.Ordinal);
        Assert.Contains("Address and search bar", json, StringComparison.Ordinal);
        Assert.Contains("Boyfriend on Demand", json, StringComparison.Ordinal);
        Assert.DoesNotContain(filler, json, StringComparison.Ordinal);
        Assert.Contains("\"algorithmVersion\":\"compact-tree-v1\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFocusResponse_RetainsFocusedNodeFlags()
    {
        var response = CompactUiSnapshotBuilder.BuildFocusResponse(
            CreateWindowDescriptor(),
            Snapshot(
                "focused",
                "1/0/0/4",
                "Edit",
                name: "Search",
                hasKeyboardFocus: true,
                isKeyboardFocusable: true,
                isSelected: true,
                actions: ["focus", "set_value"],
                bounds: Bounds(80, 90, 220, 32),
                children:
                [
                    Snapshot(
                        "focused/0",
                        "1/0/0/4/0",
                        "Text",
                        name: "Boyfriend on Demand")
                ]),
            budgetHintChars: 900,
            includeImage: false);

        var json = CompactUiSnapshotJson.Serialize(response);

        Assert.Contains("\"hasKeyboardFocus\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"isSelected\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"uiPath\":\"1/0/0/4\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildWindowResponse_RendersImageWithFallbackLane()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "heronwin-compact-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var response = CompactUiSnapshotBuilder.BuildWindowResponse(
                CreateWindowDescriptor(),
                Snapshot(
                    "root",
                    "root",
                    "Window",
                    name: "Netflix - Microsoft Edge",
                    bounds: Bounds(0, 0, 800, 600),
                    children:
                    [
                        Snapshot(
                            "1/0",
                            "1/0",
                            "Button",
                            name: "Play",
                            actions: ["invoke"],
                            bounds: Bounds(120, 180, 180, 52)),
                        Snapshot(
                            "1/1",
                            "1/1",
                            "Text",
                            name: "Continue Watching")
                    ]),
                budgetHintChars: 1_400,
                includeImage: true,
                debugArtifactDirectory: tempDirectory);

            Assert.NotNull(response.RenderedImage);
            Assert.True(File.Exists(response.RenderedImage!.ImagePath));
            Assert.Equal(1_080, response.RenderedImage.ImageSize.Width);
            Assert.Equal(600, response.RenderedImage.ImageSize.Height);

            using var bitmap = new Bitmap(response.RenderedImage.ImagePath);
            var background = Color.FromArgb(246, 248, 251);
            var hasNonBackgroundPixel = false;
            for (var y = 0; y < bitmap.Height && !hasNonBackgroundPixel; y += 12)
            {
                for (var x = 0; x < bitmap.Width; x += 12)
                {
                    if (bitmap.GetPixel(x, y).ToArgb() != background.ToArgb())
                    {
                        hasNonBackgroundPixel = true;
                        break;
                    }
                }
            }

            Assert.True(hasNonBackgroundPixel);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private static WindowDescriptor CreateWindowDescriptor()
        => new(
            "0x00123456",
            "Netflix - Microsoft Edge",
            "Chrome_WidgetWin_1",
            43210,
            new WindowBounds(0, 0, 800, 600));

    private static ElementBounds Bounds(double left, double top, double width, double height)
        => new(left, top, width, height);

    private static UiElementSnapshot Snapshot(
        string path,
        string uiPath,
        string controlType,
        string? name = null,
        string? automationId = null,
        string? className = null,
        bool isEnabled = true,
        bool isOffscreen = false,
        bool hasKeyboardFocus = false,
        bool isKeyboardFocusable = false,
        bool isSelected = false,
        IReadOnlyList<string>? actions = null,
        ElementBounds? bounds = null,
        IReadOnlyList<UiElementSnapshot>? children = null)
        => new(
            path,
            uiPath,
            name ?? string.Empty,
            controlType,
            automationId ?? string.Empty,
            className ?? string.Empty,
            isEnabled,
            isOffscreen,
            hasKeyboardFocus,
            isKeyboardFocusable,
            isSelected,
            actions ?? [],
            bounds,
            children ?? []);
}
