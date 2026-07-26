using System.Net;
using System.Text;
using System.Text.Json;
using AnimeGoNet.App.Configuration;
using AnimeGoNet.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
                        BaseUrl = new Uri("https://ai.test.invalid/compatible/"),
                        ApiKey = "ai-api-secret",
                        Model = "test-model",
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
            "openai_compatible",
            metadata.GetProperty("ai").GetProperty("provider").GetString());
        Assert.Equal(
            "https://ai.test.invalid/compatible/",
            metadata.GetProperty("ai").GetProperty("base_url").GetString());
        Assert.Equal("test-model", metadata.GetProperty("ai").GetProperty("model").GetString());
        Assert.True(metadata.GetProperty("ai").GetProperty("api_key_configured").GetBoolean());
        Assert.Equal(2, metadata.GetProperty("ai").GetProperty("retry_count").GetInt32());
        Assert.True(metadata.GetProperty("ai")
            .GetProperty("use_bangumi_pubdate_first").GetBoolean());
        Assert.Equal(
            "http://tmdb.mcp.local/mcp",
            metadata.GetProperty("ai").GetProperty("tmdb_mcp_url").GetString());
        Assert.Equal(
            123456,
            json.RootElement.GetProperty("torrent_fetch").GetProperty("max_response_bytes").GetInt64());
        Assert.DoesNotContain("local-access-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tmdb-api-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tmdb-bearer-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ai-api-secret", text, StringComparison.Ordinal);
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
        Assert.Contains("id=\"configuration-dialog\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-form\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-tmdb-key-clear\"", html, StringComparison.Ordinal);
        Assert.Contains("saveConfiguration", script, StringComparison.Ordinal);
        Assert.Contains("resetConfiguration", script, StringComparison.Ordinal);
        Assert.Contains("expected_configuration_revision", script, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrivateConfigurationUsesRevisionAndSecretTriState()
    {
        await using var app = await RunningApp.StartAsync();
        using var first = await app.Client.PutAsync(
            "/api/v1/config",
            Payload(
                expectedRevision: 0,
                apiKey: "new-api-secret",
                readToken: "new-read-secret"));
        var firstText = await first.Content.ReadAsStringAsync();
        using var firstJson = JsonDocument.Parse(firstText);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(1, firstJson.RootElement.GetProperty("configuration_revision").GetInt64());
        Assert.True(firstJson.RootElement.GetProperty("restart_required").GetBoolean());
        Assert.DoesNotContain("new-api-secret", firstText, StringComparison.Ordinal);
        Assert.DoesNotContain("new-read-secret", firstText, StringComparison.Ordinal);

        using (var currentResponse = await app.Client.GetAsync("/api/v1/config"))
        using (var current = JsonDocument.Parse(await currentResponse.Content.ReadAsStreamAsync()))
        {
            Assert.Equal(1, current.RootElement.GetProperty("configuration_revision").GetInt64());
            Assert.Equal(0, current.RootElement.GetProperty("applied_configuration_revision").GetInt64());
            Assert.True(current.RootElement.GetProperty("restart_required").GetBoolean());
            Assert.False(current.RootElement.GetProperty("metadata")
                .GetProperty("tmdb").GetProperty("api_key_configured").GetBoolean());
            var editable = current.RootElement.GetProperty("editable");
            Assert.Equal("configured", editable.GetProperty("tmdb_api_key_state").GetString());
            Assert.Equal(
                "configured",
                editable.GetProperty("tmdb_read_access_token_state").GetString());
        }

        var store = app.App.Services.GetRequiredService<ApplicationOverrideStore>();
        var saved = await store.LoadAsync();
        Assert.Equal("new-api-secret", saved.Settings?.TmdbApiKey);
        Assert.Equal("new-read-secret", saved.Settings?.TmdbReadAccessToken);

        using var preserve = await app.Client.PutAsync(
            "/api/v1/config",
            Payload(expectedRevision: 1, aiEpisode: true));
        Assert.Equal(HttpStatusCode.OK, preserve.StatusCode);
        var preserved = await store.LoadAsync();
        Assert.Equal("new-api-secret", preserved.Settings?.TmdbApiKey);
        Assert.Equal("new-read-secret", preserved.Settings?.TmdbReadAccessToken);
        Assert.True(preserved.Settings?.AiUseEpisodeMatch);
        using (var desiredResponse = await app.Client.GetAsync("/api/v1/config"))
        using (var desired = JsonDocument.Parse(await desiredResponse.Content.ReadAsStreamAsync()))
        {
            Assert.True(desired.RootElement.GetProperty("editable")
                .GetProperty("ai_use_episode_match").GetBoolean());
        }

        using var clear = await app.Client.PutAsync(
            "/api/v1/config",
            Payload(expectedRevision: 2, clearApiKey: true));
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        var cleared = await store.LoadAsync();
        Assert.True(cleared.Settings?.TmdbApiKeyOverridden);
        Assert.Null(cleared.Settings?.TmdbApiKey);
        Assert.Equal("new-read-secret", cleared.Settings?.TmdbReadAccessToken);
        using (var clearedResponse = await app.Client.GetAsync("/api/v1/config"))
        using (var clearedJson = JsonDocument.Parse(await clearedResponse.Content.ReadAsStreamAsync()))
        {
            Assert.Equal(
                "cleared",
                clearedJson.RootElement.GetProperty("editable")
                    .GetProperty("tmdb_api_key_state").GetString());
        }

        using var conflict = await app.Client.PutAsync(
            "/api/v1/config",
            Payload(expectedRevision: 1));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        using var reset = await app.Client.DeleteAsync(
            "/api/v1/config?expected_revision=3");
        using var resetJson = JsonDocument.Parse(await reset.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(4, resetJson.RootElement.GetProperty("configuration_revision").GetInt64());
        Assert.True(resetJson.RootElement.GetProperty("reverted_to_deployment_default").GetBoolean());
        Assert.Null((await store.LoadAsync()).Settings);
        using var resetConfigResponse = await app.Client.GetAsync("/api/v1/config");
        using var resetConfig = JsonDocument.Parse(
            await resetConfigResponse.Content.ReadAsStreamAsync());
        Assert.Equal(
            "inherit",
            resetConfig.RootElement.GetProperty("editable")
                .GetProperty("tmdb_api_key_state").GetString());
        Assert.Equal(
            "https://api.themoviedb.org/",
            resetConfig.RootElement.GetProperty("editable")
                .GetProperty("tmdb_base_url").GetString());
    }

    [Fact]
    public async Task InvalidPrivateConfigurationDoesNotWriteSecretFile()
    {
        await using var app = await RunningApp.StartAsync();

        using var credentialUrl = await app.Client.PutAsync(
            "/api/v1/config",
            Payload(
                expectedRevision: 0,
                baseUrl: "https://user:password@api.themoviedb.org/"));
        using var conflictingSecret = await app.Client.PutAsync(
            "/api/v1/config",
            Payload(
                expectedRevision: 0,
                apiKey: "must-not-be-written",
                clearApiKey: true));

        Assert.Equal(HttpStatusCode.BadRequest, credentialUrl.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, conflictingSecret.StatusCode);
        var snapshot = await app.App.Services
            .GetRequiredService<ApplicationOverrideStore>()
            .LoadAsync();
        Assert.Equal(0, snapshot.Revision);
        Assert.Null(snapshot.Settings);
        Assert.False(File.Exists(Path.Combine(
            app.RootPath,
            "data",
            "config",
            "application.private.json")));
    }

    private static StringContent Payload(
        long expectedRevision,
        string? apiKey = null,
        string? readToken = null,
        bool clearApiKey = false,
        bool aiEpisode = false,
        string baseUrl = "https://api.themoviedb.org/")
    {
        var json = JsonSerializer.Serialize(new
        {
            tmdb_base_url = baseUrl,
            tmdb_language = "zh-CN",
            tmdb_http_timeout_seconds = 30,
            tmdb_api_key = apiKey,
            clear_tmdb_api_key = clearApiKey,
            tmdb_read_access_token = readToken,
            clear_tmdb_read_access_token = false,
            season_failure_skip = false,
            season_failure_backtrace = true,
            season_failure_use_title_season = true,
            season_failure_use_first_season = true,
            ai_use_season_match = false,
            ai_use_episode_match = aiEpisode,
            ai_http_timeout_seconds = 600,
            tmdb_failure_use_bangumi = false,
            mikan_trusted_offset_cache_enabled = false,
            torrent_http_timeout_seconds = 30,
            torrent_max_response_bytes = 16 * 1024 * 1024,
            torrent_max_redirects = 3,
            torrent_staging_ttl_seconds = 900,
            expected_configuration_revision = expectedRevision,
        });
        return new StringContent(json, Encoding.UTF8, "application/json");
    }
}
