using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.Data.Metadata;

public sealed record MetadataTaskClaim(
    string RunId,
    string TaskId,
    string Title,
    int? MikanId,
    int? GroupId,
    int? BangumiSubjectId,
    int AttemptNumber,
    string LeaseToken,
    int? AniDbAnimeId = null,
    string? ImdbTitleId = null,
    IReadOnlyList<MetadataTaskFileProjection>? Files = null,
    string? SourceAdapter = null,
    string? SourcePublishedAtRaw = null,
    DateTimeOffset? SourcePublishedAt = null,
    int TorrentFileCount = 0);

public sealed record MetadataAttempt(
    string Stage,
    string Strategy,
    int? Priority,
    string Result,
    string? ErrorCode,
    bool Retryable,
    int AttemptNumber,
    long DurationMilliseconds,
    string? Reason = null);

public sealed record MetadataAttemptProjection(
    string AttemptId,
    string RunId,
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
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset RunStartedAtUtc,
    DateTimeOffset? RunCompletedAtUtc);

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

public enum MetadataRetryResult
{
    Retried,
    NotFound,
    InvalidState,
    ActiveLease,
}

public sealed record MetadataTaskFileProjection(
    string FileId,
    string RelativePath,
    long SizeBytes,
    string? SourceEpisode,
    string? FileEpisodeCandidate,
    int? PreResolvedEpisodeNumber = null,
    string? PreResolvedOtherReason = null,
    int? TmdbSeasonNumber = null);

public sealed record MetadataEpisodeTaskClaim(
    MetadataTaskClaim Resolution,
    int TmdbSeriesId,
    int TmdbSeasonNumber,
    IReadOnlyList<MetadataTaskFileProjection> Files,
    bool SeasonResolvedByAi = false,
    bool HasMultipleSeasons = false,
    bool EpisodeResolvedByTrustedOffset = false,
    bool AiMetadataAttempted = false);

public sealed record MetadataCanonicalSeason(
    TmdbSeries Series,
    TmdbSeason Season);

public sealed record MetadataSeasonFileSeed(
    string RelativePath,
    int? EpisodeNumber,
    string? OtherReason,
    int? SeasonNumber = null);

public sealed record MetadataEpisodeFileResolution(
    string FileId,
    TmdbEpisode? Episode,
    string Disposition,
    string? OtherReason,
    string? AssociatedFileId = null,
    string? RenameSuffix = null,
    int? TrustedEpisodeNumber = null)
{
    public int? ResolvedEpisodeNumber => Episode?.EpisodeNumber ?? TrustedEpisodeNumber;
}

public sealed record MetadataTaskListProjection(
    string TaskId,
    string Title,
    string SourceId,
    string Status,
    int? MikanId,
    int? BangumiSubjectId,
    int? TmdbSeriesId,
    int? TmdbSeasonNumber,
    string? SeriesStrategy,
    string? SeasonStrategy,
    string? EpisodeStrategy,
    string? FailureKind,
    string? FailureReason,
    int EpisodeFileCount,
    int OtherFileCount,
    int DuplicateFileCount,
    int PendingFileCount,
    DateTimeOffset UpdatedAtUtc);

public enum MikanWorkImpactCategory
{
    Future,
    RetryableFailed,
    Active,
    ResolvedProtected,
    CompletedProtected,
    Other,
}

public sealed record MikanWorkImpactTaskProjection(
    string TaskId,
    string Title,
    string SourceId,
    string Status,
    int? BangumiSubjectId,
    int? TmdbSeriesId,
    int? TmdbSeasonNumber,
    string? OrganizationState,
    MikanWorkImpactCategory Category,
    DateTimeOffset UpdatedAtUtc);

public sealed record MikanWorkImpactProjection(
    int MikanId,
    int TotalTaskCount,
    int FutureTaskCount,
    int RetryableFailedTaskCount,
    int ActiveTaskCount,
    int ResolvedProtectedTaskCount,
    int CompletedProtectedTaskCount,
    int OtherTaskCount,
    bool IsTruncated,
    IReadOnlyList<MikanWorkImpactTaskProjection> Tasks);

public sealed class MikanWorkRuleRematchRevisionException(
    int mikanId,
    long expectedRevision,
    long actualRevision) : Exception(
        $"Mikan work rule {mikanId} revision changed from {expectedRevision} to {actualRevision}.");
