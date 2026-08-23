using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.Tests.Metadata;

public sealed class TmdbClientTests
{
    [Fact]
    public async Task MovieSearchUsesAnimatedMovieDiscoverAndMapsCanonicalFields()
    {
        const string json = """
            {"total_results":1,"results":[{"id":129,"title":"千与千寻","original_title":"千と千尋の神隠し","release_date":"2001-07-20","poster_path":"/movie.jpg"}]}
            """;
        using var handler = new RecordingHandler(_ => Json(json));
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);

        var movie = Assert.Single(await client.SearchMoviesAsync("千と千尋の神隠し"));

        Assert.Equal(129, movie.Id);
        Assert.Equal("千与千寻", movie.Title);
        Assert.Equal("千と千尋の神隠し", movie.OriginalTitle);
        Assert.Equal(new DateOnly(2001, 7, 20), movie.ReleaseDate);
        Assert.Equal("/movie.jpg", movie.PosterPath);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/3/discover/movie", request.Path);
        Assert.Contains("sort_by=primary_release_date.desc", request.Query, StringComparison.Ordinal);
        Assert.Contains("with_genres=16", request.Query, StringComparison.Ordinal);
        Assert.Contains("with_text_query=", request.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MovieDetailsUseMovieEndpointAndValidateIdentity()
    {
        const string json = """
            {"id":129,"title":"千与千寻","original_title":"千と千尋の神隠し","release_date":"2001-07-20","poster_path":"/movie.jpg"}
            """;
        using var handler = new RecordingHandler(_ => Json(json));
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);

        var movie = Assert.IsType<TmdbMovie>(await client.GetMovieAsync(129));

        Assert.Equal(129, movie.Id);
        Assert.Equal("/3/movie/129", Assert.Single(handler.Requests).Path);
    }

    [Fact]
    public async Task SearchPreservesUpstreamDiscoverParametersAndMapsCanonicalFields()
    {
        const string json = """
            {"total_results":1,"results":[{"id":72517,"name":"来自深渊","original_name":"メイドインアビス","first_air_date":"2017-07-07","poster_path":"/series.jpg"}]}
            """;
        using var handler = new RecordingHandler(_ => Json(json));
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);

        var series = Assert.Single(await client.SearchSeriesAsync("メイドインアビス"));

