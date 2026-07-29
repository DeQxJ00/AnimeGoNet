namespace AnimeGoNet.Data.Library;

public enum DirectoryDatabaseEntryKind
{
    Anime,
    Season,
    Episode,
}

public sealed record DirectoryDatabaseEntry(
    string RelativePath,
    DirectoryDatabaseEntryKind Kind,
    string InfoHash,
    string AnimeName,
    long CreateAtUnix,
    long UpdateAtUnix,
    int? SeasonNumber = null,
    int? EpisodeType = null,
    int? EpisodeNumber = null,
    bool? Seeded = null,
    bool? Downloaded = null,
    bool? Renamed = null,
    bool? Scraped = null);

public sealed record DirectoryDatabaseIssue(
    string RelativePath,
    string ErrorCode);

public sealed record DirectoryDatabaseScanResult(
    int ScannedCount,
    IReadOnlyList<DirectoryDatabaseEntry> Entries,
    IReadOnlyList<DirectoryDatabaseIssue> Issues);

public sealed record DirectoryDatabaseRefreshResult(
    string RunId,
    int ScannedCount,
    int IndexedCount,
    int RejectedCount);

public sealed record DirectoryDatabaseStatus(
    int EntryCount,
    string? LastRunId,
    string? LastRunStatus,
    int LastScannedCount,
    int LastIndexedCount,
    int LastRejectedCount,
    string? LastFailureCode,
    DateTimeOffset? LastStartedAtUtc,
    DateTimeOffset? LastCompletedAtUtc);

public sealed record DirectoryDatabaseWriteRequest(
    string SaveRootPath,
    string InfoHash,
    string AnimeName,
    int SeasonNumber,
    IReadOnlyList<DirectoryDatabaseEpisodeWrite> Episodes,
    bool Seeded = false);

public sealed record DirectoryDatabaseEpisodeWrite(
    string MediaPath,
    int EpisodeType,
    int EpisodeNumber);
