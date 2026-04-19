using System.Text.Json;
using Xunit;

namespace HeronWin.Brain.Tests;

public sealed class AgentRunnerDecisionTests
{
    [Theory]
    [InlineData("The UI tree is too sparse to describe the screen confidently.")]
    [InlineData("I cannot confirm the current visible screen state from the UI Automation tree.")]
    [InlineData("The current in-app screen is uncertain and I am not inferring it.")]
    [InlineData("The result is ambiguous from the currently exposed controls.")]
    [InlineData("Understood - I'll treat UI actions as unconfirmed until I verify the new screen.")]
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
    public void HasExplicitlyUnresolvedOutcome_ReturnsTrue_ForNextStepLanguage()
    {
        var actual = AgentRunner.HasExplicitlyUnresolvedOutcome(
            "Subtitles are visible. Next step should be to open playback controls and disable them.");

        Assert.True(actual);
    }

    [Fact]
    public void HasExplicitlyUnresolvedOutcome_ReturnsTrue_ForCurlyApostropheAndPendingSearchLanguage()
    {
        var actual = AgentRunner.HasExplicitlyUnresolvedOutcome(
            "The search action didn’t take, so Boyfriend on Demand isn’t visible yet and the requested in-site search remains uncompleted.");

        Assert.True(actual);
    }

    [Fact]
    public void HasExplicitlyUnresolvedOutcome_ReturnsTrue_ForCannotOpenUntilVisibleLanguage()
    {
        var actual = AgentRunner.HasExplicitlyUnresolvedOutcome(
            "We’re on Netflix home, but Boyfriend on Demand isn’t visible yet, so I can’t open it from search results or play episode one yet.");

        Assert.True(actual);
    }

    [Fact]
    public void HasExplicitlyUnresolvedOutcome_ReturnsFalse_ForConditionalNoOp()
    {
        var actual = AgentRunner.HasExplicitlyUnresolvedOutcome(
            "Confirmed current page is Netflix Home in Edge, not a profile picker. I did not click anything because the requested condition was absent.");

        Assert.False(actual);
    }

    [Fact]
    public void HasExplicitlyUnresolvedOutcome_ReturnsFalse_ForProfileSelectionNoOpWording()
    {
        var actual = AgentRunner.HasExplicitlyUnresolvedOutcome(
            "The visible screen is the Netflix home page, not a profile selection screen. I did not click anything.");

        Assert.False(actual);
    }

