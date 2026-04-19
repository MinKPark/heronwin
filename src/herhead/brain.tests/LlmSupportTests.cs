using System.Net;
using Xunit;

namespace HeronWin.Brain.Tests;

public sealed class LlmSupportTests
{
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
