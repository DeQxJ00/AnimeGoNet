using AnimeGoNet.Core.Sources;

namespace AnimeGoNet.Core.Configuration;

public sealed record AnimeGoOptions
{
    public required PathOptions Paths { get; init; }

    public required WebBindingOptions Web { get; init; }

    public required OutboundProxyOptions OutboundProxy { get; init; }

    public required IReadOnlyDictionary<string, QbittorrentInstanceOptions> Downloaders { get; init; }

    public required MetadataMatchingOptions Metadata { get; init; }

    public required TorrentFetchOptions TorrentFetch { get; init; }

    public required ScheduleOptions Schedule { get; init; }

    public required DataUpdateOptions DataUpdate { get; init; }

    public required IReadOnlyList<SourceProfileSeed> InitialSourceProfiles { get; init; }
}

public sealed record OutboundProxyOptions
{
    public Uri? Url { get; init; }

    public IReadOnlyList<string> HostPatterns { get; init; } = [];
}

public sealed record WebBindingOptions
{
    public required string Host { get; init; }

    public int Port { get; init; } = 7991;
}

public sealed record ScheduleOptions
{
    public string RefreshDatabaseCron { get; init; } = "0 0 6 * * *";
}

public sealed record DataUpdateOptions
{
    public const string DefaultManifestUrl =
        "https://github.com/DeQxJ00/AnimeGoNetData/releases/latest/download/manifest.json";

    public bool Enabled { get; init; } = true;

    public string Cron { get; init; } = "0 0 4 * * ?";

    public Uri? ManifestUrl { get; init; } = new(DefaultManifestUrl);

    public bool AutoDownload { get; init; } = true;

    public bool AutoImport { get; init; } = true;

    public int KeepVersions { get; init; } = 2;

    public TimeSpan HttpTimeout { get; init; } = TimeSpan.FromSeconds(300);
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

    public string MovieSavePath { get; init; } = string.Empty;

    public string EffectiveMovieSavePath => string.IsNullOrWhiteSpace(MovieSavePath)
        ? SavePath
        : MovieSavePath;
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
    public required MikanClientOptions Mikan { get; init; }

    public required TmdbClientOptions Tmdb { get; init; }

    public required BangumiClientOptions Bangumi { get; init; }

    public required SeasonFailureOptions SeasonFailure { get; init; }

    public required AiMatchingOptions Ai { get; init; }

    public bool TmdbFailureUseBangumi { get; init; }

    public bool WriteBangumiIdWhenTmdbMatched { get; init; }

    public bool MikanTrustedOffsetCacheEnabled { get; init; }

    public int MikanTrustedOffsetRequiredEpisodes { get; init; } = 3;
}

public sealed record MikanClientOptions
{
    public const string DefaultBaseUrl = "https://mikanime.tv/";

    public const string DefaultHost = "mikanime.tv";

    public Uri BaseUrl { get; init; } = new(DefaultBaseUrl);

    public TimeSpan EpisodeIdentityCacheTtl { get; init; } = TimeSpan.FromDays(365);

    public TimeSpan BangumiIdentityCacheTtl { get; init; } = TimeSpan.FromDays(365);
}

public sealed record TmdbClientOptions
{
    public Uri BaseUrl { get; init; } = new("https://api.themoviedb.org/");

    public Uri ImageBaseUrl { get; init; } = new("https://image.tmdb.org/t/p/");

    public string? ApiKey { get; init; }

    public string? ReadAccessToken { get; init; }

    public string Language { get; init; } = "zh-CN";

    public TimeSpan HttpTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public int RetryCount { get; init; } = 3;

    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan CacheTtl { get; init; } = TimeSpan.FromHours(144);
}

public sealed record BangumiClientOptions
{
    public Uri BaseUrl { get; init; } = new("https://api.bgm.tv/");

    public TimeSpan HttpTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public int RetryCount { get; init; } = 3;

    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(5);
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
    public const int MaximumPromptTemplateLength = 128 * 1024;

    public const int MaximumFileIdentityFuzzyMatchLimit = 2;

    public const string FixedAniDbMappingUrlTemplate =
        "https://raw.githubusercontent.com/DeQxJ00/Anime-Lists-Json/refs/heads/main/api/anidb/{anidbid}.json";

    public string Provider { get; init; } = "openai_compatible";

    public Uri? BaseUrl { get; init; }

    public string? ApiKey { get; init; }

    public string? Model { get; init; }

    public string? PromptTemplate { get; init; }

    public AiApiMode ApiMode { get; init; } = AiApiMode.Responses;

    public string? ReasoningEffort { get; init; }

    public bool WebSearchEnabled { get; init; } = true;

    public int FileIdentityFuzzyMatchLimit { get; init; } = 1;

    public bool UseMetadataMatch { get; init; }

    public bool DebugMode { get; init; }

    public TimeSpan HttpTimeout { get; init; } = TimeSpan.FromSeconds(600);

    public int RetryCount { get; init; } = 2;

    public bool UseBangumiPubDateFirst { get; init; } = true;

    public Uri TmdbMcpUrl { get; init; } = new("http://tmdb.mcp.local/mcp");

    public Uri BangumiMcpUrl { get; init; } = new("http://bgm.mcp.local/mcp");

    public string AniDbMappingUrlTemplate { get; init; } =
        FixedAniDbMappingUrlTemplate;
}

public enum AiApiMode
{
    ChatCompletions = 1,
    Responses = 2,
}

public sealed record SourceProfileSeed
{
    public required string Id { get; init; }

    public string? DisplayName { get; init; }

    public required string Adapter { get; init; }

    public string MediaType { get; init; } = AnimeGoNet.Core.Media.MediaTypes.Tv;

    public bool PreferAniDbTmdbMapping { get; init; }

    public string AniDbTmdbMappingUrlTemplate { get; init; } =
        AiMatchingOptions.FixedAniDbMappingUrlTemplate;

    public required string DownloaderId { get; init; }

    public required FileStrategy FileStrategy { get; init; }

    public string LinkType { get; init; } = SourceDownloadPolicy.HardLinkType;

    public required IReadOnlyList<string> AllowedTorrentHosts { get; init; }

    public string Category { get; init; } = "animegonet";

    public IReadOnlyList<string> Tags { get; init; } = [];

    public string? DynamicTagTemplate { get; init; }

    public int SeedingTimeMinutes { get; init; }

    public bool RssFilterEnabled { get; init; }

    public bool RssPriorityEnabled { get; init; }

    public bool DuplicateNotificationEnabled { get; init; } = true;

    public string? MikanIdentityCookie { get; init; }

    public string? RssFeedUrl { get; init; }

    public bool RssScheduleEnabled { get; init; }

    public string RssScheduleCron { get; init; } =
        SourceRssSchedulePolicy.DefaultCron;

    public override string ToString() =>
        $"SourceProfileSeed {{ Id = {Id}, Adapter = {Adapter}, "
        + $"CredentialsConfigured = {MikanIdentityCookie is not null} }}";
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
