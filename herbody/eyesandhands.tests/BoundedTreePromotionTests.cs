using Xunit;

namespace HeronWin.HerBody.EyesAndHands.Tests;

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
    public void UiElementSnapshot_ExposesUiPathAliasForOriginalPath()
    {
        var snapshot = new UiElementSnapshot(
            "1/0/3",
            "Play",
            "Button",
            "PlayButton",
            "Button",
            IsEnabled: true,
            IsOffscreen: false,
            HasKeyboardFocus: false,
            IsKeyboardFocusable: true,
            AvailableActions: ["focus", "invoke"],
            Bounds: null,
            Children: []);

        Assert.Equal("1/0/3", snapshot.Path);
        Assert.Equal("1/0/3", snapshot.UiPath);
        Assert.Contains("\"UiPath\": \"1/0/3\"", WindowAutomation.Serialize(snapshot));
    }
}