    [Fact]
    public void HasExplicitlyUnresolvedOutcome_ReturnsFalse_ForExplicitConditionalNoOp()
    {
        var actual = AgentRunner.HasExplicitlyUnresolvedOutcome(
            "I did not see a profile-selection screen, so there was no profile to choose. This is a conditional no-op based on the current evidence.");

        Assert.False(actual);
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

    [Theory]
    [InlineData("Subtitles are on right now. I'm turning them off.")]
    [InlineData("자막 메뉴를 열어서 꺼볼게요.")]
    public void HasDeferredActionPromise_ReturnsTrue_ForFutureActionPromises(string text)
    {
        var actual = AgentRunner.HasDeferredActionPromise(text);

        Assert.True(actual);
    }

    [Theory]
    [InlineData("Subtitles are still visible on the current Netflix playback frame.")]
    [InlineData("현재 화면에는 자막이 계속 보이고 있어요.")]
    public void HasDeferredActionPromise_ReturnsFalse_ForStatusOnlyReplies(string text)
    {
        var actual = AgentRunner.HasDeferredActionPromise(text);

        Assert.False(actual);
    }

    [Fact]
    public void ShouldCollectFocusSnapshotAfterAction_ReturnsTrue_ForNavigationKeys()
    {
        var actual = AgentRunner.ShouldCollectFocusSnapshotAfterAction(
            "press_window_key",
            new Dictionary<string, object?> { ["key"] = "Tab" });

        Assert.True(actual);
    }

    [Fact]
    public void BuildToolSpecificGuidance_ReturnsHint_ForNavigationKeyFallback()
    {
        var actual = AgentRunner.BuildToolSpecificGuidance(
            "press_window_key",
            "{}",
            new Dictionary<string, object?> { ["key"] = "Tab" });

        Assert.NotNull(actual);
        Assert.Contains("Refresh focus or window state", actual!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("direct tool-supported target", actual, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildFocusedElementContinuationGuidance_ReturnsHint_WhenExactRequestedTargetIsFocused()
    {
        var actual = AgentRunner.BuildFocusedElementContinuationGuidance(
            "If Netflix is showing the profile selection screen, select the profile named Min.",
            "press_window_key",
            """
            {
              "FocusedElement": {
                "Path": "focused",
                "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0/0",
                "Name": "Min",
                "ControlType": "Hyperlink",
                "AvailableActions": [ "focus", "invoke", "scroll_into_view" ]
              }
            }
            """);

        Assert.NotNull(actual);
        Assert.Contains("Focus alone does not complete", actual!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Invoke the focused target now", actual, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildFocusedElementContinuationGuidance_ReturnsNull_ForFocusOnlyRequest()
    {
        var actual = AgentRunner.BuildFocusedElementContinuationGuidance(
            "Focus the profile named Min.",
            "press_window_key",
            """
            {
              "FocusedElement": {
                "Path": "focused",
                "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0/0",
                "Name": "Min",
                "ControlType": "Hyperlink",
                "AvailableActions": [ "focus", "invoke", "scroll_into_view" ]
              }
            }
            """);

        Assert.Null(actual);
    }

    [Fact]
    public void BuildToolSpecificGuidance_ReturnsFailureHint_ForTaskbarAppLaunchWithoutSelectedWindow()
    {
        var actual = AgentRunner.BuildToolSpecificGuidance(
            "activate_taskbar_app",
            """{"SelectedWindow":null}""",
            new Dictionary<string, object?>());

        Assert.NotNull(actual);
        Assert.Contains("Do not imply that the app opened successfully", actual!, StringComparison.Ordinal);
        Assert.Contains("do not treat the unchanged current window as the requested app", actual, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fresh evidence", actual, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("materially different launch route", actual, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolSpecificGuidance_ReturnsFailureHint_ForTaskbarSearchLaunchWithoutSelectedWindow()
    {
        var actual = AgentRunner.BuildToolSpecificGuidance(
            "launch_application",
            """{"SelectedWindow":null}""",
            new Dictionary<string, object?>());

        Assert.NotNull(actual);
        Assert.Contains("Do not imply that the app opened successfully", actual!, StringComparison.Ordinal);
        Assert.Contains("do not assume a same-title app window exists", actual, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not treat the unchanged current window as the requested app", actual, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fresh evidence", actual, StringComparison.OrdinalIgnoreCase);
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
    public void TryRewriteSelectWindowForRequestedApp_RewritesUnrelatedWindowToLaunch()
    {
        var recentListWindowsOutput =
            """
            {
              "SelectedWindowHandle": null,
              "Windows": [
                {
                  "Handle": "0x00250450",
                  "Title": "YouTube - Personal - Microsoft Edge",
                  "ClassName": "Chrome_WidgetWin_1",
                  "ProcessId": 43600,
                  "Bounds": { "Left": -1928, "Top": -8, "Width": 1936, "Height": 1048 },
                  "IsSelected": false
                }
              ]
            }
            """;

        var rewritten = AgentRunner.TryRewriteSelectWindowForRequestedApp(
            "Play Netflix.",
            new Dictionary<string, object?> { ["windowHandle"] = "0x00250450" },
            recentListWindowsOutput,
            canLaunchRequestedApp: true,
            out var rewrittenToolName,
            out var rewrittenArgs);

        Assert.True(rewritten);
        Assert.Equal("launch_application", rewrittenToolName);
        Assert.Equal("Netflix", rewrittenArgs["appName"]);
    }

    [Fact]
    public void TryRewriteSelectWindowForRequestedApp_RewritesToMatchingOpenAppWindow_WhenAvailable()
    {
        var recentListWindowsOutput =
            """
            {
              "SelectedWindowHandle": null,
              "Windows": [
                {
                  "Handle": "0x00250450",
                  "Title": "YouTube - Personal - Microsoft Edge",
                  "ClassName": "Chrome_WidgetWin_1",
                  "ProcessId": 43600,
                  "Bounds": { "Left": -1928, "Top": -8, "Width": 1936, "Height": 1048 },
                  "IsSelected": false
                },
                {
                  "Handle": "0x00060A88",
                  "Title": "Netflix - Microsoft Edge",
                  "ClassName": "Chrome_WidgetWin_1",
                  "ProcessId": 43601,
                  "Bounds": { "Left": 0, "Top": 0, "Width": 1936, "Height": 1048 },
                  "IsSelected": false
                }
              ]
            }
            """;

        var rewritten = AgentRunner.TryRewriteSelectWindowForRequestedApp(
            "Play Netflix.",
            new Dictionary<string, object?> { ["windowHandle"] = "0x00250450" },
            recentListWindowsOutput,
            canLaunchRequestedApp: true,
            out var rewrittenToolName,
            out var rewrittenArgs);

        Assert.True(rewritten);
        Assert.Equal("activate_window", rewrittenToolName);
        Assert.Equal("0x00060A88", rewrittenArgs["windowHandle"]);
    }

    [Fact]
    public void TryRewriteSelectWindowForRequestedApp_DoesNotRewriteWhenTargetAlreadyMatchesRequestedApp()
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
                  "ProcessId": 43601,
                  "Bounds": { "Left": 0, "Top": 0, "Width": 1936, "Height": 1048 },
                  "IsSelected": false
                }
              ]
            }
            """;

        var rewritten = AgentRunner.TryRewriteSelectWindowForRequestedApp(
            "Play Netflix.",
            new Dictionary<string, object?> { ["windowHandle"] = "0x00060A88" },
            recentListWindowsOutput,
            canLaunchRequestedApp: true,
            out _,
            out _);

        Assert.False(rewritten);
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
    public void TryRewriteSelectWindowArguments_UsesSelectedHandle_WhenClassNameMatchesLocalizedWindow()
    {
        var recentListWindowsOutput =
            """
            {
              "SelectedWindowHandle": "0x00180398",
              "Windows": [
                {
                  "Handle": "0x00180398",
                  "Title": "Boot To Work.bat - 메모장",
                  "ClassName": "Notepad",
                  "ProcessId": 23792,
                  "Bounds": { "Left": 1972, "Top": 52, "Width": 1440, "Height": 746 },
                  "IsSelected": true
                },
                {
                  "Handle": "0x00100784",
                  "Title": "brain.debug.log - 메모장",
                  "ClassName": "Notepad",
                  "ProcessId": 23792,
                  "Bounds": { "Left": 299, "Top": 196, "Width": 1440, "Height": 746 },
                  "IsSelected": false
                }
              ]
            }
            """;

        var rewritten = AgentRunner.TryRewriteSelectWindowArguments(
            new Dictionary<string, object?> { ["titleContains"] = "Notepad" },
            recentListWindowsOutput,
            out var actualArgs);

        Assert.True(rewritten);
        Assert.Equal("0x00180398", actualArgs["windowHandle"]);
        Assert.False(actualArgs.ContainsKey("titleContains"));
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
    public void NeedsBrowserWindowPreflight_ReturnsTrue_ForWebsiteShortcutOnNonBrowserWindow()
    {
        using var modifiers = JsonDocument.Parse("""["Control"]""");
        var actual = AgentRunner.NeedsBrowserWindowPreflight(
            "Go to the Netflix website.",
            "press_window_key",
            new Dictionary<string, object?> { ["key"] = "L", ["modifiers"] = modifiers.RootElement },
            """{"Window":{"Handle":"0x004C08DE","Title":"heronwin - Visual Studio Code","ClassName":"Chrome_WidgetWin_1"}}""");

        Assert.True(actual);
    }

    [Fact]
    public void NeedsBrowserWindowPreflight_ReturnsFalse_WhenBrowserIsAlreadySelected()
    {
        var actual = AgentRunner.NeedsBrowserWindowPreflight(
            "Go to the Netflix website.",
            "type_window_text",
            new Dictionary<string, object?> { ["text"] = "https://www.netflix.com" },
            """{"Window":{"Handle":"0x00060A88","Title":"Netflix - Microsoft Edge","ClassName":"Chrome_WidgetWin_1"}}""");

        Assert.False(actual);
    }

    [Fact]
    public void ShouldBlockTaskbarSearchForBrowserContentQuery_ReturnsTrue_ForShowSearchInBrowser()
    {
        var actual = AgentRunner.ShouldBlockTaskbarSearchForBrowserContentQuery(
            "Search for the show Boyfriend on Demand.",
            "launch_application",
            """{"Window":{"Handle":"0x00060A88","Title":"Netflix - Microsoft Edge","ClassName":"Chrome_WidgetWin_1"}}""");

        Assert.True(actual);
    }

    [Fact]
    public void ShouldBlockTaskbarSearchForBrowserContentQuery_ReturnsFalse_ForExplicitAppLaunch()
    {
        var actual = AgentRunner.ShouldBlockTaskbarSearchForBrowserContentQuery(
            "Launch the Calculator app from the taskbar search.",
            "launch_application",
            """{"Window":{"Handle":"0x00060A88","Title":"Netflix - Microsoft Edge","ClassName":"Chrome_WidgetWin_1"}}""");

        Assert.False(actual);
    }

    [Fact]
    public void ShouldBlockProcessLaunchForBrowserRequest_ReturnsTrue_ForWebsiteNavigation()
    {
        var actual = AgentRunner.ShouldBlockProcessLaunchForBrowserRequest(
            "Go to the Netflix website.",
            "start_process",
            """{"Window":{"Handle":"0x004C08DE","Title":"heronwin - Visual Studio Code","ClassName":"Chrome_WidgetWin_1"}}""");

        Assert.True(actual);
    }

    [Fact]
    public void ShouldBlockProcessLaunchForBrowserRequest_ReturnsTrue_ForInBrowserContentSearch()
    {
        var actual = AgentRunner.ShouldBlockProcessLaunchForBrowserRequest(
            "Search for Boyfriend on Demand within Netflix.",
            "start_process",
            """{"Window":{"Handle":"0x00060A88","Title":"Netflix - Microsoft Edge","ClassName":"Chrome_WidgetWin_1"}}""");

        Assert.True(actual);
    }

    [Fact]
    public void ShouldBlockProcessLaunchForBrowserRequest_ReturnsFalse_ForNormalDesktopLaunch()
    {
        var actual = AgentRunner.ShouldBlockProcessLaunchForBrowserRequest(
            "Open Notepad.",
            "start_process",
            """{"Window":{"Handle":"0x004C08DE","Title":"heronwin - Visual Studio Code","ClassName":"Chrome_WidgetWin_1"}}""");

        Assert.False(actual);
    }

    [Fact]
    public void ShouldAskToFallbackToWebsite_ReturnsTrue_ForFailedNetflixLaunch()
    {
        var actual = AgentRunner.ShouldAskToFallbackToWebsite(
            "Open Netflix.",
            "Netflix",
            """{"SelectedWindow":null}""",
            """
            {
              "Window": {
                "Handle": "0x00010001",
                "Title": "Search",
                "ClassName": "Windows.UI.Core.CoreWindow"
              },
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window"
              }
            }
            """);

        Assert.True(actual);
    }

    [Fact]
    public void ShouldAskToFallbackToWebsite_ReturnsFalse_ForExplicitWebsiteRequest()
    {
        var actual = AgentRunner.ShouldAskToFallbackToWebsite(
            "Go to the Netflix website.",
            "Netflix",
            """{"SelectedWindow":null}""",
            """
            {
              "Window": {
                "Handle": "0x00010001",
                "Title": "Search",
                "ClassName": "Windows.UI.Core.CoreWindow"
              },
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window"
              }
            }
            """);

        Assert.False(actual);
    }

    [Fact]
    public void ShouldAskToFallbackToWebsite_ReturnsFalse_WhenLaunchAlreadyReachedRequestedApp()
    {
        var actual = AgentRunner.ShouldAskToFallbackToWebsite(
            "Open Netflix.",
            "Netflix",
            """{"SelectedWindow":{"Handle":"0x00060A88","Title":"Netflix - Microsoft Edge","ClassName":"Chrome_WidgetWin_1"}}""",
            """
            {
              "Window": {
                "Handle": "0x00060A88",
                "Title": "Home - Netflix",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window"
              }
            }
            """);

        Assert.False(actual);
    }

    [Fact]
    public void TryFindNetflixProfileSelectionTargetPath_ReturnsExactInvokableProfile()
    {
        var snapshot =
            """
            {
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "1/0/0/1/1/0/0/0/0/0/0",
                    "UiPath": "1/0/0/1/1/0/0/0/0/0/0",
                    "Name": "Netflix",
                    "ControlType": "Document",
                    "Children": [
                      {
                        "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/1",
                        "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/1",
                        "Name": "Who's watching?",
                        "ControlType": "Text",
                        "ClassName": "profile-gate-label"
                      },
                      {
                        "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0",
                        "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0",
                        "Name": "Min",
                        "ControlType": "ListItem",
                        "ClassName": "profile",
                        "AvailableActions": [ "scroll_into_view" ],
                        "Children": [
                          {
                            "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0/0",
                            "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0/0",
                            "Name": "Min",
                            "ControlType": "Hyperlink",
                            "ClassName": "profile-link",
                            "AvailableActions": [ "focus", "invoke", "scroll_into_view" ]
                          }
                        ]
                      }
                    ]
                  }
                ]
              }
            }
            """;

        var actual = AgentRunner.TryFindNetflixProfileSelectionTargetPath(
            "If Netflix is showing the profile selection screen, select the profile named Min.",
            snapshot,
            out var matchedPath);

        Assert.True(actual);
        Assert.Equal("1/0/0/1/1/0/0/0/0/0/0/0/0/2/0/0", matchedPath);
    }

    [Fact]
    public void TryFindNetflixProfileSelectionTargetPath_PrefersExactProfileNameOverGenericProfileControls()
    {
        var snapshot =
            """
            {
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "1/0/0/1/1/0/0/0/0/0/0",
                    "UiPath": "1/0/0/1/1/0/0/0/0/0/0",
                    "Name": "Netflix",
                    "ControlType": "Document",
                    "Children": [
                      {
                        "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/1",
                        "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/1",
                        "Name": "Who's watching?",
                        "ControlType": "Text",
                        "ClassName": "profile-gate-label"
                      },
                      {
                        "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0",
                        "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0",
                        "Name": "Min",
                        "ControlType": "ListItem",
                        "ClassName": "profile",
                        "AvailableActions": [ "scroll_into_view" ],
                        "Children": [
                          {
                            "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0/0",
                            "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0/0",
                            "Name": "Min",
                            "ControlType": "Hyperlink",
                            "ClassName": "profile-link",
                            "AvailableActions": [ "focus", "invoke", "scroll_into_view" ]
                          }
                        ]
                      },
                      {
                        "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/4",
                        "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/4",
                        "Name": "Add Profile",
                        "ControlType": "ListItem",
                        "ClassName": "profile",
                        "AvailableActions": [ "scroll_into_view" ],
                        "Children": [
                          {
                            "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/4/0",
                            "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/4/0",
                            "Name": "Add Profile",
                            "ControlType": "Hyperlink",
                            "ClassName": "profile-link",
                            "AvailableActions": [ "focus", "invoke", "scroll_into_view" ]
                          }
                        ]
                      },
                      {
                        "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/3",
                        "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/3",
                        "Name": "Manage Profiles",
                        "ControlType": "Hyperlink",
                        "ClassName": "profile-button",
                        "AvailableActions": [ "focus", "invoke", "scroll_into_view" ]
                      }
                    ]
                  }
                ]
              }
            }
            """;

        var actual = AgentRunner.TryFindNetflixProfileSelectionTargetPath(
            "If Netflix is showing the profile selection screen, select the profile named Min.",
            snapshot,
            out var matchedPath);

        Assert.True(actual);
        Assert.Equal("1/0/0/1/1/0/0/0/0/0/0/0/0/2/0/0", matchedPath);
    }

    [Fact]
    public void TryFindNetflixProfileSelectionTargetPath_ReturnsFalse_WhenProfilePickerIsAbsent()
    {
        var snapshot =
            """
            {
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "1/0/0",
                    "UiPath": "1/0/0",
                    "Name": "Netflix Home",
                    "ControlType": "Document"
                  }
                ]
              }
            }
            """;

        var actual = AgentRunner.TryFindNetflixProfileSelectionTargetPath(
            "Select the profile named Min.",
            snapshot,
            out _);

        Assert.False(actual);
    }

    [Fact]
    public void TryBuildRemainingNetflixPinDigits_ReturnsRemainingDigits_FromFocusedOrdinal()
    {
        var windowSnapshot =
            """
            {
              "Window": {
                "Handle": "0x009C0680",
                "Title": "Netflix",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "1/0",
                    "UiPath": "1/0",
                    "Name": "Enter Min's PIN to add a profile",
                    "ControlType": "Text"
                  }
                ]
              }
            }
            """;
        var focusSnapshot =
            """
            {
              "Window": {
                "Handle": "0x009C0680",
                "Title": "Netflix",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "FocusedElement": {
                "Path": "focused",
                "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/5",
                "Name": "PIN Entry Input 2.",
                "ControlType": "Edit",
                "ClassName": "pin-number-input focus-visible"
              }
            }
            """;

        var actual = AgentRunner.TryBuildRemainingNetflixPinDigits(
            "If Netflix asks for a profile passcode, type 3579 one digit at a time.",
            windowSnapshot,
            focusSnapshot,
            out var remainingDigits);

        Assert.True(actual);
        Assert.Equal("579", remainingDigits);
    }

    [Fact]
    public void ShouldRefreshNetflixPinFocusBeforeContinuation_ReturnsTrue_WhenPinWindowHasNoFocusedOrdinal()
    {
        var actual = AgentRunner.ShouldRefreshNetflixPinFocusBeforeContinuation(
            """
            {
              "Window": {
                "Title": "Netflix",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "1/0",
                    "UiPath": "1/0",
                    "Name": "Enter your PIN",
                    "ControlType": "Text"
                  },
                  {
                    "Path": "1/1",
                    "UiPath": "1/1",
                    "Name": "Forgot PIN",
                    "ControlType": "Hyperlink"
                  }
                ]
              }
            }
            """,
            null);

        Assert.True(actual);
    }

    [Fact]
    public void ShouldRefreshNetflixPinFocusBeforeContinuation_ReturnsFalse_WhenFocusedOrdinalIsKnown()
    {
        var actual = AgentRunner.ShouldRefreshNetflixPinFocusBeforeContinuation(
            """
            {
              "Window": {
                "Title": "Netflix",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "1/0",
                    "UiPath": "1/0",
                    "Name": "Enter your PIN",
                    "ControlType": "Text"
                  }
                ]
              }
            }
            """,
            """
            {
              "Window": {
                "Title": "Netflix",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "FocusedElement": {
                "Path": "focused",
                "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/7",
                "Name": "PIN Entry Input 4.",
                "ControlType": "Edit",
                "ClassName": "pin-number-input focus-visible"
              }
            }
            """);

        Assert.False(actual);
    }

    [Fact]
    public void TryBuildRemainingNetflixPinDigits_ReturnsFalse_WhenPinDigitsAreAbsent()
    {
        var actual = AgentRunner.TryBuildRemainingNetflixPinDigits(
            "If Netflix asks for a profile passcode, type it one digit at a time.",
            """{"Window":{"Title":"Netflix"}}""",
            """{"Window":{"Title":"Netflix"}}""",
            out _);

        Assert.False(actual);
    }

    [Fact]
    public void TryBuildRemainingNetflixPinDigits_ReturnsFalse_ForManageProfileLockSettingsPage()
    {
        var actual = AgentRunner.TryBuildRemainingNetflixPinDigits(
            "If Netflix asks for a profile passcode, type 3579 one digit at a time.",
            """
            {
              "Window": {
                "Title": "Account Profile Lock - Netflix",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "1/0",
                    "UiPath": "1/0",
                    "Name": "Manage Profile Lock",
                    "ControlType": "Text"
                  },
                  {
                    "Path": "1/1",
                    "UiPath": "1/1",
                    "Name": "Edit PIN",
                    "ControlType": "Button"
                  },
                  {
                    "Path": "1/2",
                    "UiPath": "1/2",
                    "Name": "Delete Profile Lock",
                    "ControlType": "Button"
                  }
                ]
              }
            }
            """,
            """
            {
              "Window": {
                "Title": "Account Profile Lock - Netflix",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "FocusedElement": {
                "Path": "focused",
                "UiPath": "1/1",
                "Name": "Edit PIN",
                "ControlType": "Button"
              }
            }
            """,
            out _);

        Assert.False(actual);
    }

    [Fact]
    public void ShouldBlockUnnamedProfilePickerAction_ReturnsTrue_ForGuessedManageProfilesClick()
    {
        var snapshot =
            """
            {
              "Window": {
                "Handle": "0x00250450",
                "Title": "Netflix - Microsoft Edge",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "1/0/0/1/1/0/0/0/0/0/0",
                    "UiPath": "1/0/0/1/1/0/0/0/0/0/0",
                    "Name": "Netflix",
                    "ControlType": "Document",
                    "Children": [
                      {
                        "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/1",
                        "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/1",
                        "Name": "Who's watching?",
                        "ControlType": "Text",
                        "ClassName": "profile-gate-label"
                      },
                      {
                        "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0",
                        "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0",
                        "Name": "Min",
                        "ControlType": "ListItem",
                        "ClassName": "profile"
                      },
                      {
                        "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/3",
                        "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/3",
                        "Name": "Manage Profiles",
                        "ControlType": "Hyperlink",
                        "ClassName": "profile-button",
                        "AvailableActions": [ "invoke" ]
                      }
                    ]
                  }
                ]
              }
            }
            """;

        var actual = AgentRunner.ShouldBlockUnnamedProfilePickerAction(
            "Play Netflix.",
            "click_window_element",
            new Dictionary<string, object?> { ["elementPath"] = "1/0/0/1/1/0/0/0/0/0/0/0/0/3" },
            snapshot,
            out var blockedMessage);

        Assert.True(actual);
        Assert.Contains("profile picker is visible", blockedMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not guess which profile to choose", blockedMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldBlockUnnamedProfilePickerAction_ReturnsFalse_ForExplicitProfileSelection()
    {
        var snapshot =
            """
            {
              "Window": {
                "Handle": "0x00250450",
                "Title": "Netflix - Microsoft Edge",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "1/0/0/1/1/0/0/0/0/0/0",
                    "UiPath": "1/0/0/1/1/0/0/0/0/0/0",
                    "Name": "Netflix",
                    "ControlType": "Document",
                    "Children": [
                      {
                        "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/1",
                        "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/1",
                        "Name": "Who's watching?",
                        "ControlType": "Text",
                        "ClassName": "profile-gate-label"
                      },
                      {
                        "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0",
                        "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0",
                        "Name": "Min",
                        "ControlType": "ListItem",
                        "ClassName": "profile"
                      }
                    ]
                  }
                ]
              }
            }
            """;

        var actual = AgentRunner.ShouldBlockUnnamedProfilePickerAction(
            "Select Min.",
            "click_window_element",
            new Dictionary<string, object?> { ["elementPath"] = "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0" },
            snapshot,
            out var blockedMessage);

        Assert.False(actual);
        Assert.Equal(string.Empty, blockedMessage);
    }

    [Fact]
    public void ShouldBlockUnnamedProfilePickerAction_ReturnsFalse_ForExplicitManageProfilesRequest()
    {
        var snapshot =
            """
            {
              "Window": {
                "Handle": "0x00250450",
                "Title": "Netflix - Microsoft Edge",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "1/0/0/1/1/0/0/0/0/0/0",
                    "UiPath": "1/0/0/1/1/0/0/0/0/0/0",
                    "Name": "Netflix",
                    "ControlType": "Document",
                    "Children": [
                      {
                        "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/1",
                        "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/1",
                        "Name": "Who's watching?",
                        "ControlType": "Text",
                        "ClassName": "profile-gate-label"
                      },
                      {
                        "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/3",
                        "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/3",
                        "Name": "Manage Profiles",
                        "ControlType": "Hyperlink",
                        "ClassName": "profile-button",
                        "AvailableActions": [ "invoke" ]
                      }
                    ]
                  }
                ]
              }
            }
            """;

        var actual = AgentRunner.ShouldBlockUnnamedProfilePickerAction(
            "Open Manage Profiles.",
            "click_window_element",
            new Dictionary<string, object?> { ["elementPath"] = "1/0/0/1/1/0/0/0/0/0/0/0/0/3" },
            snapshot,
            out var blockedMessage);

        Assert.False(actual);
        Assert.Equal(string.Empty, blockedMessage);
    }

    [Fact]
    public void TryExtractStructuredNetflixPinDigits_ReturnsTrue_ForNetflixPinFocusAndMultiDigitText()
    {
        var focusSnapshot =
            """
            {
              "Window": {
                "Handle": "0x009C0680",
                "Title": "Netflix",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "FocusedElement": {
                "Path": "focused",
                "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/4",
                "Name": "PIN Entry Input 1.",
                "ControlType": "Edit",
                "ClassName": "pin-number-input focus-visible",
                "IsEnabled": true,
                "HasKeyboardFocus": true
              }
            }
            """;

        var actual = AgentRunner.TryExtractStructuredNetflixPinDigits(
            "type_window_text",
            new Dictionary<string, object?> { ["text"] = "3579" },
            recentWindowContext: null,
            recentFocusContext: focusSnapshot,
            out var digits);

        Assert.True(actual);
        Assert.Equal("3579", digits);
    }

    [Fact]
    public void TryExtractStructuredNetflixPinDigits_ReturnsFalse_ForSingleDigitOrNonPinFocus()
    {
        var focusSnapshot =
            """
            {
              "Window": {
                "Handle": "0x009C0680",
                "Title": "Netflix",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "FocusedElement": {
                "Path": "focused",
                "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/4",
                "Name": "Search",
                "ControlType": "Edit",
                "ClassName": "search-box",
                "IsEnabled": true,
                "HasKeyboardFocus": true
              }
            }
            """;

        var actual = AgentRunner.TryExtractStructuredNetflixPinDigits(
            "type_window_text",
            new Dictionary<string, object?> { ["text"] = "3" },
            recentWindowContext: null,
            recentFocusContext: focusSnapshot,
            out var digits);

        Assert.False(actual);
        Assert.Equal(string.Empty, digits);
    }

    [Fact]
    public void TryBuildBrowserSelectionArguments_PrefersUsableEdgeWindow()
    {
        var recentListWindowsOutput =
            """
            {
              "SelectedWindowHandle": "0x004C08DE",
              "Windows": [
                {
                  "Handle": "0x004C08DE",
                  "Title": "heronwin - Visual Studio Code",
                  "ClassName": "Chrome_WidgetWin_1",
                  "ProcessId": 17924,
                  "Bounds": { "Left": 1912, "Top": 0, "Width": 1936, "Height": 1048 },
                  "IsSelected": true
                },
                {
                  "Handle": "0x00060A88",
                  "Title": "Netflix - Microsoft Edge",
                  "ClassName": "Chrome_WidgetWin_1",
                  "ProcessId": 43600,
                  "Bounds": { "Left": -1928, "Top": -8, "Width": 1936, "Height": 1048 },
                  "IsSelected": false
                }
              ]
            }
            """;

        var rewritten = AgentRunner.TryBuildBrowserSelectionArguments(
            recentListWindowsOutput,
            out var actualArgs);

        Assert.True(rewritten);
        Assert.Equal("0x00060A88", actualArgs["windowHandle"]);
    }

    [Fact]
    public void TryRewriteBrowserAddressBarActionToShortcut_ReturnsTrue_ForBrowserAddressBarElement()
    {
        var actual = AgentRunner.TryRewriteBrowserAddressBarActionToShortcut(
            "invoke_window_element",
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
            "invoke_window_element",
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
    public void GetCurrentUiTreeContext_PreservesMatchingTreeSnapshot_AfterScreenshotOnlyUpdate()
    {
        var uiTreeSnapshot =
            """
            {
              "Window": {
                "Handle": "0x00250450",
                "Title": "Netflix - Microsoft Edge",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0",
                    "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0",
                    "ControlType": "Group",
                    "AutomationId": "appMountPoint",
                    "Children": [
                      {
                        "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/2",
                        "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2",
                        "ControlType": "List",
                        "Children": [
                          {
                            "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0",
                            "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0",
                            "Name": "Min",
                            "ControlType": "ListItem",
                            "ClassName": "profile"
                          }
                        ]
                      }
                    ]
                  }
                ]
              }
            }
            """;
        var screenshotOnlySnapshot =
            """
            {
              "SelectedWindow": {
                "Handle": "0x00250450",
                "Title": "Netflix - Microsoft Edge",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "Screenshot": {
                "MimeType": "image/png"
              }
            }
            """;

        var actionableUiTreeContext = AgentRunner.GetCurrentUiTreeContext(screenshotOnlySnapshot, uiTreeSnapshot);
        var rewritten = AgentRunner.TryRewriteGenericContainerActionToNamedTarget(
            "Could you select profile min and end?",
            "click_window_element",
            new Dictionary<string, object?> { ["elementPath"] = "1/0/0/1/1/0/0/0/0/0/0/0/0" },
            actionableUiTreeContext,
            out var rewrittenArgs);

        Assert.Equal(uiTreeSnapshot, actionableUiTreeContext);
        Assert.True(rewritten);
        Assert.Equal("1/0/0/1/1/0/0/0/0/0/0/0/0/2/0", rewrittenArgs["elementPath"]);
    }

    [Fact]
    public void GetCurrentUiTreeContext_ReturnsNull_WhenScreenshotWindowDiffersFromStoredTree()
    {
        var recentUiTreeContext =
            """
            {
              "Window": {
                "Handle": "0x00250450",
                "Title": "Netflix - Microsoft Edge",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window"
              }
            }
            """;
        var recentWindowContext =
            """
            {
              "SelectedWindow": {
                "Handle": "0x00060A88",
                "Title": "YouTube - Personal - Microsoft Edge",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "Screenshot": {
                "MimeType": "image/png"
              }
            }
            """;

        var actual = AgentRunner.GetCurrentUiTreeContext(recentWindowContext, recentUiTreeContext);

        Assert.Null(actual);
    }

    [Fact]
    public void ResolveToolResultContextForModel_ReturnsStoredUiElementContext_ForActionTool()
    {
        var profile = LlmModelProfiles.Create(LlmProviderId.OpenAiApi, "gpt-5.4-mini");
        const string keptUiElementContext = "UI snapshot compacted for gpt-5.4-mini. Window: Netflix.";
        const string rawToolText = "{ \"SelectedWindow\": { \"Handle\": \"0x0033061A\", \"Title\": \"Netflix\" } }";

        var actual = AgentRunner.ResolveToolResultContextForModel(
            "launch_application",
            rawToolText,
            toolIsError: false,
            currentUiElementContext: keptUiElementContext,
            currentFocusElementContext: null,
            profile);

        Assert.Equal(keptUiElementContext, actual);
    }

    [Fact]
    public void ResolveToolResultContextForModel_ReturnsStoredUiElementContext_ForScreenshotTool()
    {
        var profile = LlmModelProfiles.Create(LlmProviderId.OpenAiApi, "gpt-5.4-mini");
        const string keptUiElementContext = "UI snapshot compacted for gpt-5.4-mini. Window: Netflix.";
        const string rawToolText = "{ \"SelectedWindow\": { \"Handle\": \"0x0033061A\", \"Title\": \"Netflix\" }, \"Screenshot\": { \"MimeType\": \"image/png\" } }";

        var actual = AgentRunner.ResolveToolResultContextForModel(
            "capture_window_screenshot",
            rawToolText,
            toolIsError: false,
            currentUiElementContext: keptUiElementContext,
            currentFocusElementContext: null,
            profile);

        Assert.Equal(keptUiElementContext, actual);
    }

    [Fact]
    public void ResolveToolResultContextForModel_ReturnsStoredFocusContext_ForFocusDescribeTool()
    {
        var profile = LlmModelProfiles.Create(LlmProviderId.OpenAiApi, "gpt-5.4-mini");
        const string keptFocusContext = "UI snapshot compacted for gpt-5.4-mini. Focused element: Edit \"Search the web\".";
        const string rawToolText = "{ \"FocusedElement\": { \"Name\": \"Search the web\", \"ControlType\": \"Edit\" } }";

        var actual = AgentRunner.ResolveToolResultContextForModel(
            "describe_window_focus",
            rawToolText,
            toolIsError: false,
            currentUiElementContext: null,
            currentFocusElementContext: keptFocusContext,
            profile);

        Assert.Equal(keptFocusContext, actual);
    }

    [Fact]
    public void ResolveToolResultContextForModel_ReturnsLlmProjection_ForCompactDescribeTool()
    {
        var profile = LlmModelProfiles.Create(LlmProviderId.OpenAiApi, "gpt-5.4-mini");
        const string compactToolText = """
        {"window":{"handle":"0x0033061A","title":"Netflix"},"sourceStats":{"sourceNodeCount":12,"keptNodeCount":4,"omittedNodeCount":8,"algorithmVersion":"compact-tree-v1"},"compactTree":{"path":"root","uiPath":"root","controlType":"Window","name":"Netflix"},"llmTree":{"uiPath":"root","controlType":"Window","name":"Netflix"}}
        """;

        var actual = AgentRunner.ResolveToolResultContextForModel(
            "describe_window_compact",
            compactToolText,
            toolIsError: false,
            currentUiElementContext: "older context",
            currentFocusElementContext: null,
            profile);

        Assert.DoesNotContain("compactTree", actual, StringComparison.Ordinal);
        Assert.Contains("window", actual, StringComparison.Ordinal);
        Assert.Contains("sourceStats", actual, StringComparison.Ordinal);
        Assert.Contains("llmTree", actual, StringComparison.Ordinal);
        Assert.Contains("uiPath", actual, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveToolResultContextForModel_FallsBackToRawSnapshot_WhenStoredContextUnavailable()
    {
        var profile = new LlmModelProfile(
            LlmProviderId.OpenAiApi,
            "gpt-5.4-mini",
            ContextCompressionTriggerRatio: 0.55,
            WindowSnapshotCharBudget: 320,
            FocusSnapshotCharBudget: 200,
            MaxThrottleRetries: 2);
        var snapshot =
            """
            {
              "Window": {
                "Handle": "0x0033061A",
                "Title": "Netflix",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "Name": "Netflix",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "1/0/0/1/1/0/0/0/0/0/0",
                    "UiPath": "1/0/0/1/1/0/0/0/0/0/0",
                    "Name": "Netflix",
                    "ControlType": "Document",
                    "AvailableActions": [ "focus", "scroll_into_view", "set_value" ],
                    "Children": [
                      {
                        "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/1",
                        "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/1",
                        "Name": "Who's watching?",
                        "ControlType": "Text"
                      },
                      {
                        "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0",
                        "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0",
                        "Name": "Min",
                        "ControlType": "ListItem"
                      }
                    ]
                  }
                ]
              }
            }
            """;

        var actual = AgentRunner.ResolveToolResultContextForModel(
            "describe_window",
            snapshot,
            toolIsError: false,
            currentUiElementContext: null,
            currentFocusElementContext: null,
            profile);

        Assert.Equal(snapshot, actual);
    }

    [Fact]
    public void ResolveToolResultContextForModel_UsesRawErrorText_WhenActionToolFails()
    {
        var profile = LlmModelProfiles.Create(LlmProviderId.OpenAiApi, "gpt-5.4-mini");
        const string keptUiElementContext = "UI snapshot compacted for gpt-5.4-mini. Window: Netflix.";
        const string rawToolText = "Error: timed out waiting for the selected window.";

        var actual = AgentRunner.ResolveToolResultContextForModel(
            "click_window_element",
            rawToolText,
            toolIsError: true,
            currentUiElementContext: keptUiElementContext,
            currentFocusElementContext: null,
            profile);

        Assert.Equal(rawToolText, actual);
    }

    [Fact]
    public void TryRewriteGenericContainerActionToNamedTarget_RewritesClickToExactNamedListItem()
    {
        var snapshot =
            """
            {
              "Window": {
                "Handle": "0x00250450",
                "Title": "Netflix - Microsoft Edge",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0",
                    "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0",
                    "ControlType": "Group",
                    "AutomationId": "appMountPoint",
                    "Children": [
                      {
                        "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/1",
                        "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/1",
                        "Name": "Who's watching?",
                        "ControlType": "Text"
                      },
                      {
                        "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/2",
                        "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2",
                        "ControlType": "List",
                        "Children": [
                          {
                            "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0",
                            "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0",
                            "Name": "Min",
                            "ControlType": "ListItem",
                            "ClassName": "profile"
                          },
                          {
                            "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/1",
                            "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/1",
                            "Name": "Esther",
                            "ControlType": "ListItem",
                            "ClassName": "profile"
                          }
                        ]
                      }
                    ]
                  }
                ]
              }
            }
            """;

        var rewritten = AgentRunner.TryRewriteGenericContainerActionToNamedTarget(
            "Could you select profile min and end?",
            "click_window_element",
            new Dictionary<string, object?> { ["elementPath"] = "1/0/0/1/1/0/0/0/0/0/0/0/0" },
            snapshot,
            out var rewrittenArgs);

        Assert.True(rewritten);
        Assert.Equal("1/0/0/1/1/0/0/0/0/0/0/0/0/2/0", rewrittenArgs["elementPath"]);
    }

    [Fact]
    public void TryRewriteGenericContainerActionToNamedTarget_RewritesWrongHeadingTextToExactNamedListItem()
    {
        var snapshot =
            """
            {
              "Window": {
                "Handle": "0x00250450",
                "Title": "Netflix - Microsoft Edge",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "1/0/0/1/1/0/0/0/0/0/0",
                    "UiPath": "1/0/0/1/1/0/0/0/0/0/0",
                    "Name": "Netflix",
                    "ControlType": "Document",
                    "Children": [
                      {
                        "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/1",
                        "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/1",
                        "Name": "Who's watching?",
                        "ControlType": "Text",
                        "ClassName": "profile-gate-label"
                      },
                      {
                        "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/2",
                        "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2",
                        "ControlType": "List",
                        "Children": [
                          {
                            "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0",
                            "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0",
                            "Name": "Min",
                            "ControlType": "ListItem",
                            "ClassName": "profile"
                          },
                          {
                            "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/1",
                            "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/1",
                            "Name": "Esther",
                            "ControlType": "ListItem",
                            "ClassName": "profile"
                          }
                        ]
                      }
                    ]
                  }
                ]
              }
            }
            """;

        var rewritten = AgentRunner.TryRewriteGenericContainerActionToNamedTarget(
            "Select Min.",
            "click_window_element",
            new Dictionary<string, object?> { ["elementPath"] = "1/0/0/1/1/0/0/0/0/0/0/0/0/1" },
            snapshot,
            out var rewrittenArgs);

        Assert.True(rewritten);
        Assert.Equal("1/0/0/1/1/0/0/0/0/0/0/0/0/2/0", rewrittenArgs["elementPath"]);
    }

    [Fact]
    public void TryRewriteGenericContainerActionToNamedTarget_DoesNotRewriteSpecificTarget()
    {
        var snapshot =
            """
            {
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0",
                    "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0",
                    "Name": "Min",
                    "ControlType": "ListItem",
                    "ClassName": "profile"
                  }
                ]
              }
            }
            """;

        var rewritten = AgentRunner.TryRewriteGenericContainerActionToNamedTarget(
            "Could you select profile min and end?",
            "click_window_element",
            new Dictionary<string, object?> { ["elementPath"] = "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0" },
            snapshot,
            out _);

        Assert.False(rewritten);
    }

    [Fact]
    public void TryRewriteGenericContainerActionToNamedTarget_RewritesInvalidInvokePathToExactNamedHyperlink()
    {
        var snapshot =
            """
            {
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0",
                    "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0",
                    "Name": "Min",
                    "ControlType": "ListItem",
                    "ClassName": "profile",
                    "AvailableActions": [ "scroll_into_view" ],
                    "Children": [
                      {
                        "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0/0",
                        "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0/0",
                        "Name": "Min",
                        "ControlType": "Hyperlink",
                        "ClassName": "profile-link",
                        "AvailableActions": [ "focus", "invoke", "scroll_into_view" ]
                      }
                    ]
                  }
                ]
              }
            }
            """;

        var rewritten = AgentRunner.TryRewriteGenericContainerActionToNamedTarget(
            "Select Min.",
            "invoke_window_element",
            new Dictionary<string, object?> { ["elementPath"] = "1/0/0/1/1/0/0/0/0/0/0/2/0/0" },
            snapshot,
            out var rewrittenArgs);

        Assert.True(rewritten);
        Assert.Equal("1/0/0/1/1/0/0/0/0/0/0/0/0/2/0/0", rewrittenArgs["elementPath"]);
    }

    [Fact]
    public void TryRewriteGenericContainerActionToNamedTarget_RewritesAsrVariantProfileNameToExactNamedHyperlink()
    {
        var snapshot =
            """
            {
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0",
                    "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0",
                    "Name": "Min",
                    "ControlType": "ListItem",
                    "ClassName": "profile",
                    "AvailableActions": [ "scroll_into_view" ],
                    "Children": [
                      {
                        "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0/0",
                        "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0/0",
                        "Name": "Min",
                        "ControlType": "Hyperlink",
                        "ClassName": "profile-link",
                        "AvailableActions": [ "focus", "invoke", "scroll_into_view" ]
                      }
                    ]
                  }
                ]
              }
            }
            """;

        var rewritten = AgentRunner.TryRewriteGenericContainerActionToNamedTarget(
            "Let's pick men.",
            "invoke_window_element",
            new Dictionary<string, object?> { ["elementPath"] = "1/0/0/1/1/0/0/0/0/0/0/2/0/0" },
            snapshot,
            out var rewrittenArgs);

        Assert.True(rewritten);
        Assert.Equal("1/0/0/1/1/0/0/0/0/0/0/0/0/2/0/0", rewrittenArgs["elementPath"]);
    }

    [Fact]
    public void TryRewriteGenericContainerActionToNamedTarget_PrefersInteractiveChildForClickWhenNamesDuplicate()
    {
        var snapshot =
            """
            {
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0",
                    "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0",
                    "Name": "Min",
                    "ControlType": "ListItem",
                    "ClassName": "profile",
                    "AvailableActions": [ "scroll_into_view" ],
                    "Children": [
                      {
                        "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0/0",
                        "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0/0",
                        "Name": "Min",
                        "ControlType": "Hyperlink",
                        "ClassName": "profile-link",
                        "AvailableActions": [ "focus", "invoke", "scroll_into_view" ]
                      }
                    ]
                  }
                ]
              }
            }
            """;

        var rewritten = AgentRunner.TryRewriteGenericContainerActionToNamedTarget(
            "Select Min.",
            "click_window_element",
            new Dictionary<string, object?> { ["elementPath"] = "1/0/0/1/1/0/0/0/0/0/0/2/0/0" },
            snapshot,
            out var rewrittenArgs);

        Assert.True(rewritten);
        Assert.Equal("1/0/0/1/1/0/0/0/0/0/0/0/0/2/0/0", rewrittenArgs["elementPath"]);
    }

    [Fact]
    public void TryRewriteGenericContainerActionToNamedTarget_PrefersExactProfileNameOverGenericProfileLinks()
    {
        var snapshot =
            """
            {
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/1",
                    "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/1",
                    "Name": "Who's watching?",
                    "ControlType": "Text",
                    "ClassName": "profile-gate-label"
                  },
                  {
                    "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0",
                    "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0",
                    "Name": "Min",
                    "ControlType": "ListItem",
                    "ClassName": "profile",
                    "AvailableActions": [ "scroll_into_view" ],
                    "Children": [
                      {
                        "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0/0",
                        "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/0/0",
                        "Name": "Min",
                        "ControlType": "Hyperlink",
                        "ClassName": "profile-link",
                        "AvailableActions": [ "focus", "invoke", "scroll_into_view" ]
                      }
                    ]
                  },
                  {
                    "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/4",
                    "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/4",
                    "Name": "Add Profile",
                    "ControlType": "ListItem",
                    "ClassName": "profile",
                    "AvailableActions": [ "scroll_into_view" ],
                    "Children": [
                      {
                        "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/4/0",
                        "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/2/4/0",
                        "Name": "Add Profile",
                        "ControlType": "Hyperlink",
                        "ClassName": "profile-link",
                        "AvailableActions": [ "focus", "invoke", "scroll_into_view" ]
                      }
                    ]
                  },
                  {
                    "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/3",
                    "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/3",
                    "Name": "Manage Profiles",
                    "ControlType": "Hyperlink",
                    "ClassName": "profile-button",
                    "AvailableActions": [ "focus", "invoke", "scroll_into_view" ]
                  }
                ]
              }
            }
            """;

        var rewritten = AgentRunner.TryRewriteGenericContainerActionToNamedTarget(
            "If Netflix is showing the profile selection screen, select the profile named Min and continue until either Min opens or Min's profile PIN prompt is visible.",
            "invoke_window_element",
            new Dictionary<string, object?> { ["elementPath"] = "1/0/0/1/1/0/0/0/0/0/0/2/0/0" },
            snapshot,
            out var rewrittenArgs);

        Assert.True(rewritten);
        Assert.Equal("1/0/0/1/1/0/0/0/0/0/0/0/0/2/0/0", rewrittenArgs["elementPath"]);
    }

    [Fact]
    public void TryRewriteGenericContainerActionToNamedTarget_DoesNotRewriteExplicitRootCloseInvocation()
    {
        var snapshot =
            """
            {
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "Name": "*6346219690640510 - 메모장",
                "ControlType": "Window",
                "AvailableActions": [ "close", "focus", "maximize", "minimize" ],
                "Children": [
                  {
                    "Path": "4/3",
                    "UiPath": "4/3",
                    "Name": "Close",
                    "ControlType": "Button",
                    "AutomationId": "Close",
                    "AvailableActions": [ "invoke" ]
                  }
                ]
              }
            }
            """;

        var rewritten = AgentRunner.TryRewriteGenericContainerActionToNamedTarget(
            "Close it.",
            "invoke_window_element",
            new Dictionary<string, object?> { ["elementPath"] = "root" },
            snapshot,
            out _);

        Assert.False(rewritten);
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
            "launch_application",
            new Dictionary<string, object?> { ["appName"] = "Netflix" });

        Assert.Equal("Okay, let me open Netflix.", actual);
    }

    [Fact]
    public void BuildToolStepNarration_ReturnsShortcutSentence_ForModifiedKey()
    {
        using var modifiers = JsonDocument.Parse("""["Control"]""");
        var actual = AgentRunner.BuildToolStepNarration(
            "press_window_key",
            new Dictionary<string, object?> { ["key"] = "L", ["modifiers"] = modifiers.RootElement.Clone() });

        Assert.Equal("Okay, I'm pressing Control plus L.", actual);
    }

    [Fact]
    public void BuildToolStepNarration_ReturnsUrlSentence_ForTypedUrl()
    {
        var actual = AgentRunner.BuildToolStepNarration(
            "type_window_text",
            new Dictionary<string, object?> { ["text"] = "https://www.netflix.com" });

        Assert.Equal("Okay, I'm putting the site in.", actual);
    }

    [Fact]
    public void TryExtractToolStepNarrationFromAssistantContent_UsesStructuredSayText()
    {
        var actual = AgentRunner.TryExtractToolStepNarrationFromAssistantContent(
            """{"say":"I'm opening Netflix now.","log":"Launching Netflix from the taskbar."}""");

        Assert.Equal("I'm opening Netflix now.", actual);
    }

    [Fact]
    public void ResolveToolStepNarration_PrefersAssistantContent_ForSingleToolCall()
    {
        var actual = AgentRunner.ResolveToolStepNarration(
            """{"say":"I'm clicking Play now.","log":"Trying the visible Play control."}""",
            1,
            "click_window_element",
            new Dictionary<string, object?>());

        Assert.NotNull(actual);
        Assert.Equal("I'm clicking Play now.", actual!.Text);
        Assert.Equal("assistant_content", actual.Source);
    }

    [Fact]
    public void ResolveToolStepNarration_SuppressesSilentInspectionTools()
    {
        var actual = AgentRunner.ResolveToolStepNarration(
            """{"say":"I'm checking what is open.","log":"Batching a couple of setup steps."}""",
            2,
            "list_windows",
            new Dictionary<string, object?>());

      Assert.Null(actual);
    }

    [Fact]
    public void ResolveToolStepNarration_SuppressesFallback_ForSequentialWindowEvidenceTools()
    {
        var actual = AgentRunner.ResolveToolStepNarration(
            assistantContent: null,
            toolCallCount: 2,
            toolName: "capture_window_screenshot",
            args: new Dictionary<string, object?>(),
            previousToolName: "describe_window");

        Assert.Null(actual);
    }

    [Fact]
    public void ResolveToolStepNarration_SuppressesAssistantContent_ForSilentInspectionTools()
    {
        var actual = AgentRunner.ResolveToolStepNarration(
            """{"say":"Let me zoom in on that for a sec.","log":"Capturing a screenshot after the UI tree."}""",
            1,
            "capture_window_screenshot",
            new Dictionary<string, object?>(),
            previousToolName: "describe_window");

      Assert.Null(actual);
    }

    [Fact]
    public void TryRewriteDescribeSelectedWindowToFullDepth_RewritesMaxDepthToFullDepth()
    {
        var rewritten = AgentRunner.TryRewriteDescribeSelectedWindowToFullDepth(
            new Dictionary<string, object?> { ["maxDepth"] = 3 },
            out var rewrittenArgs);

        Assert.True(rewritten);
        Assert.True(rewrittenArgs.TryGetValue("fullDepth", out var fullDepthValue));
        Assert.Equal(true, fullDepthValue);
        Assert.False(rewrittenArgs.ContainsKey("maxDepth"));
    }

    [Fact]
    public void TryRewriteDescribeSelectedWindowToFullDepth_DoesNotRewriteWhenAlreadyFullDepth()
    {
        var rewritten = AgentRunner.TryRewriteDescribeSelectedWindowToFullDepth(
            new Dictionary<string, object?> { ["fullDepth"] = true },
            out var rewrittenArgs);

        Assert.False(rewritten);
        Assert.Empty(rewrittenArgs);
    }

    [Theory]
    [InlineData("click_window_element")]
    [InlineData("set_window_element_text")]
    public void IsDesktopActionTool_ReturnsTrue_ForDirectUiStateChanges(string toolName)
    {
        var actual = AgentRunner.IsDesktopActionTool(toolName);

        Assert.True(actual);
    }

    [Fact]
    public void ShouldCapturePostActionDebugScreenshot_ReturnsTrue_WhenDebugTraceIsEnabledAndScreenshotToolExists()
    {
        var actual = AgentRunner.ShouldCapturePostActionDebugScreenshot(
            "click_window_element",
            debugTraceEnabled: true,
            new HashSet<string>(StringComparer.Ordinal) { "capture_window_screenshot" });

        Assert.True(actual);
    }

    [Fact]
    public void ShouldCapturePostActionDebugScreenshot_ReturnsFalse_WhenDebugTraceIsDisabled()
    {
        var actual = AgentRunner.ShouldCapturePostActionDebugScreenshot(
            "click_window_element",
            debugTraceEnabled: false,
            new HashSet<string>(StringComparer.Ordinal) { "capture_window_screenshot" });

        Assert.False(actual);
    }

    [Fact]
    public void ShouldCapturePostActionDebugScreenshot_ReturnsFalse_WhenToolIsNotDesktopAction()
    {
        var actual = AgentRunner.ShouldCapturePostActionDebugScreenshot(
            "describe_window",
            debugTraceEnabled: true,
            new HashSet<string>(StringComparer.Ordinal) { "capture_window_screenshot" });

        Assert.False(actual);
    }

    [Fact]
    public void ShouldCapturePostActionDebugScreenshot_ReturnsFalse_WhenScreenshotToolIsUnavailable()
    {
        var actual = AgentRunner.ShouldCapturePostActionDebugScreenshot(
            "click_window_element",
            debugTraceEnabled: true,
            new HashSet<string>(StringComparer.Ordinal));

        Assert.False(actual);
    }

    [Fact]
    public void ShouldCaptureScreenshotAfterAction_ReturnsTrue_WhenUiTreeDidNotChange()
    {
        const string snapshot =
            """
            {
              "Window": {
                "Handle": "0x00060A88",
                "Title": "Netflix",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "0",
                    "UiPath": "0",
                    "Name": "Search",
                    "ControlType": "Edit"
                  }
                ]
              }
            }
            """;

        var actual = AgentRunner.ShouldCaptureScreenshotAfterAction(snapshot, snapshot);

        Assert.True(actual);
    }

    [Fact]
    public void ShouldCaptureScreenshotAfterAction_ReturnsFalse_WhenUiTreeChanged()
    {
        const string beforeSnapshot =
            """
            {
              "Window": {
                "Handle": "0x00060A88",
                "Title": "Netflix",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "0",
                    "UiPath": "0",
                    "Name": "Search",
                    "ControlType": "Edit"
                  }
                ]
              }
            }
            """;
        const string afterSnapshot =
            """
            {
              "Window": {
                "Handle": "0x00060A88",
                "Title": "Netflix",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "0",
                    "UiPath": "0",
                    "Name": "Boyfriend on Demand",
                    "ControlType": "ListItem"
                  }
                ]
              }
            }
            """;

        var actual = AgentRunner.ShouldCaptureScreenshotAfterAction(beforeSnapshot, afterSnapshot);

        Assert.False(actual);
    }

    [Fact]
    public void ShouldCaptureScreenshotAfterAction_ReturnsTrue_WhenPostActionTreeIsUnavailable()
    {
        const string beforeSnapshot =
            """
            {
              "Window": {
                "Handle": "0x00060A88",
                "Title": "Netflix",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window"
              }
            }
            """;
        const string afterSnapshot =
            """
            {
              "Window": {
                "Handle": "0x00060A88",
                "Title": "Netflix",
                "ClassName": "Chrome_WidgetWin_1"
              }
            }
            """;

        var actual = AgentRunner.ShouldCaptureScreenshotAfterAction(beforeSnapshot, afterSnapshot);

        Assert.True(actual);
    }

    [Fact]
    public void BuildToolSpecificGuidance_ReturnsPostActionVerificationHint_ForClick()
    {
        var actual = AgentRunner.BuildToolSpecificGuidance(
            "click_window_element",
            "{}",
            new Dictionary<string, object?>());

        Assert.NotNull(actual);
        Assert.Contains("freshest post-action snapshot or screenshot", actual!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("before the click does not verify the post-click state", actual, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolSpecificGuidance_ReturnsPostActionVerificationHint_ForDirectValueEntry()
    {
        var actual = AgentRunner.BuildToolSpecificGuidance(
            "set_window_element_text",
            "{}",
            new Dictionary<string, object?>());

        Assert.NotNull(actual);
        Assert.Contains("intended text", actual!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not yet confirmed", actual, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryRewriteBrowserSearchFieldValueEntryToTyping_ReturnsTrue_ForBrowserSearchInput()
    {
        var snapshot =
            """
            {
              "Window": {
                "Handle": "0x00060A88",
                "Title": "Netflix",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "1/0/0/1/1/0/0/0/0/0/0",
                    "UiPath": "1/0/0/1/1/0/0/0/0/0/0",
                    "Name": "Netflix",
                    "ControlType": "Document",
                    "AutomationId": "RootWebArea",
                    "ClassName": "RootWebArea"
                  },
                  {
                    "Path": "1/0/0/1/1/0/0/0/0/0/0/0/0/0/3",
                    "UiPath": "1/0/0/1/1/0/0/0/0/0/0/0/0/0/3",
                    "Name": "Search",
                    "ControlType": "Edit",
                    "AutomationId": "searchInput",
                    "AvailableActions": [ "focus", "set_value" ]
                  }
                ]
              }
            }
            """;

        var actual = AgentRunner.TryRewriteBrowserSearchFieldValueEntryToTyping(
            "Search for Boyfriend on Demand within Netflix.",
            "set_window_element_text",
            new Dictionary<string, object?>
            {
                ["elementPath"] = "1/0/0/1/1/0/0/0/0/0/0/0/0/0/3",
                ["text"] = "Boyfriend on Demand"
            },
            snapshot,
            out var rewrittenArgs,
            out var browserSearchFieldPath);

        Assert.True(actual);
        Assert.Equal("Boyfriend on Demand", rewrittenArgs["text"]);
        Assert.Equal("1/0/0/1/1/0/0/0/0/0/0/0/0/0/3", browserSearchFieldPath);
    }

    [Fact]
    public void TryRewriteBrowserSearchControlAction_RewritesWrongValidPathToWebDocumentSearchControl()
    {
        var snapshot =
            """
            {
              "Window": {
                "Handle": "0x00060A88",
                "Title": "Netflix",
                "ClassName": "Chrome_WidgetWin_1"
              },
              "ElementTree": {
                "Path": "root",
                "UiPath": "root",
                "ControlType": "Window",
                "Children": [
                  {
                    "Path": "0",
                    "UiPath": "0",
                    "Name": "Open in app",
                    "ControlType": "Button",
                    "AutomationId": "openInApp",
                    "AvailableActions": [ "invoke", "focus" ]
                  },
                  {
                    "Path": "1",
                    "UiPath": "1",
                    "Name": "Netflix",
                    "ControlType": "Document",
                    "AutomationId": "RootWebArea",
                    "ClassName": "RootWebArea",
                    "Children": [
                      {
                        "Path": "1/0",
                        "UiPath": "1/0",
                        "Name": "Search",
                        "ControlType": "Button",
                        "AutomationId": "searchButton",
                        "AvailableActions": [ "invoke", "focus" ]
                      }
                    ]
                  }
                ]
              }
            }
            """;

        var actual = AgentRunner.TryRewriteBrowserSearchControlAction(
            "Search for Boyfriend on Demand within Netflix using the visible Search control.",
            "invoke_window_element",
            new Dictionary<string, object?> { ["elementPath"] = "0" },
            snapshot,
            out var rewrittenArgs);

        Assert.True(actual);
        Assert.Equal("1/0", rewrittenArgs["elementPath"]);
    }

    [Fact]
    public void BuildToolSpecificGuidance_ReturnsNull_ForModifiedShortcut()
    {
        var actual = AgentRunner.BuildToolSpecificGuidance(
            "press_window_key",
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

