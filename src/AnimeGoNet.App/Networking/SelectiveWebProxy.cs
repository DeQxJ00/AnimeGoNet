using System.Net;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Networking;

internal sealed class SelectiveWebProxy(OutboundProxyOptions options) : IWebProxy
{
    public ICredentials? Credentials { get; set; }

    public Uri? GetProxy(Uri destination) =>
        OutboundProxyPolicy.ShouldProxy(destination, options)
            ? options.Url
            : destination;

    public bool IsBypassed(Uri host) =>
        !OutboundProxyPolicy.ShouldProxy(host, options);
}
