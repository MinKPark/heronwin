using HeronWin.Brain;
using Xunit;

namespace HeronWin.Ava.Tests;

public sealed class AvaCliTests
{
    [Fact]
    public void ParseAva_ReturnsHelpMode()
    {
        var options = BrainConsoleMode.ParseAva(["--help"]);

        Assert.True(options.ShowHelp);
        Assert.False(options.IsValidationRun);
        Assert.False(options.IsTraceReport);
        Assert.False(options.IsReportRegeneration);
    }

    [Fact]
    public void ParseAva_ReturnsValidationArguments()
    {
        var scenarioPath = Path.Combine(Path.GetTempPath(), "ux-scenario.yml");
        var validationConfigPath = Path.Combine(Path.GetTempPath(), "validation.yml");

        var options = BrainConsoleMode.ParseAva([
            "--ux-scenario",
            scenarioPath,
            "--validation-config",
            validationConfigPath
        ]);

        Assert.True(options.IsValidationRun);
        Assert.Equal(Path.GetFullPath(scenarioPath), options.UxScenarioPath);
        Assert.Equal(Path.GetFullPath(validationConfigPath), options.ValidationConfigPath);
        Assert.Null(options.RunBundlePath);
        Assert.Null(options.TraceReportPath);
        Assert.Null(options.RegenerateReportPath);
    }

    [Fact]
    public void ParseAva_ReturnsBundleRunArgument()
    {
        var bundlePath = Path.Combine(Path.GetTempPath(), "bundle.yml");

        var options = BrainConsoleMode.ParseAva(["--run", bundlePath]);

        Assert.True(options.IsValidationRun);
        Assert.Equal(Path.GetFullPath(bundlePath), options.RunBundlePath);
        Assert.Null(options.UxScenarioPath);
        Assert.Null(options.ValidationConfigPath);
        Assert.Null(options.RegenerateReportPath);
    }

    [Fact]
    public void ParseAva_ReturnsTraceReportArgument()
    {
        var tracePath = Path.Combine(Path.GetTempPath(), "ava.debug.jsonl");

        var options = BrainConsoleMode.ParseAva(["--trace-report", tracePath]);

        Assert.True(options.IsTraceReport);
        Assert.False(options.IsValidationRun);
        Assert.False(options.IsReportRegeneration);
        Assert.Equal(Path.GetFullPath(tracePath), options.TraceReportPath);
    }

    [Fact]
    public void ParseAva_ReturnsReportRegenerationArgument()
    {
        var runPath = Path.Combine(Path.GetTempPath(), "ava-run");

        var options = BrainConsoleMode.ParseAva(["--regenerate-report", runPath]);

        Assert.True(options.IsReportRegeneration);
        Assert.False(options.IsTraceReport);
        Assert.False(options.IsValidationRun);
        Assert.Equal(Path.GetFullPath(runPath), options.RegenerateReportPath);
    }

    [Fact]
    public void ParseAva_ReturnsLatestReportRegenerationArgument()
    {
        var options = BrainConsoleMode.ParseAva(["--regenerate-report", "latest"]);

        Assert.True(options.IsReportRegeneration);
        Assert.Equal("latest", options.RegenerateReportPath);
    }

    [Theory]
    [InlineData("--ux-scenario", "scenario.yml")]
    [InlineData("--validation-config", "validation.yml")]
    public void ParseAva_RejectsPartialDirectValidation(string flag, string path)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => BrainConsoleMode.ParseAva([flag, path]));

        Assert.Contains("requires both", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseAva_RejectsBundleMixedWithDirectValidation()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => BrainConsoleMode.ParseAva([
                "--run",
                "bundle.yml",
                "--ux-scenario",
                "scenario.yml",
                "--validation-config",
                "validation.yml"
            ]));

        Assert.Contains("either --run", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseAva_RejectsTraceReportMixedWithValidation()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => BrainConsoleMode.ParseAva([
                "--trace-report",
                "trace.jsonl",
                "--ux-scenario",
                "scenario.yml",
                "--validation-config",
                "validation.yml"
            ]));

        Assert.Contains("only one AVA mode", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseAva_RejectsReportRegenerationMixedWithValidation()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => BrainConsoleMode.ParseAva([
                "--regenerate-report",
                "latest",
                "--run",
                "bundle.yml"
            ]));

        Assert.Contains("only one AVA mode", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseAva_RejectsUnknownArgument()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => BrainConsoleMode.ParseAva(["--scenario", "legacy.yml"]));

        Assert.Contains("Unknown argument", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
