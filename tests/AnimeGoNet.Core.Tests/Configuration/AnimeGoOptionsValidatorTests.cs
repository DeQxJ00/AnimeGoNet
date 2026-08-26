using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.Core.Tests.Configuration;

public sealed class AnimeGoOptionsValidatorTests
{
    [Fact]
    public void DockerDefaultsUseSeparateTvAndMovieLibraryRoots()
    {
        var defaults = AnimeGoDefaults.CreateDocker();

        Assert.Equal("/download/anime", defaults.Paths.SavePath);
        Assert.Equal("/download/movies", defaults.Paths.MovieSavePath);
        Assert.Empty(AnimeGoOptionsValidator.Validate(defaults));
    }

    [Fact]
    public void RejectsMovieLibraryRootThatEqualsTvLibraryRoot()
    {
        var defaults = AnimeGoDefaults.CreateDocker();
        var options = defaults with
        {
            Paths = defaults.Paths with { MovieSavePath = defaults.Paths.SavePath },
        };

        var errors = AnimeGoOptionsValidator.Validate(options);

        Assert.Contains("movie_save_path must be different from save_path.", errors);
    }

    [Theory]
    [InlineData("http://127.0.0.1", 7991)]
    [InlineData("127.0.0.1/path", 7991)]
    [InlineData(" localhost", 7991)]
    [InlineData("localhost", -1)]
    [InlineData("localhost", 65536)]
    public void RejectsInvalidWebBinding(string host, int port)
    {
        var defaults = AnimeGoDefaults.CreateDocker();
        var options = defaults with
        {
            Web = defaults.Web with { Host = host, Port = port },
        };

        var errors = AnimeGoOptionsValidator.Validate(options);

        Assert.Contains(errors, error => error.StartsWith("Web ", StringComparison.Ordinal));
    }

    [Fact]
    public void AllowsDownloaderOutsideSharedDownloadRootForLongTermSeeding()
    {
        var defaults = AnimeGoDefaults.CreateDocker();
        var options = defaults with
        {
            Downloaders = new Dictionary<string, QbittorrentInstanceOptions>
            {
                ["bt"] = defaults.Downloaders["bt"] with { DownloadPath = "/another-volume" },
            },
        };

        var errors = AnimeGoOptionsValidator.Validate(options);

        Assert.Empty(errors);
    }

