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
                        BaseUrl = new Uri("https://metadata.test.invalid/tmdb/"),
                        ProxyUrl = new Uri("http://127.0.0.1:7890/"),
                        ApiKey = "tmdb-api-secret",
                        ReadAccessToken = "tmdb-bearer-secret",
                        Language = "ja-JP",
                    },
                    Bangumi = options.Metadata.Bangumi with
                    {
                        BaseUrl = new Uri("https://metadata.test.invalid/bangumi/"),
                        ProxyUrl = new Uri("socks5://127.0.0.1:1080/"),
                        HttpTimeout = TimeSpan.FromSeconds(45),
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
                        UseMetadataMatch = true,
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
        Assert.Equal(
            "https://metadata.test.invalid/tmdb/",
            tmdb.GetProperty("base_url").GetString());
        Assert.Equal("http://127.0.0.1:7890/", tmdb.GetProperty("proxy_url").GetString());
        Assert.True(tmdb.GetProperty("api_key_configured").GetBoolean());
        Assert.True(tmdb.GetProperty("read_access_token_configured").GetBoolean());
        var bangumi = metadata.GetProperty("bangumi");
        Assert.Equal(
            "https://metadata.test.invalid/bangumi/",
            bangumi.GetProperty("base_url").GetString());
        Assert.Equal(
            "socks5://127.0.0.1:1080/",
            bangumi.GetProperty("proxy_url").GetString());
        Assert.Equal(45, bangumi.GetProperty("http_timeout_seconds").GetDouble());
        Assert.True(metadata.GetProperty("season_failure").GetProperty("skip").GetBoolean());
        Assert.True(metadata.GetProperty("ai").GetProperty("use_metadata_match").GetBoolean());
        Assert.True(metadata.GetProperty("ai").GetProperty("use_season_match").GetBoolean());
        Assert.True(metadata.GetProperty("ai").GetProperty("use_episode_match").GetBoolean());
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
        Assert.Contains("id=\"configuration-lock-summary\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-tmdb-key-clear\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-tmdb-proxy\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-bangumi-url\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-bangumi-proxy\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"TMDB 季度失败优先级\"", html, StringComparison.Ordinal);
        Assert.Contains("data-priority=\"4\"", html, StringComparison.Ordinal);
        Assert.Contains("data-priority=\"3\"", html, StringComparison.Ordinal);
        Assert.Contains("data-priority=\"independent\"", html, StringComparison.Ordinal);
        Assert.Contains("data-priority=\"2\"", html, StringComparison.Ordinal);
        Assert.Contains("data-priority=\"1\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-ai-metadata\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"configuration-ai-season\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"configuration-ai-episode\"", html, StringComparison.Ordinal);
        Assert.Contains("一个任务只使用一个提示词", html, StringComparison.Ordinal);
        Assert.Contains("默认关闭，不占确定性优先级", html, StringComparison.Ordinal);
        Assert.Contains("需要 bgmid；当前 tmdbid + Season 联合匹配失败后", html, StringComparison.Ordinal);
        Assert.Contains("用每个前作的日文名、中文名和开播日期重新搜索并验证完整 tmdbid + Season", html, StringComparison.Ordinal);
        Assert.Contains("TMDBFailUseTitleSeason", html, StringComparison.Ordinal);
        Assert.Contains("只用本地标题解析器读取任务 title", html, StringComparison.Ordinal);
        Assert.Contains("不验证 TMDB Season；解析不到继续 P1", html, StringComparison.Ordinal);
        Assert.Contains("TMDBFailUseFirstSeason", html, StringComparison.Ordinal);
        Assert.Contains("勾选即使用本地 S01，不验证 TMDB Season", html, StringComparison.Ordinal);
        Assert.Contains("Bangumi 完全兜底（一般不启用这个）", html, StringComparison.Ordinal);
        Assert.Contains("季度固定 S01；需要 bgmid；不输出有效 tmdbid", html, StringComparison.Ordinal);
        Assert.Contains("bangumi_proxy_url", script, StringComparison.Ordinal);
        Assert.Contains("seasonFailurePriority", script, StringComparison.Ordinal);
        Assert.Contains(
            "一个任务、一个提示词，统一返回并验证 TMDB Series、Season 和全部文件的 Episode",
            script,
            StringComparison.Ordinal);
        Assert.Contains("需要 bgmid；当前 tmdbid + Season 联合匹配失败后", script, StringComparison.Ordinal);
        Assert.Contains("用每个前作的日文名、中文名和开播日期重新搜索并验证完整 tmdbid + Season", script, StringComparison.Ordinal);
        Assert.Contains("TMDBFailUseTitleSeason", script, StringComparison.Ordinal);
        Assert.Contains("只用本地标题解析器读取任务 title", script, StringComparison.Ordinal);
        Assert.Contains("不验证 TMDB Season；解析不到继续 P1", script, StringComparison.Ordinal);
        Assert.Contains("TMDBFailUseFirstSeason", script, StringComparison.Ordinal);
        Assert.Contains("勾选即使用本地 S01，不验证 TMDB Season", script, StringComparison.Ordinal);
        Assert.Contains("Bangumi 完全兜底（一般不启用这个）", script, StringComparison.Ordinal);
        Assert.Contains("内部仍按现有逻辑写 0", script, StringComparison.Ordinal);
        Assert.Contains("ai_use_metadata_match", script, StringComparison.Ordinal);
        Assert.DoesNotContain("configuration-ai-season", script, StringComparison.Ordinal);
        Assert.DoesNotContain("configuration-ai-episode", script, StringComparison.Ordinal);
        Assert.Contains("saveConfiguration", script, StringComparison.Ordinal);
        Assert.Contains("resetConfiguration", script, StringComparison.Ordinal);
        Assert.Contains("expected_configuration_revision", script, StringComparison.Ordinal);
        Assert.Contains("locked_fields", script, StringComparison.Ordinal);
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
                readToken: "new-read-secret",
                tmdbProxy: "http://127.0.0.1:7890/",
                bangumiBase: "https://metadata.test.invalid/bangumi/",
                bangumiProxy: "socks5://127.0.0.1:1080/"));
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
        Assert.Equal("http://127.0.0.1:7890/", saved.Settings?.TmdbProxyUrl);
        Assert.Equal(
            "https://metadata.test.invalid/bangumi/",
            saved.Settings?.BangumiBaseUrl);
        Assert.Equal("socks5://127.0.0.1:1080/", saved.Settings?.BangumiProxyUrl);

        using var preserve = await app.Client.PutAsync(
            "/api/v1/config",
            Payload(
                expectedRevision: 1,
                aiMetadata: true,
                tmdbProxy: "http://127.0.0.1:7890/",
                bangumiBase: "https://metadata.test.invalid/bangumi/",
                bangumiProxy: "socks5://127.0.0.1:1080/"));
        Assert.Equal(HttpStatusCode.OK, preserve.StatusCode);
        var preserved = await store.LoadAsync();
        Assert.Equal("new-api-secret", preserved.Settings?.TmdbApiKey);
        Assert.Equal("new-read-secret", preserved.Settings?.TmdbReadAccessToken);
        Assert.True(preserved.Settings?.AiUseMetadataMatch);
        using (var desiredResponse = await app.Client.GetAsync("/api/v1/config"))
        using (var desired = JsonDocument.Parse(await desiredResponse.Content.ReadAsStreamAsync()))
        {
            Assert.True(desired.RootElement.GetProperty("editable")
                .GetProperty("ai_use_metadata_match").GetBoolean());
            Assert.Equal(
                "https://metadata.test.invalid/bangumi/",
                desired.RootElement.GetProperty("editable")
                    .GetProperty("bangumi_base_url").GetString());
        }

        using var clear = await app.Client.PutAsync(
            "/api/v1/config",
            Payload(
                expectedRevision: 2,
                clearApiKey: true,
                tmdbProxy: "http://127.0.0.1:7890/",
                bangumiBase: "https://metadata.test.invalid/bangumi/",
                bangumiProxy: "socks5://127.0.0.1:1080/"));
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
    public async Task LegacyAiUpdateFieldsEnableTheUnifiedSwitch()
    {
        await using var app = await RunningApp.StartAsync();
        using var response = await app.Client.PutAsync(
            "/api/v1/config",
            Payload(
                expectedRevision: 0,
                aiMetadata: null,
                legacyAiEpisode: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await app.App.Services
            .GetRequiredService<ApplicationOverrideStore>()
            .LoadAsync();
        Assert.True(saved.Settings?.AiUseMetadataMatch);
        Assert.True(saved.Settings?.AiUseSeasonMatch);
        Assert.True(saved.Settings?.AiUseEpisodeMatch);
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
        using var credentialProxy = await app.Client.PutAsync(
            "/api/v1/config",
            Payload(
                expectedRevision: 0,
                tmdbProxy: "http://user:password@proxy.invalid/"));
        using var invalidBangumiBase = await app.Client.PutAsync(
            "/api/v1/config",
            Payload(
                expectedRevision: 0,
                bangumiBase: "https://api.bgm.tv/no-trailing-slash"));

        Assert.Equal(HttpStatusCode.BadRequest, credentialUrl.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, conflictingSecret.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, credentialProxy.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidBangumiBase.StatusCode);
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

    [Fact]
    public async Task EnvironmentControlledFieldsAreProjectedReadOnlyAndRejectedOnWrite()
    {
        const string environmentApiKey = "environment-only-secret";
        var environmentBaseUrl = new Uri("https://environment.invalid/tmdb/");
        await using var app = await RunningApp.StartAsync(
            configure: options => options with
            {
                Metadata = options.Metadata with
                {
                    Tmdb = options.Metadata.Tmdb with
                    {
                        BaseUrl = environmentBaseUrl,
                        ApiKey = environmentApiKey,
                    },
                    Ai = options.Metadata.Ai with { UseMetadataMatch = true },
                },
            },
            deploymentEnvironmentVariables:
            [
                "TMDB_BASE_URL",
                "tmdb_api_key",
                "ai_use_episode_match",
            ]);

        using (var currentResponse = await app.Client.GetAsync("/api/v1/config"))
        {
            var currentText = await currentResponse.Content.ReadAsStringAsync();
            using var current = JsonDocument.Parse(currentText);
            var editable = current.RootElement.GetProperty("editable");
            var locks = editable.GetProperty("locked_fields")
                .EnumerateArray()
                .ToDictionary(
                    item => item.GetProperty("field").GetString()!,
                    item => item);

            Assert.Equal(environmentBaseUrl.AbsoluteUri, editable
                .GetProperty("tmdb_base_url").GetString());
            Assert.Equal("environment", locks["tmdb_base_url"]
                .GetProperty("source").GetString());
            Assert.Equal("TMDB_BASE_URL", locks["tmdb_base_url"]
                .GetProperty("environment_variables")[0].GetString());
            Assert.True(locks.ContainsKey("tmdb_api_key"));
            Assert.True(locks.ContainsKey("ai_use_metadata_match"));
            Assert.DoesNotContain(environmentApiKey, currentText, StringComparison.Ordinal);
        }

        using var baseUrlWrite = await app.Client.PutAsync(
            "/api/v1/config",
            Payload(
                expectedRevision: 0,
                baseUrl: "https://private.invalid/tmdb/",
                aiMetadata: true));
        var baseUrlError = await baseUrlWrite.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, baseUrlWrite.StatusCode);
        Assert.Contains("configuration_field_locked", baseUrlError, StringComparison.Ordinal);
        Assert.Contains("tmdb_base_url", baseUrlError, StringComparison.Ordinal);

        using var secretWrite = await app.Client.PutAsync(
            "/api/v1/config",
            Payload(
                expectedRevision: 0,
                baseUrl: environmentBaseUrl.AbsoluteUri,
                apiKey: "must-not-be-written",
                aiMetadata: true));
        var secretError = await secretWrite.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, secretWrite.StatusCode);
        Assert.Contains("configuration_field_locked", secretError, StringComparison.Ordinal);
        Assert.Contains("tmdb_api_key", secretError, StringComparison.Ordinal);
        Assert.DoesNotContain("must-not-be-written", secretError, StringComparison.Ordinal);

        using var allowedWrite = await app.Client.PutAsync(
            "/api/v1/config",
            Payload(
                expectedRevision: 0,
                baseUrl: environmentBaseUrl.AbsoluteUri,
                aiMetadata: true));
        Assert.Equal(HttpStatusCode.OK, allowedWrite.StatusCode);
        var store = app.App.Services.GetRequiredService<ApplicationOverrideStore>();
        var stored = await store.LoadAsync();
        Assert.Equal(environmentBaseUrl.AbsoluteUri, stored.Settings?.TmdbBaseUrl);
        Assert.Null(stored.Settings?.TmdbApiKey);
        Assert.True(stored.Settings?.SeasonFailureBacktrace);
        Assert.Contains("tmdb_base_url", stored.Settings?.InheritedFields ?? []);
        Assert.Contains("tmdb_api_key", stored.Settings?.InheritedFields ?? []);
        Assert.Contains("ai_use_metadata_match", stored.Settings?.InheritedFields ?? []);

        var withoutEnvironment = AnimeGoDefaults.CreateNative(
            Path.Combine(app.RootPath, "without-environment"));
        var reappliedWithoutEnvironment = ApplicationOverrideStore.Apply(
            withoutEnvironment,
            stored);
        Assert.Equal(
            withoutEnvironment.Metadata.Tmdb.BaseUrl,
            reappliedWithoutEnvironment.Metadata.Tmdb.BaseUrl);
        Assert.Equal(
            withoutEnvironment.Metadata.Tmdb.ApiKey,
            reappliedWithoutEnvironment.Metadata.Tmdb.ApiKey);
        Assert.Equal(
            withoutEnvironment.Metadata.Ai.UseMetadataMatch,
            reappliedWithoutEnvironment.Metadata.Ai.UseMetadataMatch);

        await store.SaveAsync(
            stored.Settings! with
            {
                TmdbBaseUrl = "https://legacy-private.invalid/tmdb/",
                InheritedFields = stored.Settings.InheritedFields?
                    .Where(field => field != "tmdb_base_url")
                    .ToArray(),
            },
            stored.Revision);
        using var reprojectedResponse = await app.Client.GetAsync("/api/v1/config");
        using var reprojected = JsonDocument.Parse(
            await reprojectedResponse.Content.ReadAsStreamAsync());
        Assert.Equal(
            environmentBaseUrl.AbsoluteUri,
            reprojected.RootElement.GetProperty("editable")
                .GetProperty("tmdb_base_url").GetString());
        Assert.Equal(
            environmentBaseUrl.AbsoluteUri,
            reprojected.RootElement.GetProperty("metadata")
                .GetProperty("tmdb").GetProperty("base_url").GetString());

        using var saveAlongsideLegacyOverride = await app.Client.PutAsync(
            "/api/v1/config",
            Payload(
                expectedRevision: stored.Revision + 1,
                baseUrl: environmentBaseUrl.AbsoluteUri,
                aiMetadata: true));
        Assert.Equal(HttpStatusCode.OK, saveAlongsideLegacyOverride.StatusCode);
        var preserved = await store.LoadAsync();
        Assert.Equal(
            "https://legacy-private.invalid/tmdb/",
            preserved.Settings?.TmdbBaseUrl);
        Assert.DoesNotContain(
            "tmdb_base_url",
            preserved.Settings?.InheritedFields ?? []);
    }

    private static StringContent Payload(
        long expectedRevision,
        string? apiKey = null,
        string? readToken = null,
        bool clearApiKey = false,
        bool? aiMetadata = false,
        bool legacyAiSeason = false,
        bool legacyAiEpisode = false,
        string baseUrl = "https://api.themoviedb.org/",
        string? tmdbProxy = null,
        string bangumiBase = "https://api.bgm.tv/",
        string? bangumiProxy = null)
    {
        var json = JsonSerializer.Serialize(new
        {
            tmdb_base_url = baseUrl,
            tmdb_proxy_url = tmdbProxy,
            tmdb_language = "zh-CN",
            tmdb_http_timeout_seconds = 30,
            tmdb_api_key = apiKey,
            clear_tmdb_api_key = clearApiKey,
            tmdb_read_access_token = readToken,
            clear_tmdb_read_access_token = false,
            bangumi_base_url = bangumiBase,
            bangumi_proxy_url = bangumiProxy,
            bangumi_http_timeout_seconds = 30,
            season_failure_skip = false,
            season_failure_backtrace = true,
            season_failure_use_title_season = true,
            season_failure_use_first_season = true,
            ai_use_metadata_match = aiMetadata,
            ai_use_season_match = legacyAiSeason,
            ai_use_episode_match = legacyAiEpisode,
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
