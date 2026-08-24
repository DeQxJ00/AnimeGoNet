namespace AnimeGoNet.Data.Library;

public enum AnimeLibrarySort
{
    LastUpdated = 1,
    Name = 2,
    AirDate = 3,
    AddedAt = 4,
    EpisodeChangedAt = 5,
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
    AnimeLibrarySortDirection Direction = AnimeLibrarySortDirection.Descending,
    string? Search = null);

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
    DateTimeOffset? LastEpisodeChangedAt,
    string ResourceRevision,
    int EpisodeTotal,
    int EpisodeSnapshotCount,
    int EpisodeDownloaded,
    string? SeriesResolutionSource,
    string? SeriesResolutionRunId,
    string? SeriesResolutionAttemptId,
    string? SeasonResolutionSource,
    string? SeasonResolutionRunId,
    string? SeasonResolutionAttemptId,
    string ValidationStatus,
    string? LastResolutionRunId,
    IReadOnlyList<string> Warnings);

public sealed record AnimeSeasonListPage(
    int Page,
    int PageSize,
    int TotalItems,
    IReadOnlyList<AnimeSeasonListProjection> Items);

public sealed record AnimeMovieListProjection(
    int TmdbMovieId,
    string Title,
    string OriginalTitle,
    string? PosterPath,
    DateOnly? ReleaseDate,
    DateTimeOffset AddedAt,
    DateTimeOffset LastUpdatedAt,
    bool Completed,
    string? DownloadSourceId,
    DateTimeOffset? CompletedAtUtc,
    bool MediaPathKnown);

public sealed record AnimeMovieListPage(
    int Page,
    int PageSize,
    int TotalItems,
    IReadOnlyList<AnimeMovieListProjection> Items);

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
    bool MediaPathKnown,
    int? GroupId,
    string? GroupName);

public sealed record AnimeSeasonManualOffsetProjection(
    int MikanId,
    int? BangumiSubjectId,
    int? TmdbSeriesId,
    int? TmdbSeasonNumber,
    int EpisodeOffset,
    bool Enabled,
    long Revision,
    DateTimeOffset UpdatedAtUtc);

public sealed record AnimeSeasonRelatedTaskProjection(
    string TaskId,
    string Title,
    string SourceId,
    string Status,
    int? MikanId,
    int? GroupId,
    int? BangumiSubjectId,
    int? LatestRunAttemptNumber,
    string? LatestRunStatus,
    DateTimeOffset UpdatedAtUtc);

public sealed record AnimeSeasonMikanBindingProjection(
    string SourceProfileId,
    int MikanId,
    int? GroupId,
    DateTimeOffset LastUsedAtUtc);

public sealed record AnimeSeasonResolutionAttemptProjection(
    string TaskId,
    string TaskTitle,
    int RunAttemptNumber,
    string RunStatus,
    string Stage,
    string Strategy,
    int? Priority,
    string Result,
    string? ErrorCode,
    string? Reason,
    bool Retryable,
    int AttemptNumber,
    long DurationMilliseconds,
    DateTimeOffset CreatedAtUtc);

public sealed record AnimeSeasonAuditProjection(
    IReadOnlyList<AnimeSeasonManualOffsetProjection> ManualOffsets,
    IReadOnlyList<AnimeSeasonMikanBindingProjection> MikanBindings,
    int RelatedTaskTotal,
    bool RelatedTasksTruncated,
    IReadOnlyList<AnimeSeasonRelatedTaskProjection> RelatedTasks,
    int ResolutionAttemptTotal,
    bool ResolutionAttemptsTruncated,
    IReadOnlyList<AnimeSeasonResolutionAttemptProjection> ResolutionAttempts);

public sealed record AnimeSeasonDetailProjection(
    AnimeSeasonListProjection Season,
    IReadOnlyList<AnimeEpisodeProjection> Episodes,
    AnimeSeasonAuditProjection Audit);

public sealed record AnimePosterProjection(
    string? PosterPath,
    string Source);
