using HeronWin.Brain;
using Xunit;

namespace HeronWin.Ava.Tests;

public sealed class AvaScenarioLoaderTests
{
    [Fact]
    public void BrainScenarioLoader_AcceptsUxScenarioShape()
    {
        var suite = BrainScenarioLoader.Parse(
            """
            name: Active window smoke
            commands:
              - Describe active window.
              - Check focused control.
            assertions:
              allowToolErrors: true
            """,
            "active-window-smoke.yml");

        var scenario = Assert.Single(suite.Scenarios);
        Assert.Equal("Active window smoke", scenario.Name);
        Assert.Equal(["Describe active window.", "Check focused control."], scenario.Commands);
        Assert.True(scenario.Assertions.AllowToolErrors);
    }
}
