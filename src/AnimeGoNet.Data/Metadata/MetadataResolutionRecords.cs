using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.Data.Metadata;

public sealed record MetadataTaskClaim(
    string RunId,
    string TaskId,
    string Title,
    int? BangumiSubjectId,
    int AttemptNumber,
    string LeaseToken);

public sealed record MetadataAttempt(
    string Stage,
    string Strategy,
    int? Priority,
    string Result,
    string? ErrorCode,
    bool Retryable,
    int AttemptNumber,
    long DurationMilliseconds);

public sealed record MetadataRunProjection(
    string RunId,
    string TaskId,
    string Status,
    int AttemptNumber,
    int? TmdbSeriesId,
    int? TmdbSeasonNumber,
    bool TmdbAccessConfirmed,
    MetadataFailureKind? FailureKind,
    bool FallbackEligible,
    string? FallbackDenialReason);
