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
    public void BangumiEpisodeIdHasHighestCrossSourcePriority()
    {
        var mikan = FallbackDedupScopeResolver.Resolve(
            "mikan", 3951, "work", "item", new string('a', 40),
            "Show/EP07.mkv", 100, "7", bangumiEpisodeId: 1001);
        var u2 = FallbackDedupScopeResolver.Resolve(
            "u2", null, "different-work", "other-item", new string('b', 40),
            "Other/07.mkv", 120, "7", bangumiEpisodeId: 1001);

        Assert.Equal(new FallbackDedupScope("bangumi_episode", "1001"), mikan);
        Assert.Equal(mikan, u2);
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
