using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Tests.Metadata;

public sealed class MetadataHttpClientFactoryTests
{
    [Fact]
    public void MissingProxyDisablesAmbientSystemProxy()
    {
        using var handler = MetadataHttpClientFactory.CreateHandler(new OutboundProxyOptions());

        Assert.False(handler.UseProxy);
    }

    [Theory]
    [InlineData("http://127.0.0.1:7890/")]
    [InlineData("socks5://127.0.0.1:1080/")]
    public void SelectiveProxyIsAppliedOnlyToMatchingMetadataHost(string proxyUrl)
    {
        var expected = new Uri(proxyUrl);
        using var handler = MetadataHttpClientFactory.CreateHandler(new OutboundProxyOptions
        {
            Url = expected,
            HostPatterns = ["metadata.invalid"],
        });

        Assert.True(handler.UseProxy);
        Assert.NotNull(handler.Proxy);
        Assert.Equal(expected, handler.Proxy.GetProxy(new Uri("https://metadata.invalid/")));
        Assert.True(handler.Proxy.IsBypassed(new Uri("https://direct.invalid/")));
    }
}
