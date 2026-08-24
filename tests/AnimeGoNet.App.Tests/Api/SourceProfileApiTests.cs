using System.Net;
using System.Text;
using System.Text.Json;
using AnimeGoNet.App.Tests.Plugins;
using AnimeGoNet.Data.Sqlite;
using AnimeGoNet.Data.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Api;

public sealed class SourceProfileApiTests
{
    [Fact]
    public async Task MikanMovieSourcePersistsTypeAndPreviewsMovieLibraryRoute()
    {
        await using var app = await RunningApp.StartAsync();
        using var create = await app.Client.PostAsync("/api/v1/sources", Json(new
        {
            id = "mikan-movie",
            display_name = "Mikan movie",
            adapter = "mikan",
            downloader_id = "bt",
            file_strategy = "move",
            allowed_torrent_hosts = new List<string> { "mikanani.me" },
            enabled = true,
            media_type = "movie",
        }));
        using var created = JsonDocument.Parse(await create.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal("movie", created.RootElement.GetProperty("media_type").GetString());
        using var preview = await app.Client.PostAsync(
            "/api/v1/sources/mikan-movie/route-preview",
            Json(new { title = "Movie preview", mikanid = 3951, bgmid = 547888 }));
        using var route = JsonDocument.Parse(await preview.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal("movie", route.RootElement.GetProperty("media_type").GetString());
        Assert.EndsWith(
            Path.Combine("download", "movies"),
            route.RootElement.GetProperty("save_path").GetString(),
            StringComparison.OrdinalIgnoreCase);

        using var invalid = await app.Client.PostAsync("/api/v1/sources", Json(new
        {
            id = "u2-movie",
            display_name = "U2 movie",
            adapter = "u2",
            downloader_id = "pt",
            file_strategy = "link",
            allowed_torrent_hosts = new List<string> { "u2.invalid" },
            enabled = true,
            media_type = "movie",
        }));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task CreateAcceptsDiscoveredExternalSourceAdapterAndPreviewFailsClosedWhileDisabled()
    {
        await using var app = await RunningApp.StartAsync(
            prepareData: layout => ExternalPluginPackageFixture.Write(
                layout.PluginsPath,
                "source"));

        using var create = await app.Client.PostAsync("/api/v1/sources", Json(new
        {
            id = "external-source-profile",
            display_name = "External source profile",
            adapter = "com.example.source",
            downloader_id = "pt",
            file_strategy = "link",
            allowed_torrent_hosts = new List<string> { "tracker.example" },
            enabled = true,
        }));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStreamAsync());
        Assert.Equal(
            "com.example.source",
            created.RootElement.GetProperty("adapter").GetString());

        using var preview = await app.Client.PostAsync(
            "/api/v1/sources/external-source-profile/route-preview",
            Json(new { title = "External fixture" }));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        using var result = JsonDocument.Parse(await preview.Content.ReadAsStreamAsync());
        Assert.False(result.RootElement.GetProperty("valid").GetBoolean());
        Assert.Contains(
            result.RootElement.GetProperty("errors").EnumerateArray(),
            error => error.GetString()!.Contains("unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateUpdateAndIngestUseVersionedDownloaderRoute()
    {
        await using var app = await RunningApp.StartAsync();

        using var create = await app.Client.PostAsync("/api/v1/sources", Json(new
        {
            id = "u2",
            display_name = "U2",
            adapter = "u2",
            downloader_id = "pt",
            file_strategy = "link",
            allowed_torrent_hosts = new List<string> { "U2.INVALID", "*.u2.invalid" },
            category = "Anime/U2",
            tags = new List<string> { "PT", "Web" },
            dynamic_tag_template = "{year}年{quarter}月新番,EP{ep}",
            seeding_time_minutes = 1440,
            rss_filter_enabled = true,
            rss_priority_enabled = true,
            enabled = true,
        }));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStreamAsync());
        Assert.Equal(1, created.RootElement.GetProperty("revision").GetInt64());
        Assert.Equal("u2.invalid", created.RootElement.GetProperty("allowed_torrent_hosts")[0].GetString());
        Assert.Equal("Anime/U2", created.RootElement.GetProperty("category").GetString());
        Assert.Equal(2, created.RootElement.GetProperty("tags").GetArrayLength());
        Assert.Equal(
            "{year}年{quarter}月新番,EP{ep}",
            created.RootElement.GetProperty("dynamic_tag_template").GetString());
        Assert.Equal(1440, created.RootElement.GetProperty("seeding_time_minutes").GetInt32());
        Assert.True(
            created.RootElement.GetProperty("duplicate_notification_enabled").GetBoolean());
        Assert.Equal("/api/v1/sources/u2", create.Headers.Location?.OriginalString);

        using var rules = await app.Client.GetAsync("/api/v1/rss-rules/u2");
        Assert.Equal(HttpStatusCode.OK, rules.StatusCode);

        using var ingest = await app.Client.PostAsync("/api/v1/ingest", Json(new
        {
            source = "u2",
            data = new[]
            {
                new
                {
                    torrent = "https://u2.invalid/passkey/episode-1.torrent",
                    info = new
                    {
                        title = "U2 episode 1",
                        source_item_id = "u2-item-1",
                        source_work_id = "u2-work-1",
                    },
                },
            },
        }));
        Assert.Equal(HttpStatusCode.OK, ingest.StatusCode);
        using var ingested = JsonDocument.Parse(await ingest.Content.ReadAsStreamAsync());
        var item = ingested.RootElement.GetProperty("items")[0];
        Assert.Equal("pt", item.GetProperty("downloader_id").GetString());
        Assert.Equal(1, item.GetProperty("source_profile_revision").GetInt64());

        using var update = await app.Client.PutAsync("/api/v1/sources/u2", Json(new
        {
            display_name = "U2 via BT",
            downloader_id = "bt",
            file_strategy = "move",
            allowed_torrent_hosts = new List<string> { "u2.invalid" },
            category = "Anime/Move",
            tags = new List<string> { "Moved" },
            seeding_time_minutes = 0,
            rss_filter_enabled = false,
            rss_priority_enabled = false,
            duplicate_notification_enabled = false,
            enabled = true,
            expected_revision = 1,
        }));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var updated = JsonDocument.Parse(await update.Content.ReadAsStreamAsync());
        Assert.Equal(2, updated.RootElement.GetProperty("revision").GetInt64());
        Assert.Equal("bt", updated.RootElement.GetProperty("downloader_id").GetString());
        Assert.Equal("Anime/Move", updated.RootElement.GetProperty("category").GetString());
        Assert.Equal(
            "{year}年{quarter}月新番,EP{ep}",
            updated.RootElement.GetProperty("dynamic_tag_template").GetString());
        Assert.Equal(0, updated.RootElement.GetProperty("seeding_time_minutes").GetInt32());
        Assert.False(
            updated.RootElement.GetProperty("duplicate_notification_enabled").GetBoolean());
        Assert.Contains(
            "does not preserve seeding",
            updated.RootElement.GetProperty("file_strategy_warning").GetString(),
            StringComparison.Ordinal);
        Assert.Equal(1, updated.RootElement.GetProperty("ingest_task_count").GetInt64());

        var taskId = item.GetProperty("ingest_id").GetString()!;
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using (var connection = await database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT downloader_id, source_profile_revision, route_snapshot_json
                FROM ingest_tasks WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$id", taskId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("pt", reader.GetString(0));
            Assert.Equal(1, reader.GetInt64(1));
            using var route = JsonDocument.Parse(reader.GetString(2));
            Assert.Equal("Anime/U2", route.RootElement.GetProperty("category").GetString());
            Assert.Equal("PT", route.RootElement.GetProperty("tags")[0].GetString());
            Assert.Equal(
                "{year}年{quarter}月新番,EP{ep}",
                route.RootElement.GetProperty("dynamic_tag_template").GetString());
            Assert.Equal(1440, route.RootElement.GetProperty("seeding_time_minutes").GetInt32());
            Assert.True(
                route.RootElement.GetProperty("duplicate_notification_enabled").GetBoolean());
        }

        using var preview = await app.Client.PostAsync(
            "/api/v1/sources/u2/route-preview",
            Json(new { title = "U2 episode 2" }));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        using var previewJson = JsonDocument.Parse(await preview.Content.ReadAsStreamAsync());
        Assert.False(
            previewJson.RootElement.GetProperty("duplicate_notification_enabled").GetBoolean());

        using var preserveNotification = await app.Client.PutAsync("/api/v1/sources/u2", Json(new
        {
            display_name = "U2 via BT",
            downloader_id = "bt",
            file_strategy = "move",
            allowed_torrent_hosts = new List<string> { "u2.invalid" },
            category = "Anime/Move",
            tags = new List<string> { "Moved" },
            seeding_time_minutes = 0,
            rss_filter_enabled = false,
            rss_priority_enabled = false,
            enabled = true,
            expected_revision = 2,
        }));
        Assert.Equal(HttpStatusCode.OK, preserveNotification.StatusCode);
        using var preserved = JsonDocument.Parse(
            await preserveNotification.Content.ReadAsStreamAsync());
        Assert.Equal(3, preserved.RootElement.GetProperty("revision").GetInt64());
        Assert.False(
            preserved.RootElement.GetProperty("duplicate_notification_enabled").GetBoolean());

        using var stale = await app.Client.PutAsync("/api/v1/sources/u2", Json(new
        {
            display_name = "stale",
            downloader_id = "bt",
            file_strategy = "link",
            allowed_torrent_hosts = new List<string> { "u2.invalid" },
            enabled = true,
            expected_revision = 1,
        }));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using var deleteReferenced = await app.Client.DeleteAsync(
            "/api/v1/sources/u2?expected_revision=3");
        Assert.Equal(HttpStatusCode.Conflict, deleteReferenced.StatusCode);
    }

    [Fact]
    public async Task ListGetAndDeleteProtectDefaultAndAllowUnreferencedProfile()
    {
        await using var app = await RunningApp.StartAsync();
        using var list = await app.Client.GetAsync("/api/v1/sources");
        using var listed = JsonDocument.Parse(await list.Content.ReadAsStreamAsync());
        var mikan = Assert.Single(listed.RootElement.GetProperty("items").EnumerateArray());
        Assert.True(mikan.GetProperty("is_default").GetBoolean());
        Assert.Equal("move", mikan.GetProperty("file_strategy").GetString());
        Assert.Equal("animegonet", mikan.GetProperty("category").GetString());
        Assert.Equal(
            "{year}年{quarter}月新番",
            mikan.GetProperty("dynamic_tag_template").GetString());
        Assert.Equal(0, mikan.GetProperty("seeding_time_minutes").GetInt32());
        Assert.True(
            mikan.GetProperty("duplicate_notification_enabled").GetBoolean());

        using var create = await app.Client.PostAsync("/api/v1/sources", Json(new
        {
            id = "ttg",
            display_name = "TTG",
            adapter = "ttg",
            downloader_id = "pt",
            file_strategy = "link",
            allowed_torrent_hosts = new List<string> { "ttg.invalid" },
            enabled = true,
        }));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await app.Client.GetAsync("/api/v1/sources/ttg")).StatusCode);

        using var deleted = await app.Client.DeleteAsync(
            "/api/v1/sources/ttg?expected_revision=1");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await app.Client.GetAsync("/api/v1/sources/ttg")).StatusCode);

        using var defaultDelete = await app.Client.DeleteAsync(
            "/api/v1/sources/mikan?expected_revision=1");
        Assert.Equal(HttpStatusCode.Conflict, defaultDelete.StatusCode);
    }

