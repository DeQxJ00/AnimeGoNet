using System.Net;
using System.Text.Json;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Tests.Api;

public sealed class ConfigurationApiTests
{
    [Fact]
    public async Task EffectiveConfigurationIsTypedAndNeverReturnsCredentials()
    {
        await using var app = await RunningApp.StartAsync(
            accessKey: "local-access-secret",
            configure: options => options with
            {
                Metadata = options.Metadata with
                {
                    Tmdb = options.Metadata.Tmdb with
                    {
                        ApiKey = "tmdb-api-secret",
                        ReadAccessToken = "tmdb-bearer-secret",
                        Language = "ja-JP",
                    },
                    SeasonFailure = new SeasonFailureOptions
                    {
                        Skip = true,
                        Backtrace = true,
                        UseTitleSeason = true,
                        UseFirstSeason = false,
                    },
                    Ai = options.Metadata.Ai with
                    {
                        UseSeasonMatch = true,
                        UseEpisodeMatch = false,
                    },
                    TmdbFailureUseBangumi = true,
                    MikanTrustedOffsetCacheEnabled = true,
                },
                TorrentFetch = options.TorrentFetch with
                {
                    MaxResponseBytes = 123456,
                    MaxRedirects = 2,
                },
            });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await app.Client.GetAsync("/api/v1/config")).StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/config");
        request.Headers.Add("X-AnimeGo-Access-Key", "local-access-secret");
        using var response = await app.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(text);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            Path.Combine(app.RootPath, "data"),
            json.RootElement.GetProperty("paths").GetProperty("data_path").GetString());
        var deployment = json.RootElement.GetProperty("deployment");
        Assert.False(deployment.GetProperty("running_in_container").GetBoolean());
        Assert.False(deployment.GetProperty("background_workers_enabled").GetBoolean());
        Assert.True(deployment.GetProperty("access_key_configured").GetBoolean());
        Assert.True(deployment.GetProperty("paths_restart_required").GetBoolean());
        var metadata = json.RootElement.GetProperty("metadata");
        var tmdb = metadata.GetProperty("tmdb");
        Assert.Equal("ja-JP", tmdb.GetProperty("language").GetString());
        Assert.True(tmdb.GetProperty("api_key_configured").GetBoolean());
        Assert.True(tmdb.GetProperty("read_access_token_configured").GetBoolean());
        Assert.True(metadata.GetProperty("season_failure").GetProperty("skip").GetBoolean());
        Assert.True(metadata.GetProperty("ai").GetProperty("use_season_match").GetBoolean());
        Assert.False(metadata.GetProperty("ai").GetProperty("use_episode_match").GetBoolean());
        Assert.Equal(600, metadata.GetProperty("ai").GetProperty("http_timeout_seconds").GetDouble());
        Assert.Equal(
            123456,
            json.RootElement.GetProperty("torrent_fetch").GetProperty("max_response_bytes").GetInt64());
        Assert.DoesNotContain("local-access-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tmdb-api-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tmdb-bearer-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaticWebUiLoadsRedactedConfigurationPanel()
    {
        await using var app = await RunningApp.StartAsync();

        var html = await app.Client.GetStringAsync("/");
        var script = await app.Client.GetStringAsync("/app.js");

        Assert.Contains("id=\"configuration\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-reload\"", html, StringComparison.Ordinal);
        Assert.Contains("/api/v1/config", script, StringComparison.Ordinal);
        Assert.Contains("loadConfiguration", script, StringComparison.Ordinal);
        Assert.Contains("凭据永不回传", script, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
    }
}
