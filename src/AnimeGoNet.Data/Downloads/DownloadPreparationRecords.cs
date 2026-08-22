namespace AnimeGoNet.Data.Downloads;

public sealed record DownloadPreparationFile(
    string FileId,
    string RelativePath,
    long SizeBytes,
    string Disposition,
    string? OtherReason);

public sealed record DownloadPreparationClaim(
    string JobId,
    string TaskId,
    string DownloaderId,
    string InfoHash,
    string LeaseToken,
    int AttemptCount,
    string? DynamicTagTemplate,
    DateOnly? DynamicTagAirDate,
    int? DynamicTagEpisodeNumber,
    IReadOnlyList<DownloadPreparationFile> Files);

public sealed record DownloadFileAssignment(
    string FileId,
    int DownloadFileIndex,
    int Priority,
    bool Wanted);

public sealed record DownloadDynamicTagAssignment(
    IReadOnlyList<string> Tags,
    string State,
    string? FailureCode);
