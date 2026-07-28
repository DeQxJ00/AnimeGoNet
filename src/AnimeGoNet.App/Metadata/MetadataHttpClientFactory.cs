using System.Net;

namespace AnimeGoNet.App.Metadata;

internal static class MetadataHttpClientFactory
{
    public static HttpClient Create(Uri? proxyUrl)
    {
        var client = new HttpClient(CreateHandler(proxyUrl), disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        return client;
    }

    internal static HttpClientHandler CreateHandler(Uri? proxyUrl)
    {
        var handler = new HttpClientHandler();
        if (proxyUrl is not null)
        {
            handler.UseProxy = true;
            handler.Proxy = new WebProxy(proxyUrl);
        }
        else
        {
            handler.UseProxy = false;
        }
        return handler;
    }
}
