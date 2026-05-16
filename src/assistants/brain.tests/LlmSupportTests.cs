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

    [Theory]
    [InlineData("spark")]
    [InlineData("codex-spark")]
    [InlineData("gpt-5.3-codex-spark")]
    public void OpenAiCodexModels_ResolveSparkAliases_AsTextOnlySpark(string model)
    {
        var actual = OpenAiCodexModels.Resolve(model);

        Assert.Equal(OpenAiCodexModels.SparkModelName, actual.EffectiveModel);
        Assert.Equal(OpenAiCodexModels.SparkModelName, actual.CliModel);
        Assert.True(actual.IsSpark);
        Assert.False(actual.SupportsImageInputs);
    }

    [Fact]
    public void OpenAiCodexModels_NormalizeConfiguredModel_LeavesDefaultEmpty()
    {
        var actual = OpenAiCodexModels.NormalizeConfiguredModel(string.Empty);

        Assert.Equal(string.Empty, actual);
    }

    [Fact]
    public void OpenAiCodexModels_NormalizeConfiguredModel_ExpandsSparkAlias()
    {
        var actual = OpenAiCodexModels.NormalizeConfiguredModel("spark");

        Assert.Equal(OpenAiCodexModels.SparkModelName, actual);
    }

    [Fact]
    public void Create_ReturnsMoreAggressiveCompression_ForCodexSpark()
    {
        var spark = LlmModelProfiles.Create(LlmProviderId.OpenAiCodex, "spark");
        var standard = LlmModelProfiles.Create(LlmProviderId.OpenAiCodex, "gpt-5.5");

        Assert.Equal(OpenAiCodexModels.SparkModelName, spark.ModelName);
        Assert.True(spark.ContextCompressionTriggerRatio < standard.ContextCompressionTriggerRatio);
        Assert.True(spark.WindowSnapshotCharBudget < standard.WindowSnapshotCharBudget);
        Assert.True(spark.FocusSnapshotCharBudget < standard.FocusSnapshotCharBudget);
    }

    [Fact]
    public void BuildExecArguments_PassesSparkModelWithoutImages()
    {
        var args = OpenAiCodexCliSupport.BuildExecArguments(
            OpenAiCodexModels.Resolve("spark"),
            "schema.json",
            "output.json",
            ["screen.png"]);

        Assert.Contains("--model", args);
        Assert.Contains(OpenAiCodexModels.SparkModelName, args);
        Assert.DoesNotContain("--image", args);
        Assert.Equal("-", args[^1]);
    }

    [Fact]
    public void BuildExecArguments_PreservesImagesForNonSparkModel()
    {
        var args = OpenAiCodexCliSupport.BuildExecArguments(
            OpenAiCodexModels.Resolve("gpt-5.5"),
            "schema.json",
            "output.json",
            ["screen.png"]);

        Assert.Contains("--model", args);
        Assert.Contains("gpt-5.5", args);
        Assert.Contains("--image", args);
        Assert.Contains("screen.png", args);
    }

    [Fact]
    public void CodexBridgeRequest_OmitsVisualAttachmentsForSpark()
    {
        var request = CodexBridgeRequest.Create(
            [new AgentMessage.VisualContext("Screenshot evidence.", [new ToolImage("image/png", "AA==")])],
            [],
            null,
            OpenAiCodexModels.Resolve("spark"));

        Assert.Empty(request.ImageAttachments);
        Assert.Equal(1, request.OmittedImageCount);
        Assert.Contains("Image attachments omitted", request.Prompt, StringComparison.Ordinal);
        Assert.Contains(OpenAiCodexModels.SparkModelName, request.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void CodexBridgeRequest_PreservesVisualAttachmentsForNonSparkModel()
    {
        var request = CodexBridgeRequest.Create(
            [new AgentMessage.VisualContext("Screenshot evidence.", [new ToolImage("image/png", "AA==")])],
            [],
            null,
            OpenAiCodexModels.Resolve("gpt-5.5"));

        var attachment = Assert.Single(request.ImageAttachments);
        Assert.Equal("message-001-image-01.png", attachment.FileName);
        Assert.Equal(0, request.OmittedImageCount);
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
