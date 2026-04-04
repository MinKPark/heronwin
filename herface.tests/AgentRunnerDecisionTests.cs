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
    public void HasExplicitlyUnresolvedOutcome_ReturnsTrue_ForIncompleteRequest()
    {
        var actual = AgentRunner.HasExplicitlyUnresolvedOutcome(
            "The address bar is visible and the original request is not complete yet.");

        Assert.True(actual);
    }

    [Fact]
    public void AlignReplyOutcomeConsistency_UsesLogSummary_WhenSayContradictsLog()
    {
        var reply = new AgentReply(
            LogText: "The screenshot shows Edge on a new tab page. The original request is not complete yet.",
            SpokenText: "Netflix is open in Edge now.",
            RawText: "{}");

        var actual = AgentRunner.AlignReplyOutcomeConsistency(reply);

        Assert.Equal("The screenshot shows Edge on a new tab page.", actual.SpokenText);
        Assert.Equal(reply.LogText, actual.LogText);
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
    public void BuildRuntimeToolPolicy_ReturnsBrowserNewTabGuidance_WhenInputToolExists()
    {
        var actual = AgentRunner.BuildRuntimeToolPolicy(
            [
                new ToolDefinition("send_input_to_window", "desc", default)
            ]);

        Assert.NotNull(actual);
        Assert.Contains("open a new tab first", actual!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("address bar", actual, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildRuntimeToolPolicy_ReturnsBrowserShortcutGuidance_WhenInvokeAndInputToolsExist()
    {
        var actual = AgentRunner.BuildRuntimeToolPolicy(
            [
                new ToolDefinition("invoke_selected_window_element", "desc", default),
                new ToolDefinition("focus_selected_window_element", "desc", default),
                new ToolDefinition("send_input_to_window", "desc", default)
            ]);

        Assert.NotNull(actual);
        Assert.Contains("Control+L", actual!, StringComparison.Ordinal);
        Assert.Contains("Escape or F11", actual, StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("windowHandle", actual, StringComparison.Ordinal);
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
            """{"SelectedWindow":null}""",
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
            """{"SelectedWindow":null}""",
            new Dictionary<string, object?>());

        Assert.NotNull(actual);
        Assert.Contains("Do not imply that the app opened successfully", actual!, StringComparison.Ordinal);
        Assert.Contains("do not assume a same-title app window exists", actual, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("launch failed", actual, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildLaunchFollowUpSelectionArguments_UsesWindowHandle_WhenAvailable()
    {
        var actual = AgentRunner.TryBuildLaunchFollowUpSelectionArguments(
            """{"SelectedWindow":{"Handle":"0x00123456","Title":"Netflix - Microsoft Edge"}}""");

        Assert.NotNull(actual);
        Assert.Equal("0x00123456", actual!["windowHandle"]);
        Assert.False(actual.ContainsKey("titleContains"));
    }

    [Fact]
    public void TryBuildLaunchFollowUpSelectionArguments_FallsBackToTitle_WhenHandleIsMissing()
    {
        var actual = AgentRunner.TryBuildLaunchFollowUpSelectionArguments(
            """{"SelectedWindow":{"Title":"Netflix - Microsoft Edge"}}""");

        Assert.NotNull(actual);
        Assert.Equal("Netflix - Microsoft Edge", actual!["titleContains"]);
        Assert.False(actual.ContainsKey("windowHandle"));
    }

    [Fact]
    public void TryBuildLaunchFollowUpSelectionArguments_ReturnsNull_WhenSelectedWindowIsNull()
    {
        var actual = AgentRunner.TryBuildLaunchFollowUpSelectionArguments(
            """{"SelectedWindow":null}""");

        Assert.Null(actual);
    }

    [Fact]
    public void TryRewriteSelectWindowArguments_UsesUniqueHandleFromRecentWindowList()
    {
        var recentListWindowsOutput =
            """
            {
              "SelectedWindowHandle": null,
              "Windows": [
                {
                  "Handle": "0x00060A88",
                  "Title": "Netflix - Microsoft Edge",
                  "ClassName": "Chrome_WidgetWin_1",
                  "ProcessId": 43600,
                  "Bounds": { "Left": -1928, "Top": -8, "Width": 1936, "Height": 1048 },
                  "IsSelected": false
                },
                {
                  "Handle": "0x006E0A06",
                  "Title": "Microsoft Edge",
                  "ClassName": "ApplicationFrameWindow",
                  "ProcessId": 25624,
                  "Bounds": { "Left": -32000, "Top": -32000, "Width": 160, "Height": 28 },
                  "IsSelected": false
                }
              ]
            }
            """;

        var rewritten = AgentRunner.TryRewriteSelectWindowArguments(
            new Dictionary<string, object?> { ["titleContains"] = "Microsoft Edge" },
            recentListWindowsOutput,
            out var actualArgs);

        Assert.True(rewritten);
        Assert.Equal("0x00060A88", actualArgs["windowHandle"]);
        Assert.False(actualArgs.ContainsKey("titleContains"));
    }

    [Fact]
    public void TryRewriteSelectWindowArguments_DoesNotRewriteWhenMultipleUsableMatchesRemain()
    {
        var recentListWindowsOutput =
            """
            {
              "SelectedWindowHandle": null,
              "Windows": [
                {
                  "Handle": "0x00060A88",
                  "Title": "Netflix - Microsoft Edge",
                  "ClassName": "Chrome_WidgetWin_1",
                  "ProcessId": 43600,
                  "Bounds": { "Left": -1928, "Top": -8, "Width": 1936, "Height": 1048 },
                  "IsSelected": false
                },
                {
                  "Handle": "0x00070A90",
                  "Title": "Docs - Microsoft Edge",
                  "ClassName": "Chrome_WidgetWin_1",
                  "ProcessId": 43601,
                  "Bounds": { "Left": 0, "Top": 0, "Width": 1936, "Height": 1048 },
                  "IsSelected": false
                }
              ]
            }
            """;

        var rewritten = AgentRunner.TryRewriteSelectWindowArguments(
            new Dictionary<string, object?> { ["titleContains"] = "Microsoft Edge" },
            recentListWindowsOutput,
            out var actualArgs);

        Assert.False(rewritten);
        Assert.Equal("Microsoft Edge", actualArgs["titleContains"]);
        Assert.False(actualArgs.ContainsKey("windowHandle"));
    }

    [Fact]
    public void ShouldPrimeBrowserAddressBarForUrlEntry_ReturnsTrue_ForBrowserWindowContext()
    {
        var actual = AgentRunner.ShouldPrimeBrowserAddressBarForUrlEntry(
            new Dictionary<string, object?> { ["text"] = "https://www.netflix.com" },
            """{"Window":{"Handle":"0x00060A88","Title":"Netflix - Microsoft Edge","ClassName":"Chrome_WidgetWin_1"}}""",
            null);

        Assert.True(actual);
    }

    [Fact]
    public void ShouldPrimeBrowserAddressBarForUrlEntry_ReturnsTrue_ForFocusedAddressBar()
    {
        var actual = AgentRunner.ShouldPrimeBrowserAddressBarForUrlEntry(
            new Dictionary<string, object?> { ["text"] = "https://www.netflix.com" },
            null,
            """{"FocusedElement":{"Path":"1/0","Name":"Address and search bar","ControlType":"Edit","ClassName":"OmniboxViewViews"}}""");

        Assert.True(actual);
    }

    [Fact]
    public void ShouldPrimeBrowserAddressBarForUrlEntry_ReturnsFalse_ForNonUrlText()
    {
        var actual = AgentRunner.ShouldPrimeBrowserAddressBarForUrlEntry(
            new Dictionary<string, object?> { ["text"] = "netflix" },
            """{"Window":{"Handle":"0x00060A88","Title":"Netflix - Microsoft Edge","ClassName":"Chrome_WidgetWin_1"}}""",
            null);

        Assert.False(actual);
    }

    [Fact]
    public void ShouldPrimeBrowserAddressBarForUrlEntry_ReturnsFalse_WhenFocusedElementIsNotAddressBar()
    {
        var actual = AgentRunner.ShouldPrimeBrowserAddressBarForUrlEntry(
            new Dictionary<string, object?> { ["text"] = "https://www.netflix.com" },
            """{"Window":{"Handle":"0x00060A88","Title":"Netflix - Microsoft Edge","ClassName":"Chrome_WidgetWin_1"}}""",
            """{"FocusedElement":{"Path":"focused","Name":"Search box","ControlType":"Edit","ClassName":"SearchBox"}}""");

        Assert.False(actual);
    }

    [Fact]
    public void ShouldOpenNewTabBeforeBrowserUrlEntry_ReturnsTrue_ForEdgeWebsiteRequest()
    {
        var actual = AgentRunner.ShouldOpenNewTabBeforeBrowserUrlEntry(
            "Open the Netflix website in Edge.",
            new Dictionary<string, object?> { ["text"] = "https://www.netflix.com" },
            """{"Window":{"Handle":"0x00060A88","Title":"YouTube - Personal - Microsoft Edge","ClassName":"Chrome_WidgetWin_1"}}""");

        Assert.True(actual);
    }

    [Fact]
    public void ShouldOpenNewTabBeforeBrowserUrlEntry_ReturnsTrue_ForRootWindowToolOutput()
    {
        var actual = AgentRunner.ShouldOpenNewTabBeforeBrowserUrlEntry(
            "Open the Netflix website in Edge.",
            new Dictionary<string, object?> { ["text"] = "https://www.netflix.com" },
            """{"Handle":"0x00060A88","Title":"YouTube - Personal - Microsoft Edge","ClassName":"Chrome_WidgetWin_1"}""");

        Assert.True(actual);
    }

    [Fact]
    public void ShouldOpenNewTabBeforeBrowserUrlEntry_ReturnsFalse_WhenUserWantsCurrentTab()
    {
        var actual = AgentRunner.ShouldOpenNewTabBeforeBrowserUrlEntry(
            "Open the Netflix website in the current tab.",
            new Dictionary<string, object?> { ["text"] = "https://www.netflix.com" },
            """{"Window":{"Handle":"0x00060A88","Title":"YouTube - Personal - Microsoft Edge","ClassName":"Chrome_WidgetWin_1"}}""");

        Assert.False(actual);
    }

    [Fact]
    public void TryRewriteBrowserAddressBarActionToShortcut_ReturnsTrue_ForBrowserAddressBarElement()
    {
        var actual = AgentRunner.TryRewriteBrowserAddressBarActionToShortcut(
            "invoke_selected_window_element",
            new Dictionary<string, object?> { ["elementPath"] = "1/0/0/0/0/0" },
            """
            {
              "Window": {
                "Handle": "0x00060A88",
                "Title": "YouTube - Personal - Microsoft Edge",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "1/0/0/0/0/0",
                    "UiPath": "1/0/0/0/0/0",
                    "Name": "Address and search bar",
                    "ControlType": "Edit",
                    "ClassName": "OmniboxViewViews"
                  }
                ]
              }
            }
            """,
            out var rewrittenArgs);

        Assert.True(actual);
        Assert.Equal("L", rewrittenArgs["key"]);
        Assert.Equal(new[] { "Control" }, rewrittenArgs["modifiers"]);
    }

    [Fact]
    public void TryRewriteBrowserAddressBarActionToShortcut_ReturnsFalse_ForNonAddressBarElement()
    {
        var actual = AgentRunner.TryRewriteBrowserAddressBarActionToShortcut(
            "invoke_selected_window_element",
            new Dictionary<string, object?> { ["elementPath"] = "1/0/0/0/1/0/0/0/0/0/0" },
            """
            {
              "Window": {
                "Handle": "0x00060A88",
                "Title": "YouTube - Personal - Microsoft Edge",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "1/0/0/0/1/0/0/0/0/0/0",
                    "UiPath": "1/0/0/0/1/0/0/0/0/0/0",
                    "Name": "YouTube",
                    "ControlType": "Document"
                  }
                ]
              }
            }
            """,
            out _);

        Assert.False(actual);
    }

    [Fact]
    public void ShouldExitBrowserFullscreenBeforeBrowserShortcut_ReturnsTrue_ForFullscreenBrowserSnapshot()
    {
        var actual = AgentRunner.ShouldExitBrowserFullscreenBeforeBrowserShortcut(
            """
            {
              "Window": {
                "Handle": "0x00060A88",
                "Title": "YouTube - Personal - Microsoft Edge",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "1/0/0/0/1/0/0/0/0/0/0/0/1/2/0",
                    "UiPath": "1/0/0/0/1/0/0/0/0/0/0/0/1/2/0",
                    "Name": "YouTube Video Player in Fullscreen",
                    "ControlType": "Group",
                    "ClassName": "ytp-fullscreen"
                  }
                ]
              }
            }
            """);

        Assert.True(actual);
    }

    [Fact]
    public void ShouldExitBrowserFullscreenBeforeBrowserShortcut_ReturnsFalse_ForNormalBrowserSnapshot()
    {
        var actual = AgentRunner.ShouldExitBrowserFullscreenBeforeBrowserShortcut(
            """
            {
              "Window": {
                "Handle": "0x00060A88",
                "Title": "Netflix - Microsoft Edge",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "1/0/0/0/0/0",
                    "UiPath": "1/0/0/0/0/0",
                    "Name": "Address and search bar",
                    "ControlType": "Edit",
                    "ClassName": "OmniboxViewViews"
                  }
                ]
              }
            }
            """);

        Assert.False(actual);
    }

    [Fact]
    public void DescribeLaunchFollowUpSelectionTarget_FormatsTitleAndHandle()
    {
        var actual = AgentRunner.DescribeLaunchFollowUpSelectionTarget(
            """{"SelectedWindow":{"Handle":"0x00123456","Title":"Netflix - Microsoft Edge"}}""");

        Assert.Equal("Netflix - Microsoft Edge (0x00123456)", actual);
    }

    [Fact]
    public void DescribePrimaryWindowFromToolOutput_UsesWindowProperty_WhenSelectedWindowIsAbsent()
    {
        var actual = AgentRunner.DescribePrimaryWindowFromToolOutput(
            """{"Window":{"Handle":"0x00ABCDEF","Title":"Settings"}}""");

        Assert.Equal("Settings (0x00ABCDEF)", actual);
    }

    [Fact]
    public void DescribePrimaryWindowFromToolOutput_UsesRootWindowShape_WhenNestedWindowIsAbsent()
    {
        var actual = AgentRunner.DescribePrimaryWindowFromToolOutput(
            """{"Handle":"0x00ABCDEF","Title":"Settings"}""");

        Assert.Equal("Settings (0x00ABCDEF)", actual);
    }

    [Fact]
    public void BuildToolStepNarration_ReturnsSearchLaunchSentence()
    {
        var actual = AgentRunner.BuildToolStepNarration(
            "launch_app_via_taskbar_search",
            new Dictionary<string, object?> { ["appName"] = "Netflix" });

        Assert.Equal("I'm launching Netflix from Search.", actual);
    }

    [Fact]
    public void BuildToolStepNarration_ReturnsShortcutSentence_ForModifiedKey()
    {
        using var modifiers = JsonDocument.Parse("""["Control"]""");
        var actual = AgentRunner.BuildToolStepNarration(
            "send_input_to_window",
            new Dictionary<string, object?> { ["key"] = "L", ["modifiers"] = modifiers.RootElement.Clone() });

        Assert.Equal("I'm pressing Control plus L.", actual);
    }

    [Fact]
    public void BuildToolStepNarration_ReturnsUrlSentence_ForTypedUrl()
    {
        var actual = AgentRunner.BuildToolStepNarration(
            "send_input_to_window",
            new Dictionary<string, object?> { ["text"] = "https://www.netflix.com" });

        Assert.Equal("I'm typing the URL.", actual);
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
