using AnimeGoNet.Core.Configuration;
using AnimeGoNet.App.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Configuration;

public sealed class AiDeploymentConfigurationTests
{
    [Fact]
    public async Task FlatDeploymentKeysConfigureAiWithoutStartingWorkers()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "animegonet-ai-deployment-config",
            Guid.NewGuid().ToString("N"));
        try
        {
            var prompt = AiMetadataPromptRenderer.LoadTemplate()
                .Replace("你是一个动画", "DEPLOYMENT-PROMPT 你是一个动画", StringComparison.Ordinal);
            await using var app = await AnimeGoApplication.BuildAsync(
                Args(root,
                    "--ai_provider=openai_compatible",
                    "--ai_base_url=https://ai.test.invalid/compatible/",
                    "--ai_api_key=deployment-secret",
                    "--ai_model=test-model",
                    $"--ai_prompt_template={prompt}",
                    "--ai_use_metadata_match=true",
                    "--ai_timeout_second=600",
                    "--ai_retry_count=3",
                    "--ai_use_bangumi_pubdate_first=false",
                    "--ai_tmdb_mcp_url=http://tmdb.test.invalid/mcp",
                    "--ai_bangumi_mcp_url=http://bgm.test.invalid/mcp"),
                runningInContainer: false,
                startBackgroundWorkers: false);

            var ai = app.Services.GetRequiredService<AnimeGoOptions>().Metadata.Ai;
            Assert.Equal(new Uri("https://ai.test.invalid/compatible/"), ai.BaseUrl);
            Assert.Equal("deployment-secret", ai.ApiKey);
            Assert.Equal("test-model", ai.Model);
            Assert.Equal(prompt, ai.PromptTemplate);
            Assert.True(ai.UseMetadataMatch);
            Assert.Equal(TimeSpan.FromSeconds(600), ai.HttpTimeout);
            Assert.Equal(3, ai.RetryCount);
            Assert.False(ai.UseBangumiPubDateFirst);
            Assert.Equal(new Uri("http://tmdb.test.invalid/mcp"), ai.TmdbMcpUrl);
            Assert.Equal(new Uri("http://bgm.test.invalid/mcp"), ai.BangumiMcpUrl);
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
    public async Task InvalidFlatBooleanFailsWithSafeConfigurationName()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "animegonet-ai-deployment-config",
            Guid.NewGuid().ToString("N"));
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => AnimeGoApplication.BuildAsync(
                    Args(root, "--ai_use_metadata_match=not-a-boolean"),
                    runningInContainer: false,
                    startBackgroundWorkers: false));

            Assert.Contains("ai_use_metadata_match", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("deployment-secret", exception.Message, StringComparison.Ordinal);
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
    public async Task AniDbMappingEndpointCannotBeRedirectedByDeploymentConfiguration()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "animegonet-ai-deployment-config",
            Guid.NewGuid().ToString("N"));
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => AnimeGoApplication.BuildAsync(
                    Args(
                        root,
                        "--ai_anidb_mapping_url_template=http://127.0.0.1/private/{anidbid}"),
                    runningInContainer: false,
                    startBackgroundWorkers: false));

            Assert.Contains("fixed", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("127.0.0.1", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("--ai_use_season_match=true")]
    [InlineData("--ai_use_episode_match=true")]
    public async Task LegacyAiSwitchesEnableUnifiedMetadataMatching(string legacyArgument)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "animegonet-ai-deployment-config",
            Guid.NewGuid().ToString("N"));
        try
        {
            await using var app = await AnimeGoApplication.BuildAsync(
                Args(root, legacyArgument),
                runningInContainer: false,
                startBackgroundWorkers: false);

            Assert.True(app.Services
                .GetRequiredService<AnimeGoOptions>()
                .Metadata.Ai.UseMetadataMatch);
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
    public async Task CanonicalAiSwitchOverridesLegacyAliases()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "animegonet-ai-deployment-config",
            Guid.NewGuid().ToString("N"));
        try
        {
            await using var app = await AnimeGoApplication.BuildAsync(
                Args(
                    root,
                    "--ai_use_metadata_match=false",
                    "--ai_use_season_match=true",
                    "--ai_use_episode_match=true"),
                runningInContainer: false,
                startBackgroundWorkers: false);

            Assert.False(app.Services
                .GetRequiredService<AnimeGoOptions>()
                .Metadata.Ai.UseMetadataMatch);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string[] Args(string root, params string[] extra) =>
    [
        $"--data_path={Path.Combine(root, "data")}",
        $"--download_path={Path.Combine(root, "download")}",
        $"--save_path={Path.Combine(root, "library")}",
        .. extra,
    ];
}
