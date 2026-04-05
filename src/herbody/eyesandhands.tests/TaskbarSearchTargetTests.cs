using Xunit;

namespace HeronWin.HerBody.EyesAndHands.Tests;

public sealed class TaskbarSearchTargetTests
{
    [Fact]
    public void ResolveTaskbarSearchTarget_PrefersSearchButtonAutomationId()
    {
        var elements = new[]
        {
            CreateElement(path: "2/0/1", name: "Start", automationId: "StartButton"),
            CreateElement(path: "2/0/2", name: "Search", automationId: "SearchButton"),
            CreateElement(path: "2/0/3", name: "Search Highlights", automationId: "SomeOtherId"),
        };

        var resolved = WindowAutomation.ResolveTaskbarSearchTarget(elements);

        Assert.Equal("2/0/2", resolved.Path);
        Assert.Equal("SearchButton", resolved.AutomationId);
    }

    [Fact]
    public void ResolveTaskbarSearchTarget_FallsBackToVisibleSearchLabel()
    {
        var elements = new[]
        {
            CreateElement(path: "2/0/1", name: "Widgets", automationId: "WidgetsButton"),
            CreateElement(path: "2/0/2", name: "Search", automationId: ""),
        };

        var resolved = WindowAutomation.ResolveTaskbarSearchTarget(elements);

        Assert.Equal("2/0/2", resolved.Path);
    }

    [Fact]
    public void ResolveTaskbarSearchTarget_ThrowsWhenSearchControlIsMissing()
    {
        var elements = new[]
        {
            CreateElement(path: "2/0/1", name: "Start", automationId: "StartButton"),
            CreateElement(path: "2/0/2", name: "Visual Studio Code", automationId: "Appid: Microsoft.VisualStudioCode", isAppButton: true),
        };

        var error = Assert.Throws<InvalidOperationException>(() => WindowAutomation.ResolveTaskbarSearchTarget(elements));

        Assert.Contains("Search", error.Message);
    }

    private static TaskbarElementSummary CreateElement(
        string path,
        string name,
        string automationId,
        bool isAppButton = false)
    {
        return new TaskbarElementSummary(
            path,
            name,
            "Button",
            automationId,
            "Button",
            true,
            false,
            false,
            true,
            [],
            null,
            isAppButton);
    }
}
