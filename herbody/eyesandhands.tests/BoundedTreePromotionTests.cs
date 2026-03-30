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
            ["scroll_into_view"],
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
}
