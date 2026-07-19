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
                    HttpTimeout = TimeSpan.Zero,
                    Language = " ",
                },
            },
        };

        var errors = AnimeGoOptionsValidator.Validate(options);

        Assert.Contains(errors, error => error.Contains("TMDB base URL", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("TMDB HTTP timeout", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("TMDB language", StringComparison.Ordinal));
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
