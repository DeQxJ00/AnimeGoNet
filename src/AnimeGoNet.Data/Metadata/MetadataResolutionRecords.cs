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
    int TorrentFileCount = 0,
    string? SourceProfileId = null,
    string? SourceId = null,
    bool DuplicateNotificationEnabled = true,
    bool IsForcedReadaptation = false,
    string MediaType = "tv",
    bool PreferAniDbTmdbMapping = false,
    string? AniDbTmdbMappingUrlTemplate = null);

public sealed record MetadataAttempt(
    string Stage,
    string Strategy,
    int? Priority,
    string Result,
    string? ErrorCode,
    bool Retryable,
    int AttemptNumber,
    long DurationMilliseconds,
    string? Reason = null,
    AiMetadataProviderUsage? AiUsage = null,
    string? AiTriggerReason = null);

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
    DateTimeOffset? RunCompletedAtUtc,
    AiMetadataProviderUsage? AiUsage);

public sealed record MetadataAiInvocationLogFilter(
    int Page,
    int PageSize,
    string? Search = null,
    string? Stage = null,
    string? Result = null,
    string? Model = null,
    string? ErrorCategory = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null);

public sealed record MetadataAiInvocationLogProjection(
    string AttemptId,
    string RunId,
    string TaskId,
    string Title,
    string SourceId,
    int? MikanId,
    int? BangumiSubjectId,
    int? TmdbSeriesId,
    int? TmdbSeasonNumber,
    string RunStatus,
    string Stage,
    string Strategy,
    string Result,
    string? ErrorCode,
    string ErrorCategory,
    string? AiTriggerReason,
    string? Reason,
    bool Retryable,
    long DurationMilliseconds,
    DateTimeOffset CreatedAtUtc,
    AiMetadataProviderUsage Usage,
    IReadOnlyList<MetadataAiValidatedEpisodeProjection> ValidatedEpisodes);

public sealed record MetadataAiValidatedEpisodeProjection(
    int TmdbSeriesId,
    int TmdbSeasonNumber,
    int TmdbEpisodeNumber,
    string? EpisodeName);

public sealed record MetadataAiInvocationLogSummary(
    int TotalItems,
    int MatchedItems,
    int FailedItems,
    int OutputFormatFailedItems,
    long PromptTokens,
    long CompletionTokens,
    long TotalTokens,
    long RequestCount,
    long ToolCallCount);

public sealed record MetadataAiInvocationLogPage(
    MetadataAiInvocationLogFilter Filter,
    MetadataAiInvocationLogSummary Summary,
    IReadOnlyList<MetadataAiInvocationLogProjection> Items);

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
    bool AiMetadataAttempted = false,
    bool IsOtherReadaptation = false);

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
    int? TrustedEpisodeNumber = null,
    TmdbResolutionSource? ResolutionSource = null,
    string? ResolutionAttemptId = null)
{
    public int? ResolvedEpisodeNumber => Episode?.EpisodeNumber ?? TrustedEpisodeNumber;
}

public sealed record MetadataDuplicateHit(
    long TmdbSeriesId,
    int TmdbSeasonNumber,
    int TmdbEpisodeNumber,
    string Reason);

public sealed record MetadataEpisodeCompletionResult(
    IReadOnlyList<MetadataDuplicateHit> DuplicateHits);

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
    string? FailureStage,
    string? FailureCode,
    bool? FailureRetryable,
    string? LatestRunStatus,
    bool? TmdbAccessConfirmed,
    bool? BangumiFallbackEligible,
    string? BangumiFallbackDenialReason,
    string HandlingCategory,
    IReadOnlyList<int> EpisodeNumbers,
    int EpisodeFileCount,
    int MovieFileCount,
    int OtherFileCount,
    int DuplicateFileCount,
    int PendingFileCount,
    DateTimeOffset UpdatedAtUtc,
    string ReadaptationReviewState,
    string? ReviewKind,
    TmdbResolutionEvidence? SeriesResolution = null,
    TmdbResolutionEvidence? SeasonResolution = null,
    TmdbResolutionEvidence? EpisodeResolution = null,
    bool EpisodeResolutionMixed = false);

public sealed record MetadataTaskAttentionSummary(
    int OtherTaskCount,
    int FailedTaskCount,
    int ReviewPendingTaskCount);

public sealed record MetadataTaskFileDetailProjection(
    string RelativePath,
    long SizeBytes,
    string? SourceEpisode,
    string? FileEpisodeCandidate,
    string Disposition,
    string? OtherReason,
    int? TmdbSeriesId,
    string? TmdbSeriesName,
    int? TmdbSeasonNumber,
    string? TmdbSeasonName,
    int? TmdbEpisodeNumber,
    string? TmdbEpisodeName,
    TmdbResolutionEvidence? EpisodeResolution = null);

public sealed record MetadataTaskAiProjection(
    string Stage,
    string Result,
    string? ErrorCode,
    string? Reason,
    long DurationMilliseconds,
    DateTimeOffset AttemptedAtUtc,
    AiMetadataProviderUsage? Usage);

public sealed record MetadataTaskSourceProjection(
    string SourceProfileId,
    long SourceProfileRevision,
    string SourceId,
    string SourceTitle,
    string? SourceItemIdFingerprint,
    string? SourceWorkIdFingerprint,
    int? MikanId,
    int? GroupId,
    int? BangumiSubjectId,
    int? AniDbAnimeId,
    string? ImdbTitleId,
    bool SourcePublishedAtRawAvailable,
    DateTimeOffset? SourcePublishedAt);

public sealed record MetadataTaskDetailProjection(
    MetadataTaskListProjection Summary,
    MetadataTaskSourceProjection Source,
    MetadataTaskAiProjection? Ai,
    IReadOnlyList<MetadataTaskFileDetailProjection> Files);

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
