using System.IO.Compression;
using System.Net;
using System.Text;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Sqlite;

namespace AnimeGoNet.App.Tests.Metadata;

public sealed class U2AniDbMetadataResolverTests
{
    [Fact]
    public async Task MissingAniDbUsesAnitomyAnimeTitleForTmdbSearch()
    {
        await using var fixture = await ResolverFixture.CreateAsync("{}");

        var result = await fixture.Resolver.ResolveTaskTitleAsync(
            "[Group] Toradora! - 07v2 [1080p].mkv");

        Assert.True(result.IsSuccess);
        Assert.Equal("u2_anitomy_title", result.Strategy);
        Assert.Equal("Toradora!", Assert.Single(fixture.Tmdb.SearchTitles));
    }

    [Fact]
    public async Task EnabledMappingUsesTmdbIdDirectlyAndMappedSeasonWithoutTitleSearch()
    {
        await using var fixture = await ResolverFixture.CreateAsync(
            """{"tmdbtv":"100","tmdbseason":"2"}""",
            seasons: [Season(1), Season(2)]);

        var result = await fixture.Resolver.ResolveAsync(99, "ignored release title", useTmdbMapping: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(100, result.Details!.Series.Id);
        Assert.Equal(2, result.Season!.SeasonNumber);
        Assert.Empty(fixture.Tmdb.SearchTitles);
    }

    [Fact]
    public async Task DisabledMappingUsesOfficialCachedTitleButStillUsesMappedSeason()
    {
        await using var fixture = await ResolverFixture.CreateAsync(
            """{"tmdbtv":"999","tmdbseason":"2"}""",
            seasons: [Season(1), Season(2)],
            importTitles: true);

        var result = await fixture.Resolver.ResolveAsync(99, "release title must not be searched", useTmdbMapping: false);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Season!.SeasonNumber);
        Assert.Equal("Official Title", fixture.Tmdb.SearchTitles[0]);
        Assert.DoesNotContain("release title must not be searched", fixture.Tmdb.SearchTitles);
    }

    [Fact]
    public async Task MultipleRegularSeasonsWithoutMappedSeasonRequireAi()
    {
        await using var fixture = await ResolverFixture.CreateAsync(
            """{"tmdbtv":"100"}""",
            seasons: [Season(0, "Specials"), Season(1), Season(2)]);

        var result = await fixture.Resolver.ResolveAsync(99, "release", useTmdbMapping: true);

        Assert.False(result.IsSuccess);
        Assert.Equal("u2_tmdb_season_requires_ai", result.FailureCode);
    }

