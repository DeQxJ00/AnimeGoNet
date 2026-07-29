using System.Net;
using System.Net.Sockets;
using System.Text;
using AnimeGoNet.App.Feeds;
using AnimeGoNet.App.Torrents;

namespace AnimeGoNet.App.Tests.Networking;

public sealed class LoopbackHttpFixtureTests
{
    private const string Rss = """
        <rss><channel><item>
        <title>Loopback fixture</title>
        <link>https://mikanani.me/Home/Episode/loopback</link>
        <enclosure type="application/x-bittorrent" length="5"
          url="https://mikanani.me/Home/Episode/loopback.torrent" />
        </item></channel></rss>
        """;

    [Fact]
    public async Task RssReaderParsesChunkedResponseFromRealLoopbackServer()
    {
        await using var server = new OneShotLoopbackServer(
            BuildChunkedResponse(
                HttpStatusCode.OK,
                Encoding.UTF8.GetBytes(Rss)));
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        var reader = new RssFeedReader(new RssFeedHttpClient(client));

        var document = await reader.ParseUrlAsync(
            new Uri(server.Origin, "/feed.xml?fixture=1").AbsoluteUri);

        var item = Assert.Single(document.Items);
        Assert.Equal("Loopback fixture", item.Title);
        Assert.Equal(5, item.Length);
        var request = await server.RequestHeaders;
        Assert.StartsWith("GET /feed.xml?fixture=1 HTTP/1.1\r\n", request);
        Assert.Contains(
            $"Host: 127.0.0.1:{server.Origin.Port}\r\n",
            request,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PinnedTransportConnectsOnlyToValidatedAddressAndDoesNotAutoRedirect()
    {
        await using var server = new OneShotLoopbackServer(
            BuildResponse(
                HttpStatusCode.Found,
                [],
                "/next"));
        var uri = new Uri(
            $"http://fixture.example.invalid:{server.Origin.Port}/private-feed");
        var transport = new PinnedTorrentHttpTransport();

        await using var response = await transport.SendAsync(
            uri,
            [IPAddress.Loopback],
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(new Uri("/next", UriKind.Relative), response.RedirectLocation);
        Assert.Equal(0, response.ContentLength);
        var request = await server.RequestHeaders;
        Assert.StartsWith("GET /private-feed HTTP/1.1\r\n", request);
        Assert.Contains(
            $"Host: fixture.example.invalid:{server.Origin.Port}\r\n",
            request,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "User-Agent: AnimeGoNet/1.0\r\n",
            request,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PinnedTransportStreamsBodyFromRealSocketWithoutResolvingUriHost()
    {
        var payload = Encoding.ASCII.GetBytes("loopback-transport-body");
        await using var server = new OneShotLoopbackServer(
            BuildResponse(HttpStatusCode.OK, payload));
        var transport = new PinnedTorrentHttpTransport();

        await using var response = await transport.SendAsync(
            new Uri($"http://never-resolve.invalid:{server.Origin.Port}/fixture"),
            [IPAddress.Loopback],
            CancellationToken.None);
        using var output = new MemoryStream();
        await response.Content.CopyToAsync(output);

        Assert.Equal(payload, output.ToArray());
        Assert.Equal(payload.Length, response.ContentLength);
        Assert.Equal(1, server.AcceptedConnections);
    }

    private static byte[] BuildResponse(
        HttpStatusCode status,
        byte[] body,
        string? location = null)
    {
        var reason = status switch
        {
            HttpStatusCode.OK => "OK",
            HttpStatusCode.Found => "Found",
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
        var headers = new StringBuilder()
            .Append("HTTP/1.1 ")
            .Append(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append(' ')
            .Append(reason)
            .Append("\r\nContent-Type: application/octet-stream\r\n")
            .Append("Content-Length: ")
            .Append(body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append("\r\nConnection: close\r\n");
        if (location is not null)
        {
            headers.Append("Location: ").Append(location).Append("\r\n");
        }
        headers.Append("\r\n");
        return [.. Encoding.ASCII.GetBytes(headers.ToString()), .. body];
    }

    private static byte[] BuildChunkedResponse(
        HttpStatusCode status,
        byte[] body)
    {
        if (status != HttpStatusCode.OK)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        using var output = new MemoryStream();
        output.Write(Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: application/rss+xml; charset=utf-8\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "Connection: close\r\n\r\n"));
        for (var offset = 0; offset < body.Length;)
        {
            var count = Math.Min(37, body.Length - offset);
            output.Write(Encoding.ASCII.GetBytes(
                count.ToString("X", System.Globalization.CultureInfo.InvariantCulture) +
                "\r\n"));
            output.Write(body, offset, count);
            output.Write("\r\n"u8);
            offset += count;
        }
        output.Write("0\r\n\r\n"u8);
        return output.ToArray();
    }

    private sealed class OneShotLoopbackServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stopping = new();
        private readonly Task<string> _request;

        public OneShotLoopbackServer(byte[] response)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start(1);
            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            Origin = new Uri($"http://127.0.0.1:{endpoint.Port}/");
            _request = ServeAsync(response, _stopping.Token);
        }

        public Uri Origin { get; }

        public int AcceptedConnections { get; private set; }

        public Task<string> RequestHeaders => _request;

        public async ValueTask DisposeAsync()
        {
            await _stopping.CancelAsync();
            _listener.Stop();
            try
            {
                await _request.ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is OperationCanceledException
                    or ObjectDisposedException
                    or SocketException)
            {
            }
            _stopping.Dispose();
        }

        private async Task<string> ServeAsync(
            byte[] response,
            CancellationToken cancellationToken)
        {
            using var client = await _listener
                .AcceptTcpClientAsync(cancellationToken)
                .ConfigureAwait(false);
            AcceptedConnections++;
            await using var stream = client.GetStream();
            var request = await ReadHeadersAsync(stream, cancellationToken)
                .ConfigureAwait(false);
            await stream.WriteAsync(response, cancellationToken)
                .ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            return request;
        }

        private static async Task<string> ReadHeadersAsync(
            Stream stream,
            CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            var single = new byte[1];
            while (buffer.Length < 16 * 1024)
            {
                var read = await stream.ReadAsync(single, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
                buffer.WriteByte(single[0]);
                if (buffer.Length >= 4)
                {
                    var value = buffer.GetBuffer();
                    var end = (int)buffer.Length;
                    if (value[end - 4] == '\r'
                        && value[end - 3] == '\n'
                        && value[end - 2] == '\r'
                        && value[end - 1] == '\n')
                    {
                        return Encoding.ASCII.GetString(
                            value,
                            0,
                            end);
                    }
                }
            }

            throw new InvalidDataException(
                "Loopback fixture request headers were incomplete or too large.");
        }
    }
}
