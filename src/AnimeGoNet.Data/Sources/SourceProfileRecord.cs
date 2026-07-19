namespace AnimeGoNet.Data.Sources;

public sealed record SourceProfileRecord(
    string Id,
    string Adapter,
    string DownloaderId,
    string FileStrategy,
    bool RssFilterEnabled,
    bool RssPriorityEnabled,
    long Revision);
