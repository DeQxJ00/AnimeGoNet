using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Cache;

namespace AnimeGoNet.App.Metadata;

internal sealed class TmdbCachingClient(
    ITmdbClient inner,
    SqliteJsonCacheStore cache,
    TmdbClientOptions options,
    TimeProvider? timeProvider = null,
    bool ownsInner = false,
    MetadataRefreshScope? refreshScope = null) : ITmdbClient, IDisposable
{
    internal const string DatabaseName = "bolt";
    internal const string BucketName = "themoviedb";
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly string _partition = Digest(
        options.BaseUrl.AbsoluteUri.TrimEnd('/'),
        options.Language.Trim().ToLowerInvariant());
    private int _disposed;

    public async Task<IReadOnlyList<TmdbSeries>> SearchSeriesAsync(
        string title,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title) || options.CacheTtl <= TimeSpan.Zero)
        {
            return await inner.SearchSeriesAsync(title, cancellationToken).ConfigureAwait(false);
        }

        var key = Key("search", title.Trim());
        var cached = refreshScope?.BypassCaches == true ? null : await ReadAsync(
            key,
            TmdbJsonContext.Default.TmdbSeriesArray,
            static value => value.All(IsValidSeries)
                && value.Select(item => item.Id).Distinct().Count() == value.Length,
            cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        var result = (await inner.SearchSeriesAsync(title, cancellationToken)
            .ConfigureAwait(false)).ToArray();
        await WriteAsync(
            key,
            result,
            TmdbJsonContext.Default.TmdbSeriesArray,
            cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<TmdbSeries?> GetSeriesAsync(
        int seriesId,
        CancellationToken cancellationToken = default)
    {
        if (seriesId <= 0 || options.CacheTtl <= TimeSpan.Zero)
        {
            return await inner.GetSeriesAsync(seriesId, cancellationToken).ConfigureAwait(false);
        }

        return (await GetSeriesDetailsAsync(seriesId, cancellationToken)
            .ConfigureAwait(false))?.Series;
    }

    public async Task<TmdbSeriesDetails?> GetSeriesDetailsAsync(
        int seriesId,
        CancellationToken cancellationToken = default)
    {
        if (seriesId <= 0 || options.CacheTtl <= TimeSpan.Zero)
        {
            return await inner.GetSeriesDetailsAsync(seriesId, cancellationToken).ConfigureAwait(false);
        }

        var key = Key("series", seriesId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var cached = refreshScope?.BypassCaches == true ? null : await ReadAsync(
            key,
            TmdbJsonContext.Default.TmdbSeriesDetails,
            value => IsValidDetails(value, seriesId),
            cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        var result = await inner.GetSeriesDetailsAsync(seriesId, cancellationToken)
            .ConfigureAwait(false);
        if (result is not null)
        {
            await WriteAsync(
                key,
                result,
                TmdbJsonContext.Default.TmdbSeriesDetails,
                cancellationToken).ConfigureAwait(false);
        }
        return result;
    }

    public async Task<TmdbSeason?> GetSeasonAsync(
        int seriesId,
        int seasonNumber,
        CancellationToken cancellationToken = default)
    {
        if (seriesId <= 0 || seasonNumber <= 0 || options.CacheTtl <= TimeSpan.Zero)
        {
            return await inner.GetSeasonAsync(seriesId, seasonNumber, cancellationToken)
                .ConfigureAwait(false);
        }

        var key = Key("season", FormattableString.Invariant($"{seriesId}:{seasonNumber}"));
        var cached = refreshScope?.BypassCaches == true ? null : await ReadAsync(
            key,
            TmdbJsonContext.Default.TmdbSeason,
            value => IsValidSeason(value, seriesId, seasonNumber),
            cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        var result = await inner.GetSeasonAsync(seriesId, seasonNumber, cancellationToken)
            .ConfigureAwait(false);
        if (result is not null)
        {
            await WriteAsync(
                key,
                result,
                TmdbJsonContext.Default.TmdbSeason,
                cancellationToken).ConfigureAwait(false);
        }
        return result;
    }

    public async Task<TmdbEpisode?> GetEpisodeAsync(
        int seriesId,
        int seasonNumber,
        int episodeNumber,
        CancellationToken cancellationToken = default)
    {
        if (seriesId <= 0 || seasonNumber <= 0 || episodeNumber <= 0
            || options.CacheTtl <= TimeSpan.Zero)
        {
            return await inner.GetEpisodeAsync(
                seriesId, seasonNumber, episodeNumber, cancellationToken).ConfigureAwait(false);
        }

        var key = Key(
            "episode",
            FormattableString.Invariant($"{seriesId}:{seasonNumber}:{episodeNumber}"));
        var cached = refreshScope?.BypassCaches == true ? null : await ReadAsync(
            key,
            TmdbJsonContext.Default.TmdbEpisode,
            value => IsValidEpisode(value, seriesId, seasonNumber, episodeNumber),
            cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        var result = await inner.GetEpisodeAsync(
            seriesId, seasonNumber, episodeNumber, cancellationToken).ConfigureAwait(false);
        if (result is not null)
        {
            await WriteAsync(
                key,
                result,
                TmdbJsonContext.Default.TmdbEpisode,
                cancellationToken).ConfigureAwait(false);
        }
        return result;
    }

    private async Task<T?> ReadAsync<T>(
        string key,
        JsonTypeInfo<T> typeInfo,
        Func<T, bool> validate,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var entry = await cache.GetJsonAsync(
                DatabaseName,
                BucketName,
                key,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
            if (entry is null)
            {
                return null;
            }

            var value = JsonSerializer.Deserialize(entry.ValueJson, typeInfo);
            if (value is not null && validate(value))
            {
                return value;
            }

            await TryDeleteAsync(key, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsRecoverableCacheFailure(exception))
        {
            await TryDeleteAsync(key, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task WriteAsync<T>(
        string key,
        T value,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(value, typeInfo);
            await cache.PutJsonAsync(
                DatabaseName,
                BucketName,
                key,
                json,
                options.CacheTtl,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsRecoverableCacheFailure(exception))
        {
            // Cache persistence is best effort; a successful authoritative TMDB response wins.
        }
    }

    private async Task TryDeleteAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await cache.DeleteAsync(DatabaseName, BucketName, key, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsRecoverableCacheFailure(exception))
        {
        }
    }

    private string Key(string operation, string identity) =>
        $"v1:{operation}:{Digest(_partition, identity)}";

    private static string Digest(params string[] values)
    {
        var input = string.Join('\n', values);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))
            .ToLowerInvariant();
    }

    private static bool IsValidDetails(TmdbSeriesDetails value, int expectedSeriesId) =>
        IsValidSeries(value.Series)
        && value.Series.Id == expectedSeriesId
        && value.Seasons.All(season => IsValidSeason(season, expectedSeriesId, season.SeasonNumber))
        && value.Seasons.Select(season => season.SeasonNumber).Distinct().Count() == value.Seasons.Count;

    private static bool IsValidSeries(TmdbSeries value) =>
        value.Id > 0 && value.Name is not null && value.OriginalName is not null;

    private static bool IsValidSeason(TmdbSeason value, int seriesId, int seasonNumber) =>
        value.Id > 0
        && value.SeriesId == seriesId
        && value.SeasonNumber == seasonNumber
        && value.SeasonNumber >= 0
        && value.EpisodeCount >= 0
        && value.Name is not null
        && (value.Episodes is null
            || (value.Episodes.All(episode =>
                    IsValidEpisode(episode, seriesId, seasonNumber, episode.EpisodeNumber))
                && value.Episodes.Select(episode => episode.Id).Distinct().Count()
                    == value.Episodes.Count
                && value.Episodes.Select(episode => episode.EpisodeNumber).Distinct().Count()
                    == value.Episodes.Count));

    private static bool IsValidEpisode(
        TmdbEpisode value,
        int seriesId,
        int seasonNumber,
        int episodeNumber) =>
        value.Id > 0
        && value.SeriesId == seriesId
        && value.SeasonNumber == seasonNumber
        && value.EpisodeNumber == episodeNumber
        && value.EpisodeNumber > 0
        && value.Name is not null;

    private static bool IsRecoverableCacheFailure(Exception exception) =>
        exception is JsonException
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException
            or Microsoft.Data.Sqlite.SqliteException;

    public void Dispose()
    {
        if (ownsInner && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            (inner as IDisposable)?.Dispose();
        }
    }
}
