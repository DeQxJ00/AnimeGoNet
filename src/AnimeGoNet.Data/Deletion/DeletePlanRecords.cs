namespace AnimeGoNet.Data.Deletion;

public static class DeleteItemKinds
{
    public const string BusinessRecord = "business_record";
    public const string DownloaderTask = "downloader_task";
    public const string SourceFile = "source_file";
    public const string MediaFile = "media_file";
}

public sealed record DeleteSelection(
    bool DeleteBusinessRecord,
    bool DeleteDownloaderTask,
    bool DeleteSourceFiles,
    bool DeleteMediaFiles)
{
    public bool Any =>
        DeleteBusinessRecord || DeleteDownloaderTask || DeleteSourceFiles || DeleteMediaFiles;

    internal bool Includes(string itemKind) => itemKind switch
    {
        DeleteItemKinds.BusinessRecord => DeleteBusinessRecord,
        DeleteItemKinds.DownloaderTask => DeleteDownloaderTask,
        DeleteItemKinds.SourceFile => DeleteSourceFiles,
        DeleteItemKinds.MediaFile => DeleteMediaFiles,
        _ => false,
    };
}

public sealed record DeletePlanTarget(
    string ItemKind,
    string TargetKey,
    string? RootPath,
    string? DownloaderId,
    string DisplayValue);

public sealed record DeletePlanPreview(
    string TaskId,
    string TaskTitle,
    string TaskStatus,
    string Fingerprint,
    IReadOnlyList<DeletePlanTarget> BusinessRecords,
    IReadOnlyList<DeletePlanTarget> DownloaderTasks,
    IReadOnlyList<DeletePlanTarget> SourceFiles,
    IReadOnlyList<DeletePlanTarget> MediaFiles)
{
    public IReadOnlyList<DeletePlanTarget> AllTargets =>
        [.. BusinessRecords, .. DownloaderTasks, .. SourceFiles, .. MediaFiles];
}

public sealed record DeleteExecutionPlan(
    string ExecutionId,
    string TaskId,
    string Fingerprint,
    DeleteSelection Selection,
    string State,
    IReadOnlyList<DeletePlanTarget> Targets,
    DateTimeOffset CreatedAtUtc);
