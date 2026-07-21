namespace AnimeGoNet.Data.Library;

public enum MediaOrganizationStage
{
    MoveFiles,
    CleanupDownloader,
}

public sealed record MediaOrganizationFile(
    string TaskFileId,
    string RelativePath,
    long SizeBytes,
    string Disposition,
    int TmdbSeriesId,
    int SeasonNumber,
    int? EpisodeNumber,
    string CanonicalSeriesName);

public sealed record MediaOrganizationClaim(
    string JobId,
    string TaskId,
    string DownloaderId,
    string InfoHash,
    string DownloadRootPath,
    string SaveRootPath,
    string SourceId,
    string? SourceItemId,
    int? BangumiSubjectId,
    string LeaseToken,
    int AttemptCount,
    MediaOrganizationStage Stage,
    IReadOnlyList<MediaOrganizationFile> Files);

public sealed record MediaOperationPlan(
    string TaskFileId,
    string SourcePath,
    string TargetPath);

public sealed record MediaOperationRecord(
    string OperationId,
    string TaskFileId,
    string SourcePath,
    string TargetPath,
    string State,
    long BytesVerified);
