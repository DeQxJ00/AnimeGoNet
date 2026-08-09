using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.Core.Tests.Configuration;

public sealed class OutboundProxyPolicyTests
{
    private static readonly OutboundProxyOptions Options = new()
    {
        Url = new Uri("http://127.0.0.1:7890/"),
        HostPatterns = ["api.example.com", "*.media.example.com"],
    };

    [Theory]
    [InlineData("https://api.example.com/v1", true)]
    [InlineData("https://API.EXAMPLE.COM/v1", true)]
    [InlineData("https://cdn.media.example.com/poster", true)]
    [InlineData("https://deep.cdn.media.example.com/poster", true)]
    [InlineData("https://media.example.com/poster", false)]
    [InlineData("https://notexample.com/", false)]
    public void SelectsOnlyConfiguredHosts(string url, bool expected)
    {
        Assert.Equal(expected, OutboundProxyPolicy.ShouldProxy(new Uri(url), Options));
    }
}
