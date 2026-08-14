using System.Net;
using System.Text;
using System.Text.Json;
using AnimeGoNet.App.Configuration;
using AnimeGoNet.App.Metadata;
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
                OutboundProxy = new OutboundProxyOptions
                {
                    Url = new Uri("http://127.0.0.1:7890/"),
                    HostPatterns = ["metadata.test.invalid", "*.mikanime.tv"],
                },
                Metadata = options.Metadata with
                {
                    Mikan = new MikanClientOptions
                    {
                        BaseUrl = new Uri("http://mikan.local/"),
                        EpisodeIdentityCacheTtl = TimeSpan.Zero,
                        BangumiIdentityCacheTtl = TimeSpan.FromHours(72),
                    },
                    Tmdb = options.Metadata.Tmdb with
                    {
                        BaseUrl = new Uri("https://metadata.test.invalid/tmdb/"),
                        ImageBaseUrl = new Uri("http://image.tmdb.local/t/p/"),
                        ApiKey = "tmdb-api-secret",
                        ReadAccessToken = "tmdb-bearer-secret",
                        Language = "ja-JP",
                        RetryCount = 4,
                        RetryDelay = TimeSpan.FromSeconds(6.5),
                        CacheTtl = TimeSpan.FromHours(48),
                    },
                    Bangumi = options.Metadata.Bangumi with
                    {
                        BaseUrl = new Uri("https://metadata.test.invalid/bangumi/"),
                        HttpTimeout = TimeSpan.FromSeconds(45),
                        RetryCount = 5,
                        RetryDelay = TimeSpan.FromSeconds(7.5),
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
                    WriteBangumiIdWhenTmdbMatched = true,
                    MikanTrustedOffsetCacheEnabled = true,
                    MikanTrustedOffsetRequiredEpisodes = 5,
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
        var outboundProxy = json.RootElement.GetProperty("outbound_proxy");
        Assert.Equal("http://127.0.0.1:7890/", outboundProxy.GetProperty("url").GetString());
        Assert.Equal(
            ["metadata.test.invalid", "*.mikanime.tv"],
            outboundProxy.GetProperty("hosts").EnumerateArray().Select(item => item.GetString()));
        var metadata = json.RootElement.GetProperty("metadata");
        Assert.Equal(
            "http://mikan.local/",
            metadata.GetProperty("mikan").GetProperty("base_url").GetString());
        Assert.Equal(
            0,
            metadata.GetProperty("mikan").GetProperty(
                "episode_identity_cache_hours").GetDouble());
        Assert.Equal(
            72,
            metadata.GetProperty("mikan").GetProperty(
                "bangumi_identity_cache_hours").GetDouble());
        var tmdb = metadata.GetProperty("tmdb");
        Assert.Equal("ja-JP", tmdb.GetProperty("language").GetString());
        Assert.Equal(
            "https://metadata.test.invalid/tmdb/",
            tmdb.GetProperty("base_url").GetString());
        Assert.Equal(
            "http://image.tmdb.local/t/p/",
            tmdb.GetProperty("image_base_url").GetString());
        Assert.False(tmdb.TryGetProperty("proxy_url", out _));
        Assert.True(tmdb.GetProperty("api_key_configured").GetBoolean());
        Assert.True(tmdb.GetProperty("read_access_token_configured").GetBoolean());
        Assert.Equal(4, tmdb.GetProperty("retry_count").GetInt32());
        Assert.Equal(6.5, tmdb.GetProperty("retry_delay_seconds").GetDouble());
        Assert.Equal(48, tmdb.GetProperty("cache_hours").GetDouble());
        var bangumi = metadata.GetProperty("bangumi");
        Assert.Equal(
            "https://metadata.test.invalid/bangumi/",
            bangumi.GetProperty("base_url").GetString());
        Assert.False(bangumi.TryGetProperty("proxy_url", out _));
        Assert.Equal(45, bangumi.GetProperty("http_timeout_seconds").GetDouble());
        Assert.Equal(5, bangumi.GetProperty("retry_count").GetInt32());
        Assert.Equal(7.5, bangumi.GetProperty("retry_delay_seconds").GetDouble());
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
            "http://bgm.mcp.local/mcp",
            metadata.GetProperty("ai").GetProperty("bangumi_mcp_url").GetString());
        Assert.True(metadata.GetProperty("write_bangumi_id_when_tmdb_matched").GetBoolean());
        Assert.Equal(
            5,
            metadata.GetProperty("mikan_trusted_offset_required_episodes").GetInt32());
        Assert.True(json.RootElement.GetProperty("editable")
            .GetProperty("write_bangumi_id_when_tmdb_matched").GetBoolean());
        Assert.Equal(
            123456,
            json.RootElement.GetProperty("torrent_fetch").GetProperty("max_response_bytes").GetInt64());
        var dataUpdate = json.RootElement.GetProperty("data_update");
        Assert.False(dataUpdate.GetProperty("enabled").GetBoolean());
        Assert.Equal("0 0 4 * * ?", dataUpdate.GetProperty("cron").GetString());
        Assert.Null(dataUpdate.GetProperty("manifest_url").GetString());
        Assert.True(dataUpdate.GetProperty("auto_download").GetBoolean());
        Assert.True(dataUpdate.GetProperty("auto_import").GetBoolean());
        Assert.Equal(2, dataUpdate.GetProperty("keep_versions").GetInt32());
        Assert.True(dataUpdate.GetProperty("hot_reload_supported").GetBoolean());
        Assert.DoesNotContain("local-access-secret", text, StringComparison.Ordinal);
        var editable = json.RootElement.GetProperty("editable");
        Assert.Equal("tmdb-api-secret", editable.GetProperty("tmdb_api_key").GetString());
        Assert.Equal(
            "tmdb-bearer-secret",
            editable.GetProperty("tmdb_read_access_token").GetString());
        Assert.Equal("ai-api-secret", editable.GetProperty("ai_api_key").GetString());
    }

    [Fact]
    public async Task MikanIdentityCacheHoursAreEditableAndPersisted()
    {
        await using var app = await RunningApp.StartAsync();

        using var invalid = await app.Client.PutAsync(
            "/api/v1/config",
            Payload(
                expectedRevision: 0,
                mikanTrustedOffsetRequiredEpisodes: 0));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var write = await app.Client.PutAsync(
            "/api/v1/config",
            Payload(
                expectedRevision: 0,
                mikanEpisodeIdentityCacheHours: 0,
                mikanBangumiIdentityCacheHours: 4320,
                mikanTrustedOffsetRequiredEpisodes: 5));

        Assert.Equal(HttpStatusCode.OK, write.StatusCode);
        using var response = await app.Client.GetAsync("/api/v1/config");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var editable = json.RootElement.GetProperty("editable");
        Assert.Equal(0, editable.GetProperty(
            "mikan_episode_identity_cache_hours").GetDouble());
        Assert.Equal(4320, editable.GetProperty(
            "mikan_bangumi_identity_cache_hours").GetDouble());
        Assert.Equal(5, editable.GetProperty(
            "mikan_trusted_offset_required_episodes").GetInt32());

        var stored = await app.App.Services
            .GetRequiredService<ApplicationOverrideStore>()
            .LoadAsync();
        Assert.Equal(0, stored.Settings?.MikanEpisodeIdentityCacheHours);
        Assert.Equal(4320, stored.Settings?.MikanBangumiIdentityCacheHours);
        Assert.Equal(5, stored.Settings?.MikanTrustedOffsetRequiredEpisodes);
    }

    [Fact]
    public async Task StaticWebUiLoadsPrefilledConfigurationPanel()
    {
        await using var app = await RunningApp.StartAsync();

        var html = await app.Client.GetStringAsync("/");
        var script = await app.Client.GetStringAsync("/app.js");

        Assert.Contains("id=\"configuration\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-reload\"", html, StringComparison.Ordinal);
        Assert.Contains("/api/v1/config", script, StringComparison.Ordinal);
        Assert.Contains("loadConfiguration", script, StringComparison.Ordinal);
        Assert.Contains("配置编辑器会回填已保存凭据", script, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-dialog\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-form\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-lock-summary\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-tmdb-key-clear\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-mikan-url\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-tmdb-image-url\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-outbound-proxy-url\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-outbound-proxy-hosts\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"configuration-tmdb-proxy\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-tmdb-retry-count\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-tmdb-retry-delay\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-bangumi-url\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"configuration-bangumi-proxy\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-bangumi-retry-count\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-bangumi-retry-delay\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-data-update-enabled\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-data-update-cron\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-data-update-manifest\"", html, StringComparison.Ordinal);
        Assert.Contains("AnimeGoNetData 更新策略与 Cron 即时生效", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"TMDB 季度失败优先级\"", html, StringComparison.Ordinal);
        Assert.Contains("data-priority=\"4\"", html, StringComparison.Ordinal);
        Assert.Contains("data-priority=\"3\"", html, StringComparison.Ordinal);
        Assert.Contains("data-priority=\"independent\"", html, StringComparison.Ordinal);
        Assert.Contains("data-priority=\"2\"", html, StringComparison.Ordinal);
        Assert.Contains("data-priority=\"1\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-ai-metadata\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-ai-base-url\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-ai-model\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-ai-reasoning-effort\"", html, StringComparison.Ordinal);
        Assert.Contains("none（不发送 reasoning）", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-ai-key\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-ai-key-clear\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-ai-tmdb-mcp-url\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-ai-bangumi-mcp-url\"", html, StringComparison.Ordinal);
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
        Assert.Contains("outbound_proxy_hosts", script, StringComparison.Ordinal);
        Assert.DoesNotContain("bangumi_proxy_url", script, StringComparison.Ordinal);
        Assert.Contains("mikan_base_url", script, StringComparison.Ordinal);
        Assert.Contains("tmdb_image_base_url", script, StringComparison.Ordinal);
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
        Assert.Contains("previewConfiguration", script, StringComparison.Ordinal);
        Assert.Contains("confirmConfiguration", script, StringComparison.Ordinal);
        Assert.Contains("resetConfiguration", script, StringComparison.Ordinal);
        Assert.Contains("/api/v1/config/preview", script, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-preview\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-confirm\"", html, StringComparison.Ordinal);
        Assert.Contains("保存前差异", html, StringComparison.Ordinal);
        Assert.Contains("确认保存并备份", html, StringComparison.Ordinal);
        Assert.Contains("expected_configuration_revision", script, StringComparison.Ordinal);
        Assert.Contains("locked_fields", script, StringComparison.Ordinal);
        Assert.Contains("controlling_keys", script, StringComparison.Ordinal);
        Assert.Contains("环境变量或命令行", script, StringComparison.Ordinal);
        Assert.Contains("data_update_manifest_url", script, StringComparison.Ordinal);
        Assert.Contains("修改已即时生效", script, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewValidatesAndReturnsVisibleEffectAwareDiffWithoutWriting()
    {
        await using var app = await RunningApp.StartAsync();
        using var preview = await app.Client.PostAsync(
            "/api/v1/config/preview",
            DataUpdatePayload(
                expectedRevision: 0,
                enabled: true,
                cron: "0 15 4 * * ?",
                manifestUrl: "https://updates.test.invalid/manifest.json",
                tmdbLanguage: "ja-JP",
                apiKey: "preview-secret-visible"));
        var text = await preview.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(text);

        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(0, json.RootElement
            .GetProperty("current_configuration_revision").GetInt64());
        Assert.True(json.RootElement.GetProperty("restart_required").GetBoolean());
        Assert.True(json.RootElement.GetProperty("data_update_hot_reload").GetBoolean());
        var changes = json.RootElement.GetProperty("changes")
            .EnumerateArray()
            .ToDictionary(item => item.GetProperty("field").GetString()!);
        Assert.Equal("zh-CN", changes["tmdb_language"].GetProperty("before").GetString());
        Assert.Equal("ja-JP", changes["tmdb_language"].GetProperty("after").GetString());
        Assert.Equal("restart", changes["tmdb_language"].GetProperty("effect").GetString());
        Assert.True(changes["tmdb_api_key"].GetProperty("sensitive").GetBoolean());
        Assert.Equal(JsonValueKind.Null, changes["tmdb_api_key"].GetProperty("before").ValueKind);
        Assert.Equal("preview-secret-visible", changes["tmdb_api_key"].GetProperty("after").GetString());
        Assert.Equal(
            "hot_reload",
            changes["data_update_cron"].GetProperty("effect").GetString());
        Assert.Contains("preview-secret-visible", text, StringComparison.Ordinal);
        Assert.Equal(
            0,
            (await app.App.Services.GetRequiredService<ApplicationOverrideStore>()
                .LoadAsync()).Revision);
        Assert.False(File.Exists(Path.Combine(
            app.RootPath,
            "data",
            "config",
            "application.private.json")));

        using var stale = await app.Client.PostAsync(
            "/api/v1/config/preview",
            DataUpdatePayload(
                expectedRevision: 1,
                enabled: false,
                cron: "0 0 4 * * ?"));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task OverwriteAndResetReportAndPersistPreviousRevisionBackups()
    {
        await using var app = await RunningApp.StartAsync();
        using var first = await app.Client.PutAsync(
            "/api/v1/config",
            DataUpdatePayload(
                expectedRevision: 0,
                enabled: true,
                cron: "0 15 4 * * ?",
                manifestUrl: "https://updates.test.invalid/manifest.json"));
        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(JsonValueKind.Null, firstJson.RootElement
            .GetProperty("backup_revision").ValueKind);

        using var second = await app.Client.PutAsync(
            "/api/v1/config",
            DataUpdatePayload(
                expectedRevision: 1,
                enabled: true,
                cron: "0 30 4 * * ?",
                manifestUrl: "https://updates.test.invalid/manifest.json"));
        using var secondJson = JsonDocument.Parse(await second.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(1, secondJson.RootElement.GetProperty("backup_revision").GetInt64());

        var backups = Path.Combine(app.RootPath, "data", "backups");
        var revisionOne = Assert.Single(Directory.GetFiles(
            backups,
            "application.private.revision-00000000000000000001.json"));
        using (var backup = JsonDocument.Parse(await File.ReadAllTextAsync(revisionOne)))
        {
            Assert.Equal(1, backup.RootElement.GetProperty("revision").GetInt64());
            Assert.Equal(
                "0 15 4 * * ?",
                backup.RootElement.GetProperty("settings")
                    .GetProperty("data_update_cron").GetString());
        }

        using var reset = await app.Client.DeleteAsync(
            "/api/v1/config?expected_revision=2");
        using var resetJson = JsonDocument.Parse(await reset.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(2, resetJson.RootElement.GetProperty("backup_revision").GetInt64());
        Assert.Single(Directory.GetFiles(
            backups,
            "application.private.revision-00000000000000000002.json"));
        Assert.Null((await app.App.Services
            .GetRequiredService<ApplicationOverrideStore>()
            .LoadAsync()).Settings);
    }

    [Fact]
    public async Task DataUpdateOnlyWriteHotAppliesRuntimePolicyAndRevision()
    {
        await using var app = await RunningApp.StartAsync();

        using var write = await app.Client.PutAsync(
            "/api/v1/config",
            DataUpdatePayload(
                expectedRevision: 0,
                enabled: true,
                cron: "0 15 4 * * ?",
                manifestUrl: "https://updates.test.invalid/manifest.json",
                autoDownload: false,
                autoImport: false,
                keepVersions: 4,
                timeoutSeconds: 45));
        using var writeJson = JsonDocument.Parse(await write.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, write.StatusCode);
        Assert.Equal(1, writeJson.RootElement
            .GetProperty("configuration_revision").GetInt64());
        Assert.False(writeJson.RootElement.GetProperty("restart_required").GetBoolean());

        using var configurationResponse = await app.Client.GetAsync("/api/v1/config");
        using var configuration = JsonDocument.Parse(
            await configurationResponse.Content.ReadAsStreamAsync());
        Assert.Equal(1, configuration.RootElement
            .GetProperty("applied_configuration_revision").GetInt64());
        Assert.False(configuration.RootElement.GetProperty("restart_required").GetBoolean());
        var dataUpdate = configuration.RootElement.GetProperty("data_update");
        Assert.True(dataUpdate.GetProperty("enabled").GetBoolean());
        Assert.Equal("0 15 4 * * ?", dataUpdate.GetProperty("cron").GetString());
        Assert.Equal(
            "https://updates.test.invalid/manifest.json",
            dataUpdate.GetProperty("manifest_url").GetString());
        Assert.False(dataUpdate.GetProperty("auto_download").GetBoolean());
        Assert.False(dataUpdate.GetProperty("auto_import").GetBoolean());
        Assert.Equal(4, dataUpdate.GetProperty("keep_versions").GetInt32());
        Assert.Equal(45, dataUpdate.GetProperty("http_timeout_seconds").GetDouble());

        using var statusResponse = await app.Client.GetAsync("/api/v1/data-update");
        using var status = JsonDocument.Parse(await statusResponse.Content.ReadAsStreamAsync());
        Assert.True(status.RootElement.GetProperty("scheduled_enabled").GetBoolean());
        Assert.Equal("0 15 4 * * ?", status.RootElement.GetProperty("cron").GetString());
        Assert.True(status.RootElement.GetProperty("manifest_configured").GetBoolean());
        Assert.False(status.RootElement.GetProperty("auto_download").GetBoolean());
        Assert.False(status.RootElement.GetProperty("auto_import").GetBoolean());
        Assert.Equal(4, status.RootElement.GetProperty("keep_versions").GetInt32());

        var stored = await app.App.Services
            .GetRequiredService<ApplicationOverrideStore>()
            .LoadAsync();
        Assert.True(stored.Settings?.DataUpdateEnabled);
        Assert.Equal("0 15 4 * * ?", stored.Settings?.DataUpdateCron);
        Assert.True(stored.Settings?.DataUpdateManifestUrlOverridden);
        Assert.Equal(
            "https://updates.test.invalid/manifest.json",
            stored.Settings?.DataUpdateManifestUrl);
        Assert.Equal(144, stored.Settings?.TmdbCacheHours);
    }

    [Fact]
    public async Task MixedWriteHotAppliesDataUpdateButLeavesOtherChangesPendingRestart()
    {
        await using var app = await RunningApp.StartAsync();

        using var write = await app.Client.PutAsync(
            "/api/v1/config",
            DataUpdatePayload(
                expectedRevision: 0,
                enabled: true,
                cron: "0 20 4 * * ?",
                manifestUrl: "https://updates.test.invalid/manifest.json",
                tmdbLanguage: "ja-JP"));
        using var writeJson = JsonDocument.Parse(await write.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, write.StatusCode);
        Assert.True(writeJson.RootElement.GetProperty("restart_required").GetBoolean());

        using var configurationResponse = await app.Client.GetAsync("/api/v1/config");
        using var configuration = JsonDocument.Parse(
            await configurationResponse.Content.ReadAsStreamAsync());
        Assert.Equal(0, configuration.RootElement
            .GetProperty("applied_configuration_revision").GetInt64());
        Assert.Equal("zh-CN", configuration.RootElement.GetProperty("metadata")
            .GetProperty("tmdb").GetProperty("language").GetString());
        Assert.Equal("ja-JP", configuration.RootElement.GetProperty("editable")
            .GetProperty("tmdb_language").GetString());
        Assert.Equal("0 20 4 * * ?", configuration.RootElement
            .GetProperty("data_update").GetProperty("cron").GetString());
    }

    [Fact]
    public async Task DataUpdateEnvironmentLockIsProjectedAndRejected()
    {
        await using var app = await RunningApp.StartAsync(
            configure: options => options with
            {
                DataUpdate = options.DataUpdate with
                {
                    Cron = "0 5 4 * * ?",
                },
            },
            deploymentEnvironmentVariables: ["DATA_UPDATE_CRON"]);

        using var currentResponse = await app.Client.GetAsync("/api/v1/config");
        using var current = JsonDocument.Parse(
            await currentResponse.Content.ReadAsStreamAsync());
        var cronLock = Assert.Single(
            current.RootElement.GetProperty("editable")
                .GetProperty("locked_fields")
                .EnumerateArray(),
            item => item.GetProperty("field").GetString() == "data_update_cron");
        Assert.Equal(
            "DATA_UPDATE_CRON",
            cronLock.GetProperty("environment_variables")[0].GetString());

        using var write = await app.Client.PutAsync(
            "/api/v1/config",
            DataUpdatePayload(
                expectedRevision: 0,
                enabled: false,
                cron: "0 15 4 * * ?"));
        var error = await write.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, write.StatusCode);
        Assert.Contains("configuration_field_locked", error, StringComparison.Ordinal);
        Assert.Contains("data_update_cron", error, StringComparison.Ordinal);
        Assert.Equal(
            0,
            (await app.App.Services.GetRequiredService<ApplicationOverrideStore>()
                .LoadAsync()).Revision);
    }

    [Fact]
    public async Task ResetHotAppliesDeploymentDataUpdatePolicy()
    {
        await using var app = await RunningApp.StartAsync();
        using var write = await app.Client.PutAsync(
            "/api/v1/config",
            DataUpdatePayload(
                expectedRevision: 0,
                enabled: true,
                cron: "0 25 4 * * ?",
                manifestUrl: "https://updates.test.invalid/manifest.json"));
        Assert.Equal(HttpStatusCode.OK, write.StatusCode);

        using var reset = await app.Client.DeleteAsync(
            "/api/v1/config?expected_revision=1");
        using var resetJson = JsonDocument.Parse(await reset.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.False(resetJson.RootElement.GetProperty("restart_required").GetBoolean());

        using var statusResponse = await app.Client.GetAsync("/api/v1/data-update");
        using var status = JsonDocument.Parse(await statusResponse.Content.ReadAsStreamAsync());
        Assert.False(status.RootElement.GetProperty("scheduled_enabled").GetBoolean());
        Assert.Equal("0 0 4 * * ?", status.RootElement.GetProperty("cron").GetString());
        Assert.False(status.RootElement.GetProperty("manifest_configured").GetBoolean());
    }

    [Fact]
    public async Task PrivateConfigurationUsesRevisionAndSecretTriState()
    {
        await using var app = await RunningApp.StartAsync();
        var customPrompt = AiMetadataPromptRenderer.LoadTemplate()
            .Replace("你是一个动画", "CONFIGURATION-PROMPT 你是一个动画", StringComparison.Ordinal);
        using var first = await app.Client.PutAsync(
            "/api/v1/config",
            Payload(
                expectedRevision: 0,
                apiKey: "new-api-secret",
                readToken: "new-read-secret",
                aiBaseUrl: "http://openai.test.invalid/",
                aiModel: "test-live-model",
                aiReasoningEffort: "high",
                aiPromptTemplate: customPrompt,
                aiApiKey: "new-ai-secret",
                aiTmdbMcpUrl: "http://tmdb-mcp.test.invalid/mcp",
                aiBangumiMcpUrl: "http://bgm-mcp.test.invalid/mcp",
                outboundProxy: "http://127.0.0.1:7890/",
                bangumiBase: "https://metadata.test.invalid/bangumi/",
                outboundHosts: ["api.themoviedb.org", "api.bgm.tv"]));
        var firstText = await first.Content.ReadAsStringAsync();
        using var firstJson = JsonDocument.Parse(firstText);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(1, firstJson.RootElement.GetProperty("configuration_revision").GetInt64());
        Assert.True(firstJson.RootElement.GetProperty("restart_required").GetBoolean());

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
            Assert.Equal("new-api-secret", editable.GetProperty("tmdb_api_key").GetString());
            Assert.Equal(336, editable.GetProperty("tmdb_cache_hours").GetDouble());
            Assert.Equal(
                "configured",
                editable.GetProperty("tmdb_read_access_token_state").GetString());
            Assert.Equal(
                "new-read-secret",
                editable.GetProperty("tmdb_read_access_token").GetString());
            Assert.Equal("http://openai.test.invalid/", editable.GetProperty("ai_base_url").GetString());
            Assert.Equal("test-live-model", editable.GetProperty("ai_model").GetString());
            Assert.Equal("high", editable.GetProperty("ai_reasoning_effort").GetString());
            Assert.Equal(customPrompt, editable.GetProperty("ai_prompt_template").GetString());
            Assert.Equal("configured", editable.GetProperty("ai_api_key_state").GetString());
            Assert.Equal("new-ai-secret", editable.GetProperty("ai_api_key").GetString());
            Assert.Equal(
                "http://tmdb-mcp.test.invalid/mcp",
                editable.GetProperty("ai_tmdb_mcp_url").GetString());
            Assert.Equal(
                "http://bgm-mcp.test.invalid/mcp",
                editable.GetProperty("ai_bangumi_mcp_url").GetString());
        }

        var store = app.App.Services.GetRequiredService<ApplicationOverrideStore>();
        var saved = await store.LoadAsync();
        Assert.Equal("new-api-secret", saved.Settings?.TmdbApiKey);
        Assert.Equal("new-read-secret", saved.Settings?.TmdbReadAccessToken);
        Assert.Equal("http://openai.test.invalid/", saved.Settings?.AiBaseUrl);
        Assert.Equal("test-live-model", saved.Settings?.AiModel);
        Assert.True(saved.Settings?.AiReasoningEffortOverridden);
        Assert.Equal("high", saved.Settings?.AiReasoningEffort);
        Assert.Equal(customPrompt, saved.Settings?.AiPromptTemplate);
        Assert.Equal("new-ai-secret", saved.Settings?.AiApiKey);
        Assert.Equal("http://tmdb-mcp.test.invalid/mcp", saved.Settings?.AiTmdbMcpUrl);
        Assert.Equal("http://bgm-mcp.test.invalid/mcp", saved.Settings?.AiBangumiMcpUrl);
        Assert.Equal("http://127.0.0.1:7890/", saved.Settings?.OutboundProxyUrl);
        Assert.Equal(
            ["api.themoviedb.org", "api.bgm.tv"],
            saved.Settings?.OutboundProxyHosts);
        Assert.Equal(
            "https://metadata.test.invalid/bangumi/",
            saved.Settings?.BangumiBaseUrl);
        Assert.Equal(3, saved.Settings?.TmdbRetryCount);
        Assert.Equal(5, saved.Settings?.TmdbRetryDelaySeconds);
        Assert.Equal(336, saved.Settings?.TmdbCacheHours);
        Assert.Equal(3, saved.Settings?.BangumiRetryCount);
        Assert.Equal(5, saved.Settings?.BangumiRetryDelaySeconds);

        using var preserve = await app.Client.PutAsync(
            "/api/v1/config",
            Payload(
                expectedRevision: 1,
                aiMetadata: true,
                aiBaseUrl: "http://openai.test.invalid/",
                aiModel: "test-live-model",
                aiTmdbMcpUrl: "http://tmdb-mcp.test.invalid/mcp",
                aiBangumiMcpUrl: "http://bgm-mcp.test.invalid/mcp",
                outboundProxy: "http://127.0.0.1:7890/",
                bangumiBase: "https://metadata.test.invalid/bangumi/",
                outboundHosts: ["api.themoviedb.org", "api.bgm.tv"]));
        Assert.Equal(HttpStatusCode.OK, preserve.StatusCode);
        var preserved = await store.LoadAsync();
        Assert.Equal("new-api-secret", preserved.Settings?.TmdbApiKey);
        Assert.Equal("new-read-secret", preserved.Settings?.TmdbReadAccessToken);
        Assert.Equal("new-ai-secret", preserved.Settings?.AiApiKey);
        Assert.Equal("high", preserved.Settings?.AiReasoningEffort);
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
                clearAiApiKey: true,
                aiBaseUrl: "http://openai.test.invalid/",
                aiModel: "test-live-model",
                aiTmdbMcpUrl: "http://tmdb-mcp.test.invalid/mcp",
                aiBangumiMcpUrl: "http://bgm-mcp.test.invalid/mcp",
                outboundProxy: "http://127.0.0.1:7890/",
                bangumiBase: "https://metadata.test.invalid/bangumi/",
                outboundHosts: ["api.themoviedb.org", "api.bgm.tv"]));
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        var cleared = await store.LoadAsync();
        Assert.True(cleared.Settings?.TmdbApiKeyOverridden);
        Assert.Null(cleared.Settings?.TmdbApiKey);
        Assert.Equal("new-read-secret", cleared.Settings?.TmdbReadAccessToken);
        Assert.True(cleared.Settings?.AiApiKeyOverridden);
        Assert.Null(cleared.Settings?.AiApiKey);
        using (var clearedResponse = await app.Client.GetAsync("/api/v1/config"))
        using (var clearedJson = JsonDocument.Parse(await clearedResponse.Content.ReadAsStreamAsync()))
        {
            Assert.Equal(
                "cleared",
                clearedJson.RootElement.GetProperty("editable")
                    .GetProperty("tmdb_api_key_state").GetString());
            Assert.Equal(
                JsonValueKind.Null,
                clearedJson.RootElement.GetProperty("editable")
                    .GetProperty("tmdb_api_key").ValueKind);
            Assert.Equal(
                "cleared",
                clearedJson.RootElement.GetProperty("editable")
                    .GetProperty("ai_api_key_state").GetString());
            Assert.Equal(
                JsonValueKind.Null,
                clearedJson.RootElement.GetProperty("editable")
                    .GetProperty("ai_api_key").ValueKind);
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
    public async Task ReasoningEffortCanBeExplicitlyDisabled()
    {
        await using var app = await RunningApp.StartAsync();
        using var response = await app.Client.PutAsync(
            "/api/v1/config",
            Payload(expectedRevision: 0, aiReasoningEffort: "none"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await app.App.Services
            .GetRequiredService<ApplicationOverrideStore>()
            .LoadAsync();
        Assert.True(saved.Settings?.AiReasoningEffortOverridden);
        Assert.Null(saved.Settings?.AiReasoningEffort);
        using var currentResponse = await app.Client.GetAsync("/api/v1/config");
        using var current = JsonDocument.Parse(
            await currentResponse.Content.ReadAsStreamAsync());
        Assert.Equal(
            "none",
            current.RootElement.GetProperty("editable")
                .GetProperty("ai_reasoning_effort").GetString());
    }

    [Fact]
    public async Task ReasoningEffortPreviewIsReviewableAndInvalidValueIsRejected()
    {
        await using var app = await RunningApp.StartAsync();
        using var previewResponse = await app.Client.PostAsync(
            "/api/v1/config/preview",
            Payload(expectedRevision: 0, aiReasoningEffort: "high"));
        using var preview = JsonDocument.Parse(
            await previewResponse.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var change = Assert.Single(
            preview.RootElement.GetProperty("changes").EnumerateArray(),
            item => item.GetProperty("field").GetString() == "ai_reasoning_effort");
        Assert.Equal("none", change.GetProperty("before").GetString());
        Assert.Equal("high", change.GetProperty("after").GetString());
        Assert.Equal("restart", change.GetProperty("effect").GetString());

        using var invalid = await app.Client.PutAsync(
            "/api/v1/config",
            Payload(expectedRevision: 0, aiReasoningEffort: "ultra"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains(
            "ai_reasoning_effort",
            await invalid.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsPromptMissingProductionPlaceholdersBeforeSavingOverride()
    {
        await using var app = await RunningApp.StartAsync();

        using var response = await app.Client.PutAsync(
            "/api/v1/config",
            Payload(expectedRevision: 0, aiPromptTemplate: "not-a-production-prompt"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ai_prompt_required_placeholder_missing", body, StringComparison.Ordinal);
        Assert.Null((await app.App.Services
            .GetRequiredService<ApplicationOverrideStore>()
            .LoadAsync()).Settings);
    }

    [Fact]
    public async Task PromptPreviewUsesVersionLengthAndHashInsteadOfEchoingTemplate()
    {
        await using var app = await RunningApp.StartAsync();
        var customPrompt = AiMetadataPromptRenderer.LoadTemplate()
            .Replace("你是一个动画", "PROMPT-CONTENT-MUST-NOT-ECHO 你是一个动画", StringComparison.Ordinal);

        using var response = await app.Client.PostAsync(
            "/api/v1/config/preview",
            Payload(expectedRevision: 0, aiPromptTemplate: customPrompt));
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var change = Assert.Single(
            json.RootElement.GetProperty("changes").EnumerateArray(),
            item => item.GetProperty("field").GetString() == "ai_prompt_template");
        Assert.Contains("tmdb-ai-match-v16", change.GetProperty("after").GetString(), StringComparison.Ordinal);
        Assert.Contains("sha256:", change.GetProperty("after").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("PROMPT-CONTENT-MUST-NOT-ECHO", body, StringComparison.Ordinal);
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
                outboundProxy: "http://user:password@proxy.invalid/",
                outboundHosts: ["api.themoviedb.org"]));
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
            Assert.Equal(environmentApiKey, editable.GetProperty("tmdb_api_key").GetString());
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

    [Fact]
    public async Task CommandLineAndCanonicalEnvironmentLocksAreProjectedWithoutValues()
    {
        var deployedBaseUrl = new Uri("https://command.invalid/tmdb/");
        await using var app = await RunningApp.StartAsync(
            configure: options => options with
            {
                Metadata = options.Metadata with
                {
                    Tmdb = options.Metadata.Tmdb with { BaseUrl = deployedBaseUrl },
                },
            },
            deploymentEnvironmentVariables: ["metadata__tmdb__base_url"],
            args:
            [
                $"--tmdb_base_url={deployedBaseUrl.AbsoluteUri}",
                "--tmdb_fail_backtrace=false",
            ]);

        using (var currentResponse = await app.Client.GetAsync("/api/v1/config"))
        using (var current = JsonDocument.Parse(
            await currentResponse.Content.ReadAsStreamAsync()))
        {
            var locks = current.RootElement.GetProperty("editable")
                .GetProperty("locked_fields")
                .EnumerateArray()
                .ToDictionary(item => item.GetProperty("field").GetString()!);
            var baseUrlLock = locks["tmdb_base_url"];
            Assert.Equal(
                "environment_and_command_line",
                baseUrlLock.GetProperty("source").GetString());
            Assert.Equal(
                "metadata__tmdb__base_url",
                baseUrlLock.GetProperty("environment_variables")[0].GetString());
            Assert.Equal(
                "--tmdb_base_url",
                baseUrlLock.GetProperty("command_line_arguments")[0].GetString());
            Assert.Equal(2, baseUrlLock.GetProperty("controlling_keys").GetArrayLength());
            Assert.Equal(
                "command_line",
                locks["season_failure_backtrace"].GetProperty("source").GetString());
            Assert.Equal(
                "--tmdb_fail_backtrace",
                locks["season_failure_backtrace"]
                    .GetProperty("controlling_keys")[0].GetString());
        }

        using var write = await app.Client.PutAsync(
            "/api/v1/config",
            Payload(
                expectedRevision: 0,
                baseUrl: deployedBaseUrl.AbsoluteUri));
        var error = await write.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, write.StatusCode);
        Assert.Contains("configuration_field_locked", error, StringComparison.Ordinal);
        Assert.Contains("season_failure_backtrace", error, StringComparison.Ordinal);
        Assert.DoesNotContain("command.invalid/tmdb/", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeploymentGlobalProxyProjectsAndRejectsLockedWrites()
    {
        var proxy = new Uri("http://127.0.0.1:7890/");
        await using var app = await RunningApp.StartAsync(
            configure: options => options with
            {
                OutboundProxy = new OutboundProxyOptions
                {
                    Url = proxy,
                    HostPatterns = ["api.themoviedb.org"],
                },
            },
            deploymentEnvironmentVariables:
            [
                "ANIMEGO_OUTBOUND_PROXY_URL",
                "ANIMEGO_OUTBOUND_PROXY_HOSTS",
            ]);

        using (var currentResponse = await app.Client.GetAsync("/api/v1/config"))
        using (var current = JsonDocument.Parse(
            await currentResponse.Content.ReadAsStreamAsync()))
        {
            var editable = current.RootElement.GetProperty("editable");
            var locks = editable.GetProperty("locked_fields")
                .EnumerateArray()
                .ToDictionary(item => item.GetProperty("field").GetString()!);
            Assert.Equal(proxy.AbsoluteUri, editable.GetProperty("outbound_proxy_url").GetString());
            Assert.Equal(
                "api.themoviedb.org",
                editable.GetProperty("outbound_proxy_hosts")[0].GetString());
            Assert.Equal(
                "ANIMEGO_OUTBOUND_PROXY_URL",
                locks["outbound_proxy_url"].GetProperty("environment_variables")[0].GetString());
            Assert.Equal(
                "ANIMEGO_OUTBOUND_PROXY_HOSTS",
                locks["outbound_proxy_hosts"].GetProperty("environment_variables")[0].GetString());
        }

        using var write = await app.Client.PutAsync(
            "/api/v1/config",
            Payload(
                expectedRevision: 0,
                outboundProxy: "http://127.0.0.1:7891/",
                outboundHosts: ["api.bgm.tv"]));
        var error = await write.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, write.StatusCode);
        Assert.Contains("configuration_field_locked", error, StringComparison.Ordinal);
        Assert.Contains("outbound_proxy_url", error, StringComparison.Ordinal);
        Assert.Contains("outbound_proxy_hosts", error, StringComparison.Ordinal);
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
        bool? aiMetadata = false,
        bool legacyAiSeason = false,
        bool legacyAiEpisode = false,
        string baseUrl = "https://api.themoviedb.org/",
        string? outboundProxy = null,
        string[]? outboundHosts = null,
        string bangumiBase = "https://api.bgm.tv/",
        string? aiBaseUrl = null,
        string? aiModel = null,
        string? aiPromptTemplate = null,
        string? aiApiKey = null,
        bool clearAiApiKey = false,
        string aiTmdbMcpUrl = "http://tmdb.mcp.local/mcp",
        string aiBangumiMcpUrl = "http://bgm.mcp.local/mcp",
        int tmdbRetryCount = 3,
        double tmdbRetryDelaySeconds = 5,
        int bangumiRetryCount = 3,
        double bangumiRetryDelaySeconds = 5,
        double? mikanEpisodeIdentityCacheHours = null,
        double? mikanBangumiIdentityCacheHours = null,
        string? aiReasoningEffort = null,
        int? mikanTrustedOffsetRequiredEpisodes = null)
    {
        var json = JsonSerializer.Serialize(new
        {
            outbound_proxy_url = outboundProxy,
            outbound_proxy_hosts = outboundHosts ?? [],
            mikan_episode_identity_cache_hours = mikanEpisodeIdentityCacheHours,
            mikan_bangumi_identity_cache_hours = mikanBangumiIdentityCacheHours,
            tmdb_base_url = baseUrl,
            tmdb_language = "zh-CN",
            tmdb_http_timeout_seconds = 30,
            tmdb_retry_count = tmdbRetryCount,
            tmdb_retry_delay_seconds = tmdbRetryDelaySeconds,
            tmdb_cache_hours = 336,
            tmdb_api_key = apiKey,
            clear_tmdb_api_key = clearApiKey,
            tmdb_read_access_token = readToken,
            clear_tmdb_read_access_token = false,
            bangumi_base_url = bangumiBase,
            bangumi_http_timeout_seconds = 30,
            bangumi_retry_count = bangumiRetryCount,
            bangumi_retry_delay_seconds = bangumiRetryDelaySeconds,
            ai_base_url = aiBaseUrl,
            ai_model = aiModel,
            ai_reasoning_effort = aiReasoningEffort,
            ai_prompt_template = aiPromptTemplate,
            ai_api_key = aiApiKey,
            clear_ai_api_key = clearAiApiKey,
            ai_tmdb_mcp_url = aiTmdbMcpUrl,
            ai_bangumi_mcp_url = aiBangumiMcpUrl,
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
            mikan_trusted_offset_required_episodes = mikanTrustedOffsetRequiredEpisodes,
            torrent_http_timeout_seconds = 30,
            torrent_max_response_bytes = 16 * 1024 * 1024,
            torrent_max_redirects = 3,
            torrent_staging_ttl_seconds = 900,
            data_update_enabled = false,
            data_update_cron = "0 0 4 * * ?",
            data_update_manifest_url = (string?)null,
            data_update_auto_download = true,
            data_update_auto_import = true,
            data_update_keep_versions = 2,
            data_update_http_timeout_seconds = 300,
            expected_configuration_revision = expectedRevision,
        });
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static StringContent DataUpdatePayload(
        long expectedRevision,
        bool enabled,
        string cron,
        string? manifestUrl = null,
        bool autoDownload = true,
        bool autoImport = true,
        int keepVersions = 2,
        double timeoutSeconds = 300,
        string tmdbLanguage = "zh-CN",
        string? apiKey = null)
    {
        var json = JsonSerializer.Serialize(new
        {
            outbound_proxy_url = (string?)null,
            outbound_proxy_hosts = Array.Empty<string>(),
            tmdb_base_url = "https://api.themoviedb.org/",
            tmdb_language = tmdbLanguage,
            tmdb_http_timeout_seconds = 30,
            tmdb_api_key = apiKey,
            clear_tmdb_api_key = false,
            tmdb_read_access_token = (string?)null,
            clear_tmdb_read_access_token = false,
            bangumi_base_url = "https://api.bgm.tv/",
            bangumi_http_timeout_seconds = 30,
            season_failure_skip = false,
            season_failure_backtrace = false,
            season_failure_use_title_season = false,
            season_failure_use_first_season = false,
            ai_use_metadata_match = false,
            ai_use_season_match = false,
            ai_use_episode_match = false,
            ai_http_timeout_seconds = 600,
            tmdb_failure_use_bangumi = false,
            mikan_trusted_offset_cache_enabled = false,
            torrent_http_timeout_seconds = 30,
            torrent_max_response_bytes = 16 * 1024 * 1024,
            torrent_max_redirects = 3,
            torrent_staging_ttl_seconds = 900,
            data_update_enabled = enabled,
            data_update_cron = cron,
            data_update_manifest_url = manifestUrl,
            data_update_auto_download = autoDownload,
            data_update_auto_import = autoImport,
            data_update_keep_versions = keepVersions,
            data_update_http_timeout_seconds = timeoutSeconds,
            expected_configuration_revision = expectedRevision,
        });
        return new StringContent(json, Encoding.UTF8, "application/json");
    }
}
