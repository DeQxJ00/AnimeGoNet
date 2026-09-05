namespace AnimeGoNet.Core.Configuration;

public static class AnimeGoDefaults
{
    public const int AiHttpTimeoutSeconds = 600;

    public static AnimeGoOptions CreateDocker()
    {
        var paths = new PathOptions
        {
            DataPath = "/data",
            DownloadPath = "/download/incomplete",
            SavePath = "/download/anime",
            MovieSavePath = "/download/movies",
        };

        return Create(
            paths,
            "0.0.0.0",
            new Uri("http://qbittorrent-bt:8080"),
            new Uri("http://qbittorrent-pt:8080"));
    }

    public static AnimeGoOptions CreateNative(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var root = Path.GetFullPath(rootPath);
        var paths = new PathOptions
        {
            DataPath = Path.Combine(root, "data"),
            DownloadPath = Path.Combine(root, "download", "incomplete"),
            SavePath = Path.Combine(root, "download", "anime"),
            MovieSavePath = Path.Combine(root, "download", "movies"),
        };

        return Create(
            paths,
            "127.0.0.1",
            new Uri("http://127.0.0.1:8080"),
            new Uri("http://127.0.0.1:8081"));
    }

    private static AnimeGoOptions Create(
        PathOptions paths,
        string webHost,
        Uri btDownloaderBaseUrl,
        Uri ptDownloaderBaseUrl)
    {
        return new AnimeGoOptions
        {
            Paths = paths,
            Web = new WebBindingOptions
            {
                Host = webHost,
                Port = 7991,
            },
            OutboundProxy = new OutboundProxyOptions(),
            Downloaders = new Dictionary<string, QbittorrentInstanceOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["bt"] = new()
                {
                    BaseUrl = btDownloaderBaseUrl,
                    DownloadPath = PathBoundary.Combine(paths.DownloadPath, "bt"),
                },
                ["pt"] = new()
                {
                    BaseUrl = ptDownloaderBaseUrl,
                    DownloadPath = PathBoundary.Combine(paths.DownloadPath, "pt"),
                },
            },
            Metadata = new MetadataMatchingOptions
            {
                Mikan = new MikanClientOptions(),
                Tmdb = new TmdbClientOptions(),
                Bangumi = new BangumiClientOptions(),
                SeasonFailure = new SeasonFailureOptions(),
                Ai = new AiMatchingOptions
                {
                    HttpTimeout = TimeSpan.FromSeconds(AiHttpTimeoutSeconds),
                },
                TmdbFailureUseBangumi = false,
                MikanTrustedOffsetCacheEnabled = false,
                MikanTrustedOffsetRequiredEpisodes = 3,
            },
            TorrentFetch = new TorrentFetchOptions(),
            Schedule = new ScheduleOptions(),
            DataUpdate = new DataUpdateOptions(),
            InitialSourceProfiles =
            [
                new SourceProfileSeed
                {
                    Id = "mikan",
                    Adapter = "mikan",
                    DownloaderId = "bt",
                    FileStrategy = FileStrategy.Move,
                    AllowedTorrentHosts = [MikanClientOptions.DefaultHost, "mikanani.me"],
                    Category = "animegonet",
                    Tags = [],
                    DynamicTagTemplate = "{year}年{quarter}月新番",
                    SeedingTimeMinutes = 0,
                    RssFilterEnabled = true,
                    RssPriorityEnabled = true,
                    DuplicateNotificationEnabled = true,
                },
            ],
        };
    }
}
