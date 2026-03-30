using Xunit;

namespace HeronWin.HerBody.EyesAndHands.Tests;

public sealed class KeyboardInputParsingTests
{
    [Theory]
    [InlineData("a", 0x41)]
    [InlineData("Z", 0x5A)]
    [InlineData("1", 0x31)]
    [InlineData("Enter", 0x0D)]
    [InlineData("Esc", 0x1B)]
    [InlineData("Tab", 0x09)]
    [InlineData("F5", 0x74)]
    [InlineData("F24", 0x87)]
    public void ResolveVirtualKey_SupportsNamedKeysAndAsciiKeys(string key, ushort expected)
    {
        var actual = WindowAutomation.ResolveVirtualKey(key);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void NormalizeModifierNames_CanonicalizesAndDeduplicatesModifiers()
    {
        var actual = WindowAutomation.NormalizeModifierNames(["Ctrl", "shift", "control", "META"]);

        Assert.Equal(["control", "shift", "win"], actual);
    }

    [Fact]
    public void ResolveModifierVirtualKeys_MapsCanonicalModifierNames()
    {
        var actual = WindowAutomation.ResolveModifierVirtualKeys(["control", "alt", "win"]);

        Assert.Equal([NativeMethods.VkControl, NativeMethods.VkAlt, NativeMethods.VkLWin], actual);
    }
}
