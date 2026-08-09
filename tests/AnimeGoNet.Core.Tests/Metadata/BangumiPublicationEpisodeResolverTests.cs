using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.Core.Tests.Metadata;

public sealed class BangumiPublicationEpisodeResolverTests
{
    [Fact]
    public void SelectsClosestNormalIntegerEpisodeUsingSourceLocalDate()
    {
        BangumiEpisode[] episodes =
        [
            new(1, 0, 6, new DateOnly(2026, 7, 15)),
            new(2, 0, 7, new DateOnly(2026, 7, 22)),
            new(3, 0, 8, new DateOnly(2026, 7, 29)),
        ];

        var result = BangumiPublicationEpisodeResolver.SelectClosest(
            episodes,
            new DateTimeOffset(2026, 7, 22, 23, 30, 0, TimeSpan.FromHours(8)));

        Assert.Equal(7, result);
    }

    [Fact]
    public void FutureEpisodeIsExcludedEvenWhenEquallyClose()
    {
        BangumiEpisode[] episodes =
        [
            new(1, 0, 9, new DateOnly(2026, 7, 23)),
            new(2, 0, 8, new DateOnly(2026, 7, 21)),
        ];

        Assert.Equal(
            8,
            BangumiPublicationEpisodeResolver.SelectClosest(
                episodes,
                new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.FromHours(8))));
    }

    [Fact]
    public void FutureEpisodeIsNeverSelected()
    {
        Assert.Null(BangumiPublicationEpisodeResolver.SelectClosest(
            [new BangumiEpisode(1, 0, 4, new DateOnly(2026, 7, 27))],
            new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.FromHours(8))));
    }

    [Fact]
    public void ExcludesSpecialFractionalAndMissingButHasNoTorrentDelayWindow()
    {
        BangumiEpisode[] episodes =
        [
            new(1, 1, 7, new DateOnly(2026, 7, 22)),
            new(2, 0, 7.5m, new DateOnly(2026, 7, 22)),
            new(3, 0, 8, null),
            new(4, 0, 9, new DateOnly(2026, 5, 1)),
        ];

        Assert.Equal(
            9,
            BangumiPublicationEpisodeResolver.SelectClosest(
                episodes,
                new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.FromHours(8))));
    }

    [Fact]
    public void SameDateUsesStableEpisodeThenIdentityOrder()
    {
        BangumiEpisode[] episodes =
        [
            new(20, 0, 8, new DateOnly(2026, 7, 22)),
            new(30, 0, 7, new DateOnly(2026, 7, 22)),
            new(10, 0, 7, new DateOnly(2026, 7, 22)),
        ];

        Assert.Equal(
            7,
            BangumiPublicationEpisodeResolver.SelectClosest(
                episodes,
                new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.FromHours(8))));
    }
}
