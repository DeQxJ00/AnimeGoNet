using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Networking;

internal static class OutboundHttpClientFactory
{
    public static HttpClient Create(OutboundProxyOptions options)
    {
        var client = new HttpClient(CreateHandler(options), disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        return client;
    }

    internal static HttpClientHandler CreateHandler(OutboundProxyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var enabled = options.Url is not null && options.HostPatterns.Count > 0;
        return new HttpClientHandler
        {
            UseProxy = enabled,
            Proxy = enabled ? new SelectiveWebProxy(options) : null,
        };
    }
}
