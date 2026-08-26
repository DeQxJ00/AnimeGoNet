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

public sealed class U2AniDbMetadataResolver(
    HttpClient httpClient,
    AnidbTitleCacheStore titleCache,
    TmdbSeriesResolver seriesResolver,
    ITmdbClient tmdb)
{
    public const string MappingUrlTemplate =
        "https://raw.githubusercontent.com/DeQxJ00/Anime-Lists-Json/refs/heads/main/api/anidb/{anidbid}.json";

    public async Task<U2AniDbResolution> ResolveAsync(
        int aid,
        string taskTitle,
        string? mappingUrlTemplate = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(aid, 0);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskTitle);

        var mappedId = await TryReadMappingAsync(aid, mappingUrlTemplate, cancellationToken).ConfigureAwait(false);
        if (mappedId is > 0)
        {
            var direct = await ResolveSeriesIdAsync(
                mappedId.Value, taskTitle, "u2_anidb_mapping", cancellationToken).ConfigureAwait(false);
            if (direct.IsSuccess)
            {
                return direct;
            }
        }

        var titles = await titleCache.GetPreferredTitlesAsync(aid, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var candidates = titles
            .Append(taskTitle.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0)
        {
            return new U2AniDbResolution(null, null, "u2_anidb_title_cache", [], "anidb_title_cache_empty");
        }

        var attempted = new List<string>();
        TmdbSeriesDetails? details = null;
        TmdbSeason? season = null;
        foreach (var title in candidates)
        {
            var result = await seriesResolver.ResolveAsync(
                title,
                async (series, token) =>
                {
                    attempted.Add(title);
                    details = await GetSeriesDetailsAsync(series.Id, token).ConfigureAwait(false);
                    season = details is null ? null : SelectSeason(details, taskTitle);
                    if (details is null || season is null)
                    {
                        return false;
                    }

                    season = await GetSeasonAsync(
                        details.Series.Id, season.SeasonNumber, token).ConfigureAwait(false);
                    return season is not null;
                },
                cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess && details is not null && season is not null)
            {
                return new U2AniDbResolution(
                    details,
                    season,
                    "u2_anidb_title_cache",
                    attempted.Concat(result.AttemptedTitles).Distinct(StringComparer.Ordinal).ToArray(),
                    null);
            }
        }

        return new U2AniDbResolution(
            null,
            null,
            "u2_anidb_title_cache",
            attempted.Distinct(StringComparer.Ordinal).ToArray(),
            "anidb_title_tmdb_search_not_matched");
    }

    private async Task<U2AniDbResolution> ResolveSeriesIdAsync(
        int seriesId,
        string taskTitle,
        string strategy,
        CancellationToken cancellationToken)
    {
        var details = await GetSeriesDetailsAsync(seriesId, cancellationToken).ConfigureAwait(false);
        var season = details is null ? null : SelectSeason(details, taskTitle);
        if (details is not null && season is not null)
        {
            season = await GetSeasonAsync(seriesId, season.SeasonNumber, cancellationToken)
                .ConfigureAwait(false);
        }

        return details is not null && season is not null
            ? new U2AniDbResolution(details, season, strategy, [], null)
            : new U2AniDbResolution(null, null, strategy, [], "anidb_tmdb_mapping_validation_failed");
    }

    private async Task<int?> TryReadMappingAsync(
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
            if (!document.RootElement.TryGetProperty("tmdbtv", out var value))
            {
                return null;
            }

            var text = value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : value.GetRawText();
            return int.TryParse(
                text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var id) && id > 0
                    ? id
                    : null;
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
        if ((!IsValidSeason(season, seriesId, seasonNumber)) && tmdb is ITmdbRefreshClient refresh)
        {
            season = await refresh.RefreshSeasonAsync(seriesId, seasonNumber, cancellationToken).ConfigureAwait(false);
        }
        return IsValidSeason(season, seriesId, seasonNumber) ? season : null;
    }

    private static TmdbSeason? SelectSeason(TmdbSeriesDetails details, string title)
    {
        var explicitSeason = TmdbSeasonFallbackSelector.SelectTitleSeason(title, details.Seasons);
        if (explicitSeason is not null && explicitSeason.SeasonNumber > 0)
        {
            return explicitSeason;
        }

        var regular = details.Seasons
            .Where(value => value.SeasonNumber > 0
                && !string.Equals(value.Name, "Specials", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return regular.Length == 1 ? regular[0] : null;
    }

    private static bool IsValidSeason(TmdbSeason? season, int seriesId, int seasonNumber) =>
        season is not null
        && season.Id > 0
        && season.SeriesId == seriesId
        && season.SeasonNumber == seasonNumber;
}
