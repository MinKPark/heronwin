using Xunit;

namespace HeronWin.HerFace.Tests;

public sealed class HttpClientFactoryTests
{
    [Theory]
    [InlineData("http://127.0.0.1:9")]
    [InlineData("http://localhost:9")]
    [InlineData("http://[::1]:9")]
    public void IsBrokenLoopbackProxy_ReturnsTrue_ForLoopbackPort9(string value)
    {
        var actual = HerfaceHttpClientFactory.IsBrokenLoopbackProxy(value);

        Assert.True(actual);
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://proxy.example.com:9")]
    [InlineData("")]
    [InlineData(null)]
    public void IsBrokenLoopbackProxy_ReturnsFalse_ForOtherValues(string? value)
    {
        var actual = HerfaceHttpClientFactory.IsBrokenLoopbackProxy(value);

        Assert.False(actual);
    }
}
