using System.Net;
using System.Net.Sockets;
using AnimeGoNet.App.Networking;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Sources;

namespace AnimeGoNet.App.Torrents;

public sealed class PinnedTorrentHttpTransport : ITorrentHttpTransport
{
    private readonly OutboundProxyOptions? _outboundProxy;
    private readonly OutboundHttpLogSink? _logSink;
    private readonly string _service;

    public PinnedTorrentHttpTransport(OutboundProxyOptions? outboundProxy = null)
        : this(outboundProxy, null, "Torrent")
    {
    }

    internal PinnedTorrentHttpTransport(
        OutboundProxyOptions? outboundProxy,
        OutboundHttpLogSink? logSink,
        string service)
    {
        _outboundProxy = outboundProxy;
        _logSink = logSink;
        _service = service;
    }

    public async ValueTask<TorrentHttpResponse> SendAsync(
        Uri uri,
        IReadOnlyList<IPAddress> validatedAddresses,
        CancellationToken cancellationToken) =>
        await SendAsync(
            uri,
            validatedAddresses,
            new TorrentHttpRequestOptions(),
            cancellationToken).ConfigureAwait(false);

    public async ValueTask<TorrentHttpResponse> SendAsync(
        Uri uri,
        IReadOnlyList<IPAddress> validatedAddresses,
        TorrentHttpRequestOptions requestOptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(validatedAddresses);
        ArgumentNullException.ThrowIfNull(requestOptions);
        if (validatedAddresses.Count == 0)
        {
            throw new ArgumentException("At least one validated address is required.", nameof(validatedAddresses));
        }

        var useProxy = _outboundProxy is not null
            && OutboundProxyPolicy.ShouldProxy(uri, _outboundProxy);
        HttpMessageHandler handler = useProxy
            ? new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseProxy = true,
                Proxy = new SelectiveWebProxy(_outboundProxy!),
            }
            : CreatePinnedHandler(uri, validatedAddresses);
        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("AnimeGoNet/1.0");
        if (requestOptions.MikanIdentityCookie is { } cookie)
        {
            request.Headers.TryAddWithoutValidation(
                "Cookie",
                $"{MikanIdentityCookie.Name}={cookie}");
        }
        var trace = _logSink?.Start(_service, request.Method, uri);
        try
        {
            var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            trace?.Complete((int)response.StatusCode);
            var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var owner = new HttpResponseOwner(response, client);
            return new TorrentHttpResponse(
                response.StatusCode,
                response.Headers.Location,
                response.Content.Headers.ContentLength,
                content,
                owner);
        }
        catch (Exception exception)
        {
            trace?.Fail(exception);
            client.Dispose();
            throw;
        }
    }

    private static SocketsHttpHandler CreatePinnedHandler(
        Uri uri,
        IReadOnlyList<IPAddress> validatedAddresses) =>
        new()
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            ConnectCallback = async (context, token) =>
            {
                if (!string.Equals(context.DnsEndPoint.Host, uri.IdnHost, StringComparison.OrdinalIgnoreCase))
                {
                    throw new HttpRequestException("The HTTP connection target did not match the validated host.");
                }

                SocketException? lastError = null;
                foreach (var address in validatedAddresses)
                {
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                    {
                        NoDelay = true,
                    };
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), token)
                            .ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (SocketException exception)
                    {
                        lastError = exception;
                        socket.Dispose();
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }

                throw new HttpRequestException("No validated address accepted the connection.", lastError);
            },
        };

    private sealed class HttpResponseOwner(HttpResponseMessage response, HttpClient client) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            response.Dispose();
            client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
