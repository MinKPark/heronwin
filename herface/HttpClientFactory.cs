namespace HeronWin.HerFace;

internal sealed record HerfaceHttpClientSetup(
    HttpClient Client,
    bool BypassedBrokenLoopbackProxy,
    string? BypassedProxyValue);

internal static class HerfaceHttpClientFactory
{
    public static HerfaceHttpClientSetup Create()
    {
        var brokenProxy = FindBrokenLoopbackProxyFromEnvironment();
        if (brokenProxy is null)
        {
            return new HerfaceHttpClientSetup(new HttpClient(), false, null);
        }

        var handler = new SocketsHttpHandler
        {
            UseProxy = false
        };

        return new HerfaceHttpClientSetup(new HttpClient(handler, disposeHandler: true), true, brokenProxy);
    }

    internal static string? FindBrokenLoopbackProxyFromEnvironment()
    {
        foreach (var variableName in ProxyVariableNames)
        {
            var value = Environment.GetEnvironmentVariable(variableName);
            if (IsBrokenLoopbackProxy(value))
            {
                return value;
            }
        }

        return null;
    }

    internal static bool IsBrokenLoopbackProxy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Port != 9)
        {
            return false;
        }

        return uri.IsLoopback;
    }

    private static readonly string[] ProxyVariableNames =
    [
        "HTTPS_PROXY",
        "HTTP_PROXY",
        "ALL_PROXY",
        "https_proxy",
        "http_proxy",
        "all_proxy"
    ];
}
