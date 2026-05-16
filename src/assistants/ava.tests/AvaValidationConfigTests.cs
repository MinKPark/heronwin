using HeronWin.Ava;
using Xunit;

namespace HeronWin.Ava.Tests;

public sealed class AvaValidationConfigTests
{
    [Fact]
    public void Parse_AppliesDefaults()
    {
        var config = AvaValidationConfigLoader.Parse(
            """
            name: Minimal
            """,
            "minimal.yml");

        Assert.Equal("Minimal", config.Name);
        Assert.Equal(AvaValidationConfig.DefaultProfile, config.Profile);
        Assert.Equal(AvaContinuationPolicy.ContinueAndReport, config.ContinuationPolicy);
        Assert.Equal(["after"], config.Checkpoints);

        var step = config.ResolveStep(0);
        Assert.Null(step.Name);
        Assert.Equal("continue-and-report", step.ContinuationPolicy);
        Assert.Equal(["after"], step.Checkpoints);
        Assert.Null(step.WindowHandle);
    }

    [Fact]
    public void ResolveStep_InheritsDefaultsAndAppliesPerStepOverrides()
    {
        var config = AvaValidationConfigLoader.Parse(
            """
            name: Custom
            profile: federal-windows-uia-min
            continuationPolicy: continue-and-report
            checkpoints:
              - after
            steps:
              - name: Inventory
              - name: Focus
                continuationPolicy: stop-on-fail
                checkpoints:
                  - before
                  - after
            """,
            "custom.yml");

        var inherited = config.ResolveStep(0);
        Assert.Equal("Inventory", inherited.Name);
        Assert.Equal("continue-and-report", inherited.ContinuationPolicy);
        Assert.Equal(["after"], inherited.Checkpoints);
        Assert.Null(inherited.WindowHandle);

        var overridden = config.ResolveStep(1);
        Assert.Equal("Focus", overridden.Name);
        Assert.Equal("stop-on-fail", overridden.ContinuationPolicy);
        Assert.Equal(["before", "after"], overridden.Checkpoints);
        Assert.Null(overridden.WindowHandle);
    }

    [Fact]
    public void ResolveStep_InheritsDefaultWindowHandleAndAppliesPerStepOverride()
    {
        var config = AvaValidationConfigLoader.Parse(
            """
            name: Window Target
            windowHandle: 0x00010001
            steps:
              - name: Inventory
              - name: Focus
                windowHandle: 0x00020002
            """,
            "window-target.yml");

        var inherited = config.ResolveStep(0);
        Assert.Equal("0x00010001", inherited.WindowHandle);

        var overridden = config.ResolveStep(1);
        Assert.Equal("0x00020002", overridden.WindowHandle);
    }

    [Fact]
    public void Parse_AcceptsFederalWebMinimumProfile()
    {
        var config = AvaValidationConfigLoader.Parse(
            """
            name: Federal web minimum
            profile: federal-web-min
            """,
            "federal-web-min.yml");

        Assert.Equal(AvaProfileIds.FederalWebMin, config.Profile);
    }
}
