using AnimeGoNet.Core.Diagnostics;

namespace AnimeGoNet.Core.Torrents;

public sealed record TorrentMetadata(
    string Name,
    string InfoHash,
    long TotalSize,
    IReadOnlyList<TorrentFile> Files);

public sealed record TorrentFile(string RelativePath, long Size, bool IsPadding);

public sealed record TorrentMetainfoLimits
{
    public int MaxDepth { get; init; } = 64;

    public int MaxFiles { get; init; } = 10_000;

    public int MaxPathComponents { get; init; } = 64;

    public long MaxTotalSize { get; init; } = 16L * 1024 * 1024 * 1024 * 1024;
}

public sealed class TorrentMetainfoException : FormatException, IStableError
{
    public const string StableCode = "torrent_metainfo_invalid";

    public TorrentMetainfoException(string message)
        : base(message)
    {
    }

    public TorrentMetainfoException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public string Code { get; } = StableCode;

    public StableErrorSemantic Semantics => StableErrorSemantic.ParseFailed;
}
