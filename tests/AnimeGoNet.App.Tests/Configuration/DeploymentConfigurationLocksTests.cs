using AnimeGoNet.App.Configuration;
using AnimeGoNet.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Configuration;

public sealed class DeploymentConfigurationLocksTests
{
    [Fact]
    public void EnvironmentNamesAreCaseInsensitiveAndLegacyAiAliasesLockCanonicalSwitch()
    {
        var locks = DeploymentConfigurationLocks.FromVariableNames(
            ["TMDB_BASE_URL", "ai_use_episode_match", "unrelated"]);

        Assert.True(locks.IsLocked("tmdb_base_url"));
        Assert.True(locks.IsLocked("ai_use_metadata_match"));
        Assert.False(locks.IsLocked("tmdb_language"));
        Assert.Equal(
            ["TMDB_BASE_URL"],
            Assert.Single(locks.Items, item => item.Field == "tmdb_base_url")
                .EnvironmentVariables);

        var legacyTmdbKey = DeploymentConfigurationLocks.FromVariableNames(
            ["ANIMEGO_THEMOVIEDB_KEY"]);
        Assert.True(legacyTmdbKey.IsLocked("tmdb_api_key"));
        var legacyTmdbCache = DeploymentConfigurationLocks.FromVariableNames(
            ["advanced__cache__themoviedb_cache_hour"]);
        Assert.True(legacyTmdbCache.IsLocked("tmdb_cache_hours"));
        Assert.Equal(
            ["ai_use_episode_match"],
            Assert.Single(locks.Items, item => item.Field == "ai_use_metadata_match")
                .EnvironmentVariables);
    }

    [Fact]
    public void GlobalProxyUrlAndHostsHaveIndependentCanonicalLocks()
    {
        var locks = DeploymentConfigurationLocks.FromVariableNames(
            ["ANIMEGO_OUTBOUND_PROXY_URL", "ANIMEGO_OUTBOUND_PROXY_HOSTS"]);

        Assert.True(locks.IsLocked("outbound_proxy_url"));
        Assert.True(locks.IsLocked("outbound_proxy_hosts"));
        Assert.Equal(2, locks.Items.Count);
        Assert.Equal(
            ["ANIMEGO_OUTBOUND_PROXY_URL"],
            Assert.Single(locks.Items, item => item.Field == "outbound_proxy_url")
                .EnvironmentVariables);
    }

