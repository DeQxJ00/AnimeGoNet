using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.Core.Tests.Metadata;

public sealed class TmdbAuthorityTests
{
    [Fact]
    public async Task InvalidIdentityStopsBeforeAnyAuthorityCall()
    {
        var client = new FakeClient();

        var result = await new TmdbAuthority(client).ValidateEpisodeAsync(1, 0, 1);

        Assert.False(result.IsSuccess);
        Assert.Equal(MetadataFailureKind.InvalidInput, result.Failure!.Kind);
        Assert.False(result.Failure.TmdbAccessConfirmed);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task TransportClassificationIsPreservedWithoutRawReason()
    {
        var client = new FakeClient
        {
            Failure = new TmdbClientException(
                MetadataFailureKind.Network,
                "tmdb_network_error",
                tmdbAccessConfirmed: false),
        };

        var result = await new TmdbAuthority(client).ValidateEpisodeAsync(1, 1, 1);

        Assert.Equal(MetadataFailureKind.Network, result.Failure!.Kind);
        Assert.Equal("tmdb_network_error", result.Failure.Code);
        Assert.False(result.Failure.TmdbAccessConfirmed);
        Assert.DoesNotContain("private", result.Failure.Code, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CandidateCannotChangeRequestedSeriesSeasonOrEpisode()
    {
        var client = new FakeClient
        {
            EpisodeOverride = new TmdbEpisode(9, 2, 1, 1, "Wrong series", null),
        };

        var result = await new TmdbAuthority(client).ValidateEpisodeAsync(1, 1, 1);

        Assert.False(result.IsSuccess);
        Assert.Equal(MetadataFailureKind.Protocol, result.Failure!.Kind);
        Assert.Equal("tmdb_episode_identity_mismatch", result.Failure.Code);
        Assert.False(result.Failure.TmdbAccessConfirmed);
    }

    private sealed class FakeClient : ITmdbClient
    {
        public int CallCount { get; private set; }

        public TmdbClientException? Failure { get; init; }

        public TmdbEpisode? EpisodeOverride { get; init; }

        public Task<IReadOnlyList<TmdbSeries>> SearchSeriesAsync(
            string title,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TmdbSeries>>([]);

        public Task<TmdbSeries?> GetSeriesAsync(int seriesId, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult<TmdbSeries?>(new TmdbSeries(seriesId, "Series", "Original", null));
        }

        public Task<TmdbSeason?> GetSeasonAsync(
            int seriesId,
            int seasonNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeason?>(new TmdbSeason(1, seriesId, seasonNumber, "Season", null, 1));

        public Task<TmdbEpisode?> GetEpisodeAsync(
            int seriesId,
            int seasonNumber,
            int episodeNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbEpisode?>(EpisodeOverride
                ?? new TmdbEpisode(1, seriesId, seasonNumber, episodeNumber, "Episode", null));
    }
}
