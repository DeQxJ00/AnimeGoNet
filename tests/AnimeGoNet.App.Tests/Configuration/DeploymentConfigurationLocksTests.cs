using AnimeGoNet.App.Configuration;
using AnimeGoNet.Core.Configuration;

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
        Assert.Equal(
            ["ai_use_episode_match"],
            Assert.Single(locks.Items, item => item.Field == "ai_use_metadata_match")
                .EnvironmentVariables);
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
}