    [Fact]
    public void AiProviderAndMcpFieldsHaveIndependentDeploymentLocks()
    {
        var locks = DeploymentConfigurationLocks.FromVariableNames(
        [
            "ai_base_url",
            "ai_api_key",
            "ai_model",
            "ai_prompt_template",
            "ai_tmdb_mcp_url",
            "ai_bangumi_mcp_url",
        ]);

        Assert.Equal(
            ["ai_api_key", "ai_bangumi_mcp_url", "ai_base_url", "ai_model", "ai_prompt_template", "ai_tmdb_mcp_url"],
            locks.Items.Select(item => item.Field).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void CanonicalTmdbCacheLockReappliesDeploymentTtl()
    {
        var deployment = AnimeGoDefaults.CreateDocker();
        var candidate = deployment with
        {
            Metadata = deployment.Metadata with
            {
                Tmdb = deployment.Metadata.Tmdb with
                {
                    CacheTtl = TimeSpan.FromHours(12),
                },
            },
        };
        var locks = DeploymentConfigurationLocks.FromVariableNames(
            ["metadata__tmdb__cache_hours"]);

        var result = locks.Reapply(deployment, candidate);

        Assert.Equal(TimeSpan.FromHours(144), result.Metadata.Tmdb.CacheTtl);
        Assert.Equal(
            ["tmdb_cache_hours"],
            locks.FindChangedLockedFields(deployment, candidate));
    }

    [Fact]
    public void CanonicalEnvironmentAndCommandLineAliasesShareOneSafeLockProjection()
    {
        var locks = DeploymentConfigurationLocks.FromSources(
            ["metadata__tmdb__base_url"],
            ["--tmdb_base_url=https://command.invalid/private"]);

        var value = Assert.Single(locks.Items);
        Assert.Equal("tmdb_base_url", value.Field);
        Assert.Equal("environment_and_command_line", value.Source);
        Assert.Equal(["metadata__tmdb__base_url"], value.EnvironmentVariables);
        Assert.Equal(["--tmdb_base_url"], value.CommandLineArguments);
        Assert.Equal(
            ["--tmdb_base_url", "metadata__tmdb__base_url"],
            value.ControllingKeys);
        Assert.DoesNotContain(
            value.ControllingKeys,
            key => key.Contains("command.invalid", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplicationCompositionDetectsAndAppliesActualCommandLineLock()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "animegonet-command-line-locks",
            Guid.NewGuid().ToString("N"));
        try
        {
            await using var app = await AnimeGoApplication.BuildAsync(
            [
                $"--data_path={Path.Combine(root, "data")}",
                $"--download_path={Path.Combine(root, "download")}",
                $"--save_path={Path.Combine(root, "library")}",
                "--background_workers_enabled=false",
                "--tmdb_fail_backtrace=true",
            ],
                runningInContainer: false,
                startBackgroundWorkers: false);

            var options = app.Services.GetRequiredService<AnimeGoOptions>();
            var locks = app.Services.GetRequiredService<DeploymentConfigurationLocks>();

            Assert.True(options.Metadata.SeasonFailure.Backtrace);
            var value = Assert.Single(
                locks.Items,
                item => item.Field == "season_failure_backtrace");
            Assert.Equal("command_line", value.Source);
            Assert.Equal(["--tmdb_fail_backtrace"], value.CommandLineArguments);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ReapplyRestoresOnlyEnvironmentControlledDeploymentValues()
    {
        var defaults = AnimeGoDefaults.CreateNative(Path.GetTempPath());
        var deployment = defaults with
        {
            Metadata = defaults.Metadata with
            {
                Tmdb = defaults.Metadata.Tmdb with
                {
                    BaseUrl = new Uri("https://environment.invalid/tmdb/"),
                    Language = "ja-JP",
                    ApiKey = "environment-secret",
                },
                Ai = defaults.Metadata.Ai with
                {
                    UseMetadataMatch = true,
                    HttpTimeout = TimeSpan.FromSeconds(601),
                },
            },
        };
        var candidate = deployment with
        {
            Metadata = deployment.Metadata with
            {
                Tmdb = deployment.Metadata.Tmdb with
                {
                    BaseUrl = new Uri("https://private.invalid/tmdb/"),
                    Language = "en-US",
                    ApiKey = "private-secret",
                },
                Ai = deployment.Metadata.Ai with
                {
                    UseMetadataMatch = false,
                    HttpTimeout = TimeSpan.FromSeconds(99),
                },
            },
        };
        var locks = DeploymentConfigurationLocks.FromVariableNames(
            ["tmdb_base_url", "tmdb_api_key", "ai_use_metadata_match"]);

        var result = locks.Reapply(deployment, candidate);

        Assert.Equal(deployment.Metadata.Tmdb.BaseUrl, result.Metadata.Tmdb.BaseUrl);
        Assert.Equal("environment-secret", result.Metadata.Tmdb.ApiKey);
        Assert.True(result.Metadata.Ai.UseMetadataMatch);
        Assert.Equal("en-US", result.Metadata.Tmdb.Language);
        Assert.Equal(TimeSpan.FromSeconds(99), result.Metadata.Ai.HttpTimeout);
        Assert.Equal(
            ["tmdb_base_url", "ai_use_metadata_match"],
            locks.FindChangedLockedFields(deployment, candidate));
    }

    [Fact]
    public void AllDataUpdateEnvironmentFieldsAreLockedAndReapplied()
    {
        var defaults = AnimeGoDefaults.CreateNative(Path.GetTempPath());
        var deployment = defaults with
        {
            DataUpdate = defaults.DataUpdate with
            {
                Enabled = true,
                Cron = "0 5 4 * * ?",
                ManifestUrl = new Uri("https://environment.invalid/manifest.json"),
                AutoDownload = false,
                AutoImport = false,
                KeepVersions = 4,
                HttpTimeout = TimeSpan.FromSeconds(45),
            },
        };
        var candidate = deployment with
        {
            DataUpdate = deployment.DataUpdate with
            {
                Enabled = false,
                Cron = "0 15 5 * * ?",
                ManifestUrl = new Uri("https://private.invalid/manifest.json"),
                AutoDownload = true,
                AutoImport = true,
                KeepVersions = 2,
                HttpTimeout = TimeSpan.FromSeconds(90),
            },
        };
        var locks = DeploymentConfigurationLocks.FromVariableNames(
        [
            "DATA_UPDATE_ENABLED",
            "DATA_UPDATE_CRON",
            "DATA_UPDATE_MANIFEST_URL",
            "DATA_UPDATE_AUTO_DOWNLOAD",
            "DATA_UPDATE_AUTO_IMPORT",
            "DATA_UPDATE_KEEP_VERSIONS",
            "DATA_UPDATE_TIMEOUT_SECOND",
        ]);

        var result = locks.Reapply(deployment, candidate);

        Assert.Equal(deployment.DataUpdate, result.DataUpdate);
        Assert.Equal(
        [
            "data_update_enabled",
            "data_update_cron",
            "data_update_manifest_url",
            "data_update_auto_download",
            "data_update_auto_import",
            "data_update_keep_versions",
            "data_update_http_timeout_seconds",
        ],
            locks.FindChangedLockedFields(deployment, candidate));
        Assert.All(
            locks.Items,
            item => Assert.Contains(
                item.EnvironmentVariables,
                name => name.StartsWith("DATA_UPDATE_", StringComparison.Ordinal)));
    }

    [Fact]
    public void MetadataRetryEnvironmentFieldsAreLockedAndReapplied()
    {
        var deployment = AnimeGoDefaults.CreateNative(Path.GetTempPath());
        var candidate = deployment with
        {
            Metadata = deployment.Metadata with
            {
                Tmdb = deployment.Metadata.Tmdb with
                {
                    RetryCount = 1,
                    RetryDelay = TimeSpan.FromSeconds(2),
                },
                Bangumi = deployment.Metadata.Bangumi with
                {
                    RetryCount = 6,
                    RetryDelay = TimeSpan.FromSeconds(7),
                },
            },
        };
        var locks = DeploymentConfigurationLocks.FromVariableNames(
        [
            "TMDB_RETRY_COUNT",
            "TMDB_RETRY_WAIT_SECOND",
            "BANGUMI_RETRY_COUNT",
            "BANGUMI_RETRY_WAIT_SECOND",
        ]);

        var result = locks.Reapply(deployment, candidate);

        Assert.Equal(deployment.Metadata.Tmdb, result.Metadata.Tmdb);
        Assert.Equal(deployment.Metadata.Bangumi, result.Metadata.Bangumi);
        Assert.Equal(
        [
            "tmdb_retry_count",
            "tmdb_retry_delay_seconds",
            "bangumi_retry_count",
            "bangumi_retry_delay_seconds",
        ],
            locks.FindChangedLockedFields(deployment, candidate));
    }

    [Fact]
    public void AllEditableSeasonFallbackAndTorrentFieldsAreLockedAndReapplied()
    {
        var defaults = AnimeGoDefaults.CreateNative(Path.GetTempPath());
        var deployment = defaults with
        {
            Metadata = defaults.Metadata with
            {
                SeasonFailure = new SeasonFailureOptions
                {
                    Skip = true,
                    Backtrace = true,
                    UseTitleSeason = true,
                    UseFirstSeason = true,
                },
                TmdbFailureUseBangumi = true,
                MikanTrustedOffsetCacheEnabled = true,
            },
            TorrentFetch = defaults.TorrentFetch with
            {
                Timeout = TimeSpan.FromSeconds(41),
                MaxResponseBytes = 123456,
                MaxRedirects = 2,
                StagingTtl = TimeSpan.FromSeconds(901),
            },
        };
        var candidate = deployment with
        {
            Metadata = deployment.Metadata with
            {
                SeasonFailure = new SeasonFailureOptions(),
                TmdbFailureUseBangumi = false,
                MikanTrustedOffsetCacheEnabled = false,
            },
            TorrentFetch = deployment.TorrentFetch with
            {
                Timeout = TimeSpan.FromSeconds(9),
                MaxResponseBytes = 654321,
                MaxRedirects = 1,
                StagingTtl = TimeSpan.FromSeconds(99),
            },
        };
        var locks = DeploymentConfigurationLocks.FromVariableNames(
        [
            "TMDB_FAIL_SKIP",
            "TMDB_FAIL_BACKTRACE",
            "TMDB_FAIL_USE_TITLE_SEASON",
            "TMDB_FAIL_USE_FIRST_SEASON",
            "TMDB_FAIL_USE_BANGUMI",
            "MIKAN_TRUSTED_OFFSET_CACHE_ENABLED",
            "TORRENT_HTTP_TIMEOUT_SECONDS",
            "TORRENT_MAX_RESPONSE_BYTES",
            "TORRENT_MAX_REDIRECTS",
            "TORRENT_STAGING_TTL_SECONDS",
        ]);

        var result = locks.Reapply(deployment, candidate);

        Assert.Equal(deployment.Metadata.SeasonFailure, result.Metadata.SeasonFailure);
        Assert.Equal(
            deployment.Metadata.TmdbFailureUseBangumi,
            result.Metadata.TmdbFailureUseBangumi);
        Assert.Equal(
            deployment.Metadata.MikanTrustedOffsetCacheEnabled,
            result.Metadata.MikanTrustedOffsetCacheEnabled);
        Assert.Equal(deployment.TorrentFetch, result.TorrentFetch);
        Assert.Equal(10, locks.FindChangedLockedFields(deployment, candidate).Count);
    }
}
