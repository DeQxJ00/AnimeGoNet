using System.Globalization;
using System.Text.Json;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Metadata;

namespace AnimeGoNet.App.Metadata;

public sealed record U2AniDbResolution(
    TmdbSeriesDetails? Details,
    TmdbSeason? Season,
    string Strategy,
    IReadOnlyList<string> AttemptedTitles,
    string? FailureCode)
{
    public bool IsSuccess => Details is not null && Season is not null && FailureCode is null;
}

public sealed record U2AniDbMovieResolution(
    TmdbMovie? Movie,
    string Strategy,
    IReadOnlyList<string> AttemptedTitles,
    MetadataFailure? Failure)
{
    public bool IsSuccess => Movie is not null && Failure is null;
}

internal sealed record U2AniDbMapping(
    int? TmdbSeriesId,
    int? TmdbSeasonNumber,
    int? TmdbMovieId,
    string? Name);

public sealed class U2AniDbMetadataResolver(
    HttpClient httpClient,
    AnidbTitleCacheStore titleCache,
    TmdbSeriesResolver seriesResolver,
    ITmdbClient tmdb,
    ITmdbMovieClient tmdbMovies,
    TmdbMovieResolver movieResolver)
{
    public const string MappingUrlTemplate =
        "https://raw.githubusercontent.com/DeQxJ00/Anime-Lists-Json/refs/heads/main/api/anidb/{anidbid}.json";

    public async Task<U2AniDbResolution> ResolveAsync(
        int aid,
        string taskTitle,
        string? mappingUrlTemplate = null,
        bool useTmdbMapping = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(aid, 0);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskTitle);

        // tmdbseason is independent of the direct-tmdbid option. Read the
        // configured mapping once even when direct tmdbid mapping is disabled.
        var mapping = await TryReadMappingAsync(aid, mappingUrlTemplate, cancellationToken)
            .ConfigureAwait(false);
        if (useTmdbMapping)
        {
            if (mapping?.TmdbSeriesId is not > 0)
            {
                return new U2AniDbResolution(
                    null, null, "u2_anidb_mapping", [], "anidb_tmdb_mapping_missing");
            }

            return await ResolveSeriesIdAsync(
                mapping.TmdbSeriesId.Value,
                mapping.TmdbSeasonNumber,
                "u2_anidb_mapping",
                cancellationToken).ConfigureAwait(false);
        }

        // GetPreferredTitlesAsync orders official titles first. Do not append
        // the U2 release title in AniDB-cache mode.
        var titles = await titleCache.GetPreferredTitlesAsync(aid, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return await ResolveTitlesAsync(
            titles,
            mapping?.TmdbSeasonNumber,
            "u2_anidb_title_cache",
            "anidb_title_cache_empty",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<U2AniDbResolution> ResolveTaskTitleAsync(
        string taskTitle,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskTitle);
        var animeTitle = ExtractAnimeTitle(taskTitle);
        return await ResolveTitlesAsync(
            string.IsNullOrWhiteSpace(animeTitle) ? [] : [animeTitle],
            mappedSeasonNumber: null,
            "u2_anitomy_title",
            "u2_anitomy_title_missing",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<U2AniDbMovieResolution> ResolveMovieAsync(
        int? aid,
        string taskTitle,
        string? mappingUrlTemplate = null,
        bool useTmdbMapping = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskTitle);
        var mapping = aid is > 0
            ? await TryReadMappingAsync(aid.Value, mappingUrlTemplate, cancellationToken)
                .ConfigureAwait(false)
            : null;

        if (useTmdbMapping && mapping?.TmdbMovieId is > 0)
        {
            try
            {
                var movie = await tmdbMovies.GetMovieAsync(
                    mapping.TmdbMovieId.Value,
                    cancellationToken).ConfigureAwait(false);
                if (movie is not null && movie.Id == mapping.TmdbMovieId.Value)
                {
                    return new U2AniDbMovieResolution(
                        movie,
                        "u2_anidb_movie_mapping",
                        [],
                        null);
                }
            }
            catch (TmdbClientException exception)
            {
                return new U2AniDbMovieResolution(
                    null,
                    "u2_anidb_movie_mapping",
                    [],
                    new MetadataFailure(
                        exception.Kind,
                        exception.SafeCode,
                        exception.TmdbAccessConfirmed));
            }
        }

        if (useTmdbMapping && !string.IsNullOrWhiteSpace(mapping?.Name))
        {
            var mappedTitleResult = await ResolveMovieTitlesAsync(
                [mapping.Name],
                "u2_anidb_movie_mapping_title",
                cancellationToken).ConfigureAwait(false);
            if (mappedTitleResult.IsSuccess
                || mappedTitleResult.Failure?.Kind is not MetadataFailureKind.SemanticNoMatch)
            {
                return mappedTitleResult;
            }
        }

        if (!useTmdbMapping && aid is > 0)
        {
            var cachedTitles = await titleCache.GetPreferredTitlesAsync(
                aid.Value,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (cachedTitles.Count > 0)
            {
                return await ResolveMovieTitlesAsync(
                    cachedTitles,
                    "u2_anidb_movie_title_cache",
                    cancellationToken).ConfigureAwait(false);
            }
        }

        var animeTitle = ExtractAnimeTitle(taskTitle);
        if (string.IsNullOrWhiteSpace(animeTitle))
        {
            return new U2AniDbMovieResolution(
                null,
                "u2_anitomy_movie_title",
                [],
                new MetadataFailure(
                    MetadataFailureKind.InvalidInput,
                    "u2_movie_title_missing",
                    false));
        }

        return await ResolveMovieTitlesAsync(
            [animeTitle],
            "u2_anitomy_movie_title",
            cancellationToken).ConfigureAwait(false);
    }

    internal static string? ExtractAnimeTitle(string taskTitle)
        => AnitomyTitleParser.ParseTitle(taskTitle).AnimeTitle;

    private async Task<U2AniDbResolution> ResolveTitlesAsync(
        IEnumerable<string> titles,
        int? mappedSeasonNumber,
        string strategy,
        string emptyFailureCode,
        CancellationToken cancellationToken)
    {
        var candidates = titles
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0)
        {
            return new U2AniDbResolution(null, null, strategy, [], emptyFailureCode);
        }

        var attempted = new List<string>();
        TmdbSeriesDetails? lastDetails = null;
        TmdbSeason? resolvedSeason = null;
        foreach (var title in candidates)
        {
            var result = await seriesResolver.ResolveAsync(
                title,
                async (series, token) =>
                {
                    attempted.Add(title);
                    var details = await GetSeriesDetailsAsync(series.Id, token).ConfigureAwait(false);
                    lastDetails = details;
                    var selected = details is null
                        ? null
                        : SelectSeason(details, mappedSeasonNumber);
                    if (selected is null)
                    {
                        return false;
                    }

                    resolvedSeason = await GetSeasonAsync(
                        details!.Series.Id,
                        selected.SeasonNumber,
                        token).ConfigureAwait(false);
                    return resolvedSeason is not null;
                },
                cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess && lastDetails is not null && resolvedSeason is not null)
            {
                return new U2AniDbResolution(
                    lastDetails,
                    resolvedSeason,
                    strategy,
                    attempted.Concat(result.AttemptedTitles).Distinct(StringComparer.Ordinal).ToArray(),
                    null);
            }
        }

        return new U2AniDbResolution(
            lastDetails,
            null,
            strategy,
            attempted.Distinct(StringComparer.Ordinal).ToArray(),
            lastDetails is null
                ? "anidb_title_tmdb_search_not_matched"
                : "u2_tmdb_season_requires_ai");
    }

    private async Task<U2AniDbResolution> ResolveSeriesIdAsync(
        int seriesId,
        int? mappedSeasonNumber,
        string strategy,
        CancellationToken cancellationToken)
    {
        var details = await GetSeriesDetailsAsync(seriesId, cancellationToken).ConfigureAwait(false);
        var selected = details is null ? null : SelectSeason(details, mappedSeasonNumber);
        var season = selected is null
            ? null
            : await GetSeasonAsync(seriesId, selected.SeasonNumber, cancellationToken).ConfigureAwait(false);

        return details is not null && season is not null
            ? new U2AniDbResolution(details, season, strategy, [], null)
            : new U2AniDbResolution(
                details,
                null,
                strategy,
                [],
                details is null
                    ? "anidb_tmdb_mapping_validation_failed"
                    : "u2_tmdb_season_requires_ai");
    }

    private async Task<U2AniDbMapping?> TryReadMappingAsync(
        int aid,
        string? mappingUrlTemplate,
        CancellationToken cancellationToken)
    {
        var template = string.IsNullOrWhiteSpace(mappingUrlTemplate)
            ? MappingUrlTemplate
            : mappingUrlTemplate.Trim();
        var url = template.Replace(
            "{anidbid}", aid.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        try
        {
            using var response = await httpClient.GetAsync(
                url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(
                stream,
                new JsonDocumentOptions { MaxDepth = 8 },
                cancellationToken).ConfigureAwait(false);
            return new U2AniDbMapping(
                ReadPositiveInt(document.RootElement, "tmdbtv"),
                ReadPositiveInt(document.RootElement, "tmdbseason"),
                ReadPositiveInt(document.RootElement, "tmdbid"),
                ReadNonEmptyString(document.RootElement, "name"));
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<U2AniDbMovieResolution> ResolveMovieTitlesAsync(
        IEnumerable<string> titles,
        string strategy,
        CancellationToken cancellationToken)
    {
        var resolved = await movieResolver.ResolveAsync(titles, cancellationToken)
            .ConfigureAwait(false);
        return new U2AniDbMovieResolution(
            resolved.Value,
            strategy,
            resolved.AttemptedTitles,
            resolved.Failure);
    }

    private static int? ReadPositiveInt(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        var text = value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.GetRawText();
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var result)
            && result > 0
                ? result
                : null;
    }

    private static string? ReadNonEmptyString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind is not JsonValueKind.String)
        {
            return null;
        }

        var result = value.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private async Task<TmdbSeriesDetails?> GetSeriesDetailsAsync(
        int seriesId,
        CancellationToken cancellationToken)
    {
        var details = await tmdb.GetSeriesDetailsAsync(seriesId, cancellationToken).ConfigureAwait(false);
        if ((details is null || details.Series.Id != seriesId) && tmdb is ITmdbRefreshClient refresh)
        {
            details = await refresh.RefreshSeriesDetailsAsync(seriesId, cancellationToken).ConfigureAwait(false);
        }
        return details is not null && details.Series.Id == seriesId ? details : null;
    }

    private async Task<TmdbSeason?> GetSeasonAsync(
        int seriesId,
        int seasonNumber,
        CancellationToken cancellationToken)
    {
        var season = await tmdb.GetSeasonAsync(seriesId, seasonNumber, cancellationToken).ConfigureAwait(false);
        if (!IsValidSeason(season, seriesId, seasonNumber) && tmdb is ITmdbRefreshClient refresh)
        {
            season = await refresh.RefreshSeasonAsync(seriesId, seasonNumber, cancellationToken).ConfigureAwait(false);
        }
        return IsValidSeason(season, seriesId, seasonNumber) ? season : null;
    }

    internal static TmdbSeason? SelectSeason(
        TmdbSeriesDetails details,
        int? mappedSeasonNumber)
    {
        var regular = details.Seasons
            .Where(value => value.SeasonNumber > 0
                && !string.Equals(value.Name, "Specials", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (regular.Length == 1)
        {
            return regular[0];
        }

        return mappedSeasonNumber is > 0
            ? regular.SingleOrDefault(value => value.SeasonNumber == mappedSeasonNumber.Value)
            : null;
    }

    private static bool IsValidSeason(TmdbSeason? season, int seriesId, int seasonNumber) =>
        season is not null
        && season.Id > 0
        && season.SeriesId == seriesId
        && season.SeasonNumber == seasonNumber
        && season.SeasonNumber > 0;
}
