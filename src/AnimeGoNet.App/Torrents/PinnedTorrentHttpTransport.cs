using System.Net;
using System.Net.Sockets;

namespace AnimeGoNet.App.Torrents;

public sealed class PinnedTorrentHttpTransport : ITorrentHttpTransport
{
    public async ValueTask<TorrentHttpResponse> SendAsync(
        Uri uri,
        IReadOnlyList<IPAddress> validatedAddresses,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(validatedAddresses);
        if (validatedAddresses.Count == 0)
        {
            throw new ArgumentException("At least one validated address is required.", nameof(validatedAddresses));
        }

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
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
        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("AnimeGoNet/1.0");
        try
        {
            var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var owner = new HttpResponseOwner(response, client);
            return new TorrentHttpResponse(
                response.StatusCode,
                response.Headers.Location,
                response.Content.Headers.ContentLength,
                content,
                owner);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

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
