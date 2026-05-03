using HeronWin.Brain;
using Xunit;

namespace HeronWin.Tars.Tests;

public sealed class TarsCliTests
{
    [Fact]
    public void ParseTars_ReturnsScenarioMode()
    {
        var scenarioPath = Path.Combine(Path.GetTempPath(), "scenario.yml");

        var options = BrainConsoleMode.ParseTars(["--scenario", scenarioPath]);

        Assert.True(options.IsScripted);
        Assert.Equal(Path.GetFullPath(scenarioPath), options.ScenarioFilePath);
    }
}
