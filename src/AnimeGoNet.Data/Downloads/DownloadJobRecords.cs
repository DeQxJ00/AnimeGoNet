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
    bool IsStale,
    long Revision,
    DateTimeOffset? SnapshotAtUtc,
    DateTimeOffset UpdatedAtUtc,
    bool DownloaderConnected,
    string? DownloaderFailureCode,
    DateTimeOffset? DownloaderLastSuccessAtUtc);

public sealed record DownloadSyncResult(int ActiveJobs, int MatchedJobs);
