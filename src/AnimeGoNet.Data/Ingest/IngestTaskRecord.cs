namespace AnimeGoNet.Data.Ingest;

public sealed record IngestTaskRecord(
    string Id,
    string SourceProfileId,
    long SourceProfileRevision,
    string DownloaderId,
    string Status);

public sealed record StagedIngestTaskRecord(
    string Id,
    string SourceProfileId,
    long SourceProfileRevision,
    string DownloaderId,
    string Status,
    string InfoHash,
    int FileCount);

public sealed record ExpiredStagedTorrentRecord(string TaskId, string StagingFileName);
