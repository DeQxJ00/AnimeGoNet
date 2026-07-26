namespace AnimeGoNet.Data.Sources;

public sealed record SourceProfileRecord(
    string Id,
    string Adapter,
    string DownloaderId,
    string FileStrategy,
    IReadOnlyList<string> AllowedTorrentHosts,
    bool RssFilterEnabled,
    bool RssPriorityEnabled,
    long Revision);

public sealed record SourceProfileAdminRecord(
    string Id,
    string DisplayName,
    string Adapter,
    string DownloaderId,
    string FileStrategy,
    IReadOnlyList<string> AllowedTorrentHosts,
    bool RssFilterEnabled,
    bool RssPriorityEnabled,
    bool Enabled,
    long Revision,
    long IngestTaskCount,
    long RssBatchCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record SourceProfileDefinition(
    string DisplayName,
    string Adapter,
    string DownloaderId,
    string FileStrategy,
    IReadOnlyList<string> AllowedTorrentHosts,
    bool RssFilterEnabled,
    bool RssPriorityEnabled,
    bool Enabled);

public sealed class SourceProfileRevisionException : InvalidOperationException;

public sealed class SourceProfileConflictException(string message) : InvalidOperationException(message);

public sealed class SourceProfileDuplicateException : InvalidOperationException;
