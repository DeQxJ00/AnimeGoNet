using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Cache;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Metadata;

public sealed class TmdbCachingClientTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DefaultApplicationCompositionUsesPersistentCachingClient()
    {
        await using var app = await RunningApp.StartAsync();
        Assert.IsType<TmdbCachingClient>(
            app.App.Services.GetRequiredService<ITmdbClient>());
    }

    [Fact]
    public async Task SuccessfulResponsesAreReusedAcrossClientInstances()
    {
        await using var fixture = await CacheFixture.CreateAsync();
        var first = new FakeTmdbClient();
        using (var client = fixture.Create(first))
        {
            Assert.Single(await client.SearchSeriesAsync("Example"));
            Assert.NotNull(await client.GetSeriesDetailsAsync(42));
            Assert.NotNull(await client.GetSeasonAsync(42, 2));
            Assert.NotNull(await client.GetEpisodeAsync(42, 2, 3));
        }

        var second = new FakeTmdbClient { ThrowOnEveryCall = true };
        using var restarted = fixture.Create(second);
        Assert.Single(await restarted.SearchSeriesAsync("Example"));
        Assert.Equal(42, (await restarted.GetSeriesAsync(42))?.Id);
        Assert.NotNull(await restarted.GetSeriesDetailsAsync(42));
        Assert.NotNull(await restarted.GetSeasonAsync(42, 2));
        Assert.NotNull(await restarted.GetEpisodeAsync(42, 2, 3));
        Assert.Equal(0, second.CallCount);

        Assert.Equal(4, (await fixture.Store.ListKeysAsync(
            TmdbCachingClient.DatabaseName,
            TmdbCachingClient.BucketName,
            Now)).Count);
    }

    [Fact]
    public async Task ExpiredEntryFetchesAndReplacesAuthoritativeValue()
    {
        await using var fixture = await CacheFixture.CreateAsync(cacheTtl: TimeSpan.FromHours(2));
        var first = new FakeTmdbClient
        {
            SearchResult = [new TmdbSeries(42, "Old", "Old", null)],
        };
        Assert.Equal("Old", Assert.Single(await fixture.Create(first)
            .SearchSeriesAsync("Example")).Name);

        fixture.Clock.UtcNow = Now.AddHours(2);
        var second = new FakeTmdbClient
        {
            SearchResult = [new TmdbSeries(43, "New", "New", null)],
        };
        Assert.Equal("New", Assert.Single(await fixture.Create(second)
            .SearchSeriesAsync("Example")).Name);
        Assert.Equal(1, second.SearchCalls);
    }

    [Fact]
    public async Task EmptySearchIsCachedButNotFoundEntitiesAreNotNegativeCached()
    {
        await using var fixture = await CacheFixture.CreateAsync();
        var first = new FakeTmdbClient
        {
            SearchResult = [],
            ReturnNotFound = true,
        };
        var client = fixture.Create(first);
        Assert.Empty(await client.SearchSeriesAsync("Missing"));
        Assert.Null(await client.GetSeriesDetailsAsync(99));
        Assert.Null(await client.GetSeasonAsync(99, 1));
        Assert.Null(await client.GetEpisodeAsync(99, 1, 1));

        var second = new FakeTmdbClient
        {
            SearchResult = [new TmdbSeries(99, "Unexpected", "Unexpected", null)],
            Series = new TmdbSeries(99, "Now available", "Now available", null),
        };
        var restarted = fixture.Create(second);
        Assert.Empty(await restarted.SearchSeriesAsync("Missing"));
        Assert.NotNull(await restarted.GetSeriesDetailsAsync(99));
        Assert.NotNull(await restarted.GetSeasonAsync(99, 1));
        Assert.NotNull(await restarted.GetEpisodeAsync(99, 1, 1));
        Assert.Equal(0, second.SearchCalls);
        Assert.Equal(3, second.CallCount);
    }

    [Fact]
    public async Task UndatedSeasonEpisodesAndEpisodeResponsesAreNotCached()
    {
        await using var fixture = await CacheFixture.CreateAsync();
        var first = new FakeTmdbClient { EpisodeAirDate = null };
        Assert.NotNull(await fixture.Create(first).GetSeasonAsync(42, 2));
        Assert.NotNull(await fixture.Create(first).GetEpisodeAsync(42, 2, 3));
        Assert.Equal(1, first.SeasonCalls);
        Assert.Equal(1, first.EpisodeCalls);
        Assert.Empty(await fixture.Store.ListKeysAsync(
            TmdbCachingClient.DatabaseName,
            TmdbCachingClient.BucketName,
            Now));

        var second = new FakeTmdbClient { EpisodeAirDate = null };
        Assert.NotNull(await fixture.Create(second).GetSeasonAsync(42, 2));
        Assert.NotNull(await fixture.Create(second).GetEpisodeAsync(42, 2, 3));
        Assert.Equal(1, second.SeasonCalls);
        Assert.Equal(1, second.EpisodeCalls);
    }

    [Fact]
    public async Task LegacyUndatedEpisodeCacheIsDeletedAndRefetched()
    {
        await using var fixture = await CacheFixture.CreateAsync();
        Assert.NotNull(await fixture.Create(new FakeTmdbClient()).GetEpisodeAsync(42, 2, 3));
        var key = Assert.Single(await fixture.Store.ListKeysAsync(
            TmdbCachingClient.DatabaseName,
            TmdbCachingClient.BucketName,
            Now));
        await fixture.Store.PutJsonAsync(
            TmdbCachingClient.DatabaseName,
            TmdbCachingClient.BucketName,
            key,
            """{"id":420203,"seriesId":42,"seasonNumber":2,"episodeNumber":3,"name":"Episode 3","airDate":null}""",
            TimeSpan.FromDays(14),
            Now);

        var inner = new FakeTmdbClient();
        var episode = Assert.IsType<TmdbEpisode>(
            await fixture.Create(inner).GetEpisodeAsync(42, 2, 3));

        Assert.Equal(new DateOnly(2026, 1, 3), episode.AirDate);
        Assert.Equal(1, inner.EpisodeCalls);
    }

    [Fact]
    public async Task LegacySeasonCacheContainingUndatedEpisodeIsDeletedAndRefetched()
    {
        await using var fixture = await CacheFixture.CreateAsync();
        Assert.NotNull(await fixture.Create(new FakeTmdbClient()).GetSeasonAsync(42, 2));
        var key = Assert.Single(await fixture.Store.ListKeysAsync(
            TmdbCachingClient.DatabaseName,
            TmdbCachingClient.BucketName,
            Now));
        await fixture.Store.PutJsonAsync(
            TmdbCachingClient.DatabaseName,
            TmdbCachingClient.BucketName,
            key,
            """
            {"id":4202,"seriesId":42,"seasonNumber":2,"name":"Season 2","airDate":"2026-01-01","episodeCount":12,"posterPath":null,"episodes":[{"id":420203,"seriesId":42,"seasonNumber":2,"episodeNumber":3,"name":"Episode 3","airDate":null}]}
            """,
            TimeSpan.FromDays(14),
            Now);

        var inner = new FakeTmdbClient();
        var season = Assert.IsType<TmdbSeason>(
            await fixture.Create(inner).GetSeasonAsync(42, 2));

        Assert.Equal(new DateOnly(2026, 1, 3), Assert.Single(season.Episodes!).AirDate);
        Assert.Equal(1, inner.SeasonCalls);
    }

    [Fact]
    public async Task FailuresAreNeverCached()
    {
        await using var fixture = await CacheFixture.CreateAsync();
        var failing = new FakeTmdbClient { ThrowOnEveryCall = true };
        await Assert.ThrowsAsync<TmdbClientException>(
            () => fixture.Create(failing).SearchSeriesAsync("Retry me"));

        var recovered = new FakeTmdbClient();
        Assert.Single(await fixture.Create(recovered).SearchSeriesAsync("Retry me"));
        Assert.Equal(1, recovered.SearchCalls);
    }

    [Fact]
    public async Task CacheIsPartitionedByBaseUrlAndLanguageWithoutPersistingCredentialsOrQuery()
    {
        await using var fixture = await CacheFixture.CreateAsync();
        var optionsA = fixture.Options with
        {
            ApiKey = "credential-that-must-not-be-cached",
            BaseUrl = new Uri("https://tmdb-a.invalid/root/"),
            Language = "zh-CN",
        };
        var first = fixture.Create(new FakeTmdbClient(), optionsA);
        Assert.Single(await first.SearchSeriesAsync("private search phrase"));

        var optionsB = optionsA with { Language = "ja-JP" };
        var secondInner = new FakeTmdbClient();
        Assert.Single(await fixture.Create(secondInner, optionsB)
            .SearchSeriesAsync("private search phrase"));
        Assert.Equal(1, secondInner.SearchCalls);

        var keys = await fixture.Store.ListKeysAsync(
            TmdbCachingClient.DatabaseName,
            TmdbCachingClient.BucketName,
            Now);
        Assert.Equal(2, keys.Count);
        Assert.All(keys, key =>
        {
            Assert.DoesNotContain("private", key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("credential", key, StringComparison.OrdinalIgnoreCase);
        });
        foreach (var key in keys)
        {
            var value = await fixture.Store.GetJsonAsync(
                TmdbCachingClient.DatabaseName,
                TmdbCachingClient.BucketName,
                key,
                Now);
            Assert.NotNull(value);
            Assert.DoesNotContain("credential-that-must-not-be-cached", value.ValueJson, StringComparison.Ordinal);
            Assert.DoesNotContain("private search phrase", value.ValueJson, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task CorruptOrIdentityMismatchedEntryIsDeletedAndRefetched()
    {
        await using var fixture = await CacheFixture.CreateAsync();
        Assert.NotNull(await fixture.Create(new FakeTmdbClient()).GetEpisodeAsync(42, 2, 3));
        var key = Assert.Single(await fixture.Store.ListKeysAsync(
            TmdbCachingClient.DatabaseName,
            TmdbCachingClient.BucketName,
            Now));
        await fixture.Store.PutJsonAsync(
            TmdbCachingClient.DatabaseName,
            TmdbCachingClient.BucketName,
            key,
            """{"id":123,"seriesId":999,"seasonNumber":2,"episodeNumber":3,"name":"forged","airDate":null}""",
            TimeSpan.FromDays(14),
            Now);

        var inner = new FakeTmdbClient();
        var episode = Assert.IsType<TmdbEpisode>(
            await fixture.Create(inner).GetEpisodeAsync(42, 2, 3));
        Assert.Equal(420203, episode.Id);
        Assert.Equal(1, inner.EpisodeCalls);
    }

    [Fact]
    public async Task CallerCancellationPropagatesDuringCacheAccess()
    {
        await using var fixture = await CacheFixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Create(new FakeTmdbClient())
                .SearchSeriesAsync("Example", cancellation.Token));
    }

    private sealed class CacheFixture : IAsyncDisposable
    {
        private readonly string _root;

        private CacheFixture(
            string root,
            AnimeGoSqliteDatabase database,
            SqliteJsonCacheStore store,
            MutableTimeProvider clock,
            TmdbClientOptions options)
        {
            _root = root;
            Database = database;
            Store = store;
            Clock = clock;
            Options = options;
        }

        public AnimeGoSqliteDatabase Database { get; }

        public SqliteJsonCacheStore Store { get; }

        public MutableTimeProvider Clock { get; }

        public TmdbClientOptions Options { get; }

        public static async Task<CacheFixture> CreateAsync(TimeSpan? cacheTtl = null)
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "animegonet-tmdb-cache-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var database = new AnimeGoSqliteDatabase(Path.Combine(root, "animegonet.db"));
            await database.InitializeAsync();
            return new CacheFixture(
                root,
                database,
                new SqliteJsonCacheStore(database),
                new MutableTimeProvider(Now),
                new TmdbClientOptions
                {
                    ApiKey = "test",
                    CacheTtl = cacheTtl ?? TimeSpan.FromDays(14),
                });
        }

        public TmdbCachingClient Create(
            ITmdbClient inner,
            TmdbClientOptions? options = null) =>
            new(inner, Store, options ?? Options, Clock);

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class FakeTmdbClient : ITmdbClient
    {
        public TmdbSeries Series { get; init; } =
            new(42, "Example", "Example JP", new DateOnly(2026, 1, 1));

        public IReadOnlyList<TmdbSeries>? SearchResult { get; init; }

        public bool ThrowOnEveryCall { get; init; }

        public bool ReturnNotFound { get; init; }

        public DateOnly? EpisodeAirDate { get; init; } = new(2026, 1, 3);

        public int SearchCalls { get; private set; }

        public int DetailsCalls { get; private set; }

        public int SeasonCalls { get; private set; }

        public int EpisodeCalls { get; private set; }

        public int CallCount => SearchCalls + DetailsCalls + SeasonCalls + EpisodeCalls;

        public Task<IReadOnlyList<TmdbSeries>> SearchSeriesAsync(
            string title,
            CancellationToken cancellationToken = default)
        {
            _ = title;
            cancellationToken.ThrowIfCancellationRequested();
            SearchCalls++;
            ThrowIfRequested();
            return Task.FromResult(SearchResult ?? (IReadOnlyList<TmdbSeries>)[Series]);
        }

        public async Task<TmdbSeries?> GetSeriesAsync(
            int seriesId,
            CancellationToken cancellationToken = default) =>
            (await GetSeriesDetailsAsync(seriesId, cancellationToken))?.Series;

        public Task<TmdbSeriesDetails?> GetSeriesDetailsAsync(
            int seriesId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DetailsCalls++;
            ThrowIfRequested();
            return Task.FromResult<TmdbSeriesDetails?>(ReturnNotFound
                ? null
                : new TmdbSeriesDetails(
                    Series with { Id = seriesId },
                    [Season(seriesId, 1), Season(seriesId, 2)]));
        }

        public Task<TmdbSeason?> GetSeasonAsync(
            int seriesId,
            int seasonNumber,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SeasonCalls++;
            ThrowIfRequested();
            return Task.FromResult<TmdbSeason?>(
                ReturnNotFound ? null : Season(seriesId, seasonNumber));
        }

        public Task<TmdbEpisode?> GetEpisodeAsync(
            int seriesId,
            int seasonNumber,
            int episodeNumber,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EpisodeCalls++;
            ThrowIfRequested();
            return Task.FromResult<TmdbEpisode?>(ReturnNotFound
                ? null
                : Episode(seriesId, seasonNumber, episodeNumber));
        }

        private void ThrowIfRequested()
        {
            if (ThrowOnEveryCall)
            {
                throw new TmdbClientException(
                    MetadataFailureKind.Network,
                    "fake_network_failure",
                    tmdbAccessConfirmed: false);
            }
        }

        private TmdbSeason Season(int seriesId, int seasonNumber) =>
            new(
                (seriesId * 100) + seasonNumber,
                seriesId,
                seasonNumber,
                $"Season {seasonNumber}",
                new DateOnly(2026, 1, 1),
                12,
                Episodes: [Episode(seriesId, seasonNumber, 3)]);

        private TmdbEpisode Episode(int seriesId, int seasonNumber, int episodeNumber) =>
            new(
                (seriesId * 10_000) + (seasonNumber * 100) + episodeNumber,
                seriesId,
                seasonNumber,
                episodeNumber,
                $"Episode {episodeNumber}",
                EpisodeAirDate is null
                    ? null
                    : new DateOnly(
                        EpisodeAirDate.Value.Year,
                        EpisodeAirDate.Value.Month,
                        episodeNumber));
    }
}
