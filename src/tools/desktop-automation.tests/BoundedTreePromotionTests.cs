using Xunit;

namespace HeronWin.Tools.DesktopAutomation.Tests;

public sealed class BoundedTreePromotionTests
{
    [Fact]
    public void ShouldPromoteChildrenInBoundedTree_PromotesKnownBrowserShellContainers()
    {
        var result = WindowAutomation.ShouldPromoteChildrenInBoundedTree(
            "Pane",
            "BrowserRootView",
            "Home - Netflix",
            string.Empty,
            ["scroll_into_view"],
            isKeyboardFocusable: false,
            hasKeyboardFocus: false);

        Assert.True(result);
    }

    [Fact]
    public void ShouldPromoteChildrenInBoundedTree_PromotesAnonymousStructuralContainers()
    {
        var result = WindowAutomation.ShouldPromoteChildrenInBoundedTree(
            "Group",
            string.Empty,
            string.Empty,
            "appMountPoint",
            ["invoke", "scroll_into_view"],
            isKeyboardFocusable: false,
            hasKeyboardFocus: false);

        Assert.True(result);
    }

    [Fact]
    public void ShouldPromoteChildrenInBoundedTree_DoesNotPromoteActionableControls()
    {
        var result = WindowAutomation.ShouldPromoteChildrenInBoundedTree(
            "Hyperlink",
            "slider-refocus",
            "Boyfriend on Demand",
            string.Empty,
            ["focus", "invoke"],
            isKeyboardFocusable: true,
            hasKeyboardFocus: false);

        Assert.False(result);
    }

    [Fact]
    public void ShouldPromoteChildrenInBoundedTree_DoesNotPromoteDocumentRoots()
    {
        var result = WindowAutomation.ShouldPromoteChildrenInBoundedTree(
            "Document",
            string.Empty,
            "Home - Netflix",
            "RootWebArea",
            ["focus", "scroll", "scroll_into_view", "set_value"],
            isKeyboardFocusable: true,
            hasKeyboardFocus: true);

        Assert.False(result);
    }

    [Fact]
    public void HasOnlyStructuralActions_RejectsInteractiveActions()
    {
        var result = WindowAutomation.HasOnlyStructuralActions(["scroll_into_view", "invoke"]);

        Assert.False(result);
    }

    [Fact]
    public void ShouldOmitElementInBoundedTree_OmitsAnonymousLeafContainers()
    {
        var result = WindowAutomation.ShouldOmitElementInBoundedTree(
            "Group",
            string.Empty,
            "appMountPoint",
            ["invoke", "scroll_into_view"],
            isKeyboardFocusable: false,
            hasKeyboardFocus: false,
            childCount: 0);

        Assert.True(result);
    }

    [Fact]
    public void ShouldOmitElementInBoundedTree_KeepsNamedLeafContainers()
    {
        var result = WindowAutomation.ShouldOmitElementInBoundedTree(
            "Group",
            "Play",
            string.Empty,
            ["invoke"],
            isKeyboardFocusable: false,
            hasKeyboardFocus: false,
            childCount: 0);

        Assert.False(result);
    }

    [Fact]
    public void HasOnlyIgnorableContainerActions_AllowsInvokeAndScroll()
    {
        var result = WindowAutomation.HasOnlyIgnorableContainerActions(["invoke", "scroll_into_view"]);

        Assert.True(result);
    }

    [Fact]
    public void UiElementSnapshot_SerializesExplicitUiPath()
    {
        var snapshot = new UiElementSnapshot(
            "1/0/3",
            "1/0/3",
            "Play",
            "Button",
            "PlayButton",
            "Button",
            IsEnabled: true,
            IsOffscreen: false,
            HasKeyboardFocus: false,
            IsKeyboardFocusable: true,
            IsSelected: false,
            AvailableActions: ["focus", "invoke"],
            Bounds: null,
            Children: []);

        Assert.Equal("1/0/3", snapshot.Path);
        Assert.Equal("1/0/3", snapshot.UiPath);
        var json = WindowAutomation.Serialize(snapshot);
        Assert.Contains("\"UiPath\": \"1/0/3\"", json);
        Assert.DoesNotContain("\"IsOffscreen\": false", json);
        Assert.DoesNotContain("\"HasKeyboardFocus\": false", json);
        Assert.DoesNotContain("\"Children\": []", json);
        Assert.DoesNotContain("\"Bounds\": null", json);
    }

    [Fact]
    public void UiElementSnapshot_OmitsEmptyStringsAndEmptyArrays()
    {
        var snapshot = new UiElementSnapshot(
            "1/2",
            "1/2",
            "",
            "Button",
            "",
            "",
            IsEnabled: false,
            IsOffscreen: false,
            HasKeyboardFocus: false,
            IsKeyboardFocusable: false,
            IsSelected: false,
            AvailableActions: [],
            Bounds: null,
            Children: []);

        var json = WindowAutomation.Serialize(snapshot);

        Assert.Contains("\"Path\": \"1/2\"", json);
        Assert.Contains("\"UiPath\": \"1/2\"", json);
        Assert.Contains("\"ControlType\": \"Button\"", json);
        Assert.DoesNotContain("\"Name\":", json);
        Assert.DoesNotContain("\"AutomationId\":", json);
        Assert.DoesNotContain("\"ClassName\":", json);
        Assert.DoesNotContain("\"IsEnabled\": false", json);
        Assert.DoesNotContain("\"AvailableActions\": []", json);
    }
}
