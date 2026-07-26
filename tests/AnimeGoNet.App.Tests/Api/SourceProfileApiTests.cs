using System.Net;
using System.Text;
using System.Text.Json;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Api;

public sealed class SourceProfileApiTests
{
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
            rss_filter_enabled = true,
            rss_priority_enabled = true,
            enabled = true,
        }));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStreamAsync());
        Assert.Equal(1, created.RootElement.GetProperty("revision").GetInt64());
        Assert.Equal("u2.invalid", created.RootElement.GetProperty("allowed_torrent_hosts")[0].GetString());
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
            rss_filter_enabled = false,
            rss_priority_enabled = false,
            enabled = true,
            expected_revision = 1,
        }));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var updated = JsonDocument.Parse(await update.Content.ReadAsStreamAsync());
        Assert.Equal(2, updated.RootElement.GetProperty("revision").GetInt64());
        Assert.Equal("bt", updated.RootElement.GetProperty("downloader_id").GetString());
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
                SELECT downloader_id || ':' || source_profile_revision
                FROM ingest_tasks WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$id", taskId);
            Assert.Equal("pt:1", await command.ExecuteScalarAsync());
        }

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
            "/api/v1/sources/u2?expected_revision=2");
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
        Assert.Contains("move · 移动且不做种", html, StringComparison.Ordinal);
        Assert.Contains("id=\"route-preview-run\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"route-preview-result\"", html, StringComparison.Ordinal);
        Assert.Contains("loadSources", script, StringComparison.Ordinal);
        Assert.Contains("previewSourceRoute", script, StringComparison.Ordinal);
        Assert.Contains("expected_revision", script, StringComparison.Ordinal);
        Assert.Contains("/api/v1/sources/", script, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
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
