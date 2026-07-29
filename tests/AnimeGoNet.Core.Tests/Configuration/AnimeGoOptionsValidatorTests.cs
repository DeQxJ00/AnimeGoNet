using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.Core.Tests.Configuration;

public sealed class AnimeGoOptionsValidatorTests
{
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
                    Language = " ",
                },
                Bangumi = defaults.Metadata.Bangumi with
                {
                    BaseUrl = new Uri("https://bangumi.invalid/no-trailing-slash"),
                    ProxyUrl = new Uri("https://proxy.invalid/path"),
                    HttpTimeout = TimeSpan.Zero,
                },
            },
        };

        var errors = AnimeGoOptionsValidator.Validate(options);

        Assert.Contains(errors, error => error.Contains("TMDB base URL", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("TMDB proxy URL", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("TMDB HTTP timeout", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("TMDB language", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Bangumi base URL", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Bangumi proxy URL", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Bangumi HTTP timeout", StringComparison.Ordinal));
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
