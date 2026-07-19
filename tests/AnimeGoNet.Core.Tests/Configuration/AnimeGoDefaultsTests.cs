using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.Core.Tests.Configuration;

public sealed class AnimeGoDefaultsTests
{
    [Fact]
    public void DockerDefaultsMatchPublishedVolumeContract()
    {
        var options = AnimeGoDefaults.CreateDocker();

        Assert.Equal("/data", options.Paths.DataPath);
        Assert.Equal("/download/incomplete", options.Paths.DownloadPath);
        Assert.Equal("/download/anime", options.Paths.SavePath);
        Assert.Equal("/download/incomplete/bt", options.Downloaders["bt"].DownloadPath);
        Assert.Empty(AnimeGoOptionsValidator.Validate(options));
    }

    [Fact]
    public void RiskyMetadataFallbacksAreDisabledByDefault()
    {
        var options = AnimeGoDefaults.CreateDocker();

        Assert.False(options.Metadata.SeasonFailure.Skip);
        Assert.False(options.Metadata.SeasonFailure.Backtrace);
        Assert.False(options.Metadata.SeasonFailure.UseTitleSeason);
        Assert.False(options.Metadata.SeasonFailure.UseFirstSeason);
        Assert.False(options.Metadata.Ai.UseSeasonMatch);
        Assert.False(options.Metadata.Ai.UseEpisodeMatch);
        Assert.False(options.Metadata.TmdbFailureUseBangumi);
        Assert.False(options.Metadata.MikanTrustedOffsetCacheEnabled);
        Assert.Equal(TimeSpan.FromSeconds(600), options.Metadata.Ai.HttpTimeout);
    }

    [Fact]
    public void MikanDefaultsToMoveAndQbittorrent()
    {
        var options = AnimeGoDefaults.CreateDocker();
        var profile = Assert.Single(options.InitialSourceProfiles);

        Assert.Equal("mikan", profile.Id);
        Assert.Equal("bt", profile.DownloaderId);
        Assert.Equal(FileStrategy.Move, profile.FileStrategy);
        Assert.True(profile.RssFilterEnabled);
        Assert.True(profile.RssPriorityEnabled);
        Assert.All(options.Downloaders.Values, downloader => Assert.Equal("qbittorrent", downloader.Type));
    }
}
