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

    [Theory]
    [InlineData("/download/incomplete", "/download/incomplete/bt", true)]
    [InlineData("/download/incomplete", "/download/incomplete-other", false)]
    [InlineData("/download/incomplete", "/download/incomplete/../anime", false)]
    public void PosixContainmentHonorsDirectoryBoundaries(string root, string candidate, bool expected)
    {
        Assert.Equal(expected, PathBoundary.IsWithin(root, candidate));
    }
}
