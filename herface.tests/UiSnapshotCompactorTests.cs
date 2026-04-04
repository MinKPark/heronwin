using System.Net;
using Xunit;

namespace HeronWin.HerFace.Tests;

public sealed class UiSnapshotCompactorTests
{
    [Fact]
    public void CompactToolTextForContext_PreservesActionableBrowserChrome_ForLargeSnapshots()
    {
        var filler = new string('x', 600);
        var snapshot = $$"""
        {
          "Window": {
            "Handle": "0x00060A88",
            "Title": "YouTube - Personal - Microsoft Edge",
            "ClassName": "Chrome_WidgetWin_1"
          },
          "ElementTree": {
            "Path": "root",
            "UiPath": "root",
            "Name": "YouTube - Personal - Microsoft Edge",
            "ControlType": "Window",
            "Children": [
              {
                "Path": "1/0",
                "UiPath": "1/0",
                "Name": "Address and search bar",
                "ControlType": "Edit",
                "ClassName": "OmniboxViewViews",
                "AvailableActions": [ "focus", "set_value" ]
              },
              {
                "Path": "1/1",
                "UiPath": "1/1",
                "Name": "New Tab",
                "ControlType": "Button",
                "AvailableActions": [ "focus", "invoke" ]
              },
              {
                "Path": "2/0",
                "UiPath": "2/0",
                "Name": "{{filler}}",
                "ControlType": "Text"
              },
              {
                "Path": "2/1",
                "UiPath": "2/1",
                "Name": "{{filler}}",
                "ControlType": "Text"
              }
            ]
          }
        }
        """;

        var profile = new LlmModelProfile(
            LlmProviderId.OpenAiApi,
            "gpt-5.4-mini",
            ContextCompressionTriggerRatio: 0.55,
            WindowSnapshotCharBudget: 650,
            FocusSnapshotCharBudget: 320,
            MaxThrottleRetries: 2);

        var actual = UiSnapshotCompactor.CompactToolTextForContext(
            "describe_selected_window",
            snapshot,
            profile);

        Assert.Contains("compacted for gpt-5.4-mini", actual, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Address and search bar", actual, StringComparison.Ordinal);
        Assert.Contains("New Tab", actual, StringComparison.Ordinal);
        Assert.DoesNotContain(filler, actual, StringComparison.Ordinal);
        Assert.True(actual.Length <= profile.WindowSnapshotCharBudget);
    }

    [Fact]
    public void CompactToolTextForContext_PreservesMeaningfulWebContent_WhenBrowserChromeIsVerbose()
    {
        var filler = new string('x', 600);
        var snapshot = $$"""
        {
          "Window": {
            "Handle": "0x016A037E",
            "Title": "Netflix - Personal - Microsoft Edge",
            "ClassName": "Chrome_WidgetWin_1"
          },
          "ElementTree": {
            "Path": "root",
            "UiPath": "root",
            "Name": "Netflix - Personal - Microsoft Edge",
            "ControlType": "Window",
            "Children": [
              {
                "Path": "1/0",
                "UiPath": "1/0",
                "Name": "Minimize",
                "ControlType": "Button"
              },
              {
                "Path": "1/1",
                "UiPath": "1/1",
                "Name": "Restore",
                "ControlType": "Button"
              },
              {
                "Path": "1/2",
                "UiPath": "1/2",
                "Name": "Close",
                "ControlType": "Button"
              },
              {
                "Path": "2/0",
                "UiPath": "2/0",
                "Name": "App bar",
                "ControlType": "ToolBar",
                "ClassName": "EdgeToolbarView",
                "Children": [
                  {
                    "Path": "2/0/0",
                    "UiPath": "2/0/0",
                    "Name": "Back",
                    "ControlType": "Button",
                    "ClassName": "BackForwardButton",
                    "AvailableActions": [ "focus", "invoke" ]
                  },
                  {
                    "Path": "2/0/1",
                    "UiPath": "2/0/1",
                    "Name": "Address and search bar",
                    "ControlType": "Edit",
                    "ClassName": "OmniboxViewViews",
                    "AvailableActions": [ "focus", "set_value" ]
                  },
                  {
                    "Path": "2/0/2",
                    "UiPath": "2/0/2",
                    "Name": "{{filler}}",
                    "ControlType": "Text"
                  }
                ]
              },
              {
                "Path": "3/0",
                "UiPath": "3/0",
                "Name": "Netflix",
                "ControlType": "Document",
                "AutomationId": "RootWebArea",
                "HasKeyboardFocus": true,
                "Children": [
                  {
                    "Path": "3/0/0",
                    "UiPath": "3/0/0",
                    "ControlType": "Pane",
                    "Children": [
                      {
                        "Path": "3/0/0/0",
                        "UiPath": "3/0/0/0",
                        "ControlType": "Pane",
                        "Children": [
                          {
                            "Path": "3/0/0/0/0",
                            "UiPath": "3/0/0/0/0",
                            "Name": "Who's watching?",
                            "ControlType": "Text",
                            "ClassName": "profile-gate-label"
                          },
                          {
                            "Path": "3/0/0/0/1",
                            "UiPath": "3/0/0/0/1",
                            "Name": "Min",
                            "ControlType": "ListItem",
                            "ClassName": "profile"
                          },
                          {
                            "Path": "3/0/0/0/2",
                            "UiPath": "3/0/0/0/2",
                            "Name": "Esther",
                            "ControlType": "ListItem",
                            "ClassName": "profile"
                          },
                          {
                            "Path": "3/0/0/0/3",
                            "UiPath": "3/0/0/0/3",
                            "Name": "Henry",
                            "ControlType": "ListItem",
                            "ClassName": "profile"
                          },
                          {
                            "Path": "3/0/0/0/4",
                            "UiPath": "3/0/0/0/4",
                            "Name": "Yoon",
                            "ControlType": "ListItem",
                            "ClassName": "profile"
                          },
                          {
                            "Path": "3/0/0/0/5",
                            "UiPath": "3/0/0/0/5",
                            "Name": "\uE716Add Profile",
                            "ControlType": "ListItem"
                          },
                          {
                            "Path": "3/0/0/0/6",
                            "UiPath": "3/0/0/0/6",
                            "Name": "Manage Profiles",
                            "ControlType": "Hyperlink",
                            "ClassName": "profile-button"
                          }
                        ]
                      }
                    ]
                  }
                ]
              }
            ]
          }
        }
        """;

        var profile = new LlmModelProfile(
            LlmProviderId.OpenAiApi,
            "gpt-5.4-mini",
            ContextCompressionTriggerRatio: 0.55,
            WindowSnapshotCharBudget: 1_200,
            FocusSnapshotCharBudget: 320,
            MaxThrottleRetries: 2);

        var actual = UiSnapshotCompactor.CompactToolTextForContext(
            "describe_selected_window",
            snapshot,
            profile);

        Assert.Contains("Min", actual, StringComparison.Ordinal);
        Assert.Contains("Add Profile", actual, StringComparison.Ordinal);
        Assert.Contains("Manage Profiles", actual, StringComparison.Ordinal);
        Assert.Contains("Address and search bar", actual, StringComparison.Ordinal);
        Assert.True(actual.Length <= profile.WindowSnapshotCharBudget);
    }

    [Fact]
    public void CompactToolTextForContext_PreservesDeepVisibleActionableResultNodes()
    {
        var filler = new string('x', 500);
        var snapshot = $$"""
        {
          "Window": {
            "Handle": "0x00070EC4",
            "Title": "Netflix - Personal - Microsoft Edge",
            "ClassName": "Chrome_WidgetWin_1"
          },
          "ElementTree": {
            "Path": "root",
            "UiPath": "root",
            "Name": "Netflix - Personal - Microsoft Edge",
            "ControlType": "Window",
            "Children": [
              {
                "Path": "1/0",
                "UiPath": "1/0",
                "Name": "Netflix",
                "ControlType": "Document",
                "AutomationId": "RootWebArea",
                "Children": [
                  {
                    "Path": "1/0/0",
                    "UiPath": "1/0/0",
                    "ControlType": "Pane",
                    "Children": [
                      {
                        "Path": "1/0/0/0",
                        "UiPath": "1/0/0/0",
                        "Name": "Search",
                        "ControlType": "Edit",
                        "AutomationId": "searchInput",
                        "AvailableActions": [ "focus", "set_value" ]
                      },
                      {
                        "Path": "1/0/0/1",
                        "UiPath": "1/0/0/1",
                        "ControlType": "Pane",
                        "Children": [
                          {
                            "Path": "1/0/0/1/0",
                            "UiPath": "1/0/0/1/0",
                            "ControlType": "Pane",
                            "Children": [
                              {
                                "Path": "1/0/0/1/0/0",
                                "UiPath": "1/0/0/1/0/0",
                                "Name": "Boyfriend on Demand",
                                "ControlType": "Hyperlink",
                                "IsOffscreen": false,
                                "AvailableActions": [ "focus", "invoke" ]
                              },
                              {
                                "Path": "1/0/0/1/0/1",
                                "UiPath": "1/0/0/1/0/1",
                                "Name": "{{filler}}",
                                "ControlType": "Text"
                              }
                            ]
                          }
                        ]
                      }
                    ]
                  }
                ]
              }
            ]
          }
        }
        """;

        var profile = new LlmModelProfile(
            LlmProviderId.OpenAiApi,
            "gpt-5.4-mini",
            ContextCompressionTriggerRatio: 0.55,
            WindowSnapshotCharBudget: 1_400,
            FocusSnapshotCharBudget: 320,
            MaxThrottleRetries: 2);

        var actual = UiSnapshotCompactor.CompactToolTextForContext(
            "describe_selected_window",
            snapshot,
            profile);

        Assert.Contains("Boyfriend on Demand", actual, StringComparison.Ordinal);
        Assert.Contains("1/0/0/1/0/0", actual, StringComparison.Ordinal);
        Assert.True(actual.Length <= profile.WindowSnapshotCharBudget);
    }

    [Fact]
    public void CompactToolTextForContext_PreservesNamedActionableResultNodes_WhenVisibilityFlagIsMissing()
    {
        var snapshot = """
        {
          "Window": {
            "Handle": "0x00070EC4",
            "Title": "Netflix - Personal - Microsoft Edge",
            "ClassName": "Chrome_WidgetWin_1"
          },
          "ElementTree": {
            "Path": "root",
            "UiPath": "root",
            "Name": "Netflix - Personal - Microsoft Edge",
            "ControlType": "Window",
            "Children": [
              {
                "Path": "1/0",
                "UiPath": "1/0",
                "Name": "Netflix",
                "ControlType": "Document",
                "AutomationId": "RootWebArea",
                "Children": [
                  {
                    "Path": "1/0/0",
                    "UiPath": "1/0/0",
                    "ControlType": "Pane",
                    "Children": [
                      {
                        "Path": "1/0/0/0",
                        "UiPath": "1/0/0/0",
                        "ControlType": "Pane",
                        "Children": [
                          {
                            "Path": "1/0/0/0/0",
                            "UiPath": "1/0/0/0/0",
                            "Name": "Boyfriend on Demand",
                            "ControlType": "Hyperlink",
                            "AvailableActions": [ "focus", "invoke" ]
                          },
                          {
                            "Path": "1/0/0/0/1",
                            "UiPath": "1/0/0/0/1",
                            "Name": "Anaconda",
                            "ControlType": "Hyperlink",
                            "AvailableActions": [ "focus", "invoke" ]
                          }
                        ]
                      }
                    ]
                  }
                ]
              }
            ]
          }
        }
        """;

        var profile = new LlmModelProfile(
            LlmProviderId.OpenAiApi,
            "gpt-5.4-mini",
            ContextCompressionTriggerRatio: 0.55,
            WindowSnapshotCharBudget: 1_200,
            FocusSnapshotCharBudget: 320,
            MaxThrottleRetries: 2);

        var actual = UiSnapshotCompactor.CompactToolTextForContext(
            "describe_selected_window",
            snapshot,
            profile);

        Assert.Contains("Boyfriend on Demand", actual, StringComparison.Ordinal);
        Assert.Contains("1/0/0/0/0", actual, StringComparison.Ordinal);
        Assert.Contains("Anaconda", actual, StringComparison.Ordinal);
        Assert.True(actual.Length <= profile.WindowSnapshotCharBudget);
    }

    [Fact]
    public void CompactToolTextForContext_PrioritizesNamedResultTiles_OverSiteLogo_WhenOffscreenFlagIsMisleading()
    {
        var snapshot = """
        {
          "Window": {
            "Handle": "0x00070EC4",
            "Title": "Netflix - Personal - Microsoft Edge",
            "ClassName": "Chrome_WidgetWin_1"
          },
          "ElementTree": {
            "Path": "root",
            "UiPath": "root",
            "Name": "Netflix - Personal - Microsoft Edge",
            "ControlType": "Window",
            "Children": [
              {
                "Path": "1/0",
                "UiPath": "1/0",
                "Name": "Netflix",
                "ControlType": "Document",
                "AutomationId": "RootWebArea",
                "Children": [
                  {
                    "Path": "1/0/0",
                    "UiPath": "1/0/0",
                    "Name": "Netflix",
                    "ControlType": "Hyperlink",
                    "ClassName": "logo icon-logoUpdate active",
                    "AvailableActions": [ "focus", "invoke" ]
                  },
                  {
                    "Path": "1/0/1",
                    "UiPath": "1/0/1",
                    "ControlType": "Pane",
                    "Children": [
                      {
                        "Path": "1/0/1/0",
                        "UiPath": "1/0/1/0",
                        "Name": "Boyfriend on Demand",
                        "ControlType": "Hyperlink",
                        "ClassName": "slider-refocus",
                        "IsOffscreen": true,
                        "AvailableActions": [ "focus", "invoke" ]
                      },
                      {
                        "Path": "1/0/1/1",
                        "UiPath": "1/0/1/1",
                        "Name": "Pursuit of Jade",
                        "ControlType": "Hyperlink",
                        "ClassName": "slider-refocus",
                        "IsOffscreen": true,
                        "AvailableActions": [ "focus", "invoke" ]
                      }
                    ]
                  }
                ]
              }
            ]
          }
        }
        """;

        var profile = new LlmModelProfile(
            LlmProviderId.OpenAiApi,
            "gpt-5.4-mini",
            ContextCompressionTriggerRatio: 0.55,
            WindowSnapshotCharBudget: 1_200,
            FocusSnapshotCharBudget: 320,
            MaxThrottleRetries: 2);

        var actual = UiSnapshotCompactor.CompactToolTextForContext(
            "describe_selected_window",
            snapshot,
            profile);

        var boyfriendIndex = actual.IndexOf("1/0/1/0: Hyperlink \"Boyfriend on Demand\"", StringComparison.Ordinal);
        var logoIndex = actual.IndexOf("1/0/0: Hyperlink \"Netflix\"", StringComparison.Ordinal);

        Assert.Contains("Boyfriend on Demand", actual, StringComparison.Ordinal);
        Assert.Contains("Pursuit of Jade", actual, StringComparison.Ordinal);
        Assert.True(boyfriendIndex >= 0);
        Assert.True(logoIndex >= 0);
        Assert.True(boyfriendIndex < logoIndex);
        Assert.True(actual.Length <= profile.WindowSnapshotCharBudget);
    }

    [Fact]
    public void Create_ReturnsMoreAggressiveCompression_ForMiniModel()
    {
        var mini = LlmModelProfiles.Create(LlmProviderId.OpenAiApi, "gpt-5.4-mini");
        var standard = LlmModelProfiles.Create(LlmProviderId.OpenAiApi, "gpt-5.4");

        Assert.True(mini.ContextCompressionTriggerRatio < standard.ContextCompressionTriggerRatio);
        Assert.True(mini.WindowSnapshotCharBudget < standard.WindowSnapshotCharBudget);
    }

    [Fact]
    public void TryGetRetryDelay_ParsesTokenRateLimitDelay_FromBody()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        var responseText = """
        {
          "error": {
            "message": "Rate limit reached for gpt-5.4-mini on tokens per min (TPM): Limit 200000, Used 167239, Requested 189937. Please try again in 47.152s.",
            "type": "tokens",
            "code": "rate_limit_exceeded"
          }
        }
        """;

        var actual = LlmThrottleRetry.TryGetRetryDelay(response, responseText, out var delay, out var limitKind);

        Assert.True(actual);
        Assert.Equal("Token throughput", limitKind);
        Assert.InRange(delay.TotalSeconds, 47, 48);
    }
}
