namespace AnimeGoNet.Data.Downloads;

public sealed record DownloadPreparationFile(
    string FileId,
    string RelativePath,
    long SizeBytes,
    string Disposition);

public sealed record DownloadPreparationClaim(
    string JobId,
    string TaskId,
    string DownloaderId,
    string InfoHash,
    string LeaseToken,
    int AttemptCount,
    IReadOnlyList<DownloadPreparationFile> Files);

public sealed record DownloadFileAssignment(
    string FileId,
    int DownloadFileIndex,
    int Priority,
    bool Wanted);
