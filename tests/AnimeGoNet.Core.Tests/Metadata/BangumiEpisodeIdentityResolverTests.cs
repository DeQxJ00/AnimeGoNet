using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.Core.Tests.Metadata;

public sealed class BangumiEpisodeIdentityResolverTests
{
    [Fact]
    public void UniqueOrdinaryEpisodeReturnsBangumiEpisodeId()
    {
        BangumiEpisode[] episodes =
        [
            new(1001, 0, 7, null),
            new(2001, 1, 7, null),
        ];

        Assert.Equal(1001, BangumiEpisodeIdentityResolver.Resolve(episodes, "07"));
    }

    [Theory]
    [InlineData("48.5")]
    [InlineData("SP")]
    [InlineData("0")]
    public void FractionalSpecialOrNonPositiveInputIsNotCanonical(string sourceEpisode)
    {
        Assert.Null(BangumiEpisodeIdentityResolver.Resolve(
            [new BangumiEpisode(1001, 0, 48.5m, null)],
            sourceEpisode));
    }

    [Fact]
    public void AmbiguousOrdinaryEpisodeDoesNotClaimCrossSourceIdentity()
    {
        BangumiEpisode[] episodes =
        [
            new(1001, 0, 7, null),
            new(1002, 0, 7, null),
        ];

        Assert.Null(BangumiEpisodeIdentityResolver.Resolve(episodes, "7"));
    }
}
