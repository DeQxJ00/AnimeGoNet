namespace AnimeGoNet.Core.Configuration;

public sealed record AnimeGoOptions
{
    public required PathOptions Paths { get; init; }

    public required IReadOnlyDictionary<string, QbittorrentInstanceOptions> Downloaders { get; init; }

    public required MetadataMatchingOptions Metadata { get; init; }

    public required TorrentFetchOptions TorrentFetch { get; init; }

    public required IReadOnlyList<SourceProfileSeed> InitialSourceProfiles { get; init; }
}

public sealed record TorrentFetchOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    public long MaxResponseBytes { get; init; } = 16 * 1024 * 1024;

    public int MaxRedirects { get; init; } = 3;

    public TimeSpan StagingTtl { get; init; } = TimeSpan.FromMinutes(15);
}

public sealed record PathOptions
{
    public required string DataPath { get; init; }

    public required string DownloadPath { get; init; }

    public required string SavePath { get; init; }
}

public sealed record QbittorrentInstanceOptions
{
    public string Type { get; init; } = DownloaderTypes.Qbittorrent;

    public required Uri BaseUrl { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }

    public required string DownloadPath { get; init; }

    public bool Enabled { get; init; } = true;
}

public sealed record MetadataMatchingOptions
{
    public required SeasonFailureOptions SeasonFailure { get; init; }

    public required AiMatchingOptions Ai { get; init; }

    public bool TmdbFailureUseBangumi { get; init; }

    public bool MikanTrustedOffsetCacheEnabled { get; init; }
}

public sealed record SeasonFailureOptions
{
    public bool Skip { get; init; }

    public bool Backtrace { get; init; }

    public bool UseTitleSeason { get; init; }

    public bool UseFirstSeason { get; init; }
}

public sealed record AiMatchingOptions
{
    public bool UseSeasonMatch { get; init; }

    public bool UseEpisodeMatch { get; init; }

    public TimeSpan HttpTimeout { get; init; } = TimeSpan.FromSeconds(600);
}

public sealed record SourceProfileSeed
{
    public required string Id { get; init; }

    public required string Adapter { get; init; }

    public required string DownloaderId { get; init; }

    public required FileStrategy FileStrategy { get; init; }

    public required IReadOnlyList<string> AllowedTorrentHosts { get; init; }

    public bool RssFilterEnabled { get; init; }

    public bool RssPriorityEnabled { get; init; }
}

public enum FileStrategy
{
    Link = 1,
    LinkDelete = 2,
    Move = 3,
    WaitMove = 4,
}

public static class DownloaderTypes
{
    public const string Qbittorrent = "qbittorrent";
}
