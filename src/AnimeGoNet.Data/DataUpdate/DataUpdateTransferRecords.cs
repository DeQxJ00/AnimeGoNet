namespace AnimeGoNet.Data.DataUpdate;

public static class DataUpdateTriggerKinds
{
    public const string Manual = "manual";
    public const string Scheduled = "scheduled";
}

public static class DataUpdateActions
{
    public const string Check = "check";
    public const string Download = "download";
    public const string DownloadImport = "download_import";
}

public static class DataUpdateTransferStatuses
{
    public const string Checking = "checking";
    public const string UpdateAvailable = "update_available";
    public const string UpToDate = "up_to_date";
    public const string Downloading = "downloading";
    public const string Downloaded = "downloaded";
    public const string Importing = "importing";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public sealed record DataUpdateTransferRun(
    string RunId,
    string TriggerKind,
    string RequestedAction,
    string Status,
    string? DataVersion,
    string? ManifestSha256,
    string? FailureCode,
    long DownloadedBytes,
    long TotalBytes,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record DownloadedDataPackage(
    string DataVersion,
    string ManifestSha256,
    string RelativeDirectory,
    string State,
    DateTimeOffset DownloadedAtUtc,
    DateTimeOffset? ImportedAtUtc);