        Assert.Equal(72517, series.Id);
        Assert.Equal("来自深渊", series.Name);
        Assert.Equal(new DateOnly(2017, 7, 7), series.FirstAirDate);
        Assert.Equal("/series.jpg", series.PosterPath);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/3/discover/tv", request.Path);
        Assert.Contains("language=zh-CN", request.Query, StringComparison.Ordinal);
        Assert.Contains("timezone=Asia%2FShanghai", request.Query, StringComparison.Ordinal);
        Assert.Contains("with_genres=16", request.Query, StringComparison.Ordinal);
        Assert.Contains("with_text_query=", request.Query, StringComparison.Ordinal);
        Assert.Contains("api_key=test-key", request.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAccessTokenUsesAuthorizationHeaderAndNeverQueryString()
    {
        const string json = """{"id":72517,"name":"来自深渊","original_name":"メイドインアビス","first_air_date":"2017-07-07"}""";
        using var handler = new RecordingHandler(_ => Json(json));
        using var http = new HttpClient(handler);
        using var client = new TmdbClient(http, Options() with
        {
            ApiKey = "fallback-key",
            ReadAccessToken = "read-token",
        });

        _ = await client.GetSeriesAsync(72517);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer", request.Authorization?.Scheme);
        Assert.Equal("read-token", request.Authorization?.Parameter);
        Assert.DoesNotContain("api_key", request.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SeriesDetailsExposeOrdinarySeasonSummariesForDateMatching()
    {
        const string json = """
            {"id":72517,"name":"来自深渊","original_name":"メイドインアビス","first_air_date":"2017-07-07","poster_path":"/series.jpg","seasons":[
              {"id":100,"name":"Specials","season_number":0,"air_date":"2017-01-01","episode_count":3},
              {"id":204984,"name":"烈日的黄金乡","season_number":2,"air_date":"2022-07-06","episode_count":12,"poster_path":"/season-2.jpg"}
            ]}
            """;
        using var handler = new RecordingHandler(_ => Json(json));
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);

        var details = Assert.IsType<TmdbSeriesDetails>(await client.GetSeriesDetailsAsync(72517));

        Assert.Equal(72517, details.Series.Id);
        Assert.Equal("/series.jpg", details.Series.PosterPath);
        Assert.Collection(
            details.Seasons,
            season => Assert.Equal(0, season.SeasonNumber),
            season =>
            {
                Assert.Equal(2, season.SeasonNumber);
                Assert.Equal(12, season.EpisodeCount);
                Assert.Equal(new DateOnly(2022, 7, 6), season.AirDate);
                Assert.Equal("/season-2.jpg", season.PosterPath);
            });
    }

    [Theory]
    [InlineData("https://image.tmdb.org/poster.jpg")]
    [InlineData("\\poster.jpg")]
    [InlineData("/poster\r.jpg")]
    public async Task InvalidPosterPathIsRejectedAsProtocolFailure(string posterPath)
    {
        var json = $$"""
            {"id":72517,"name":"来自深渊","original_name":"メイドインアビス","first_air_date":"2017-07-07","poster_path":{{System.Text.Json.JsonSerializer.Serialize(posterPath)}}}
            """;
        using var handler = new RecordingHandler(_ => Json(json));
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);

        var exception = await Assert.ThrowsAsync<TmdbClientException>(
            () => client.GetSeriesAsync(72517));

        Assert.Equal(MetadataFailureKind.Protocol, exception.Kind);
        Assert.Equal("tmdb_poster_path_invalid", exception.SafeCode);
    }

    [Fact]
    public async Task DuplicateSeasonEpisodeIdentityIsRejected()
    {
        const string json = """
            {"id":204984,"name":"Season 2","season_number":2,"air_date":"2022-07-06","episodes":[
              {"id":310001,"name":"Episode 1","air_date":"2022-07-06","season_number":2,"episode_number":1},
              {"id":310002,"name":"Duplicate 1","air_date":"2022-07-13","season_number":2,"episode_number":1}
            ]}
            """;
        using var handler = new RecordingHandler(_ => Json(json));
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);

        var exception = await Assert.ThrowsAsync<TmdbClientException>(
            () => client.GetSeasonAsync(72517, 2));

        Assert.Equal(MetadataFailureKind.Protocol, exception.Kind);
        Assert.Equal("tmdb_season_episode_identity_duplicate", exception.SafeCode);
    }

    [Fact]
    public async Task AuthorityValidatesSeriesSeasonAndEpisodeUsingOfficialEndpoints()
    {
        using var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/3/tv/72517" => Json("""{"id":72517,"name":"来自深渊","original_name":"メイドインアビス","first_air_date":"2017-07-07"}"""),
            "/3/tv/72517/season/2" => Json("""{"id":204984,"name":"烈日的黄金乡","season_number":2,"air_date":"2022-07-06","episodes":[{"id":310001,"name":"罗盘指向了黑暗","air_date":"2022-07-06","season_number":2,"episode_number":1}]}"""),
            "/3/tv/72517/season/2/episode/1" => Json("""{"id":310001,"name":"罗盘指向了黑暗","air_date":"2022-07-06","season_number":2,"episode_number":1}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);
        var authority = new TmdbAuthority(client);

        var result = await authority.ValidateEpisodeAsync(72517, 2, 1);

