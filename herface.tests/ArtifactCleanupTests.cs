using Xunit;

namespace HeronWin.HerFace.Tests;

public sealed class ArtifactCleanupTests
{
    [Fact]
    public void GetEyesAndHandsScreenshotDirectory_PointsToSharedTempFolder()
    {
        var actual = ArtifactCleanup.GetEyesAndHandsScreenshotDirectory();

        Assert.Equal(
            Path.Combine(Path.GetTempPath(), "heronwin", "eyesandhands"),
            actual);
    }
}
