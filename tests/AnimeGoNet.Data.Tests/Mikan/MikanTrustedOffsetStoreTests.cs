using AnimeGoNet.Data.Mikan;

namespace AnimeGoNet.Data.Tests.Mikan;

public sealed class MikanTrustedOffsetStoreTests
{
    [Fact]
    public async Task RequiresThreeDifferentEpisodesWithTheSameVerifiedOffset()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new MikanTrustedOffsetStore(fixture.Database);
        var now = DateTimeOffset.UtcNow;

        Assert.Null(await store.ObserveAsync(Observation(1, 13), now));
        Assert.Null(await store.ObserveAsync(Observation(2, 13), now.AddMinutes(1)));
        var trusted = Assert.IsType<MikanTrustedOffset>(
            await store.ObserveAsync(Observation(3, 13), now.AddMinutes(2)));

        Assert.True(trusted.IsTrusted);
        Assert.Equal(3, trusted.DistinctEpisodeCount);
        Assert.Equal(72517, trusted.TmdbSeriesId);
        Assert.Equal(2, trusted.TmdbSeasonNumber);
        Assert.Equal(13, trusted.EpisodeOffset);
        var resolved = Assert.IsType<MikanTrustedEpisodeResolution>(
            await store.TryResolveEpisodeAsync(3951, 7, 4, enabled: true));
        Assert.Equal(17, resolved.TmdbEpisodeNumber);
    }

    [Fact]
    public async Task ReobservingTheSameEpisodeDoesNotIncreaseEvidenceCount()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new MikanTrustedOffsetStore(fixture.Database);

        await store.ObserveAsync(Observation(1, 13), DateTimeOffset.UtcNow);
        await store.ObserveAsync(Observation(1, 13), DateTimeOffset.UtcNow.AddMinutes(1));
        await store.ObserveAsync(Observation(2, 13), DateTimeOffset.UtcNow.AddMinutes(2));

        Assert.Null(await store.GetTrustedAsync(3951, 7));
    }

    [Fact]
    public async Task ConflictingCorrectionRevokesAnExistingTrustedOffset()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new MikanTrustedOffsetStore(fixture.Database);
        for (var episode = 1; episode <= 3; episode++)
        {
            await store.ObserveAsync(Observation(episode, 13), DateTimeOffset.UtcNow.AddMinutes(episode));
        }

        var revoked = Assert.IsType<MikanTrustedOffset>(await store.ObserveAsync(
            Observation(3, 12),
            DateTimeOffset.UtcNow.AddMinutes(4)));

        Assert.False(revoked.IsTrusted);
        Assert.Null(await store.GetTrustedAsync(3951, 7));
        Assert.Null(await store.TryResolveEpisodeAsync(3951, 7, 4, enabled: true));
    }

    [Fact]
    public async Task NewEpisodeWithConflictingSignatureImmediatelyRevokesAndRestartsLearning()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new MikanTrustedOffsetStore(fixture.Database);
        for (var episode = 1; episode <= 3; episode++)
        {
            await store.ObserveAsync(Observation(episode, 13), DateTimeOffset.UtcNow.AddMinutes(episode));
        }

        var revoked = Assert.IsType<MikanTrustedOffset>(await store.ObserveAsync(
            Observation(4, 12),
            DateTimeOffset.UtcNow.AddMinutes(4)));

        Assert.False(revoked.IsTrusted);
        Assert.Null(await store.GetTrustedAsync(3951, 7));
        var state = Assert.Single(await store.ListAsync(3951, 7));
        Assert.Equal("conflict_reset", state.State);
        Assert.Equal(1, state.DistinctEpisodeCount);
        Assert.Equal(12, state.EpisodeOffset);
    }

    [Fact]
    public async Task DisabledCacheAndUnsafeEpisodeResultsAlwaysFallBack()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new MikanTrustedOffsetStore(fixture.Database);
        for (var episode = 10; episode <= 12; episode++)
        {
            await store.ObserveAsync(Observation(episode, -10), DateTimeOffset.UtcNow.AddMinutes(episode));
        }

        Assert.Null(await store.TryResolveEpisodeAsync(3951, 7, 11, enabled: false));
        Assert.Null(await store.TryResolveEpisodeAsync(3951, 7, null, enabled: true));
        Assert.Null(await store.TryResolveEpisodeAsync(3951, 7, 10, enabled: true));
        var positive = Assert.IsType<MikanTrustedEpisodeResolution>(
            await store.TryResolveEpisodeAsync(3951, 7, 11, enabled: true));
        Assert.Equal(1, positive.TmdbEpisodeNumber);
    }

    [Fact]
    public async Task ThreeNewConsistentEpisodesAfterConflictCanBecomeTrustedAgain()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new MikanTrustedOffsetStore(fixture.Database);
        for (var episode = 1; episode <= 3; episode++)
        {
            await store.ObserveAsync(Observation(episode, 13), DateTimeOffset.UtcNow.AddMinutes(episode));
        }

        for (var episode = 4; episode <= 6; episode++)
        {
            await store.ObserveAsync(Observation(episode, 12), DateTimeOffset.UtcNow.AddMinutes(episode));
        }

        var trusted = Assert.IsType<MikanTrustedOffset>(await store.GetTrustedAsync(3951, 7));
        Assert.True(trusted.IsTrusted);
        Assert.Equal(12, trusted.EpisodeOffset);
        var resolved = Assert.IsType<MikanTrustedEpisodeResolution>(
            await store.TryResolveEpisodeAsync(3951, 7, 7, enabled: true));
        Assert.Equal(19, resolved.TmdbEpisodeNumber);
    }

    private static MikanOffsetEvidenceObservation Observation(int sourceEpisode, int offset) =>
        new(3951, 7, sourceEpisode, 72517, 2, offset);
}
