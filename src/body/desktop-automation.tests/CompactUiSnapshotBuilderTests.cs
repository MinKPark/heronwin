using System.Drawing;
using Xunit;

namespace HeronWin.Body.DesktopAutomation.Tests;

public sealed class CompactUiSnapshotBuilderTests
{
    [Fact]
    public void BuildWindowResponse_PreservesMeaningfulNodesWithoutBudgetLimit()
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
            includeImage: false);

        var json = CompactUiSnapshotJson.Serialize(response);

        Assert.Contains("\"compactTree\"", json, StringComparison.Ordinal);
        Assert.Contains("\"llmTree\"", json, StringComparison.Ordinal);
        Assert.Contains("Address and search bar", json, StringComparison.Ordinal);
        Assert.Contains("watching?", json, StringComparison.Ordinal);
        Assert.Contains("Boyfriend on Demand", json, StringComparison.Ordinal);
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
            includeImage: false);

        var json = CompactUiSnapshotJson.Serialize(response);

        Assert.Contains("\"hasKeyboardFocus\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"isSelected\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"uiPath\":\"1/0/0/4\"", json, StringComparison.Ordinal);
        Assert.Equal("1/0/0/4", response.LlmTree.UiPath);
        Assert.Contains("focused", response.LlmTree.State ?? []);
        Assert.Contains("selected", response.LlmTree.State ?? []);
        Assert.Contains("focusable", response.LlmTree.State ?? []);
        Assert.Equal("1/0/0/4/0", Assert.Single(response.LlmTree.Children!).UiPath);
    }

    [Fact]
    public void BuildWindowResponse_UsesAutomationIdInLlmTree_WhenNameIsMissing()
    {
        var response = CompactUiSnapshotBuilder.BuildWindowResponse(
            CreateWindowDescriptor(),
            Snapshot(
                "root",
                "root",
                "Window",
                name: "Contoso",
                children:
                [
                    Snapshot(
                        "0",
                        "0",
                        "Button",
                        automationId: "PrimaryActionButton",
                        actions: ["invoke"])
                ]),
            includeImage: false);

        var button = Assert.Single(response.LlmTree.Children!);
        Assert.Null(button.Name);
        Assert.Equal("PrimaryActionButton", button.AutomationId);
    }

    [Fact]
    public void BuildWindowResponse_RendersImageIgnoringNodesWithoutBounds()
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
                includeImage: true,
                debugArtifactDirectory: tempDirectory);

            Assert.NotNull(response.RenderedImage);
            Assert.True(File.Exists(response.RenderedImage!.ImagePath));
            Assert.Equal(800, response.RenderedImage.ImageSize.Width);
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

    [Fact]
    public void GetRenderableImageNodes_PrefersDeeperNode_WhenParentAndChildShareBounds()
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
                        "1",
                        "1",
                        "Pane",
                        name: "Wrapper",
                        bounds: Bounds(0, 0, 800, 600),
                        children:
                        [
                            Snapshot(
                                "1/0",
                                "1/0",
                                "Document",
                                name: "Netflix",
                                bounds: Bounds(0, 0, 800, 600),
                                children:
                                [
                                    Snapshot(
                                        "1/0/0",
                                        "1/0/0",
                                        "Text",
                                        name: "Who's watching?",
                                        bounds: Bounds(120, 80, 240, 32)),
                                ]),
                        ]),
                ]),
            includeImage: false);

        var nodes = CompactUiSnapshotBuilder.GetRenderableImageNodes(response.CompactTree);

        Assert.DoesNotContain(nodes, node => string.Equals(node.Path, "1", StringComparison.Ordinal));
        Assert.Contains(nodes, node => string.Equals(node.Path, "1/0", StringComparison.Ordinal));
        Assert.Contains(nodes, node => string.Equals(node.Path, "1/0/0", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildWindowResponse_RetainsFocusedModalActions_WhenCloseButtonConsumesBudget()
    {
        var modal = Snapshot(
            "1/0/0/0/0/0/0/0/0/0",
            "1/0/0/0/0/0/0/0/0/0",
            "Window",
            name: "Confirm it's you",
            className: "confirmation-modal",
            actions: ["close"],
            bounds: Bounds(120, 80, 520, 420),
            children:
            [
                Snapshot(
                    "1/0/0/0/0/0/0/0/0/0/0",
                    "1/0/0/0/0/0/0/0/0/0/0",
                    "Button",
                    name: "Click to close modal",
                    className: "close-button",
                    hasKeyboardFocus: true,
                    isKeyboardFocusable: true,
                    actions: ["focus", "invoke"],
                    bounds: Bounds(580, 100, 36, 36)),
                Snapshot(
                    "1/0/0/0/0/0/0/0/0/0/1",
                    "1/0/0/0/0/0/0/0/0/0/1",
                    "Text",
                    name: "First, let's make sure it's you",
                    bounds: Bounds(150, 150, 320, 40)),
                Snapshot(
                    "1/0/0/0/0/0/0/0/0/0/2",
                    "1/0/0/0/0/0/0/0/0/0/2",
                    "Text",
                    name: "Before we make any changes, we'll just need a quick confirmation.",
                    bounds: Bounds(150, 190, 420, 24)),
                Snapshot(
                    "1/0/0/0/0/0/0/0/0/0/3",
                    "1/0/0/0/0/0/0/0/0/0/3",
                    "Button",
                    name: "Email a code",
                    className: "verification-option",
                    isKeyboardFocusable: true,
                    actions: ["focus", "invoke"],
                    bounds: Bounds(150, 220, 360, 64)),
                Snapshot(
                    "1/0/0/0/0/0/0/0/0/0/4",
                    "1/0/0/0/0/0/0/0/0/0/4",
                    "Button",
                    name: "Text a code",
                    className: "verification-option",
                    isKeyboardFocusable: true,
                    actions: ["focus", "invoke"],
                    bounds: Bounds(150, 300, 360, 64)),
                Snapshot(
                    "1/0/0/0/0/0/0/0/0/0/5",
                    "1/0/0/0/0/0/0/0/0/0/5",
                    "Button",
                    name: "Confirm password",
                    className: "verification-option",
                    isKeyboardFocusable: true,
                    actions: ["focus", "invoke"],
                    bounds: Bounds(150, 380, 360, 56)),
                Snapshot(
                    "1/0/0/0/0/0/0/0/0/0/6",
                    "1/0/0/0/0/0/0/0/0/0/6",
                    "Text",
                    name: "Need help? ",
                    bounds: Bounds(150, 455, 80, 24)),
                Snapshot(
                    "1/0/0/0/0/0/0/0/0/0/7",
                    "1/0/0/0/0/0/0/0/0/0/7",
                    "Hyperlink",
                    name: "Visit the Help Center",
                    isKeyboardFocusable: true,
                    actions: ["focus", "invoke"],
                    bounds: Bounds(150, 455, 180, 24)),
            ]);
        var tree = Snapshot(
            "root",
            "root",
            "Window",
            name: "Account Profile Lock - Netflix - Microsoft Edge",
            className: "Chrome_WidgetWin_1",
            actions: ["close", "maximize", "restore"],
            bounds: Bounds(0, 0, 800, 600),
            children:
            [
                Snapshot(
                    "1",
                    "1",
                    "ToolBar",
                    name: "App bar",
                    className: "EdgeToolbarView",
                    actions: ["scroll_into_view"],
                    bounds: Bounds(0, 0, 800, 60),
                    children:
                    [
                        Snapshot(
                            "1/0",
                            "1/0",
                            "Button",
                            name: "Back",
                            className: "BackForwardButton",
                            isKeyboardFocusable: true,
                            actions: ["focus", "invoke"],
                            bounds: Bounds(10, 10, 40, 40)),
                        Snapshot(
                            "1/1",
                            "1/1",
                            "Edit",
                            name: "Address and search bar",
                            className: "OmniboxViewViews",
                            hasKeyboardFocus: false,
                            isKeyboardFocusable: true,
                            actions: ["focus", "set_value"],
                            bounds: Bounds(60, 10, 620, 40)),
                        Snapshot(
                            "1/2",
                            "1/2",
                            "Button",
                            name: "Personal Profile",
                            className: "EdgeAvatarToolbarButton",
                            isKeyboardFocusable: true,
                            actions: ["focus", "invoke"],
                            bounds: Bounds(700, 10, 40, 40)),
                    ]),
                NestModal(modal),
            ]);

        var response = CompactUiSnapshotBuilder.BuildWindowResponse(
            CreateWindowDescriptor(),
            tree,
            includeImage: false);

        var json = CompactUiSnapshotJson.Serialize(response);

        Assert.Contains("Click to close modal", json, StringComparison.Ordinal);
        Assert.Contains("Email a code", json, StringComparison.Ordinal);
        Assert.Contains("Text a code", json, StringComparison.Ordinal);
        Assert.Contains("Confirm password", json, StringComparison.Ordinal);
        Assert.Contains("Visit the Help Center", json, StringComparison.Ordinal);
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

    private static UiElementSnapshot NestModal(UiElementSnapshot modal)
        => Snapshot(
            "1/0",
            "1/0",
            "Document",
            name: "Account Profile Lock - Netflix",
            automationId: "RootWebArea",
            actions: ["focus", "scroll"],
            bounds: Bounds(0, 60, 800, 540),
            children:
            [
                Snapshot(
                    "1/0/0",
                    "1/0/0",
                    "Group",
                    className: "appMountPoint",
                    actions: ["invoke"],
                    bounds: Bounds(0, 60, 800, 540),
                    children:
                    [
                        Snapshot(
                            "1/0/0/0",
                            "1/0/0/0",
                            "Pane",
                            className: "overlay-layer",
                            actions: ["scroll_into_view"],
                            bounds: Bounds(0, 60, 800, 540),
                            children:
                            [
                                Snapshot(
                                    "1/0/0/0/0",
                                    "1/0/0/0/0",
                                    "Pane",
                                    className: "overlay-root",
                                    actions: ["scroll_into_view"],
                                    bounds: Bounds(0, 60, 800, 540),
                                    children:
                                    [
                                        Snapshot(
                                            "1/0/0/0/0/0",
                                            "1/0/0/0/0/0",
                                            "Pane",
                                            className: "overlay-centerer",
                                            actions: ["scroll_into_view"],
                                            bounds: Bounds(0, 60, 800, 540),
                                            children:
                                            [
                                                Snapshot(
                                                    "1/0/0/0/0/0/0",
                                                    "1/0/0/0/0/0/0",
                                                    "Group",
                                                    className: "overlay-group",
                                                    actions: ["scroll_into_view"],
                                                    bounds: Bounds(0, 60, 800, 540),
                                                    children:
                                                    [
                                                        Snapshot(
                                                            "1/0/0/0/0/0/0/0",
                                                            "1/0/0/0/0/0/0/0",
                                                            "Group",
                                                            className: "dialog-shell",
                                                            actions: ["scroll_into_view"],
                                                            bounds: Bounds(100, 70, 560, 460),
                                                            children:
                                                            [
                                                                Snapshot(
                                                                    "1/0/0/0/0/0/0/0/0",
                                                                    "1/0/0/0/0/0/0/0/0",
                                                                    "Pane",
                                                                    className: "dialog-inner",
                                                                    actions: ["scroll_into_view"],
                                                                    bounds: Bounds(110, 75, 540, 440),
                                                                    children: [modal]),
                                                            ]),
                                                    ]),
                                            ]),
                                    ]),
                            ]),
                    ]),
            ]);

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
