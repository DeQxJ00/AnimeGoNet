using AnimeGoNet.App.Configuration;
using AnimeGoNet.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace AnimeGoNet.App.Tests.Configuration;

public sealed class ApplicationOverrideStoreTests
{
    [Fact]
    public async Task SaveReloadDeleteUseAtomicVersionedPrivateFile()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "animegonet-application-overrides",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new ApplicationOverrideStore(root);
            var initial = await store.LoadAsync();
            var saved = await store.SaveAsync(Entry(), 0);
            using var reloader = new ApplicationOverrideStore(root);
            var reloaded = await reloader.LoadAsync();

            Assert.Equal(0, initial.Revision);
            Assert.Equal(1, saved.Revision);
            Assert.Equal("private-api-key", reloaded.Settings?.TmdbApiKey);
            Assert.Equal("private-read-token", reloaded.Settings?.TmdbReadAccessToken);
            Assert.Single(Directory.GetFiles(root, "application.private.json"));
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
            await Assert.ThrowsAsync<ApplicationOverrideRevisionException>(() =>
                store.SaveAsync(Entry(), 0));

            var deleted = await store.DeleteAsync(1);
            Assert.Equal(2, deleted.Revision);
            Assert.Null(deleted.Settings);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ApplicationStartupAppliesPrivateSettingsBeforeClientConstruction()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "animegonet-application-overrides",
            Guid.NewGuid().ToString("N"));
        var options = AnimeGoDefaults.CreateNative(root);
        var layout = DirectoryLayout.From(options.Paths);
        layout.CreateDataDirectories();
        try
        {
            using (var store = new ApplicationOverrideStore(layout.ConfigurationPath))
            {
                _ = await store.SaveAsync(Entry(), 0);
            }

            await using var app = await AnimeGoApplication.BuildAsync(
                [],
                options,
                startBackgroundWorkers: false);
            var effective = app.Services.GetRequiredService<AnimeGoOptions>();
            var runtime = app.Services.GetRequiredService<ApplicationConfigurationRuntimeState>();
            var deployment = app.Services.GetRequiredService<DeploymentConfigurationOptions>();

            Assert.Equal(new Uri("https://tmdb.test.invalid/"), effective.Metadata.Tmdb.BaseUrl);
            Assert.Equal(new Uri("http://127.0.0.1:7890/"), effective.Metadata.Tmdb.ProxyUrl);
            Assert.Equal("en-US", effective.Metadata.Tmdb.Language);
            Assert.Equal("private-api-key", effective.Metadata.Tmdb.ApiKey);
            Assert.Equal("private-read-token", effective.Metadata.Tmdb.ReadAccessToken);
            Assert.True(effective.Metadata.SeasonFailure.Backtrace);
            Assert.True(effective.Metadata.Ai.UseMetadataMatch);
            Assert.Equal(TimeSpan.FromSeconds(600), effective.Metadata.Ai.HttpTimeout);
            Assert.Equal(
                new Uri("https://bangumi.test.invalid/api/"),
                effective.Metadata.Bangumi.BaseUrl);
            Assert.Equal(
                new Uri("socks5://127.0.0.1:1080/"),
                effective.Metadata.Bangumi.ProxyUrl);
            Assert.Equal(TimeSpan.FromSeconds(45), effective.Metadata.Bangumi.HttpTimeout);
            Assert.Equal(2, effective.TorrentFetch.MaxRedirects);
            Assert.Equal(1, runtime.AppliedRevision);
            Assert.Equal("zh-CN", deployment.Value.Metadata.Tmdb.Language);
            Assert.Null(deployment.Value.Metadata.Tmdb.ApiKey);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LegacyFormatOneFileInheritsNewTransportFields()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "animegonet-application-overrides",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var legacy = JsonSerializer.Serialize(new
            {
                format_version = 1,
                revision = 3,
                settings = new
                {
                    tmdb_base_url = "https://legacy-tmdb.invalid/",
                    tmdb_language = "zh-CN",
                    tmdb_http_timeout_seconds = 30,
                    tmdb_api_key_overridden = false,
                    tmdb_api_key = (string?)null,
                    tmdb_read_access_token_overridden = false,
                    tmdb_read_access_token = (string?)null,
                    season_failure_skip = false,
                    season_failure_backtrace = false,
                    season_failure_use_title_season = false,
                    season_failure_use_first_season = false,
                    ai_use_season_match = false,
                    ai_use_episode_match = false,
                    ai_http_timeout_seconds = 600,
                    tmdb_failure_use_bangumi = false,
                    mikan_trusted_offset_cache_enabled = false,
                    torrent_http_timeout_seconds = 30,
                    torrent_max_response_bytes = 16 * 1024 * 1024,
                    torrent_max_redirects = 3,
                    torrent_staging_ttl_seconds = 900,
                    updated_at_utc = "2026-07-26T12:00:00Z",
                },
            });
            await File.WriteAllTextAsync(
                Path.Combine(root, "application.private.json"),
                legacy);
            using var store = new ApplicationOverrideStore(root);
            var snapshot = await store.LoadAsync();
            var defaults = AnimeGoDefaults.CreateNative(root);
            defaults = defaults with
            {
                Metadata = defaults.Metadata with
                {
                    Tmdb = defaults.Metadata.Tmdb with
                    {
                        ProxyUrl = new Uri("http://127.0.0.1:7890/"),
                    },
                    Bangumi = defaults.Metadata.Bangumi with
                    {
                        BaseUrl = new Uri("https://deployment-bangumi.invalid/"),
                        ProxyUrl = new Uri("socks5://127.0.0.1:1080/"),
                    },
                },
            };

            var applied = ApplicationOverrideStore.Apply(defaults, snapshot);

            Assert.Equal(3, snapshot.Revision);
            Assert.Equal(
                new Uri("http://127.0.0.1:7890/"),
                applied.Metadata.Tmdb.ProxyUrl);
            Assert.Equal(
                new Uri("https://deployment-bangumi.invalid/"),
                applied.Metadata.Bangumi.BaseUrl);
            Assert.Equal(
                new Uri("socks5://127.0.0.1:1080/"),
                applied.Metadata.Bangumi.ProxyUrl);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ApplicationOverrideEntry Entry() => new(
        "https://tmdb.test.invalid/",
        "en-US",
        30,
        true,
        "private-api-key",
        true,
        "private-read-token",
        false,
        true,
        true,
        false,
        false,
        true,
        600,
        false,
        true,
        30,
        16 * 1024 * 1024,
        2,
        900,
        DateTimeOffset.Parse(
            "2026-07-26T12:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture),
        TmdbProxyUrlOverridden: true,
        TmdbProxyUrl: "http://127.0.0.1:7890/",
        BangumiBaseUrl: "https://bangumi.test.invalid/api/",
        BangumiProxyUrlOverridden: true,
        BangumiProxyUrl: "socks5://127.0.0.1:1080/",
        BangumiHttpTimeoutSeconds: 45);
}