    [Fact]
    public void RejectsRelativeDownloaderPath()
    {
        var defaults = AnimeGoDefaults.CreateDocker();
        var options = defaults with
        {
            Downloaders = new Dictionary<string, QbittorrentInstanceOptions>
            {
                ["pt"] = defaults.Downloaders["pt"] with { DownloadPath = "relative/pt-seeding" },
            },
        };

        var errors = AnimeGoOptionsValidator.Validate(options);

        Assert.Contains(errors, error => error.Contains("must be absolute", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsUnsafeDownloaderEndpointWithoutEchoingCredentials()
    {
        const string secret = "do-not-echo";
        var defaults = AnimeGoDefaults.CreateDocker();
        var options = defaults with
        {
            Downloaders = new Dictionary<string, QbittorrentInstanceOptions>
            {
                ["bt"] = defaults.Downloaders["bt"] with
                {
                    BaseUrl = new Uri($"https://admin:{secret}@qbt.invalid/api?token=private#fragment"),
                },
            },
        };

        var errors = AnimeGoOptionsValidator.Validate(options);

        Assert.Contains(errors, error => error.Contains("base URL", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, error => error.Contains(secret, StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsDuplicateOrUnstableSourceRoutingIdentity()
    {
        var defaults = AnimeGoDefaults.CreateDocker();
        var first = defaults.InitialSourceProfiles[0] with
        {
            Adapter = "Mikan",
            DownloaderId = "BT",
        };
        var options = defaults with
        {
            InitialSourceProfiles =
            [
                first,
                first with { Adapter = "mikan", DownloaderId = "bt" },
            ],
        };

        var errors = AnimeGoOptionsValidator.Validate(options);

        Assert.Contains(errors, error => error.Contains("duplicated", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("adapter", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("downloader reference", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsTransmissionWithoutSilentlyConvertingIt()
    {
        var defaults = AnimeGoDefaults.CreateDocker();
        var options = defaults with
        {
            Downloaders = new Dictionary<string, QbittorrentInstanceOptions>
            {
                ["legacy"] = defaults.Downloaders["bt"] with { Type = "transmission" },
            },
            InitialSourceProfiles =
            [
                defaults.InitialSourceProfiles[0] with { DownloaderId = "legacy" },
            ],
        };

        var errors = AnimeGoOptionsValidator.Validate(options);

        Assert.Contains(errors, error => error.Contains("unsupported type 'transmission'", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsMissingOrMalformedTorrentSecurityLimits()
    {
        var defaults = AnimeGoDefaults.CreateDocker();
        var options = defaults with
        {
            TorrentFetch = defaults.TorrentFetch with
            {
                Timeout = TimeSpan.Zero,
                MaxResponseBytes = 0,
                MaxRedirects = 11,
                StagingTtl = TimeSpan.Zero,
            },
            InitialSourceProfiles =
            [
                defaults.InitialSourceProfiles[0] with { AllowedTorrentHosts = ["https://mikanani.me/path"] },
            ],
        };

        var errors = AnimeGoOptionsValidator.Validate(options);

        Assert.Contains(errors, error => error.Contains("invalid Torrent host pattern", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("fetch timeout", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("response size", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("maximum redirects", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("staging TTL", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsInvalidTmdbTransportConfiguration()
    {
        var defaults = AnimeGoDefaults.CreateDocker();
        var options = defaults with
        {
            Metadata = defaults.Metadata with
            {
                Tmdb = defaults.Metadata.Tmdb with
                {
                    BaseUrl = new Uri("ftp://tmdb.invalid/"),
                    ImageBaseUrl = new Uri("https://image.invalid/no-trailing-slash"),
                    HttpTimeout = TimeSpan.Zero,
                    RetryCount = 11,
                    RetryDelay = TimeSpan.FromMinutes(6),
                    CacheTtl = TimeSpan.FromDays(366),
                    Language = " ",
                },
                Bangumi = defaults.Metadata.Bangumi with
                {
                    BaseUrl = new Uri("https://bangumi.invalid/no-trailing-slash"),
                    HttpTimeout = TimeSpan.Zero,
                    RetryCount = -1,
                    RetryDelay = TimeSpan.FromSeconds(-1),
                },
            },
        };

        var errors = AnimeGoOptionsValidator.Validate(options);

        Assert.Contains(errors, error => error.Contains("TMDB base URL", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("TMDB image base URL", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("TMDB HTTP timeout", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("TMDB retry count", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("TMDB retry delay", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("TMDB cache TTL", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("TMDB language", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Bangumi base URL", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Bangumi HTTP timeout", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Bangumi retry count", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Bangumi retry delay", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsMikanBaseThatIsNotAnOrigin()
    {
        var defaults = AnimeGoDefaults.CreateDocker();
        var options = defaults with
        {
            Metadata = defaults.Metadata with
            {
                Mikan = new MikanClientOptions
                {
                    BaseUrl = new Uri("http://mikan.local/prefix/"),
                },
            },
        };

        var errors = AnimeGoOptionsValidator.Validate(options);

        Assert.Contains(errors, error => error.Contains("Mikan base URL", StringComparison.Ordinal));
    }

    [Fact]
    public void MikanIdentityCacheTtlsAllowPermanentButRejectNegativeOrOverTenYears()
    {
        var defaults = AnimeGoDefaults.CreateDocker();
        var permanent = defaults with
        {
            Metadata = defaults.Metadata with
            {
                Mikan = defaults.Metadata.Mikan with
                {
                    EpisodeIdentityCacheTtl = TimeSpan.Zero,
                    BangumiIdentityCacheTtl = TimeSpan.Zero,
                },
            },
        };
        Assert.Empty(AnimeGoOptionsValidator.Validate(permanent));

        var invalid = defaults with
        {
            Metadata = defaults.Metadata with
            {
                Mikan = defaults.Metadata.Mikan with
                {
                    EpisodeIdentityCacheTtl = TimeSpan.FromHours(-1),
                    BangumiIdentityCacheTtl = TimeSpan.FromDays(3651),
                },
            },
        };
        var errors = AnimeGoOptionsValidator.Validate(invalid);

        Assert.Contains(errors, error => error.Contains(
            "Mikan Episode identity cache TTL",
            StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains(
            "Mikan Bangumi identity cache TTL",
            StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptsPrefixedMetadataApisAndGlobalSelectiveProxy()
    {
        var defaults = AnimeGoDefaults.CreateDocker();
        var options = defaults with
        {
            OutboundProxy = new OutboundProxyOptions
            {
                Url = new Uri("socks5://127.0.0.1:1080/"),
                HostPatterns = ["metadata.invalid", "*.mikanime.tv"],
            },
            Metadata = defaults.Metadata with
            {
                Tmdb = defaults.Metadata.Tmdb with
                {
                    BaseUrl = new Uri("https://metadata.invalid/tmdb/"),
                },
                Bangumi = defaults.Metadata.Bangumi with
                {
                    BaseUrl = new Uri("https://metadata.invalid/bangumi/"),
                },
            },
        };

        Assert.Empty(AnimeGoOptionsValidator.Validate(options));
    }

    [Fact]
    public void RejectsInvalidGlobalSelectiveProxy()
    {
        var defaults = AnimeGoDefaults.CreateDocker();
        var options = defaults with
        {
            OutboundProxy = new OutboundProxyOptions
            {
                Url = new Uri("https://user:secret@proxy.invalid/path"),
                HostPatterns = ["API.Example.com", "*.example.com", "*.example.com"],
            },
        };

        var errors = AnimeGoOptionsValidator.Validate(options);

        Assert.Contains(errors, error => error.Contains("Outbound proxy URL", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("lowercase", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("duplicated", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsProxyHostsWithoutProxyUrl()
    {
        var defaults = AnimeGoDefaults.CreateDocker();
        var options = defaults with
        {
            OutboundProxy = new OutboundProxyOptions
            {
                HostPatterns = ["api.example.com"],
            },
        };

        Assert.Contains(
            AnimeGoOptionsValidator.Validate(options),
            error => error.Contains("require a configured proxy URL", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsMalformedAiTransportAndToolConfiguration()
    {
        var defaults = AnimeGoDefaults.CreateDocker();
        var options = defaults with
        {
            Metadata = defaults.Metadata with
            {
                Ai = defaults.Metadata.Ai with
                {
                    UseMetadataMatch = true,
                    RetryCount = 11,
                    TmdbMcpUrl = new Uri("ftp://tmdb.invalid/mcp"),
                    AniDbMappingUrlTemplate = "https://mapping.invalid/no-placeholder.json",
                },
            },
        };

        var errors = AnimeGoOptionsValidator.Validate(options);

        Assert.Contains(errors, error => error.Contains("retry count", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("TMDB MCP URL", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("fixed", StringComparison.Ordinal));
    }

    [Fact]
    public void EnabledAiWithoutEndpointDoesNotPreventApplicationStartup()
    {
        var defaults = AnimeGoDefaults.CreateDocker();
        var options = defaults with
        {
            Metadata = defaults.Metadata with
            {
                Ai = defaults.Metadata.Ai with { UseMetadataMatch = true },
            },
        };

        Assert.Empty(AnimeGoOptionsValidator.Validate(options));
    }

    [Fact]
    public void AcceptsConfiguredOpenAiCompatibleProviderWithoutRequiringApiKey()
    {
        var defaults = AnimeGoDefaults.CreateDocker();
        var options = defaults with
        {
            Metadata = defaults.Metadata with
            {
                Ai = defaults.Metadata.Ai with
                {
                    BaseUrl = new Uri("http://local-model.invalid/api/"),
                    Model = "local-model",
                    UseMetadataMatch = true,
                },
            },
        };

        Assert.Empty(AnimeGoOptionsValidator.Validate(options));
    }

    [Fact]
    public void RejectsInvalidSourceDownloadPolicy()
    {
        var defaults = AnimeGoDefaults.CreateDocker();
        var options = defaults with
        {
            InitialSourceProfiles =
            [
                defaults.InitialSourceProfiles[0] with
                {
                    Category = "bad,category",
                    Tags = ["valid", "bad,tag"],
                    SeedingTimeMinutes = 1,
                },
            ],
        };

        var errors = AnimeGoOptionsValidator.Validate(options);

        Assert.Contains(errors, error => error.Contains("download policy", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsInvalidOrNonMikanIdentityCookieWithoutEchoingValue()
    {
        const string secret = "do-not-echo;Injected=true";
        var defaults = AnimeGoDefaults.CreateDocker();
        var invalidValue = defaults with
        {
            InitialSourceProfiles =
            [
                defaults.InitialSourceProfiles[0] with
                {
                    MikanIdentityCookie = secret,
                },
            ],
        };
        var wrongAdapter = defaults with
        {
            InitialSourceProfiles =
            [
                defaults.InitialSourceProfiles[0] with
                {
                    Adapter = "u2",
                    MikanIdentityCookie = "private-cookie",
                },
            ],
        };

        var invalidErrors = AnimeGoOptionsValidator.Validate(invalidValue);
        var wrongAdapterErrors = AnimeGoOptionsValidator.Validate(wrongAdapter);

        Assert.Contains(
            invalidErrors,
            error => error.Contains(
                "invalid Mikan identity Cookie",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            invalidErrors,
            error => error.Contains(secret, StringComparison.Ordinal));
        Assert.Contains(
            wrongAdapterErrors,
            error => error.Contains(
                "only configure a Mikan identity Cookie",
                StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsInvalidOrUnroutableSourceRssScheduleWithoutEchoingUrl()
    {
        const string secret = "https://private.invalid/rss?passkey=do-not-echo";
        var defaults = AnimeGoDefaults.CreateDocker();
        var options = defaults with
        {
            InitialSourceProfiles =
            [
                defaults.InitialSourceProfiles[0] with
                {
                    RssFeedUrl = secret,
                    RssScheduleEnabled = true,
                    RssScheduleCron = "not a cron",
                },
            ],
        };

        var errors = AnimeGoOptionsValidator.Validate(options);

        Assert.Contains(
            errors,
            error => error.Contains("RSS schedule", StringComparison.Ordinal));
        Assert.DoesNotContain(
            errors,
            error => error.Contains(secret, StringComparison.Ordinal));
        Assert.DoesNotContain(
            errors,
            error => error.Contains("do-not-echo", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptsSourceRssHostCoveredByWildcard()
    {
        var defaults = AnimeGoDefaults.CreateDocker();
        var options = defaults with
        {
            InitialSourceProfiles =
            [
                defaults.InitialSourceProfiles[0] with
                {
                    AllowedTorrentHosts = ["*.example.invalid"],
                    RssFeedUrl = "https://rss.example.invalid/feed",
                    RssScheduleEnabled = true,
                },
            ],
        };

        Assert.Empty(AnimeGoOptionsValidator.Validate(options));
    }

    [Fact]
    public void RejectsInvalidDirectoryDatabaseRefreshCron()
    {
        var defaults = AnimeGoDefaults.CreateDocker();
        var options = defaults with
        {
            Schedule = defaults.Schedule with { RefreshDatabaseCron = "every morning" },
        };

        Assert.Contains(
            AnimeGoOptionsValidator.Validate(options),
            error => error.Contains("refresh cron", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatesDataUpdatePolicyWithoutGuessingRepositoryOwner()
    {
        var defaults = AnimeGoDefaults.CreateDocker();
        var invalid = defaults with
        {
            DataUpdate = defaults.DataUpdate with
            {
                Enabled = true,
                Cron = "invalid",
                KeepVersions = 1,
                HttpTimeout = TimeSpan.FromHours(2),
            },
        };
        var errors = AnimeGoOptionsValidator.Validate(invalid);

        Assert.Contains(errors, error => error.Contains("Data update cron", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("manifest URL", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("keep versions", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("HTTP timeout", StringComparison.Ordinal));

        var valid = defaults with
        {
            DataUpdate = defaults.DataUpdate with
            {
                Enabled = true,
                ManifestUrl = new Uri(
                    "https://github.com/example/AnimeGoNetData/releases/latest/download/manifest.json"),
            },
        };
        Assert.Empty(AnimeGoOptionsValidator.Validate(valid));
    }

    [Fact]
    public void ValidatesAiReasoningAndWebSearchMode()
    {
        var defaults = AnimeGoDefaults.CreateDocker();
        var invalid = defaults with
        {
            Metadata = defaults.Metadata with
            {
                Ai = defaults.Metadata.Ai with
                {
                    ReasoningEffort = "ultra",
                    WebSearchEnabled = true,
                    ApiMode = AiApiMode.ChatCompletions,
                },
            },
        };

        var errors = AnimeGoOptionsValidator.Validate(invalid);

        Assert.Contains(errors, error => error.Contains("reasoning effort", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("web search requires Responses", StringComparison.Ordinal));

        var valid = invalid with
        {
            Metadata = invalid.Metadata with
            {
                Ai = invalid.Metadata.Ai with
                {
                    ReasoningEffort = "medium",
                    ApiMode = AiApiMode.Responses,
                },
            },
        };
        Assert.Empty(AnimeGoOptionsValidator.Validate(valid));
    }

    [Theory]
    [InlineData("/download/incomplete", "/download/incomplete/bt", true)]
    [InlineData("/download/incomplete", "/download/incomplete-other", false)]
    [InlineData("/download/incomplete", "/download/incomplete/../anime", false)]
    public void PosixContainmentHonorsDirectoryBoundaries(string root, string candidate, bool expected)
    {
        Assert.Equal(expected, PathBoundary.IsWithin(root, candidate));
    }
}
