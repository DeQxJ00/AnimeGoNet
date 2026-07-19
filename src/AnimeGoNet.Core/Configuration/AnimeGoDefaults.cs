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
        };

        return Create(
            paths,
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
        };

        return Create(
            paths,
            new Uri("http://127.0.0.1:8080"),
            new Uri("http://127.0.0.1:8081"));
    }

    private static AnimeGoOptions Create(PathOptions paths, Uri btDownloaderBaseUrl, Uri ptDownloaderBaseUrl)
    {
        return new AnimeGoOptions
        {
            Paths = paths,
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
                SeasonFailure = new SeasonFailureOptions(),
                Ai = new AiMatchingOptions
                {
                    HttpTimeout = TimeSpan.FromSeconds(AiHttpTimeoutSeconds),
                },
                TmdbFailureUseBangumi = false,
                MikanTrustedOffsetCacheEnabled = false,
            },
            InitialSourceProfiles =
            [
                new SourceProfileSeed
                {
                    Id = "mikan",
                    Adapter = "mikan",
                    DownloaderId = "bt",
                    FileStrategy = FileStrategy.Move,
                    RssFilterEnabled = true,
                    RssPriorityEnabled = true,
                },
            ],
        };
    }
}
