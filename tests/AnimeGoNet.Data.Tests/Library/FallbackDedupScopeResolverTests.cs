using AnimeGoNet.Data.Library;

namespace AnimeGoNet.Data.Tests.Library;

public sealed class FallbackDedupScopeResolverTests
{
    [Fact]
    public void MikanEpisodeNormalizesCaseWhitespaceAndNumericFormatting()
    {
        var first = Resolve(sourceEpisode: " 01.50 ");
        var second = Resolve(sourceEpisode: "1.5");

        Assert.Equal(new FallbackDedupScope("mikan_episode", "3951:source:1.5"), first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void MissingEpisodeUsesStablePerTorrentFileFingerprint()
    {
        var first = Resolve(sourceEpisode: null);
        var second = Resolve(sourceEpisode: " ");
        var otherFile = FallbackDedupScopeResolver.Resolve(
            "mikan", 3951, "work", "item", new string('a', 40),
            "Show/EP01.ass", 20, null);

        Assert.Equal("torrent_file", first.Kind);
        Assert.Equal(first, second);
        Assert.NotEqual(first, otherFile);
        Assert.Equal(64, first.Key.Length);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void NonPositiveNumericEpisodeFallsBackToPerFileIdentity(string sourceEpisode)
    {
        Assert.Equal("torrent_file", Resolve(sourceEpisode).Kind);
    }

    private static FallbackDedupScope Resolve(string? sourceEpisode) =>
        FallbackDedupScopeResolver.Resolve(
            "Mikan",
            3951,
            "work",
            "item",
            new string('a', 40),
            "Show/EP01.mkv",
            100,
            sourceEpisode);
}
