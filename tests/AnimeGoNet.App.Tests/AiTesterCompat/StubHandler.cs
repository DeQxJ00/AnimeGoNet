using System.Net;

namespace AnimeGoNet.App.Tests.AiTesterCompat;

internal sealed class StubHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content)
        };

        return Task.FromResult(response);
    }
}
