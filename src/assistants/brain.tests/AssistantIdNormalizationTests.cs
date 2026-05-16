using Xunit;

namespace HeronWin.Brain.Tests;

public sealed class AssistantIdNormalizationTests
{
    [Theory]
    [InlineData("ava")]
    [InlineData("AVA")]
    [InlineData(" ava ")]
    public void AppConfig_NormalizeAssistantId_AcceptsAva(string value)
    {
        Assert.Equal("ava", AppConfig.NormalizeAssistantId(value));
    }

    [Theory]
    [InlineData("ava")]
    [InlineData("AVA")]
    [InlineData(" ava ")]
    public void AgentPromptLoader_NormalizeAssistantId_AcceptsAva(string value)
    {
        Assert.Equal("ava", AgentPromptLoader.NormalizeAssistantId(value));
    }
}
