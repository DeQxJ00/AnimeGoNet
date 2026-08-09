using AnimeGoNet.App.Configuration;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Tests.Configuration;

public sealed class MikanEndpointRewriterTests
{
    [Fact]
    public void ReplacesOnlyOriginAndPreservesCredentialBearingPathAndQuery()
    {
        var source = new Uri(
            "https://mikanime.tv/Download/secret-value/file.torrent?token=private-value");

        var rewritten = MikanEndpointRewriter.Rewrite(
            source,
            new MikanClientOptions { BaseUrl = new Uri("http://mikan.local/") });

        Assert.Equal(
            "http://mikan.local/Download/secret-value/file.torrent?token=private-value",
            rewritten.AbsoluteUri);
    }

    [Theory]
    [InlineData("ftp://mikanime.tv/file.torrent")]
    [InlineData("https://user:password@mikanime.tv/file.torrent")]
    [InlineData("https://mikanime.tv/file.torrent#fragment")]
    public void RejectsUnsupportedSourceUrl(string value)
    {
        Assert.Throws<ArgumentException>(() => MikanEndpointRewriter.Rewrite(
            new Uri(value),
            new MikanClientOptions { BaseUrl = new Uri("http://mikan.local/") }));
    }
}
