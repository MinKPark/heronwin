using Xunit;

namespace HeronWin.Brain.Tests;

public sealed class ProviderModeTests
{
    [Theory]
    [InlineData("openai-codex")]
    [InlineData("chatgpt-subscription")]
    [InlineData("chatgpt")]
    [InlineData("chatgpt-web")]
    public void NormalizeProvider_MapsCodexAliases(string rawProvider)
    {
        var actual = AppConfig.NormalizeProvider(rawProvider);

        Assert.Equal(LlmProviderId.OpenAiCodex, actual);
    }

    [Fact]
    public void Resolve_OpenAiCodexProvider_IsTextOnlyAndScriptable()
    {
        var provider = LlmProviderCatalog.Resolve(LlmProviderId.OpenAiCodex);

        Assert.Equal(LlmProviderId.OpenAiCodex, provider.Id);
        Assert.Equal(BrainInteractiveMode.Text, provider.Capabilities.DefaultInteractiveMode);
        Assert.Contains(BrainInteractiveMode.Text, provider.Capabilities.SupportedInteractiveModes);
        Assert.DoesNotContain(BrainInteractiveMode.Voice, provider.Capabilities.SupportedInteractiveModes);
        Assert.True(provider.Capabilities.SupportsScriptedMode);
        Assert.False(provider.Capabilities.SupportsRuntimeModeSwitch);
    }

    [Theory]
    [InlineData("/mode:text", 1)]
    [InlineData("switch to text mode", 1)]
    [InlineData("/mode:voice", 0)]
    [InlineData("to voice mode", 0)]
    public void InteractiveCommands_ParsesModeSwitches(string input, int expectedMode)
    {
        var actual = BrainInteractiveCommands.TryParseModeSwitch(input, out var parsedMode);

        Assert.True(actual);
        Assert.Equal((BrainInteractiveMode)expectedMode, parsedMode);
    }

    [Theory]
    [InlineData("/exit")]
    [InlineData("quit")]
    public void InteractiveCommands_RecognizesExitCommands(string input)
    {
        Assert.True(BrainInteractiveCommands.IsExitCommand(input));
    }

    [Theory]
    [InlineData("/reset")]
    [InlineData("reset")]
    public void InteractiveCommands_RecognizesResetCommands(string input)
    {
        Assert.True(BrainInteractiveCommands.IsResetCommand(input));
    }
}
