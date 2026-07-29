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
}