        Assert.True(result.IsSuccess);
        Assert.Equal("来自深渊", result.Value!.CanonicalSeriesName);
        Assert.Equal(204984, result.Value.Season.Id);
        Assert.Single(result.Value.Season.Episodes!);
        Assert.Equal(310001, result.Value.Episode.Id);
        Assert.Equal(
            ["/3/tv/72517", "/3/tv/72517/season/2", "/3/tv/72517/season/2/episode/1"],
            handler.Requests.Select(request => request.Path).ToArray());
    }

    [Fact]
    public async Task AuthoritativeEpisodeNotFoundIsSemanticNoMatch()
    {
        using var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/3/tv/72517" => Json("""{"id":72517,"name":"","original_name":"メイドインアビス","first_air_date":"2017-07-07"}"""),
            "/3/tv/72517/season/2" => Json("""{"id":204984,"name":"Season 2","season_number":2,"air_date":"2022-07-06","episodes":[]}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);

        var result = await new TmdbAuthority(client).ValidateEpisodeAsync(72517, 2, 99);

        Assert.False(result.IsSuccess);
        Assert.Equal(MetadataFailureKind.SemanticNoMatch, result.Failure!.Kind);
        Assert.Equal("tmdb_episode_not_found", result.Failure.Code);
        Assert.True(result.Failure.TmdbAccessConfirmed);
    }

    [Fact]
    public async Task AuthenticationFailureIsStableAndDoesNotExposeCredential()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);

        var exception = await Assert.ThrowsAsync<TmdbClientException>(() => client.GetSeriesAsync(72517));

        Assert.Equal(MetadataFailureKind.Authentication, exception.Kind);
        Assert.Equal("tmdb_authentication_failed", exception.SafeCode);
        Assert.False(exception.TmdbAccessConfirmed);
        Assert.DoesNotContain("test-key", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingCredentialIsConfigurationFailureWithoutNetworkRequest()
    {
        using var handler = new RecordingHandler(_ => throw new InvalidOperationException("must not send"));
        using var http = new HttpClient(handler);
        using var client = new TmdbClient(http, Options() with { ApiKey = null });

        var exception = await Assert.ThrowsAsync<TmdbClientException>(() => client.GetSeriesAsync(1));

        Assert.Equal(MetadataFailureKind.Configuration, exception.Kind);
        Assert.Equal("tmdb_credential_missing", exception.SafeCode);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, MetadataFailureKind.RemoteService, "tmdb_rate_limited")]
    [InlineData(HttpStatusCode.ServiceUnavailable, MetadataFailureKind.RemoteService, "tmdb_service_error")]
    [InlineData(HttpStatusCode.BadRequest, MetadataFailureKind.Protocol, "tmdb_http_error")]
    public async Task HttpFailuresUseStableNonFallbackClassifications(
        HttpStatusCode statusCode,
        MetadataFailureKind expectedKind,
        string expectedCode)
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(statusCode));
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);

        var exception = await Assert.ThrowsAsync<TmdbClientException>(() => client.GetSeriesAsync(1));

        Assert.Equal(expectedKind, exception.Kind);
        Assert.Equal(expectedCode, exception.SafeCode);
        Assert.False(exception.TmdbAccessConfirmed);
    }

    [Fact]
    public async Task MalformedJsonIsProtocolFailureAndCannotEnableFallback()
    {
        using var handler = new RecordingHandler(_ => Json("{not-json"));
        using var http = new HttpClient(handler);
        using var client = new TmdbClient(
            http,
            Options() with
            {
                RetryCount = 3,
                RetryDelay = TimeSpan.Zero,
            });

        var exception = await Assert.ThrowsAsync<TmdbClientException>(() => client.GetSeriesAsync(1));

        Assert.Equal(MetadataFailureKind.Protocol, exception.Kind);
        Assert.Equal("tmdb_invalid_json", exception.SafeCode);
        Assert.False(exception.TmdbAccessConfirmed);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task NetworkExceptionIsSanitized()
    {
        using var handler = new RecordingHandler(
            _ => throw new HttpRequestException("https://tmdb.invalid/?api_key=private"));
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);

        var exception = await Assert.ThrowsAsync<TmdbClientException>(() => client.GetSeriesAsync(1));

        Assert.Equal(MetadataFailureKind.Network, exception.Kind);
        Assert.Equal("tmdb_network_error", exception.SafeCode);
        Assert.DoesNotContain("private", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PerClientTimeoutUsesStableNetworkClassification()
    {
        using var http = new HttpClient(new NeverCompletesHandler());
        using var client = new TmdbClient(
            http,
            Options() with
            {
                HttpTimeout = TimeSpan.FromMilliseconds(30),
            });

        var exception = await Assert.ThrowsAsync<TmdbClientException>(() => client.GetSeriesAsync(1));

        Assert.Equal(MetadataFailureKind.Network, exception.Kind);
        Assert.Equal("tmdb_timeout", exception.SafeCode);
        Assert.False(exception.TmdbAccessConfirmed);
    }

    [Fact]
    public async Task RetriesNetworkAndRemoteServiceFailuresWithFreshCredentialedRequests()
    {
        const string json =
            """{"id":72517,"name":"来自深渊","original_name":"メイドインアビス","first_air_date":"2017-07-07"}""";
        var attempt = 0;
        using var handler = new RecordingHandler(_ => ++attempt switch
        {
            1 => throw new HttpRequestException("transient"),
            2 => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            _ => Json(json),
        });
        using var http = new HttpClient(handler);
        using var client = new TmdbClient(
            http,
            Options() with
            {
                RetryCount = 2,
                RetryDelay = TimeSpan.Zero,
            });

        var series = Assert.IsType<TmdbSeries>(
            await client.GetSeriesAsync(72517));

        Assert.Equal(72517, series.Id);
        Assert.Equal(3, handler.Requests.Count);
        Assert.All(
            handler.Requests,
            request => Assert.Contains(
                "api_key=test-key",
                request.Query,
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task DoesNotRetrySemanticAuthenticationOrProtocolResponses(
        HttpStatusCode statusCode)
    {
        using var handler = new RecordingHandler(
            _ => new HttpResponseMessage(statusCode));
        using var http = new HttpClient(handler);
        using var client = new TmdbClient(
            http,
            Options() with
            {
                RetryCount = 3,
                RetryDelay = TimeSpan.Zero,
            });

        if (statusCode == HttpStatusCode.NotFound)
        {
            Assert.Null(await client.GetSeriesAsync(72517));
        }
        else
        {
            _ = await Assert.ThrowsAsync<TmdbClientException>(
                () => client.GetSeriesAsync(72517));
        }

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CallerCancellationStopsRetryDelayImmediately()
    {
        using var handler = new RecordingHandler(
            _ => throw new HttpRequestException("transient"));
        using var http = new HttpClient(handler);
        using var client = new TmdbClient(
            http,
            Options() with
            {
                RetryCount = 3,
                RetryDelay = TimeSpan.FromMinutes(1),
            });
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetSeriesAsync(72517, cancellation.Token));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task PerAttemptTimeoutIsRetriedBeforeStableSuccess()
    {
        using var handler = new TimeoutThenSuccessHandler();
        using var http = new HttpClient(handler);
        using var client = new TmdbClient(
            http,
            Options() with
            {
                HttpTimeout = TimeSpan.FromMilliseconds(30),
                RetryCount = 1,
                RetryDelay = TimeSpan.Zero,
            });

        var series = Assert.IsType<TmdbSeries>(
            await client.GetSeriesAsync(72517));

        Assert.Equal(72517, series.Id);
        Assert.Equal(2, handler.Attempts);
    }

    private static TmdbClient CreateClient(HttpClient http) => new(http, Options());

    private static TmdbClientOptions Options() => new()
    {
        BaseUrl = new Uri("https://tmdb.invalid/"),
        ApiKey = "test-key",
        HttpTimeout = TimeSpan.FromSeconds(2),
        RetryCount = 0,
    };

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json"),
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.RequestUri!.AbsolutePath,
                request.RequestUri.Query,
                request.Headers.Authorization));
            return Task.FromResult(responder(request));
        }
    }

    private sealed record RecordedRequest(
        string Path,
        string Query,
        AuthenticationHeaderValue? Authorization);

    private sealed class NeverCompletesHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _ = request;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class TimeoutThenSuccessHandler : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _ = request;
            Attempts++;
            if (Attempts == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return Json(
                """{"id":72517,"name":"来自深渊","original_name":"メイドインアビス","first_air_date":"2017-07-07"}""");
        }
    }
}
