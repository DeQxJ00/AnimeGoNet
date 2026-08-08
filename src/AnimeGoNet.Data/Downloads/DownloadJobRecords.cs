namespace AnimeGoNet.Data.Downloads;

public sealed record DownloadJobListItemRecord(
    string JobId,
    string TaskId,
    string Title,
    string SourceId,
    string DownloaderId,
    string InfoHash,
    string State,
    string BusinessStatus,
    double Progress,
    long DownloadedBytes,
    long TotalBytes,
    long SpeedBytesPerSecond,
    long? EtaSeconds,
    int Seeds,
    int Peers,
    string SeedingState,
    int SeedingTargetMinutes,
    long SeedingElapsedSeconds,
    DateTimeOffset? SeedingCompletedAtUtc,
    IReadOnlyList<string> DynamicTags,
    string DynamicTagState,
    string? DynamicTagFailureCode,
    bool IsStale,
    long Revision,
    DateTimeOffset? SnapshotAtUtc,
    DateTimeOffset UpdatedAtUtc,
    bool DownloaderConnected,
    string? DownloaderFailureCode,
    DateTimeOffset? DownloaderLastSuccessAtUtc);

public sealed record DownloadSyncResult(int ActiveJobs, int MatchedJobs);

public sealed record DownloadJobListQuery(
    int Page,
    int PageSize,
    string? Search,
    string? State,
    string? BusinessStatus,
    string? DownloaderId,
    string? SourceId);

public sealed record DownloadJobListPage(
    int Page,
    int PageSize,
    int TotalItems,
    DownloadJobDashboardSummary Summary,
    IReadOnlyList<DownloadJobListItemRecord> Items);

public sealed record DownloadJobDashboardSummary(
    int TotalJobs,
    int ActiveJobs,
    int PausedJobs,
    int FailedJobs,
    int StaleJobs,
    int WaitingOrganizationJobs,
    int CompletedJobs,
    int PreparationFailedJobs,
    int OrganizationFailedJobs,
    long ConnectedDownloadSpeedBytesPerSecond,
    int OfflineInstanceCount,
    string? LatestFailureCode,
    DateTimeOffset? LastDownloaderSuccessAtUtc);

public sealed record DownloadJobFileRecord(
    string RelativePath,
    long SizeBytes,
    int? DownloadFileIndex,
    int? Priority,
    bool? Wanted,
    string Disposition,
    string? OtherReason);

public sealed record DownloadJobEventRecord(
    string EventId,
    string Kind,
    string Result,
    string? FromState,
    string? ToState,
    string? FailureCode,
    DateTimeOffset CreatedAtUtc);

public sealed record DownloadJobDetailRecord(
    DownloadJobListItemRecord Summary,
    string? TaskFailureKind,
    string? TaskFailureReason,
    string PreparationState,
    int PreparationAttemptCount,
    DateTimeOffset? PreparationNextAttemptAtUtc,
    string? PreparationFailureCode,
    string OrganizationState,
    int OrganizationAttemptCount,
    DateTimeOffset? OrganizationNextAttemptAtUtc,
    string? OrganizationFailureCode,
    string OrganizationPhase,
    int OrganizationCompletedUnits,
    int OrganizationTotalUnits,
    IReadOnlyList<DownloadJobFileRecord> Files,
    IReadOnlyList<DownloadJobEventRecord> Events);

public sealed record DownloadJobControlTarget(
    string JobId,
    string TaskId,
    string DownloaderId,
    string InfoHash,
    string State,
    string BusinessStatus,
    long Revision,
    string PreparationState,
    string? PreparationLeaseToken,
    string? PreparationFailureCode,
    string OrganizationState,
    string? OrganizationLeaseToken,
    string? OrganizationFailureCode);

public enum DownloadJobControlUpdateResult
{
    Updated,
    NotFound,
    RevisionConflict,
    InvalidState,
}
