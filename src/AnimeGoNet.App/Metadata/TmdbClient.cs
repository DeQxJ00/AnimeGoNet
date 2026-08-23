using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.Metadata;

public sealed class TmdbClient : ITmdbClient, ITmdbMovieClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly TmdbClientOptions _options;
    private readonly bool _ownsHttpClient;

    public TmdbClient(HttpClient httpClient, TmdbClientOptions options, bool ownsHttpClient = false)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        _options = options;
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<IReadOnlyList<TmdbSeries>> SearchSeriesAsync(
        string title,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw Failure(MetadataFailureKind.InvalidInput, "tmdb_title_required");
        }

        var query = string.Join(
            '&',
            "sort_by=first_air_date.desc",
            $"language={Uri.EscapeDataString(_options.Language)}",
            "timezone=Asia%2FShanghai",
            "with_genres=16",
            $"with_text_query={Uri.EscapeDataString(title.Trim())}");
        var response = await GetAsync(
            $"3/discover/tv?{query}",
            TmdbJsonContext.Default.TmdbSearchResponse,
            allowNotFound: false,
            cancellationToken).ConfigureAwait(false);
        if (response?.Results is null || response.TotalResults < 0)
        {
            throw Failure(MetadataFailureKind.Protocol, "tmdb_invalid_search_response");
        }

        return response.Results.Select(MapSeries).ToArray();
    }

    public async Task<IReadOnlyList<TmdbMovie>> SearchMoviesAsync(
        string title,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw Failure(MetadataFailureKind.InvalidInput, "tmdb_movie_title_required");
        }

        var query = string.Join(
            '&',
            "sort_by=primary_release_date.desc",
            $"language={Uri.EscapeDataString(_options.Language)}",
            "with_genres=16",
            $"with_text_query={Uri.EscapeDataString(title.Trim())}");
        var response = await GetAsync(
            $"3/discover/movie?{query}",
            TmdbJsonContext.Default.TmdbMovieSearchResponse,
            allowNotFound: false,
            cancellationToken).ConfigureAwait(false);
        if (response?.Results is null || response.TotalResults < 0)
        {
            throw Failure(MetadataFailureKind.Protocol, "tmdb_invalid_movie_search_response");
        }

        return response.Results.Select(MapMovie).ToArray();
    }

    public async Task<TmdbMovie?> GetMovieAsync(
        int movieId,
        CancellationToken cancellationToken = default)
    {
        ValidatePositive(movieId, "tmdb_movie_id_invalid");
        var response = await GetAsync(
            $"3/movie/{movieId}?language={Uri.EscapeDataString(_options.Language)}",
            TmdbJsonContext.Default.TmdbMovieDto,
            allowNotFound: true,
            cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return null;
        }

        if (response.Id != movieId)
        {
            throw Failure(MetadataFailureKind.Protocol, "tmdb_movie_identity_mismatch");
        }

        return MapMovie(response);
    }

    public async Task<TmdbSeries?> GetSeriesAsync(
        int seriesId,
        CancellationToken cancellationToken = default) =>
        (await GetSeriesDetailsAsync(seriesId, cancellationToken).ConfigureAwait(false))?.Series;

    public async Task<TmdbSeriesDetails?> GetSeriesDetailsAsync(
        int seriesId,
        CancellationToken cancellationToken = default)
    {
        ValidatePositive(seriesId, "tmdb_series_id_invalid");
        var response = await GetAsync(
            $"3/tv/{seriesId}?language={Uri.EscapeDataString(_options.Language)}",
            TmdbJsonContext.Default.TmdbSeriesDto,
            allowNotFound: true,
            cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return null;
        }

        if (response.Id != seriesId)
        {
            throw Failure(MetadataFailureKind.Protocol, "tmdb_series_identity_mismatch");
        }

        var series = MapSeries(response);
        var seasons = response.Seasons?
            .Select(season => MapSeason(seriesId, season))
            .ToArray() ?? [];
        return new TmdbSeriesDetails(series, seasons);
    }

    public async Task<TmdbSeason?> GetSeasonAsync(
        int seriesId,
        int seasonNumber,
        CancellationToken cancellationToken = default)
    {
        ValidatePositive(seriesId, "tmdb_series_id_invalid");
        ValidatePositive(seasonNumber, "tmdb_season_number_invalid");
        var response = await GetAsync(
            $"3/tv/{seriesId}/season/{seasonNumber}?language={Uri.EscapeDataString(_options.Language)}",
            TmdbJsonContext.Default.TmdbSeasonDto,
            allowNotFound: true,
            cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return null;
        }

        if (response.Id <= 0 || response.SeasonNumber != seasonNumber)
        {
            throw Failure(MetadataFailureKind.Protocol, "tmdb_season_identity_mismatch");
        }

        return MapSeason(seriesId, response);
    }

    public async Task<TmdbEpisode?> GetEpisodeAsync(
        int seriesId,
        int seasonNumber,
        int episodeNumber,
        CancellationToken cancellationToken = default)
    {
        ValidatePositive(seriesId, "tmdb_series_id_invalid");
        ValidatePositive(seasonNumber, "tmdb_season_number_invalid");
        ValidatePositive(episodeNumber, "tmdb_episode_number_invalid");
        var response = await GetAsync(
            $"3/tv/{seriesId}/season/{seasonNumber}/episode/{episodeNumber}?language={Uri.EscapeDataString(_options.Language)}",
            TmdbJsonContext.Default.TmdbEpisodeDto,
            allowNotFound: true,
            cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return null;
        }

        return MapEpisode(seriesId, seasonNumber, episodeNumber, response);
    }

    private async Task<T?> GetAsync<T>(
        string relativeUri,
        JsonTypeInfo<T> jsonTypeInfo,
        bool allowNotFound,
        CancellationToken cancellationToken)
        where T : class
    {
        EnsureConfigured();
        try
        {
            return await MetadataRetryExecutor.ExecuteAsync(
                async attemptToken =>
                {
                    using var request = new HttpRequestMessage(
                        HttpMethod.Get,
                        BuildRequestUri(relativeUri));
                    if (!string.IsNullOrWhiteSpace(_options.ReadAccessToken))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer",
                            _options.ReadAccessToken.Trim());
                    }

                    using var response = await _httpClient
                        .SendAsync(request, attemptToken)
                        .ConfigureAwait(false);
                    if (allowNotFound
                        && response.StatusCode == HttpStatusCode.NotFound)
                    {
                        return null;
                    }

                    ThrowForStatus(response.StatusCode);
                    return await response.Content
                        .ReadFromJsonAsync(jsonTypeInfo, attemptToken)
                        .ConfigureAwait(false)
                        ?? throw Failure(
                            MetadataFailureKind.Protocol,
                            "tmdb_empty_response");
                },
                _options.HttpTimeout,
                _options.RetryCount,
                _options.RetryDelay,
                static exception =>
                    exception is TmdbClientException
                    {
                        Kind: MetadataFailureKind.RemoteService,
                    },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw Failure(MetadataFailureKind.Network, "tmdb_timeout");
        }
        catch (HttpRequestException)
        {
            throw Failure(MetadataFailureKind.Network, "tmdb_network_error");
        }
        catch (JsonException)
        {
            throw Failure(MetadataFailureKind.Protocol, "tmdb_invalid_json");
        }
    }

    private Uri BuildRequestUri(string relativeUri)
    {
        var separator = relativeUri.Contains('?') ? '&' : '?';
        var credential = string.IsNullOrWhiteSpace(_options.ReadAccessToken)
            ? $"{separator}api_key={Uri.EscapeDataString(_options.ApiKey!.Trim())}"
            : string.Empty;
        return new Uri(_options.BaseUrl, relativeUri + credential);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) && string.IsNullOrWhiteSpace(_options.ReadAccessToken))
        {
            throw Failure(MetadataFailureKind.Configuration, "tmdb_credential_missing");
        }
    }

    private static void ThrowForStatus(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        if (code is >= 200 and <= 299)
        {
            return;
        }

        throw statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                Failure(MetadataFailureKind.Authentication, "tmdb_authentication_failed"),
            HttpStatusCode.TooManyRequests =>
                Failure(MetadataFailureKind.RemoteService, "tmdb_rate_limited"),
            _ when code >= 500 =>
                Failure(MetadataFailureKind.RemoteService, "tmdb_service_error"),
            _ => Failure(MetadataFailureKind.Protocol, "tmdb_http_error"),
        };
    }

    private static TmdbSeries MapSeries(TmdbSeriesDto response)
    {
        if (response.Id <= 0)
        {
            throw Failure(MetadataFailureKind.Protocol, "tmdb_series_id_invalid");
        }

        return new TmdbSeries(
            response.Id,
            response.Name?.Trim() ?? string.Empty,
            response.OriginalName?.Trim() ?? string.Empty,
            ParseDate(response.FirstAirDate),
            NormalizePosterPath(response.PosterPath));
    }

    private static TmdbMovie MapMovie(TmdbMovieDto response)
    {
        if (response.Id <= 0)
        {
            throw Failure(MetadataFailureKind.Protocol, "tmdb_movie_id_invalid");
        }

        return new TmdbMovie(
            response.Id,
            response.Title?.Trim() ?? string.Empty,
            response.OriginalTitle?.Trim() ?? string.Empty,
            ParseDate(response.ReleaseDate),
            NormalizePosterPath(response.PosterPath));
    }

    private static TmdbSeason MapSeason(int seriesId, TmdbSeasonDto response)
    {
        if (response.Id <= 0 || response.SeasonNumber < 0 || response.EpisodeCount < 0)
        {
            throw Failure(MetadataFailureKind.Protocol, "tmdb_season_invalid");
        }

        var episodes = response.Episodes?
            .Select(item => MapEpisode(seriesId, response.SeasonNumber, item.EpisodeNumber, item))
            .ToArray();
        if (episodes is not null
            && (episodes.Select(item => item.Id).Distinct().Count() != episodes.Length
                || episodes.Select(item => item.EpisodeNumber).Distinct().Count() != episodes.Length))
        {
            throw Failure(MetadataFailureKind.Protocol, "tmdb_season_episode_identity_duplicate");
        }

        return new TmdbSeason(
            response.Id,
            seriesId,
            response.SeasonNumber,
            response.Name?.Trim() ?? string.Empty,
            ParseDate(response.AirDate),
            episodes?.Length ?? response.EpisodeCount,
            NormalizePosterPath(response.PosterPath),
            episodes);
    }

    private static TmdbEpisode MapEpisode(
        int seriesId,
        int expectedSeasonNumber,
        int expectedEpisodeNumber,
        TmdbEpisodeDto response)
    {
        if (response.Id <= 0
            || response.SeasonNumber != expectedSeasonNumber
            || response.EpisodeNumber != expectedEpisodeNumber
            || response.EpisodeNumber <= 0)
        {
            throw Failure(MetadataFailureKind.Protocol, "tmdb_episode_identity_mismatch");
        }

        return new TmdbEpisode(
            response.Id,
            seriesId,
            response.SeasonNumber,
            response.EpisodeNumber,
            response.Name?.Trim() ?? string.Empty,
            ParseDate(response.AirDate));
    }

    private static string? NormalizePosterPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > 256
            || normalized[0] != '/'
            || normalized.Contains('\\')
            || normalized.Any(char.IsControl))
        {
            throw Failure(MetadataFailureKind.Protocol, "tmdb_poster_path_invalid");
        }

        return normalized;
    }

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : throw Failure(MetadataFailureKind.Protocol, "tmdb_date_invalid");
    }

    private static void ValidatePositive(int value, string safeCode)
    {
        if (value <= 0)
        {
            throw Failure(MetadataFailureKind.InvalidInput, safeCode);
        }
    }

    private static TmdbClientException Failure(
        MetadataFailureKind kind,
        string safeCode) =>
        new(kind, safeCode, tmdbAccessConfirmed: false);

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}

internal sealed class UnavailableTmdbMovieClient : ITmdbMovieClient
{
    public static UnavailableTmdbMovieClient Instance { get; } = new();

    private UnavailableTmdbMovieClient()
    {
    }

    public Task<IReadOnlyList<TmdbMovie>> SearchMoviesAsync(
        string title,
        CancellationToken cancellationToken = default) =>
        Task.FromException<IReadOnlyList<TmdbMovie>>(Failure());

    public Task<TmdbMovie?> GetMovieAsync(
        int movieId,
        CancellationToken cancellationToken = default) =>
        Task.FromException<TmdbMovie?>(Failure());

    private static TmdbClientException Failure() => new(
        MetadataFailureKind.Configuration,
        "tmdb_movie_client_unavailable",
        tmdbAccessConfirmed: false);
}
