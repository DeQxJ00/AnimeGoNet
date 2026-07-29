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
    public required TmdbClientOptions Tmdb { get; init; }

    public required BangumiClientOptions Bangumi { get; init; }

    public required SeasonFailureOptions SeasonFailure { get; init; }

    public required AiMatchingOptions Ai { get; init; }

    public bool TmdbFailureUseBangumi { get; init; }

    public bool MikanTrustedOffsetCacheEnabled { get; init; }
}

public sealed record TmdbClientOptions
{
    public Uri BaseUrl { get; init; } = new("https://api.themoviedb.org/");

    public Uri? ProxyUrl { get; init; }

    public string? ApiKey { get; init; }

    public string? ReadAccessToken { get; init; }

    public string Language { get; init; } = "zh-CN";

    public TimeSpan HttpTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

public sealed record BangumiClientOptions
{
    public Uri BaseUrl { get; init; } = new("https://api.bgm.tv/");

    public Uri? ProxyUrl { get; init; }

    public TimeSpan HttpTimeout { get; init; } = TimeSpan.FromSeconds(30);
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
    public string Provider { get; init; } = "openai_compatible";

    public Uri? BaseUrl { get; init; }

    public string? ApiKey { get; init; }

    public string? Model { get; init; }

    public bool UseMetadataMatch { get; init; }

    public TimeSpan HttpTimeout { get; init; } = TimeSpan.FromSeconds(600);

    public int RetryCount { get; init; } = 2;

    public bool UseBangumiPubDateFirst { get; init; } = true;

    public Uri TmdbMcpUrl { get; init; } = new("http://tmdb.mcp.local/mcp");

    public Uri BangumiMcpUrl { get; init; } = new("http://bgm.mcp.local/mcp");

    public string AniDbMappingUrlTemplate { get; init; } =
        "https://raw.githubusercontent.com/DeQxJ00/Anime-Lists-Json/refs/heads/main/api/anidb/{anidbid}.json";
}

public sealed record SourceProfileSeed
{
    public required string Id { get; init; }

    public required string Adapter { get; init; }

    public required string DownloaderId { get; init; }

    public required FileStrategy FileStrategy { get; init; }

    public required IReadOnlyList<string> AllowedTorrentHosts { get; init; }

    public string Category { get; init; } = "animegonet";

    public IReadOnlyList<string> Tags { get; init; } = [];

    public int SeedingTimeMinutes { get; init; }

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
