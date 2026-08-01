using AnimeGoNet.App.Configuration;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Configuration;

public sealed class MetadataTransportConfigurationTests
{
    [Fact]
    public void LegacyGlobalProxyMapsToBothClientsAndSpecificValueWins()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ANIMEGO_PROXY_URL"] = "http://127.0.0.1:7890/",
            ["tmdb_proxy_url"] = "socks5://127.0.0.1:1080/",
            ["metadata:tmdb:proxy_url"] = "https://yaml-tmdb.invalid/",
            ["metadata:bangumi:proxy_url"] = "https://yaml-bangumi.invalid/",
        });

        var options = AnimeGoApplication.LoadOptions(configuration, inContainer: false);

        Assert.Equal(new Uri("socks5://127.0.0.1:1080/"), options.Metadata.Tmdb.ProxyUrl);
        Assert.Equal(new Uri("http://127.0.0.1:7890/"), options.Metadata.Bangumi.ProxyUrl);
    }

    [Fact]
    public void ExplicitEmptyLegacyGlobalProxyDisablesBothYamlProxies()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ANIMEGO_PROXY_URL"] = string.Empty,
            ["metadata:tmdb:proxy_url"] = "https://yaml-tmdb.invalid/",
            ["metadata:bangumi:proxy_url"] = "https://yaml-bangumi.invalid/",
        });

        var options = AnimeGoApplication.LoadOptions(configuration, inContainer: false);

        Assert.Null(options.Metadata.Tmdb.ProxyUrl);
        Assert.Null(options.Metadata.Bangumi.ProxyUrl);
    }

    [Fact]
    public async Task CommandLineConfigurationBindsIndependentApiAndProxyUrls()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "animegonet-metadata-transport",
            Guid.NewGuid().ToString("N"));
        var data = Path.Combine(root, "data");
        var download = Path.Combine(root, "download");
        var save = Path.Combine(root, "save");
        try
        {
            await using var app = await AnimeGoApplication.BuildAsync(
                [
                    "--data_path", data,
                    "--download_path", download,
                    "--save_path", save,
                    "--tmdb_base_url", "https://metadata.test.invalid/tmdb/",
                    "--tmdb_proxy_url", "http://127.0.0.1:7890/",
                    "--tmdb_language", "ja-JP",
                    "--tmdb_timeout_second", "40",
                    "--tmdb_retry_count", "4",
                    "--tmdb_retry_wait_second", "6.5",
                    "--bangumi_base_url", "https://metadata.test.invalid/bangumi/",
                    "--bangumi_proxy_url", "socks5://127.0.0.1:1080/",
                    "--bangumi_timeout_second", "45",
                    "--bangumi_retry_count", "5",
                    "--bangumi_retry_wait_second", "7.5",
                ],
                tmdbClient: new NullTmdbClient(),
                bangumiSubjectClient: new NullBangumiClient(),
                startBackgroundWorkers: false);
            var options = app.Services.GetRequiredService<AnimeGoOptions>();
            var deployment = app.Services.GetRequiredService<DeploymentConfigurationOptions>();

            Assert.Equal(
                new Uri("https://metadata.test.invalid/tmdb/"),
                options.Metadata.Tmdb.BaseUrl);
            Assert.Equal(
                new Uri("http://127.0.0.1:7890/"),
                options.Metadata.Tmdb.ProxyUrl);
            Assert.Equal("ja-JP", options.Metadata.Tmdb.Language);
            Assert.Equal(TimeSpan.FromSeconds(40), options.Metadata.Tmdb.HttpTimeout);
            Assert.Equal(4, options.Metadata.Tmdb.RetryCount);
            Assert.Equal(TimeSpan.FromSeconds(6.5), options.Metadata.Tmdb.RetryDelay);
            Assert.Equal(
                new Uri("https://metadata.test.invalid/bangumi/"),
                options.Metadata.Bangumi.BaseUrl);
            Assert.Equal(
                new Uri("socks5://127.0.0.1:1080/"),
                options.Metadata.Bangumi.ProxyUrl);
            Assert.Equal(TimeSpan.FromSeconds(45), options.Metadata.Bangumi.HttpTimeout);
            Assert.Equal(5, options.Metadata.Bangumi.RetryCount);
            Assert.Equal(TimeSpan.FromSeconds(7.5), options.Metadata.Bangumi.RetryDelay);
            Assert.Equal(options.Metadata, deployment.Value.Metadata);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class NullTmdbClient : ITmdbClient
    {
        public Task<IReadOnlyList<TmdbSeries>> SearchSeriesAsync(
            string title,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TmdbSeries>>([]);

        public Task<TmdbSeries?> GetSeriesAsync(
            int seriesId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeries?>(null);

        public Task<TmdbSeriesDetails?> GetSeriesDetailsAsync(
            int seriesId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeriesDetails?>(null);

        public Task<TmdbSeason?> GetSeasonAsync(
            int seriesId,
            int seasonNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeason?>(null);

        public Task<TmdbEpisode?> GetEpisodeAsync(
            int seriesId,
            int seasonNumber,
            int episodeNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbEpisode?>(null);
    }

    private sealed class NullBangumiClient : IBangumiSubjectClient
    {
        public Task<BangumiSubject?> GetSubjectAsync(
            int subjectId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BangumiSubject?>(null);

        public Task<IReadOnlyList<BangumiSubjectRelation>> GetRelatedSubjectsAsync(
            int subjectId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BangumiSubjectRelation>>([]);
    }
}
