using System.Net;

namespace AnimeGoNet.App.AiTesterCompat;

public static class HttpClientFactory
{
    public static HttpClient Create(TesterConfig config)
    {
        HttpMessageHandler handler = string.IsNullOrWhiteSpace(config.ProxyUrl)
            ? new SocketsHttpHandler()
            : new SocketsHttpHandler
            {
                Proxy = new WebProxy(config.ProxyUrl),
                UseProxy = true
            };

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds)
        };
    }
}
