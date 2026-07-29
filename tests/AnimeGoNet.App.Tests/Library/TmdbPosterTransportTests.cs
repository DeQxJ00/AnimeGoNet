using System.Net;
using AnimeGoNet.App.Library;

namespace AnimeGoNet.App.Tests.Library;

public sealed class TmdbPosterTransportTests
{
    [Fact]
    public async Task DownloadsImageWithBoundedStreamingAndNoAuthentication()
    {
        HttpRequestMessage? captured = null;
        var expected = new byte[] { 0xff, 0xd8, 0xff, 0xe0 };
        using var http = new HttpClient(new StubHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(expected),
            };
        }));
        using var transport = new HttpTmdbPosterTransport(http);

        var content = await transport.DownloadAsync(
            new Uri("https://image.tmdb.org/t/p/w500/poster.jpg"),
            1024,
            TimeSpan.FromSeconds(1));

        Assert.Equal(expected, content);
        Assert.NotNull(captured);
        Assert.Null(captured.Headers.Authorization);
        Assert.DoesNotContain(captured.Headers, header =>
            header.Key.Contains("api", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("AnimeGoNet/1.0", captured.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task RejectsDeclaredOrStreamingContentBeyondLimit()
    {
        using var declaredHttp = new HttpClient(new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[16]),
            };
            response.Content.Headers.ContentLength = 16;
            return response;
        }));
        using var declared = new HttpTmdbPosterTransport(declaredHttp);
        await Assert.ThrowsAsync<InvalidDataException>(() => declared.DownloadAsync(
            new Uri("https://image.tmdb.org/t/p/w500/poster.jpg"),
            8,
            TimeSpan.FromSeconds(1)));

        using var streamingHttp = new HttpClient(new StubHandler(_ =>
        {
            var content = new StreamContent(new MemoryStream(new byte[16]));
            content.Headers.ContentLength = null;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }));
        using var streaming = new HttpTmdbPosterTransport(streamingHttp);
        await Assert.ThrowsAsync<InvalidDataException>(() => streaming.DownloadAsync(
            new Uri("https://image.tmdb.org/t/p/w500/poster.jpg"),
            8,
            TimeSpan.FromSeconds(1)));
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(responseFactory(request));
        }
    }
}
