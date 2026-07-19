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