    [Fact]
    public async Task DeploymentControlledMikanFieldsAreProjectedAndCannotBeChanged()
    {
        await using var app = await RunningApp.StartAsync(
            configure: options => options with
            {
                InitialSourceProfiles =
                [
                    options.InitialSourceProfiles[0] with
                    {
                        Category = "environment-category",
                        DynamicTagTemplate = "{year}-environment",
                        MikanIdentityCookie = "environment-private-cookie",
                    },
                ],
            },
            deploymentEnvironmentVariables:
            [
                "ANIMEGO_CATEGORY",
                "ANIMEGO_TAG",
                "ANIMEGO_MIKAN_COOKIE",
            ]);

        using var get = await app.Client.GetAsync("/api/v1/sources/mikan");
        var getText = await get.Content.ReadAsStringAsync();
        using var current = JsonDocument.Parse(getText);
        var item = current.RootElement;
        Assert.Equal("environment-category", item.GetProperty("category").GetString());
        Assert.Equal("{year}-environment", item.GetProperty("dynamic_tag_template").GetString());
        Assert.True(item.GetProperty("mikan_identity_cookie_configured").GetBoolean());
        var locks = item.GetProperty("locked_fields").EnumerateArray().ToArray();
        Assert.Equal(3, locks.Length);
        Assert.All(locks, value => Assert.Equal("environment", value
            .GetProperty("source").GetString()));
        Assert.Contains(locks, value => value.GetProperty("field").GetString() == "category"
            && value.GetProperty("controlling_keys")[0].GetString() == "ANIMEGO_CATEGORY");
        Assert.Contains(locks, value => value.GetProperty("field").GetString()
            == "dynamic_tag_template"
            && value.GetProperty("controlling_keys")[0].GetString() == "ANIMEGO_TAG");
        Assert.Contains(locks, value => value.GetProperty("field").GetString()
            == "mikan_identity_cookie"
            && value.GetProperty("controlling_keys")[0].GetString()
            == "ANIMEGO_MIKAN_COOKIE");
        Assert.Equal(
            "environment-private-cookie",
            item.GetProperty("mikan_identity_cookie").GetString());

        using var rejected = await app.Client.PutAsync("/api/v1/sources/mikan", Json(new
        {
            display_name = "Mikan rejected",
            downloader_id = "bt",
            file_strategy = "move",
            allowed_torrent_hosts = new List<string> { "mikanani.me" },
            category = "user-category",
            dynamic_tag_template = "{year}-user",
            seeding_time_minutes = 0,
            rss_filter_enabled = true,
            rss_priority_enabled = true,
            enabled = true,
            clear_mikan_identity_cookie = true,
            expected_revision = item.GetProperty("revision").GetInt64(),
        }));
        var error = await rejected.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Contains("source_profile_field_locked", error, StringComparison.Ordinal);
        Assert.Contains("category", error, StringComparison.Ordinal);
        Assert.Contains("dynamic_tag_template", error, StringComparison.Ordinal);
        Assert.Contains("mikan_identity_cookie", error, StringComparison.Ordinal);

        using var allowed = await app.Client.PutAsync("/api/v1/sources/mikan", Json(new
        {
            display_name = "Mikan deployment controlled",
            downloader_id = "bt",
            file_strategy = "move",
            allowed_torrent_hosts = new List<string> { "mikanani.me" },
            category = "environment-category",
            dynamic_tag_template = "{year}-environment",
            seeding_time_minutes = 0,
            rss_filter_enabled = false,
            rss_priority_enabled = false,
            enabled = true,
            expected_revision = item.GetProperty("revision").GetInt64(),
        }));
        using var saved = JsonDocument.Parse(await allowed.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal("Mikan deployment controlled", saved.RootElement
            .GetProperty("display_name").GetString());
        Assert.Equal(3, saved.RootElement.GetProperty("locked_fields").GetArrayLength());
        Assert.True(saved.RootElement.GetProperty("mikan_identity_cookie_configured").GetBoolean());
    }

    [Theory]
    [InlineData("Bad_Id", "u2", "pt", "link", "u2.invalid")]
    [InlineData("u2", "other", "pt", "link", "u2.invalid")]
    [InlineData("u2", "u2", "missing", "link", "u2.invalid")]
    [InlineData("u2", "u2", "pt", "copy", "u2.invalid")]
    [InlineData("u2", "u2", "pt", "link", "*.bad*host")]
    public async Task InvalidProfileInputsAreRejected(
        string id,
        string adapter,
        string downloader,
        string strategy,
        string host)
    {
        await using var app = await RunningApp.StartAsync();
        using var response = await app.Client.PostAsync("/api/v1/sources", Json(new
        {
            id,
            display_name = "Invalid",
            adapter,
            downloader_id = downloader,
            file_strategy = strategy,
            allowed_torrent_hosts = new List<string> { host },
            enabled = true,
        }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal("source_profile_invalid", body.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("bad,category", "ok", 0, "move")]
    [InlineData("anime", "bad,tag", 60, "link")]
    [InlineData("anime", "ok", -2, "link")]
    [InlineData("anime", "ok", 1, "move")]
    public async Task InvalidDownloadPolicyIsRejected(
        string category,
        string tag,
        int seedingTimeMinutes,
        string strategy)
    {
        await using var app = await RunningApp.StartAsync();
        using var response = await app.Client.PostAsync("/api/v1/sources", Json(new
        {
            id = "u2-policy",
            display_name = "U2 Policy",
            adapter = "u2",
            downloader_id = "pt",
            file_strategy = strategy,
            allowed_torrent_hosts = new List<string> { "u2.invalid" },
            category,
            tags = new List<string> { tag },
            seeding_time_minutes = seedingTimeMinutes,
            enabled = true,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal("source_profile_invalid", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task InvalidDynamicTagTemplateIsRejectedBeforePersistence()
    {
        await using var app = await RunningApp.StartAsync();
        using var response = await app.Client.PostAsync("/api/v1/sources", Json(new
        {
            id = "u2-tags",
            display_name = "U2 Tags",
            adapter = "u2",
            downloader_id = "pt",
            file_strategy = "link",
            allowed_torrent_hosts = new List<string> { "u2.invalid" },
            dynamic_tag_template = "{unknown}",
            enabled = true,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal("source_profile_invalid", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task LegacyUpdateOmissionPreservesPolicyAndStrategyChangeGetsSafeSeedDefault()
    {
        await using var app = await RunningApp.StartAsync();
        using var create = await app.Client.PostAsync("/api/v1/sources", Json(new
        {
            id = "u2-legacy",
            display_name = "U2 Legacy",
            adapter = "u2",
            downloader_id = "pt",
            file_strategy = "link",
            allowed_torrent_hosts = new List<string> { "u2.invalid" },
            category = "Anime/Legacy",
            tags = new List<string> { "PT" },
            dynamic_tag_template = "{year}-legacy",
            seeding_time_minutes = 60,
            enabled = true,
        }));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        using var preserve = await app.Client.PutAsync("/api/v1/sources/u2-legacy", Json(new
        {
            display_name = "U2 Legacy Updated",
            downloader_id = "pt",
            file_strategy = "link",
            allowed_torrent_hosts = new List<string> { "u2.invalid" },
            enabled = true,
            expected_revision = 1,
        }));
        using var preserved = JsonDocument.Parse(await preserve.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, preserve.StatusCode);
        Assert.Equal("Anime/Legacy", preserved.RootElement.GetProperty("category").GetString());
        Assert.Equal("PT", preserved.RootElement.GetProperty("tags")[0].GetString());
        Assert.Equal(
            "{year}-legacy",
            preserved.RootElement.GetProperty("dynamic_tag_template").GetString());
        Assert.Equal(60, preserved.RootElement.GetProperty("seeding_time_minutes").GetInt32());

        using var move = await app.Client.PutAsync("/api/v1/sources/u2-legacy", Json(new
        {
            display_name = "U2 Legacy Move",
            downloader_id = "pt",
            file_strategy = "move",
            allowed_torrent_hosts = new List<string> { "u2.invalid" },
            dynamic_tag_template = "",
            enabled = true,
            expected_revision = 2,
        }));
        using var moved = JsonDocument.Parse(await move.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, move.StatusCode);
        Assert.Equal("Anime/Legacy", moved.RootElement.GetProperty("category").GetString());
        Assert.Equal("PT", moved.RootElement.GetProperty("tags")[0].GetString());
        Assert.Equal(
            JsonValueKind.Null,
            moved.RootElement.GetProperty("dynamic_tag_template").ValueKind);
        Assert.Equal(0, moved.RootElement.GetProperty("seeding_time_minutes").GetInt32());
    }

    [Fact]
    public async Task StaticWebUiContainsVersionedSourceProfileEditor()
    {
        await using var app = await RunningApp.StartAsync();
        var html = await app.Client.GetStringAsync("/");
        var script = await app.Client.GetStringAsync("/app.js");

        Assert.Contains("id=\"source-list\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"source-form\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"source-downloader\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"source-hosts\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"source-category\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"source-tags\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"source-dynamic-tag\"", html, StringComparison.Ordinal);
        Assert.Contains("{quarter_name}", html, StringComparison.Ordinal);
        Assert.Contains("id=\"source-seeding-time\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"source-mikan-cookie\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"source-mikan-cookie-clear\"", html, StringComparison.Ordinal);
        Assert.Contains("只粘贴等号后的内容", html, StringComparison.Ordinal);
        Assert.Contains(".AspNetCore.Identity.Application=</code> 后面的内容", html, StringComparison.Ordinal);
        Assert.Contains("Cookie 位置：设置与备份", html, StringComparison.Ordinal);
        Assert.Contains("管理来源与 Cookie", html, StringComparison.Ordinal);
        Assert.Contains("id=\"source-rss-url\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"source-media-type\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"source-rss-url-clear\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"source-rss-cron\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"source-rss-schedule-enabled\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"manual-rss-run-saved\"", html, StringComparison.Ordinal);
        Assert.Contains("/rss/run", script, StringComparison.Ordinal);
        Assert.Contains("不要求开启自动调度", script, StringComparison.Ordinal);
        Assert.Contains("秒 分 时 日 月 周", html, StringComparison.Ordinal);
        Assert.Contains("rss_feed_url_configured", script, StringComparison.Ordinal);
        Assert.Contains("rss_schedule_registered", script, StringComparison.Ordinal);
        Assert.Contains("media_type", script, StringComparison.Ordinal);
        Assert.Contains("已配置并已回填", script, StringComparison.Ordinal);
        Assert.Contains("不填写 Cookie 名、分号或整段 Cookie Header", script, StringComparison.Ordinal);
        Assert.Contains("move · 移动且不做种", html, StringComparison.Ordinal);
        Assert.Contains("id=\"route-preview-run\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"route-preview-result\"", html, StringComparison.Ordinal);
        Assert.Contains("loadSources", script, StringComparison.Ordinal);
        Assert.Contains("locked_fields", script, StringComparison.Ordinal);
        Assert.Contains("部署锁只读", script, StringComparison.Ordinal);
        Assert.Contains("mikan_identity_cookie", script, StringComparison.Ordinal);
        Assert.Contains("previewSourceRoute", script, StringComparison.Ordinal);
        Assert.Contains("expected_revision", script, StringComparison.Ordinal);
        Assert.Contains("/api/v1/sources/", script, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MikanCookieIsPrefilledPreservedAndExplicitlyClearable()
    {
        const string secret = ".AspNetCore.Identity.Application=private-cookie-value";
        await using var app = await RunningApp.StartAsync();
        using var create = await app.Client.PostAsync(
            "/api/v1/sources",
            Json(new
            {
                id = "mikan-private",
                display_name = "Mikan Private",
                adapter = "mikan",
                downloader_id = "bt",
                file_strategy = "move",
                allowed_torrent_hosts = new List<string> { "mikanani.me" },
                enabled = true,
                mikan_identity_cookie = secret,
            }));
        var createText = await create.Content.ReadAsStringAsync();
        using var created = JsonDocument.Parse(createText);

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.True(created.RootElement
            .GetProperty("mikan_identity_cookie_configured")
            .GetBoolean());
        Assert.Equal("private-cookie-value", created.RootElement
            .GetProperty("mikan_identity_cookie")
            .GetString());

        var store = app.App.Services.GetRequiredService<SourceProfileStore>();
        var stored = Assert.IsType<SourceProfileAdminRecord>(
            await store.GetAsync("mikan-private"));
        Assert.Equal("private-cookie-value", stored.MikanIdentityCookie);

        using var preserve = await app.Client.PutAsync(
            "/api/v1/sources/mikan-private",
            Json(new
            {
                display_name = "Mikan Private",
                downloader_id = "bt",
                file_strategy = "move",
                allowed_torrent_hosts = new List<string> { "mikanani.me" },
                enabled = true,
                expected_revision = 1,
            }));
        Assert.Equal(HttpStatusCode.OK, preserve.StatusCode);
        Assert.Equal(
            "private-cookie-value",
            (await store.GetAsync("mikan-private"))?.MikanIdentityCookie);

        using var clear = await app.Client.PutAsync(
            "/api/v1/sources/mikan-private",
            Json(new
            {
                display_name = "Mikan Private",
                downloader_id = "bt",
                file_strategy = "move",
                allowed_torrent_hosts = new List<string> { "mikanani.me" },
                enabled = true,
                clear_mikan_identity_cookie = true,
                expected_revision = 2,
            }));
        using var cleared = JsonDocument.Parse(
            await clear.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        Assert.False(cleared.RootElement
            .GetProperty("mikan_identity_cookie_configured")
            .GetBoolean());
        Assert.Null(
            (await store.GetAsync("mikan-private"))?.MikanIdentityCookie);
    }

    [Fact]
    public async Task MikanRssUrlIsPrefilledPreservedAndExplicitlyClearable()
    {
        const string secretUrl =
            "https://mikanani.me/RSS/MyBangumi?token=api-private-passkey";
        await using var app = await RunningApp.StartAsync();
        using var create = await app.Client.PostAsync(
            "/api/v1/sources",
            Json(new
            {
                id = "mikan-scheduled",
                display_name = "Mikan Scheduled",
                adapter = "mikan",
                downloader_id = "bt",
                file_strategy = "move",
                allowed_torrent_hosts = new List<string> { "mikanani.me" },
                enabled = true,
                rss_feed_url = secretUrl,
                rss_schedule_enabled = true,
                rss_schedule_cron = "0 5/15 * * * ?",
            }));
        var createText = await create.Content.ReadAsStringAsync();
        using var created = JsonDocument.Parse(createText);

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.True(created.RootElement.GetProperty("rss_feed_url_configured").GetBoolean());
        Assert.True(created.RootElement.GetProperty("rss_schedule_enabled").GetBoolean());
        Assert.False(created.RootElement.GetProperty("rss_schedule_registered").GetBoolean());
        Assert.Equal("never", created.RootElement.GetProperty("rss_last_run_state").GetString());
        Assert.Equal(secretUrl, created.RootElement.GetProperty("rss_feed_url").GetString());

        var listText = await app.Client.GetStringAsync("/api/v1/sources");
        var getText = await app.Client.GetStringAsync("/api/v1/sources/mikan-scheduled");
        Assert.Contains("api-private-passkey", listText, StringComparison.Ordinal);
        Assert.Contains("api-private-passkey", getText, StringComparison.Ordinal);
        var store = app.App.Services.GetRequiredService<SourceProfileStore>();
        Assert.Equal(secretUrl, (await store.GetAsync("mikan-scheduled"))?.RssFeedUrl);

        using var preserve = await app.Client.PutAsync(
            "/api/v1/sources/mikan-scheduled",
            Json(new
            {
                display_name = "Mikan Scheduled",
                downloader_id = "bt",
                file_strategy = "move",
                allowed_torrent_hosts = new List<string> { "mikanani.me" },
                enabled = true,
                expected_revision = 1,
            }));
        using var preserved = JsonDocument.Parse(await preserve.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, preserve.StatusCode);
        Assert.True(preserved.RootElement.GetProperty("rss_schedule_enabled").GetBoolean());
        Assert.Equal("0 5/15 * * * ?", preserved.RootElement.GetProperty("rss_schedule_cron").GetString());
        Assert.Equal(secretUrl, (await store.GetAsync("mikan-scheduled"))?.RssFeedUrl);

        using var invalidClear = await app.Client.PutAsync(
            "/api/v1/sources/mikan-scheduled",
            Json(new
            {
                display_name = "Mikan Scheduled",
                downloader_id = "bt",
                file_strategy = "move",
                allowed_torrent_hosts = new List<string> { "mikanani.me" },
                enabled = true,
                clear_rss_feed_url = true,
                rss_schedule_enabled = true,
                expected_revision = 2,
            }));
        Assert.Equal(HttpStatusCode.BadRequest, invalidClear.StatusCode);

        using var clear = await app.Client.PutAsync(
            "/api/v1/sources/mikan-scheduled",
            Json(new
            {
                display_name = "Mikan Scheduled",
                downloader_id = "bt",
                file_strategy = "move",
                allowed_torrent_hosts = new List<string> { "mikanani.me" },
                enabled = true,
                clear_rss_feed_url = true,
                expected_revision = 2,
            }));
        using var cleared = JsonDocument.Parse(await clear.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        Assert.False(cleared.RootElement.GetProperty("rss_feed_url_configured").GetBoolean());
        Assert.False(cleared.RootElement.GetProperty("rss_schedule_enabled").GetBoolean());
        Assert.Null((await store.GetAsync("mikan-scheduled"))?.RssFeedUrl);
    }

    [Fact]
    public async Task InvalidRssScheduleConfigurationIsRejectedBeforePersistence()
    {
        await using var app = await RunningApp.StartAsync();
        using var nonMikan = await app.Client.PostAsync(
            "/api/v1/sources",
            Json(new
            {
                id = "u2-rss",
                display_name = "U2 RSS",
                adapter = "u2",
                downloader_id = "pt",
                file_strategy = "link",
                allowed_torrent_hosts = new List<string> { "u2.invalid" },
                enabled = true,
                rss_feed_url = "https://u2.invalid/rss?passkey=private",
            }));
        using var invalidCron = await app.Client.PostAsync(
            "/api/v1/sources",
            Json(new
            {
                id = "mikan-invalid-cron",
                display_name = "Mikan invalid cron",
                adapter = "mikan",
                downloader_id = "bt",
                file_strategy = "move",
                allowed_torrent_hosts = new List<string> { "mikanani.me" },
                enabled = true,
                rss_feed_url = "https://mikanani.me/rss?passkey=private",
                rss_schedule_enabled = true,
                rss_schedule_cron = "not a cron",
            }));
        using var hostMismatch = await app.Client.PostAsync(
            "/api/v1/sources",
            Json(new
            {
                id = "mikan-host-mismatch",
                display_name = "Mikan host mismatch",
                adapter = "mikan",
                downloader_id = "bt",
                file_strategy = "move",
                allowed_torrent_hosts = new List<string> { "mikanani.me" },
                enabled = true,
                rss_feed_url = "https://other.invalid/rss?passkey=private",
                rss_schedule_enabled = true,
            }));

        Assert.Equal(HttpStatusCode.BadRequest, nonMikan.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidCron.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, hostMismatch.StatusCode);
        Assert.Null(await app.App.Services
            .GetRequiredService<SourceProfileStore>()
            .GetAsync("mikan-invalid-cron"));
    }

    [Fact]
    public async Task NonMikanAndHeaderInjectionCookiesAreRejectedWithoutEcho()
    {
        const string secret = "private-value;Other=injected";
        await using var app = await RunningApp.StartAsync();
        using var invalidAdapter = await app.Client.PostAsync(
            "/api/v1/sources",
            Json(new
            {
                id = "u2-cookie",
                display_name = "U2 Cookie",
                adapter = "u2",
                downloader_id = "pt",
                file_strategy = "link",
                allowed_torrent_hosts = new List<string> { "u2.invalid" },
                enabled = true,
                mikan_identity_cookie = "must-not-persist",
            }));
        using var injection = await app.Client.PostAsync(
            "/api/v1/sources",
            Json(new
            {
                id = "mikan-injection",
                display_name = "Mikan Injection",
                adapter = "mikan",
                downloader_id = "bt",
                file_strategy = "move",
                allowed_torrent_hosts = new List<string> { "mikanani.me" },
                enabled = true,
                mikan_identity_cookie = secret,
            }));
        var injectionText = await injection.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, invalidAdapter.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, injection.StatusCode);
        Assert.DoesNotContain(secret, injectionText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RoutePreviewAndIngestUseAdapterBehindCustomProfileId()
    {
        await using var app = await RunningApp.StartAsync();
        using var create = await app.Client.PostAsync("/api/v1/sources", Json(new
        {
            id = "u2-anime",
            display_name = "U2 Anime",
            adapter = "u2",
            downloader_id = "pt",
            file_strategy = "link",
            allowed_torrent_hosts = new List<string> { "u2.invalid" },
            enabled = true,
        }));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        using var preview = await app.Client.PostAsync(
            "/api/v1/sources/u2-anime/route-preview",
            Json(new { title = "U2 route preview", source_work_id = "u2-work", anidbid = 42 }));
        using var route = JsonDocument.Parse(await preview.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.True(route.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal("u2-anime", route.RootElement.GetProperty("source_profile_id").GetString());
        Assert.Equal("u2", route.RootElement.GetProperty("adapter").GetString());
        Assert.Equal("pt", route.RootElement.GetProperty("downloader_id").GetString());
        Assert.Equal("animegonet", route.RootElement.GetProperty("category").GetString());
        Assert.Equal(JsonValueKind.Null, route.RootElement.GetProperty("dynamic_tag_template").ValueKind);
        Assert.Equal(0, route.RootElement.GetProperty("seeding_time_minutes").GetInt32());
        Assert.Equal(1, route.RootElement.GetProperty("rss_rule_revision").GetInt64());

        using var ingest = await app.Client.PostAsync("/api/v1/ingest", Json(new
        {
            source = "u2-anime",
            data = new[]
            {
                new
                {
                    torrent = "https://u2.invalid/passkey/custom-profile.torrent",
                    info = new { title = "U2 route preview", source_work_id = "u2-work", anidbid = 42 },
                },
            },
        }));
        using var accepted = JsonDocument.Parse(await ingest.Content.ReadAsStreamAsync());
        var item = accepted.RootElement.GetProperty("items")[0];
        Assert.Equal("staged", item.GetProperty("status").GetString());
        Assert.Equal("u2-anime", item.GetProperty("source_profile_id").GetString());
        Assert.Equal("pt", item.GetProperty("downloader_id").GetString());
    }

    [Fact]
    public async Task RoutePreviewReturnsRealAdapterValidationWithoutSideEffects()
    {
        await using var app = await RunningApp.StartAsync();
        using var preview = await app.Client.PostAsync(
            "/api/v1/sources/mikan/route-preview",
            Json(new { title = "Missing Mikan identity" }));
        using var route = JsonDocument.Parse(await preview.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.False(route.RootElement.GetProperty("valid").GetBoolean());
        var errors = route.RootElement.GetProperty("errors").EnumerateArray()
            .Select(item => item.GetString()).ToArray();
        Assert.Contains(errors, error => error!.Contains("mikanid", StringComparison.Ordinal));
        Assert.Contains(errors, error => error!.Contains("bgmid", StringComparison.Ordinal));

        using var tasks = await app.Client.GetAsync("/api/v1/metadata/tasks");
        using var taskList = JsonDocument.Parse(await tasks.Content.ReadAsStreamAsync());
        Assert.Empty(taskList.RootElement.GetProperty("items").EnumerateArray());
    }

    private static StringContent Json(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
}
