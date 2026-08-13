using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Networking;

internal static class OutboundHttpClientFactory
{
    public static HttpClient Create(
        OutboundProxyOptions options,
        OutboundHttpLogSink? logSink = null,
        string service = "External")
    {
        HttpMessageHandler handler = CreateHandler(options);
        if (logSink is not null)
        {
            handler = new OutboundHttpLoggingHandler(handler, logSink, service);
        }

        var client = new HttpClient(handler, disposeHandler: true)
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
