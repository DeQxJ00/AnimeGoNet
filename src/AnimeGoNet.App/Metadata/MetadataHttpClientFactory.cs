using AnimeGoNet.App.Networking;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Metadata;

internal static class MetadataHttpClientFactory
{
    public static HttpClient Create(
        OutboundProxyOptions options,
        OutboundHttpLogSink? logSink = null,
        string service = "Metadata") =>
        OutboundHttpClientFactory.Create(options, logSink, service);

    internal static HttpClientHandler CreateHandler(OutboundProxyOptions options) =>
        OutboundHttpClientFactory.CreateHandler(options);
}
