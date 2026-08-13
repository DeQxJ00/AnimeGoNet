namespace AnimeGoNet.Data.Library;

public enum MediaOrganizationStage
{
    MoveFiles,
    CleanupDownloader,
}

public static class MediaOrganizationPhases
{
    public const string NotStarted = "not_started";
    public const string RenamePlanning = "rename_planning";
    public const string MediaTransfer = "media_transfer";
    public const string SubtitleTransfer = "subtitle_transfer";
    public const string NfoWrite = "nfo_write";
    public const string DirectoryIndex = "directory_index";
    public const string CleanupDownloader = "cleanup_downloader";
    public const string Completed = "completed";

    public static bool IsMovePhase(string phase) => phase is
        RenamePlanning or MediaTransfer or SubtitleTransfer or NfoWrite or DirectoryIndex;
}

public sealed record MediaOrganizationFile(
    string TaskFileId,
    string RelativePath,
    long SizeBytes,
    string Disposition,
    int TmdbSeriesId,
    int SeasonNumber,
    int? EpisodeNumber,
    string CanonicalSeriesName,
    string? RenameSuffix,
    string? AssociatedFileId,
    string? SourceEpisode = null,
    string? SourceOverridePath = null);

public sealed record MediaOrganizationClaim(
    string JobId,
    string TaskId,
    string DownloaderId,
    string InfoHash,
    string FileStrategy,
    string DownloadRootPath,
    string SaveRootPath,
    string SourceId,
    string? SourceItemId,
    int? BangumiSubjectId,
    string LeaseToken,
    int AttemptCount,
    MediaOrganizationStage Stage,
    IReadOnlyList<MediaOrganizationFile> Files,
    string? SourceWorkId = null,
    int? MikanId = null,
    bool IsOtherReadaptation = false);

public sealed record MediaOperationPlan(
    string TaskFileId,
    string SourcePath,
    string TargetPath);

public sealed record MediaOperationRecord(
    string OperationId,
    string TaskFileId,
    string Strategy,
    string SourcePath,
    string TargetPath,
    string State,
    long BytesVerified);
