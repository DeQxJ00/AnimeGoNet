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
        Assert.Equal("0.0.0.0", options.Web.Host);
        Assert.Equal(7991, options.Web.Port);
        Assert.Equal("/download/incomplete/bt", options.Downloaders["bt"].DownloadPath);
        Assert.Equal("/download/incomplete/pt", options.Downloaders["pt"].DownloadPath);
        Assert.Empty(AnimeGoOptionsValidator.Validate(options));
    }

    [Fact]
    public void NativeDefaultsBindOnlyToLoopback()
    {
        var options = AnimeGoDefaults.CreateNative(Path.GetTempPath());

        Assert.Equal("127.0.0.1", options.Web.Host);
        Assert.Equal(7991, options.Web.Port);
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
        Assert.False(options.Metadata.Ai.UseMetadataMatch);
        Assert.Equal("openai_compatible", options.Metadata.Ai.Provider);
        Assert.Null(options.Metadata.Ai.BaseUrl);
        Assert.Null(options.Metadata.Ai.ApiKey);
        Assert.Null(options.Metadata.Ai.Model);
        Assert.False(options.Metadata.TmdbFailureUseBangumi);
        Assert.False(options.Metadata.MikanTrustedOffsetCacheEnabled);
        Assert.Equal(TimeSpan.FromSeconds(600), options.Metadata.Ai.HttpTimeout);
        Assert.Equal(2, options.Metadata.Ai.RetryCount);
        Assert.True(options.Metadata.Ai.UseBangumiPubDateFirst);
        Assert.Equal(new Uri("http://tmdb.mcp.local/mcp"), options.Metadata.Ai.TmdbMcpUrl);
        Assert.Equal(new Uri("http://bgm.mcp.local/mcp"), options.Metadata.Ai.BangumiMcpUrl);
        Assert.Equal(new Uri("https://api.themoviedb.org/"), options.Metadata.Tmdb.BaseUrl);
        Assert.Null(options.Metadata.Tmdb.ProxyUrl);
        Assert.Equal("zh-CN", options.Metadata.Tmdb.Language);
        Assert.Null(options.Metadata.Tmdb.ApiKey);
        Assert.Null(options.Metadata.Tmdb.ReadAccessToken);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Metadata.Tmdb.HttpTimeout);
        Assert.Equal(3, options.Metadata.Tmdb.RetryCount);
        Assert.Equal(TimeSpan.FromSeconds(5), options.Metadata.Tmdb.RetryDelay);
        Assert.Equal(TimeSpan.FromDays(14), options.Metadata.Tmdb.CacheTtl);
        Assert.Equal(new Uri("https://api.bgm.tv/"), options.Metadata.Bangumi.BaseUrl);
        Assert.Null(options.Metadata.Bangumi.ProxyUrl);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Metadata.Bangumi.HttpTimeout);
        Assert.Equal(3, options.Metadata.Bangumi.RetryCount);
        Assert.Equal(TimeSpan.FromSeconds(5), options.Metadata.Bangumi.RetryDelay);
        Assert.Equal("0 0 6 * * *", options.Schedule.RefreshDatabaseCron);
        Assert.False(options.DataUpdate.Enabled);
        Assert.Equal("0 0 4 * * ?", options.DataUpdate.Cron);
        Assert.Null(options.DataUpdate.ManifestUrl);
        Assert.True(options.DataUpdate.AutoDownload);
        Assert.True(options.DataUpdate.AutoImport);
        Assert.Equal(2, options.DataUpdate.KeepVersions);
        Assert.Equal(TimeSpan.FromSeconds(300), options.DataUpdate.HttpTimeout);
    }

    [Fact]
    public void MikanDefaultsToMoveAndQbittorrent()
    {
        var options = AnimeGoDefaults.CreateDocker();
        var profile = Assert.Single(options.InitialSourceProfiles);

        Assert.Equal("mikan", profile.Id);
        Assert.Equal("bt", profile.DownloaderId);
        Assert.Equal(FileStrategy.Move, profile.FileStrategy);
        Assert.Contains("mikanani.me", profile.AllowedTorrentHosts);
        Assert.Equal("animegonet", profile.Category);
        Assert.Empty(profile.Tags);
        Assert.Equal(0, profile.SeedingTimeMinutes);
        Assert.True(profile.RssFilterEnabled);
        Assert.True(profile.RssPriorityEnabled);
        Assert.Equal(2, options.Downloaders.Count);
        Assert.All(options.Downloaders.Values, downloader => Assert.Equal("qbittorrent", downloader.Type));
    }
}
