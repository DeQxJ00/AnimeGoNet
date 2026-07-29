namespace AnimeGoNet.Data.Library;

public enum AnimeLibrarySort
{
    LastUpdated = 1,
    Name = 2,
    AirDate = 3,
    AddedAt = 4,
}

public enum AnimeLibrarySortDirection
{
    Ascending = 1,
    Descending = 2,
}

public sealed record AnimeSeasonListQuery(
    int Page = 1,
    int PageSize = 24,
    AnimeLibrarySort Sort = AnimeLibrarySort.LastUpdated,
    AnimeLibrarySortDirection Direction = AnimeLibrarySortDirection.Descending);

public sealed record AnimeSeasonListProjection(
    int TmdbSeriesId,
    int TmdbSeasonNumber,
    string DisplayName,
    string SortName,
    string SeasonName,
    string? SeriesPosterPath,
    string? SeasonPosterPath,
    DateOnly? AirDate,
    DateTimeOffset AddedAt,
    DateTimeOffset LastUpdatedAt,
    int EpisodeTotal,
    int EpisodeSnapshotCount,
    int EpisodeDownloaded,
    string? SeriesResolutionSource,
    string? SeasonResolutionSource,
    string ValidationStatus,
    string? LastResolutionRunId,
    IReadOnlyList<string> Warnings);

public sealed record AnimeSeasonListPage(
    int Page,
    int PageSize,
    int TotalItems,
    IReadOnlyList<AnimeSeasonListProjection> Items);

public sealed record AnimeEpisodeProjection(
    int TmdbEpisodeId,
    int EpisodeNumber,
    string? Name,
    DateOnly? AirDate,
    int? RuntimeMinutes,
    DateTimeOffset FetchedAtUtc,
    bool Downloaded,
    string? DownloadSourceId,
    DateTimeOffset? DownloadedAtUtc,
    bool MediaPathKnown);

public sealed record AnimeSeasonDetailProjection(
    AnimeSeasonListProjection Season,
    IReadOnlyList<AnimeEpisodeProjection> Episodes);
