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
