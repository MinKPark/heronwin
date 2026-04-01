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
    public void BuildToolSpecificGuidance_ReturnsHint_ForGenericContainerClick()
    {
        const string toolOutput = """
        {
          "ClickedElement": {
            "ControlType": "Group",
            "Name": "",
            "AutomationId": "appMountPoint"
          }
        }
        """;

        var actual = AgentRunner.BuildToolSpecificGuidance(
            "click_selected_window_element",
            toolOutput,
            new Dictionary<string, object?>());

        Assert.NotNull(actual);
        Assert.Contains("invoke_selected_window_element", actual);
    }

    [Fact]
    public void BuildToolSpecificGuidance_ReturnsNull_ForNamedButtonClick()
    {
        const string toolOutput = """
        {
          "ClickedElement": {
            "ControlType": "Button",
            "Name": "Play",
            "AutomationId": "play-button"
          }
        }
        """;

        var actual = AgentRunner.BuildToolSpecificGuidance(
            "click_selected_window_element",
            toolOutput,
            new Dictionary<string, object?>());

        Assert.Null(actual);
    }
}
