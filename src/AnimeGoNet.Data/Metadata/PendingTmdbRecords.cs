namespace AnimeGoNet.Data.Metadata;

public sealed record PendingTmdbSeriesSummary(
    string SeriesRowId,
    int BangumiSubjectId,
    string CanonicalName,
    IReadOnlyList<int> SeasonNumbers,
    int TaskCount,
    int ProcessedFileCount,
    int CompletionRecordCount,
    int ActiveClaimCount,
    int CompletedClaimCount,
    int DuplicateFileCount,
    string? LatestFailureKind,
    string? LatestFailureReason,
    DateTimeOffset UpdatedAtUtc);

public sealed record PendingTmdbTaskProjection(
    string TaskId,
    string Title,
    string SourceId,
    string Status,
    int? SeasonNumber,
    int OtherFileCount,
    int DuplicateFileCount,
    string? FailureKind,
    string? FailureReason,
    DateTimeOffset UpdatedAtUtc);

public sealed record PendingTmdbScopeProjection(
    string Kind,
    string Key,
    string State,
    string SourceId,
    string? SourceEpisode,
    DateTimeOffset? CompletedAtUtc);

public sealed record PendingTmdbSeriesDetail(
    PendingTmdbSeriesSummary Summary,
    IReadOnlyList<PendingTmdbTaskProjection> Tasks,
    IReadOnlyList<PendingTmdbScopeProjection> Scopes);
