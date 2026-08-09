using AnimeGoNet.App.Networking;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Metadata;

internal static class MetadataHttpClientFactory
{
    public static HttpClient Create(OutboundProxyOptions options) =>
        OutboundHttpClientFactory.Create(options);

    internal static HttpClientHandler CreateHandler(OutboundProxyOptions options) =>
        OutboundHttpClientFactory.CreateHandler(options);
}
