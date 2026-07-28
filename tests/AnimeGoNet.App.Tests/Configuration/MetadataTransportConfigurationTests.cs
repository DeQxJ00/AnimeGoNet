using AnimeGoNet.App.Configuration;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Configuration;

public sealed class MetadataTransportConfigurationTests
{
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
                    "--bangumi_base_url", "https://metadata.test.invalid/bangumi/",
                    "--bangumi_proxy_url", "socks5://127.0.0.1:1080/",
                    "--bangumi_timeout_second", "45",
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
            Assert.Equal(
                new Uri("https://metadata.test.invalid/bangumi/"),
                options.Metadata.Bangumi.BaseUrl);
            Assert.Equal(
                new Uri("socks5://127.0.0.1:1080/"),
                options.Metadata.Bangumi.ProxyUrl);
            Assert.Equal(TimeSpan.FromSeconds(45), options.Metadata.Bangumi.HttpTimeout);
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
