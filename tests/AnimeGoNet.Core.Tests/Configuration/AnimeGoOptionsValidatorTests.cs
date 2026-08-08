using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.Core.Tests.Configuration;

public sealed class AnimeGoOptionsValidatorTests
{
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
    public void RejectsDownloaderOutsideSharedDownloadRoot()
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

        Assert.Contains(errors, error => error.Contains("inside download_path", StringComparison.Ordinal));
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
                    ProxyUrl = new Uri("https://user:secret@proxy.invalid/"),
                    HttpTimeout = TimeSpan.Zero,
                    RetryCount = 11,
                    RetryDelay = TimeSpan.FromMinutes(6),
                    CacheTtl = TimeSpan.FromDays(366),
                    Language = " ",
                },
                Bangumi = defaults.Metadata.Bangumi with
                {
                    BaseUrl = new Uri("https://bangumi.invalid/no-trailing-slash"),
                    ProxyUrl = new Uri("https://proxy.invalid/path"),
                    HttpTimeout = TimeSpan.Zero,
                    RetryCount = -1,
                    RetryDelay = TimeSpan.FromSeconds(-1),
                },
            },
        };

        var errors = AnimeGoOptionsValidator.Validate(options);

        Assert.Contains(errors, error => error.Contains("TMDB base URL", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("TMDB proxy URL", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("TMDB HTTP timeout", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("TMDB retry count", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("TMDB retry delay", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("TMDB cache TTL", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("TMDB language", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Bangumi base URL", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Bangumi proxy URL", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Bangumi HTTP timeout", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Bangumi retry count", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Bangumi retry delay", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptsPrefixedMetadataApisAndIndependentProxySchemes()
    {
        var defaults = AnimeGoDefaults.CreateDocker();
        var options = defaults with
        {
            Metadata = defaults.Metadata with
            {
                Tmdb = defaults.Metadata.Tmdb with
                {
                    BaseUrl = new Uri("https://metadata.invalid/tmdb/"),
                    ProxyUrl = new Uri("http://127.0.0.1:7890/"),
                },
                Bangumi = defaults.Metadata.Bangumi with
                {
                    BaseUrl = new Uri("https://metadata.invalid/bangumi/"),
                    ProxyUrl = new Uri("socks5://127.0.0.1:1080/"),
                },
            },
        };

        Assert.Empty(AnimeGoOptionsValidator.Validate(options));
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

    [Theory]
    [InlineData("/download/incomplete", "/download/incomplete/bt", true)]
    [InlineData("/download/incomplete", "/download/incomplete-other", false)]
    [InlineData("/download/incomplete", "/download/incomplete/../anime", false)]
    public void PosixContainmentHonorsDirectoryBoundaries(string root, string candidate, bool expected)
    {
        Assert.Equal(expected, PathBoundary.IsWithin(root, candidate));
    }
}
