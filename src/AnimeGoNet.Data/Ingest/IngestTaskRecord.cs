namespace AnimeGoNet.Data.Ingest;

public sealed record IngestTaskRecord(
    string Id,
    string SourceProfileId,
    long SourceProfileRevision,
    string DownloaderId,
    string Status);
