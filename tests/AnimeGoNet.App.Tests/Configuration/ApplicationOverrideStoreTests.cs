using AnimeGoNet.App.Configuration;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace AnimeGoNet.App.Tests.Configuration;

public sealed class ApplicationOverrideStoreTests
{
    [Fact]
    public void ApplyOverridesDownloadAndSavePathsWithoutChangingDataPath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"animegonet-paths-{Guid.NewGuid():N}");
        var defaults = AnimeGoDefaults.CreateNative(root);
        var downloadPath = Path.Combine(root, "downloads-next");
        var savePath = Path.Combine(root, "library-next");
        var applied = ApplicationOverrideStore.Apply(
            defaults,
            new ApplicationOverrideSnapshot(
                1,
                1,
                Entry() with
                {
                    DownloadPath = downloadPath,
                    SavePath = savePath,
                }));

        Assert.Equal(defaults.Paths.DataPath, applied.Paths.DataPath);
        Assert.Equal(Path.GetFullPath(downloadPath), applied.Paths.DownloadPath);
        Assert.Equal(Path.GetFullPath(savePath), applied.Paths.SavePath);
    }

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
            using var store = new ApplicationOverrideStore(root, Path.Combine(root, "backups"));
            var initial = await store.LoadAsync();
            var saved = await store.SaveAsync(Entry(), 0);
            using var reloader = new ApplicationOverrideStore(root, Path.Combine(root, "backups"));
            var reloaded = await reloader.LoadAsync();

            Assert.Equal(0, initial.Revision);
            Assert.Equal(1, saved.Revision);
            Assert.Equal("private-api-key", reloaded.Settings?.TmdbApiKey);
            Assert.Equal("private-read-token", reloaded.Settings?.TmdbReadAccessToken);
            Assert.True(reloaded.Settings?.AiReasoningEffortOverridden);
            Assert.Equal("medium", reloaded.Settings?.AiReasoningEffort);
            Assert.True(reloaded.Settings?.DataUpdateEnabled);
            Assert.Equal("0 15 4 * * ?", reloaded.Settings?.DataUpdateCron);
            Assert.Equal(
                "https://updates.test.invalid/manifest.json",
                reloaded.Settings?.DataUpdateManifestUrl);
            Assert.Single(Directory.GetFiles(root, "animego.yaml"));
            Assert.Empty(Directory.GetFiles(root, "application.private.json"));
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
    public async Task OverwriteAndDeletePreserveImmutableRevisionBackups()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "animegonet-application-backups",
            Guid.NewGuid().ToString("N"));
        var backups = Path.Combine(root, "backups");
        Directory.CreateDirectory(root);
        try
        {
            using var store = new ApplicationOverrideStore(root, backups);
            await store.SaveAsync(Entry(), 0);
            Assert.False(Directory.Exists(backups));

            await store.SaveAsync(Entry() with { TmdbLanguage = "ja-JP" }, 1);
            var revisionOne = Assert.Single(Directory.GetFiles(backups, "animego-application-r1-*.yaml"));
            var original = await File.ReadAllTextAsync(revisionOne);
            Assert.Contains("en-US", original, StringComparison.Ordinal);
            await store.DeleteAsync(2);
            var revisionTwo = Assert.Single(Directory.GetFiles(backups, "animego-application-r2-*.yaml"));
            Assert.Contains("ja-JP", await File.ReadAllTextAsync(revisionTwo), StringComparison.Ordinal);
            Assert.Equal(original, await File.ReadAllTextAsync(revisionOne));
            Assert.Empty(Directory.GetFiles(backups, "*.tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OtherYamlWritersDoNotCauseBackupRevisionConflicts()
    {
        var root = Path.Combine(Path.GetTempPath(), $"animegonet-yaml-shared-{Guid.NewGuid():N}");
        try
        {
            using var store = new ApplicationOverrideStore(root, Path.Combine(root, "backups"));
            await store.SaveAsync(Entry(), 0);
            using var downloader = new DownloaderOverrideStore(root);
            await downloader.UpsertAsync("bt", new DownloaderOverrideEntry(
                "http://localhost:8080", "user", "password", root, true, 0, DateTimeOffset.UtcNow), 0);
            await store.SaveAsync(Entry() with { TmdbLanguage = "ja-JP" }, 1);
            Assert.Equal("password", (await downloader.LoadAsync()).Downloaders["bt"].Password);
            Assert.Equal("ja-JP", (await store.LoadAsync()).Settings?.TmdbLanguage);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
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
            using (var store = new ApplicationOverrideStore(
                layout.ConfigurationPath,
                layout.BackupsPath,
                Path.Combine(layout.DataPath, "animego.yaml")))
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
            Assert.Equal(new Uri("http://127.0.0.1:7890/"), effective.OutboundProxy.Url);
            Assert.Equal(
                ["tmdb.test.invalid", "*.mikanime.tv"],
                effective.OutboundProxy.HostPatterns);
            Assert.Equal("en-US", effective.Metadata.Tmdb.Language);
            Assert.Equal("private-api-key", effective.Metadata.Tmdb.ApiKey);
            Assert.Equal("private-read-token", effective.Metadata.Tmdb.ReadAccessToken);
            Assert.True(effective.Metadata.SeasonFailure.Backtrace);
            Assert.True(effective.Metadata.Ai.UseMetadataMatch);
            Assert.Equal(TimeSpan.FromSeconds(600), effective.Metadata.Ai.HttpTimeout);
            Assert.Contains(
                "PRIVATE-PROMPT",
                effective.Metadata.Ai.PromptTemplate,
                StringComparison.Ordinal);
            Assert.Equal(
                new Uri("https://bangumi.test.invalid/api/"),
                effective.Metadata.Bangumi.BaseUrl);
            Assert.Equal(TimeSpan.FromSeconds(45), effective.Metadata.Bangumi.HttpTimeout);
            Assert.Equal(2, effective.TorrentFetch.MaxRedirects);
            Assert.True(effective.DataUpdate.Enabled);
            Assert.Equal("0 15 4 * * ?", effective.DataUpdate.Cron);
            Assert.Equal(
                new Uri("https://updates.test.invalid/manifest.json"),
                effective.DataUpdate.ManifestUrl);
            Assert.False(effective.DataUpdate.AutoDownload);
            Assert.False(effective.DataUpdate.AutoImport);
            Assert.Equal(4, effective.DataUpdate.KeepVersions);
            Assert.Equal(TimeSpan.FromSeconds(45), effective.DataUpdate.HttpTimeout);
            Assert.Equal(
                effective.DataUpdate,
                app.Services.GetRequiredService<DataUpdateRuntimeState>().Value);
            Assert.Equal(1, runtime.AppliedRevision);
            Assert.Equal("en-US", deployment.Value.Metadata.Tmdb.Language);
            Assert.Equal("private-api-key", deployment.Value.Metadata.Tmdb.ApiKey);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LegacyJsonIsNotReadOrMigrated()
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
            using var store = new ApplicationOverrideStore(root, Path.Combine(root, "backups"));
            var snapshot = await store.LoadAsync();
            var defaults = AnimeGoDefaults.CreateNative(root);
            defaults = defaults with
            {
                OutboundProxy = new OutboundProxyOptions
                {
                    Url = new Uri("http://127.0.0.1:7890/"),
                    HostPatterns = ["deployment.invalid"],
                },
                Metadata = defaults.Metadata with
                {
                    Bangumi = defaults.Metadata.Bangumi with
                    {
                        BaseUrl = new Uri("https://deployment-bangumi.invalid/"),
                    },
                },
                DataUpdate = defaults.DataUpdate with
                {
                    Enabled = true,
                    Cron = "0 10 4 * * ?",
                    ManifestUrl = new Uri("https://deployment-updates.invalid/manifest.json"),
                    AutoDownload = false,
                    AutoImport = false,
                    KeepVersions = 5,
                    HttpTimeout = TimeSpan.FromSeconds(55),
                },
            };

            var applied = ApplicationOverrideStore.Apply(defaults, snapshot);

            Assert.Equal(0, snapshot.Revision);
            Assert.Null(snapshot.Settings);
            Assert.Equal(legacy, await File.ReadAllTextAsync(Path.Combine(root, "application.private.json")));
            Assert.False(File.Exists(Path.Combine(root, "animego.yaml")));
            Assert.Equal(
                new Uri("http://127.0.0.1:7890/"),
                applied.OutboundProxy.Url);
            Assert.Equal(["deployment.invalid"], applied.OutboundProxy.HostPatterns);
            Assert.Equal(
                new Uri("https://deployment-bangumi.invalid/"),
                applied.Metadata.Bangumi.BaseUrl);
            Assert.Equal(defaults.DataUpdate, applied.DataUpdate);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InheritedSeasonFallbackAndTorrentFieldsKeepDeploymentValues()
    {
        var defaults = AnimeGoDefaults.CreateNative(Path.GetTempPath());
        var deployment = defaults with
        {
            Metadata = defaults.Metadata with
            {
                SeasonFailure = new SeasonFailureOptions
                {
                    Skip = true,
                    Backtrace = false,
                    UseTitleSeason = true,
                    UseFirstSeason = false,
                },
                TmdbFailureUseBangumi = true,
                WriteBangumiIdWhenTmdbMatched = true,
                MikanTrustedOffsetCacheEnabled = false,
            },
            TorrentFetch = defaults.TorrentFetch with
            {
                Timeout = TimeSpan.FromSeconds(61),
                MaxResponseBytes = 7654321,
                MaxRedirects = 1,
                StagingTtl = TimeSpan.FromSeconds(1201),
            },
        };
        var inherited = new[]
        {
            "season_failure_skip",
            "season_failure_backtrace",
            "season_failure_use_title_season",
            "season_failure_use_first_season",
            "tmdb_failure_use_bangumi",
            "write_bangumi_id_when_tmdb_matched",
            "mikan_trusted_offset_cache_enabled",
            "torrent_http_timeout_seconds",
            "torrent_max_response_bytes",
            "torrent_max_redirects",
            "torrent_staging_ttl_seconds",
        };

        var applied = ApplicationOverrideStore.Apply(
            deployment,
            new ApplicationOverrideSnapshot(
                1,
                1,
                Entry() with { InheritedFields = inherited }));

        Assert.Equal(deployment.Metadata.SeasonFailure, applied.Metadata.SeasonFailure);
        Assert.Equal(
            deployment.Metadata.TmdbFailureUseBangumi,
            applied.Metadata.TmdbFailureUseBangumi);
        Assert.Equal(
            deployment.Metadata.WriteBangumiIdWhenTmdbMatched,
            applied.Metadata.WriteBangumiIdWhenTmdbMatched);
        Assert.Equal(
            deployment.Metadata.MikanTrustedOffsetCacheEnabled,
            applied.Metadata.MikanTrustedOffsetCacheEnabled);
        Assert.Equal(deployment.TorrentFetch, applied.TorrentFetch);
    }

    [Fact]
    public void ReasoningEffortOverrideSupportsConfiguredLevelAndExplicitNone()
    {
        var defaults = AnimeGoDefaults.CreateNative(Path.GetTempPath());
        var medium = ApplicationOverrideStore.Apply(
            defaults,
            new ApplicationOverrideSnapshot(1, 1, Entry()));
        var none = ApplicationOverrideStore.Apply(
            defaults,
            new ApplicationOverrideSnapshot(
                1,
                2,
                Entry() with { AiReasoningEffort = null }));

        Assert.Equal("medium", medium.Metadata.Ai.ReasoningEffort);
        Assert.Null(none.Metadata.Ai.ReasoningEffort);
    }

    [Fact]
    public async Task CanonicalYamlCanBeLoadedByDeploymentLoaderWithAllEditableFields()
    {
        var root = Path.Combine(Path.GetTempPath(), $"animegonet-yaml-roundtrip-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var defaults = AnimeGoDefaults.CreateNative(root);
            var path = Path.Combine(root, "custom", "deployment.yaml");
            await DeploymentYamlConfiguration.LoadOrCreateAsync(path, defaults);
            using var store = new ApplicationOverrideStore(root, Path.Combine(root, "backups"), path);
            var entry = Entry() with
            {
                DownloadPath = Path.Combine(root, "new-downloads"), SavePath = Path.Combine(root, "new-tv"), MovieSavePath = Path.Combine(root, "new-movies"),
                MikanBaseUrl = "https://mikan.test.invalid/", MikanEpisodeIdentityCacheHours = 24, MikanBangumiIdentityCacheHours = 48,
                TmdbImageBaseUrl = "https://images.test.invalid/", TmdbCacheHours = 72,
                TmdbRetryCount = 3, TmdbRetryDelaySeconds = 0.5, BangumiRetryCount = 4, BangumiRetryDelaySeconds = 0.6,
                AiUseMetadataMatch = true, AiBaseUrlOverridden = true, AiBaseUrl = "https://ai.test.invalid/",
                AiApiKeyOverridden = true, AiApiKey = "quoted: secret # with spaces", AiModelOverridden = true, AiModel = "model-test",
                AiApiMode = AiApiMode.ChatCompletions, AiWebSearchEnabled = false, AiUseBangumiPubDateFirst = false,
                AiDebugMode = true, AiTmdbMcpUrl = "https://tmdb-mcp.test.invalid/mcp", AiBangumiMcpUrl = "https://bgm-mcp.test.invalid/mcp",
                AiFileIdentityFuzzyMatchLimit = 3, MikanTrustedOffsetRequiredEpisodes = 5,
            };
            await store.SaveAsync(entry, 0);
            var yaml = await DeploymentYamlConfiguration.LoadOrCreateAsync(path, defaults);
            var configuration = new ConfigurationManager();
            configuration.AddInMemoryCollection(yaml.Values);
            var loaded = AnimeGoApplication.LoadOptions(configuration, false);
            var expected = ApplicationOverrideStore.Apply(defaults, new ApplicationOverrideSnapshot(1, 1, entry));
            Assert.Equal(JsonSerializer.Serialize(expected.Metadata), JsonSerializer.Serialize(loaded.Metadata));
            Assert.Equal(expected.Paths, loaded.Paths);
            Assert.Equal(expected.TorrentFetch, loaded.TorrentFetch);
            Assert.Equal(expected.DataUpdate, loaded.DataUpdate);
            Assert.Equal(JsonSerializer.Serialize(expected.OutboundProxy), JsonSerializer.Serialize(loaded.OutboundProxy));
            Assert.False(File.Exists(Path.Combine(root, "animego.yaml")));
            Assert.False(File.Exists(Path.Combine(root, "application.private.json")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ApplicationCompositionUsesExplicitConfigForSettingsBackupPolicyAndReset()
    {
        var root = Path.Combine(Path.GetTempPath(), $"animegonet-yaml-config-arg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "deployment", "custom.yaml");
            var defaults = AnimeGoDefaults.CreateNative(root);
            await DeploymentYamlConfiguration.LoadOrCreateAsync(path, defaults);
            using (var initial = new ApplicationOverrideStore(root, Path.Combine(root, "backups"), path))
                await initial.SaveAsync(Entry(), 0);
            await using var app = await AnimeGoApplication.BuildAsync(
                ["--config", path], runningInContainer: false, startBackgroundWorkers: false);
            var store = app.Services.GetRequiredService<ApplicationOverrideStore>();
            var startup = app.Services.GetRequiredService<DeploymentConfigurationOptions>().Value;
            Assert.Equal("en-US", startup.Metadata.Tmdb.Language);
            await store.SaveAsync(Entry() with { TmdbLanguage = "ja-JP" }, 1);
            var backup = app.Services.GetRequiredService<ConfigurationBackupAutomationStore>();
            await backup.SaveAsync(new ConfigurationBackupAutomationPolicy(true, 6));
            await store.DeleteAsync(2);
            Assert.Equal("en-US", (await store.LoadAsync()).Settings?.TmdbLanguage);
            Assert.Equal(new ConfigurationBackupAutomationPolicy(true, 6), await backup.LoadAsync());
            using var rereader = new ApplicationOverrideStore(root, Path.Combine(root, "backups"), path);
            Assert.Equal("en-US", (await rereader.LoadAsync()).Settings?.TmdbLanguage);
            Assert.False(File.Exists(Path.Combine(defaults.Paths.DataPath, "animego.yaml")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task LockedRuntimeValuesNeverOverwriteYamlAndResetPreservesOtherSections()
    {
        var root = Path.Combine(Path.GetTempPath(), $"animegonet-yaml-lock-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.yaml");
        try
        {
            await File.WriteAllTextAsync(path, "version: 1.7.1\nmetadata:\n  tmdb:\n    base_url: https://yaml.invalid/\n    api_key: yaml-secret\n    language: zh-CN\ncustom:\n  untouched: yes\n");
            using var store = new ApplicationOverrideStore(root, Path.Combine(root, "backups"), path,
                DeploymentConfigurationLocks.FromVariableNames(["TMDB_API_KEY", "TMDB_BASE_URL"]));
            await store.SaveAsync(Entry(), 0);
            using var unlockedReader = new ApplicationOverrideStore(root, Path.Combine(root, "backups"), path);
            var saved = (await unlockedReader.LoadAsync()).Settings!;
            Assert.Equal("yaml-secret", saved.TmdbApiKey);
            Assert.Equal("https://yaml.invalid/", saved.TmdbBaseUrl);
            using var backup = new ConfigurationBackupAutomationStore(DirectoryLayout.From(AnimeGoDefaults.CreateNative(root).Paths), path);
            await backup.SaveAsync(new ConfigurationBackupAutomationPolicy(true, 7));
            await store.DeleteAsync(1);
            Assert.Equal("zh-CN", (await store.LoadAsync()).Settings?.TmdbLanguage);
            Assert.Equal(new ConfigurationBackupAutomationPolicy(true, 7), await backup.LoadAsync());
            Assert.Contains("untouched: yes", await File.ReadAllTextAsync(path), StringComparison.Ordinal);
            Assert.DoesNotContain("private-api-key", await File.ReadAllTextAsync(path), StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ApplicationDownloaderAndBackupPolicyCanSaveConcurrentlyToOneYaml()
    {
        var root = Path.Combine(Path.GetTempPath(), $"animegonet-yaml-parallel-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "custom.yaml");
        try
        {
            using var app = new ApplicationOverrideStore(root, Path.Combine(root, "backups"), path);
            using var downloader = new DownloaderOverrideStore(root, path);
            using var backup = new ConfigurationBackupAutomationStore(DirectoryLayout.From(AnimeGoDefaults.CreateNative(root).Paths), path);
            await Task.WhenAll(
                app.SaveAsync(Entry(), 0),
                downloader.UpsertAsync("bt", new DownloaderOverrideEntry("http://localhost:8080", "user", "password", root, true, 0, DateTimeOffset.UtcNow), 0),
                backup.SaveAsync(new ConfigurationBackupAutomationPolicy(true, 8)));
            Assert.Equal("en-US", (await app.LoadAsync()).Settings?.TmdbLanguage);
            Assert.Equal("password", (await downloader.LoadAsync()).Downloaders["bt"].Password);
            Assert.Equal(new ConfigurationBackupAutomationPolicy(true, 8), await backup.LoadAsync());
            Assert.Empty(Directory.GetFiles(root, "*.json"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FreshStoreSeesYamlEditsAndExplicitCredentialClearing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"animegonet-yaml-edits-{Guid.NewGuid():N}");
        try
        {
            using var app = new ApplicationOverrideStore(root, Path.Combine(root, "backups"));
            await app.SaveAsync(Entry(), 0);
            await app.SaveAsync(Entry() with { TmdbApiKey = null, AiReasoningEffort = null, DataUpdateManifestUrl = null }, 1);
            var path = Path.Combine(root, "animego.yaml");
            var contents = await File.ReadAllTextAsync(path);
            await File.WriteAllTextAsync(path, contents.Replace("en-US", "ja-JP", StringComparison.Ordinal));
            using var fresh = new ApplicationOverrideStore(root, Path.Combine(root, "backups"));
            var snapshot = await fresh.LoadAsync();
            Assert.Equal("ja-JP", snapshot.Settings?.TmdbLanguage);
            Assert.Null(snapshot.Settings?.TmdbApiKey);
            Assert.True(snapshot.Settings?.TmdbApiKeyOverridden);
            Assert.Null(snapshot.Settings?.AiReasoningEffort);
            Assert.True(snapshot.Settings?.AiReasoningEffortOverridden);
            Assert.Null(snapshot.Settings?.DataUpdateManifestUrl);
            Assert.True(snapshot.Settings?.DataUpdateManifestUrlOverridden);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
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
        BangumiBaseUrl: "https://bangumi.test.invalid/api/",
        BangumiHttpTimeoutSeconds: 45,
        DataUpdateEnabled: true,
        DataUpdateCron: "0 15 4 * * ?",
        DataUpdateManifestUrlOverridden: true,
        DataUpdateManifestUrl: "https://updates.test.invalid/manifest.json",
        DataUpdateAutoDownload: false,
        DataUpdateAutoImport: false,
        DataUpdateKeepVersions: 4,
        DataUpdateHttpTimeoutSeconds: 45,
        OutboundProxyUrlOverridden: true,
        OutboundProxyUrl: "http://127.0.0.1:7890/",
        OutboundProxyHosts: ["tmdb.test.invalid", "*.mikanime.tv"],
        WriteBangumiIdWhenTmdbMatched: true,
        WriteSeasonBangumiIdWhenTmdbMatched: true,
        AiPromptTemplate: AiMetadataPromptRenderer.LoadTemplate()
            .Replace("你是一个动画", "PRIVATE-PROMPT 你是一个动画", StringComparison.Ordinal),
        AiReasoningEffortOverridden: true,
        AiReasoningEffort: "medium");
}
