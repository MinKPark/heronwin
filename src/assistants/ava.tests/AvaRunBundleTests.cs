using HeronWin.Ava;
using Xunit;

namespace HeronWin.Ava.Tests;

public sealed class AvaRunBundleTests
{
    [Fact]
    public void Parse_ResolvesScenarioAndConfigRelativeToBundleDirectory()
    {
        var bundlePath = Path.Combine(Path.GetTempPath(), "ava-bundles", "bundle.yml");

        var bundle = AvaRunBundleLoader.Parse(
            """
            name: Smoke
            uxScenario: ux/active-window-smoke.yml
            validationConfig: validation-configs/federal-windows-uia-min.yml
            """,
            bundlePath);

        var bundleDirectory = Path.GetDirectoryName(Path.GetFullPath(bundlePath))!;
        Assert.Equal("Smoke", bundle.Name);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(bundleDirectory, "ux", "active-window-smoke.yml")),
            bundle.UxScenarioPath);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(bundleDirectory, "validation-configs", "federal-windows-uia-min.yml")),
            bundle.ValidationConfigPath);
    }
}