    [Fact]
    public async Task MovieMappingUsesTmdbMovieIdDirectlyWithoutTitleSearch()
    {
        await using var fixture = await ResolverFixture.CreateAsync(
            """{"tmdbid":"129"}""");

        var result = await fixture.Resolver.ResolveMovieAsync(
            99,
            "[Group] Release title [BDRip]",
            useTmdbMapping: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(129, result.Movie!.Id);
        Assert.Equal("u2_anidb_movie_mapping", result.Strategy);
        Assert.Equal([129], fixture.Tmdb.MovieDetailRequests);
        Assert.Empty(fixture.Tmdb.MovieSearchTitles);
    }

    [Fact]
    public async Task MovieWithoutMappingUsesAnitomyTitleSearch()
    {
        await using var fixture = await ResolverFixture.CreateAsync("{}");

        var result = await fixture.Resolver.ResolveMovieAsync(
            99,
            "[Group] Spirited Away [BDRip 1080p].mkv",
            useTmdbMapping: true);

        Assert.True(result.IsSuccess);
        Assert.Equal("u2_anitomy_movie_title", result.Strategy);
        Assert.Equal("Spirited Away", Assert.Single(fixture.Tmdb.MovieSearchTitles));
    }

    [Fact]
    public async Task EnabledMovieMappingWithoutTmdbIdUsesMappingNameBeforeAnitomyTitle()
    {
        await using var fixture = await ResolverFixture.CreateAsync(
            """{"name":"Eiga Doraemon: Shin Nobita no Kaitei Kiganjou"}""");

        var result = await fixture.Resolver.ResolveMovieAsync(
            19711,
            "[Group] unrelated release title [BDRip 1080p].mkv",
            useTmdbMapping: true);

        Assert.True(result.IsSuccess);
        Assert.Equal("u2_anidb_movie_mapping_title", result.Strategy);
        Assert.Equal(
            "Eiga Doraemon: Shin Nobita no Kaitei Kiganjou",
            Assert.Single(fixture.Tmdb.MovieSearchTitles));
    }

    [Fact]
    public async Task DisabledMovieMappingUsesOfficialCachedTitle()
    {
        await using var fixture = await ResolverFixture.CreateAsync(
            """{"tmdbid":"999"}""",
            importTitles: true);

        var result = await fixture.Resolver.ResolveMovieAsync(
            99,
            "release title must not be searched",
            useTmdbMapping: false);

        Assert.True(result.IsSuccess);
        Assert.Equal("u2_anidb_movie_title_cache", result.Strategy);
        Assert.Equal("Official Title", fixture.Tmdb.MovieSearchTitles[0]);
        Assert.DoesNotContain("release title must not be searched", fixture.Tmdb.MovieSearchTitles);
    }

    [Fact]
    public void SingleRegularSeasonIsSelectedAndSeasonZeroIsExcluded()
    {
        var details = new TmdbSeriesDetails(
            Series,
            [Season(0, "Specials"), Season(3)]);

        var selected = U2AniDbMetadataResolver.SelectSeason(details, mappedSeasonNumber: null);

        Assert.Equal(3, selected!.SeasonNumber);
    }

    private static readonly TmdbSeries Series =
        new(100, "Official Title", "公式タイトル", new DateOnly(2026, 1, 1));

    private static TmdbSeason Season(int number, string? name = null) =>
        new(1000 + number, 100, number, name ?? $"Season {number}", null, 2,
            Episodes:
            [
                new TmdbEpisode(2000 + number * 10 + 1, 100, number, 1, "E1", null),
                new TmdbEpisode(2000 + number * 10 + 2, 100, number, 2, "E2", null),
            ]);

    private sealed class ResolverFixture : IAsyncDisposable
    {
        private ResolverFixture(
            string root,
            U2AniDbMetadataResolver resolver,
            FakeTmdbClient tmdb)
        {
            Root = root;
            Resolver = resolver;
            Tmdb = tmdb;
        }

        private string Root { get; }

        public U2AniDbMetadataResolver Resolver { get; }

        public FakeTmdbClient Tmdb { get; }

        public static async Task<ResolverFixture> CreateAsync(
            string mappingJson,
            IReadOnlyList<TmdbSeason>? seasons = null,
            bool importTitles = false)
        {
            var root = Path.Combine(Path.GetTempPath(), $"animegonet-u2-resolver-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var database = new AnimeGoSqliteDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();
            var cache = new AnidbTitleCacheStore(database);
            if (importTitles)
            {
                await using var archive = Archive("""
                    <animetitles><anime aid="99">
                      <title xml:lang="en" type="main">Main Title</title>
                      <title xml:lang="en" type="official">Official Title</title>
                    </anime></animetitles>
                    """);
                await cache.ImportGzipAsync(
                    archive,
                    "https://anidb.net/api/anime-titles.xml.gz",
                    null,
                    null,
                    archive.Length,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow.AddDays(1));
            }

            var tmdb = new FakeTmdbClient(seasons ?? [Season(1)]);
            var resolver = new U2AniDbMetadataResolver(
                new HttpClient(new JsonHandler(mappingJson)),
                cache,
                new TmdbSeriesResolver(tmdb),
                tmdb,
                tmdb,
                new TmdbMovieResolver(tmdb));
            return new ResolverFixture(root, resolver, tmdb);
        }

        public ValueTask DisposeAsync()
        {
            Directory.Delete(Root, recursive: true);
            return ValueTask.CompletedTask;
        }

        private static MemoryStream Archive(string xml)
        {
            var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                gzip.Write(Encoding.UTF8.GetBytes(xml));
            }
            output.Position = 0;
            return output;
        }
    }

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            });
    }

    public sealed class FakeTmdbClient(IReadOnlyList<TmdbSeason> seasons) : ITmdbClient, ITmdbMovieClient
    {
        public List<string> SearchTitles { get; } = [];

        public List<string> MovieSearchTitles { get; } = [];

        public List<int> MovieDetailRequests { get; } = [];

        public Task<IReadOnlyList<TmdbSeries>> SearchSeriesAsync(
            string title,
            CancellationToken cancellationToken = default)
        {
            SearchTitles.Add(title);
            return Task.FromResult<IReadOnlyList<TmdbSeries>>([Series]);
        }

        public Task<TmdbSeries?> GetSeriesAsync(
            int seriesId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeries?>(seriesId == 100 ? Series : null);

        public Task<TmdbSeriesDetails?> GetSeriesDetailsAsync(
            int seriesId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeriesDetails?>(
                seriesId == 100 ? new TmdbSeriesDetails(Series, seasons) : null);

        public Task<TmdbSeason?> GetSeasonAsync(
            int seriesId,
            int seasonNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeason?>(seasons.SingleOrDefault(value =>
                value.SeriesId == seriesId && value.SeasonNumber == seasonNumber));

        public Task<TmdbEpisode?> GetEpisodeAsync(
            int seriesId,
            int seasonNumber,
            int episodeNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(seasons
                .SingleOrDefault(value => value.SeriesId == seriesId && value.SeasonNumber == seasonNumber)
                ?.Episodes
                ?.SingleOrDefault(value => value.EpisodeNumber == episodeNumber));

        public Task<IReadOnlyList<TmdbMovie>> SearchMoviesAsync(
            string title,
            CancellationToken cancellationToken = default)
        {
            MovieSearchTitles.Add(title);
            return Task.FromResult<IReadOnlyList<TmdbMovie>>(
                [new TmdbMovie(129, title, title, new DateOnly(2001, 7, 20))]);
        }

        public Task<TmdbMovie?> GetMovieAsync(
            int movieId,
            CancellationToken cancellationToken = default)
        {
            MovieDetailRequests.Add(movieId);
            return Task.FromResult<TmdbMovie?>(
                new TmdbMovie(movieId, "Spirited Away", "Spirited Away", new DateOnly(2001, 7, 20)));
        }
    }
}
