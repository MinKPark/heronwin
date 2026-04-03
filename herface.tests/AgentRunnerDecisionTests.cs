using System.Text.Json;
using Xunit;

namespace HeronWin.HerFace.Tests;

public sealed class AgentRunnerDecisionTests
{
    [Theory]
    [InlineData("The UI tree is too sparse to describe the screen confidently.")]
    [InlineData("I cannot confirm the current visible screen state from the UI Automation tree.")]
    [InlineData("The current in-app screen is uncertain and I am not inferring it.")]
    [InlineData("The result is ambiguous from the currently exposed controls.")]
    public void NeedsAdditionalDesktopEvidence_ReturnsTrue_ForUncertainLanguage(string text)
    {
        var reply = new AgentReply(LogText: text, SpokenText: text, RawText: text);

        var actual = AgentRunner.NeedsAdditionalDesktopEvidence(reply);

        Assert.True(actual);
    }

    [Theory]
    [InlineData("Netflix is open and the home screen is visible with profile tiles.")]
    [InlineData("The Save button was clicked and the dialog closed.")]
    [InlineData("File Explorer is focused and the Downloads folder is selected.")]
    public void NeedsAdditionalDesktopEvidence_ReturnsFalse_ForConfidentLanguage(string text)
    {
        var reply = new AgentReply(LogText: text, SpokenText: text, RawText: text);

        var actual = AgentRunner.NeedsAdditionalDesktopEvidence(reply);

        Assert.False(actual);
    }

    [Fact]
    public void NeedsAdditionalDesktopEvidence_ReturnsFalse_ForBlankReply()
    {
        var reply = new AgentReply(LogText: string.Empty, SpokenText: string.Empty, RawText: string.Empty);

        var actual = AgentRunner.NeedsAdditionalDesktopEvidence(reply);

        Assert.False(actual);
    }

    [Fact]
    public void ShouldCollectFocusSnapshotAfterAction_ReturnsTrue_ForNavigationKeys()
    {
        var actual = AgentRunner.ShouldCollectFocusSnapshotAfterAction(
            "send_input_to_window",
            new Dictionary<string, object?> { ["key"] = "Tab" });

        Assert.True(actual);
    }

    [Fact]
    public void BuildRuntimeToolPolicy_ReturnsInvokePreference_WhenRelevantToolsExist()
    {
        var actual = AgentRunner.BuildRuntimeToolPolicy(
            [
                new ToolDefinition("invoke_selected_window_element", "desc", default),
                new ToolDefinition("send_input_to_window", "desc", default)
            ]);

        Assert.NotNull(actual);
        Assert.Contains("invoke_selected_window_element", actual!, StringComparison.Ordinal);
        Assert.Contains("send_input_to_window only", actual, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildRuntimeToolPolicy_ReturnsLaunchContinuationGuidance_WhenLaunchToolsExist()
    {
        var actual = AgentRunner.BuildRuntimeToolPolicy(
            [
                new ToolDefinition("list_windows", "desc", default),
                new ToolDefinition("select_window", "desc", default),
                new ToolDefinition("list_taskbar_elements", "desc", default),
                new ToolDefinition("select_taskbar_app", "desc", default),
                new ToolDefinition("launch_app_via_taskbar_search", "desc", default)
            ]);

        Assert.NotNull(actual);
        Assert.Contains("do not stop after saying you are checking whether it is already open", actual!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("list_taskbar_elements", actual, StringComparison.Ordinal);
        Assert.Contains("select_taskbar_app", actual, StringComparison.Ordinal);
        Assert.Contains("launch_app_via_taskbar_search", actual, StringComparison.Ordinal);
        Assert.Contains("ask the user to launch the app manually", actual, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolSpecificGuidance_ReturnsHint_ForNavigationKeyFallback()
    {
        var actual = AgentRunner.BuildToolSpecificGuidance(
            "send_input_to_window",
            "{}",
            new Dictionary<string, object?> { ["key"] = "Tab" });

        Assert.NotNull(actual);
        Assert.Contains("invoke_selected_window_element", actual!, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildToolSpecificGuidance_ReturnsFailureHint_ForTaskbarAppLaunchWithoutSelectedWindow()
    {
        var actual = AgentRunner.BuildToolSpecificGuidance(
            "select_taskbar_app",
            """{"selectedWindow":null}""",
            new Dictionary<string, object?>());

        Assert.NotNull(actual);
        Assert.Contains("Do not imply that the app opened successfully", actual!, StringComparison.Ordinal);
        Assert.Contains("launch failed", actual, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolSpecificGuidance_ReturnsFailureHint_ForTaskbarSearchLaunchWithoutSelectedWindow()
    {
        var actual = AgentRunner.BuildToolSpecificGuidance(
            "launch_app_via_taskbar_search",
            """{"selectedWindow":null}""",
            new Dictionary<string, object?>());

        Assert.NotNull(actual);
        Assert.Contains("Do not imply that the app opened successfully", actual!, StringComparison.Ordinal);
        Assert.Contains("launch failed", actual, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolSpecificGuidance_ReturnsNull_ForModifiedShortcut()
    {
        var actual = AgentRunner.BuildToolSpecificGuidance(
            "send_input_to_window",
            "{}",
            new Dictionary<string, object?> { ["key"] = "F4", ["modifiers"] = JsonDocument.Parse("""["Alt"]""").RootElement });

        Assert.Null(actual);
    }

    [Fact]
    public void ExtractLikelyNextActions_ParsesAssistantList()
    {
        var actual = AgentRunner.ExtractLikelyNextActions(
            "Netflix opened successfully. Likely next actions: open Search, press Play on HUNINT, or open the profile menu.");

        Assert.Equal(
            ["open Search", "press Play on HUNINT", "open the profile menu"],
            actual);
    }

    [Fact]
    public void BuildOrdinalActionReferenceSummary_UsesMostRecentLikelyNextActions()
    {
        var history = new List<AgentMessage>
        {
            new AgentMessage.Assistant("{\"say\":\"Netflix opened successfully. Likely next actions: open Search, press Play on HUNINT, or open the profile menu.\",\"log\":\"\"}")
        };

        var actual = AgentRunner.BuildOrdinalActionReferenceSummary("No, do second action.", history);

        Assert.NotNull(actual);
        Assert.Contains("press Play on HUNINT", actual!, StringComparison.Ordinal);
        Assert.Contains("1) open Search.", actual, StringComparison.Ordinal);
        Assert.Contains("2) press Play on HUNINT.", actual, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildOrdinalActionReferenceSummary_AsksForClarification_WhenNoPriorListExists()
    {
        var actual = AgentRunner.BuildOrdinalActionReferenceSummary(
            "Do second action.",
            [new AgentMessage.Assistant("{\"say\":\"What should I do next?\",\"log\":\"\"}")]);

        Assert.NotNull(actual);
        Assert.Contains("ask for clarification", actual!, StringComparison.OrdinalIgnoreCase);
    }
}
