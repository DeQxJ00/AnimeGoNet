using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AnimeGoNet.App.Tests.DataUpdate;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Tests.Api;

public sealed class DataUpdateApiTests
{
    [Fact]
    public async Task StatusExposesPolicyAndEmptyVersionState()
    {
        await using var app = await RunningApp.StartAsync();

        using var response = await app.Client.GetAsync("/api/v1/data-update");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = json.RootElement;
        Assert.False(root.GetProperty("scheduled_enabled").GetBoolean());
        Assert.Equal("0 0 4 * * ?", root.GetProperty("cron").GetString());
        Assert.False(root.GetProperty("manifest_configured").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("active_version").ValueKind);
        Assert.Empty(root.GetProperty("versions").EnumerateArray());
        Assert.Empty(root.GetProperty("downloads").EnumerateArray());
    }

    [Fact]
    public async Task ManualCheckWorksWhileSchedulingIsDisabled()
    {
        var handler = new RoutingHandler();
        var manifestUrl = new Uri("https://updates.test/manifest.json");
        handler.Set(
            manifestUrl,
            () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(CheckOnlyManifest()),
            });
        using var updates = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        await using var app = await RunningApp.StartAsync(
            configure: defaults => defaults with
            {
                DataUpdate = defaults.DataUpdate with
                {
                    Enabled = false,
                    ManifestUrl = manifestUrl,
                },
            },
            dataUpdateHttpClient: updates);

        using var response = await app.Client.PostAsync(
            "/api/v1/data-update/check",
            JsonContent.Create(new { }));
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        using var statusResponse = await app.Client.GetAsync("/api/v1/data-update");
        using var status = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("update_available", result.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "2026.07.29.1",
            result.RootElement.GetProperty("data_version").GetString());
        Assert.False(status.RootElement.GetProperty("scheduled_enabled").GetBoolean());
        Assert.Equal(
            "update_available",
            status.RootElement
                .GetProperty("last_transfer_run")
                .GetProperty("status")
                .GetString());
        Assert.Equal([manifestUrl], handler.Requests);
    }

    [Fact]
    public async Task MissingManifestAndUnavailableRollbackReturnStableErrors()
    {
        await using var app = await RunningApp.StartAsync();

        using var check = await app.Client.PostAsync(
            "/api/v1/data-update/check",
            JsonContent.Create(new { }));
        using var checkJson = JsonDocument.Parse(await check.Content.ReadAsStringAsync());
        using var rollback = await app.Client.PostAsync(
            "/api/v1/data-update/rollback",
            JsonContent.Create(new { }));
        using var rollbackJson = JsonDocument.Parse(await rollback.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, check.StatusCode);
        Assert.Equal(
            "data_manifest_url_missing",
            checkJson.RootElement.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Conflict, rollback.StatusCode);
        Assert.Equal(
            "data_rollback_version_unavailable",
            rollbackJson.RootElement.GetProperty("code").GetString());
    }

    private static byte[] CheckOnlyManifest() =>
        Encoding.UTF8.GetBytes(
            $$"""
            {
              "schema_version":1,
              "data_version":"2026.07.29.1",
              "generated_at_utc":"2026-07-29T12:00:00.0000000+00:00",
              "minimum_client_version":"0.1.0",
              "upstream":{
                "repository":"https://github.com/bangumi/Archive",
                "release":"archive",
                "asset":"archive.zip",
                "sha256":"{{new string('a', 64)}}"
              },
              "assets":[
                {
                  "kind":"subjects",
                  "file_name":"subjects-v1.jsonl.gz",
                  "url":"https://updates.test/subjects-v1.jsonl.gz",
                  "size_bytes":1,
                  "sha256":"{{new string('b', 64)}}",
                  "record_count":1,
                  "subject_id_min":1,
                  "subject_id_max":1
                },
                {
                  "kind":"episodes",
                  "file_name":"episodes-v1.jsonl.gz",
                  "url":"https://updates.test/episodes-v1.jsonl.gz",
                  "size_bytes":1,
                  "sha256":"{{new string('c', 64)}}",
                  "record_count":1,
                  "subject_id_min":1,
                  "subject_id_max":1
                }
              ],
              "totals":{"subjects":1,"episodes":1}
            }
            """);
}
