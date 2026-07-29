using System.Net;
using System.Net.Http.Json;
using System.IO.Compression;
using System.Security.Cryptography;
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

    [Fact]
    public async Task OfflineZipImportsWithoutManifestEndpoint()
    {
        await using var app = await RunningApp.StartAsync();
        using var content = ZipStreamContent(CreateOfflineArchive("2026.07.29.offline"));

        using var response = await app.Client.PostAsync(
            "/api/v1/data-update/offline/import",
            content);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        using var statusResponse = await app.Client.GetAsync("/api/v1/data-update");
        using var status = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("completed", result.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "2026.07.29.offline",
            result.RootElement.GetProperty("active_version").GetString());
        Assert.Equal(
            "2026.07.29.offline",
            status.RootElement.GetProperty("active_version").GetString());
        Assert.Equal(
            "imported",
            status.RootElement
                .GetProperty("downloads")
                .EnumerateArray()
                .Single()
                .GetProperty("state")
                .GetString());
    }

    [Theory]
    [InlineData("extra", "data_offline_archive_entries_invalid")]
    [InlineData("path", "data_offline_archive_path_invalid")]
    [InlineData("hash", "data_offline_asset_sha256_mismatch")]
    public async Task InvalidOfflineZipKeepsPreviousActiveVersion(
        string fault,
        string expectedCode)
    {
        await using var app = await RunningApp.StartAsync();
        using (var baseline = ZipContent(CreateOfflineArchive("2026.07.29.baseline")))
        using (var baselineResponse = await app.Client.PostAsync(
            "/api/v1/data-update/offline/import",
            baseline))
        {
            Assert.Equal(HttpStatusCode.OK, baselineResponse.StatusCode);
        }
        using var invalid = ZipContent(CreateOfflineArchive("2026.07.29.invalid", fault));

        using var response = await app.Client.PostAsync(
            "/api/v1/data-update/offline/import",
            invalid);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        using var statusResponse = await app.Client.GetAsync("/api/v1/data-update");
        using var status = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedCode, result.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            "2026.07.29.baseline",
            status.RootElement.GetProperty("active_version").GetString());
        Assert.Single(status.RootElement.GetProperty("versions").EnumerateArray());
        Assert.Single(status.RootElement.GetProperty("downloads").EnumerateArray());
    }

    [Fact]
    public async Task OfflineZipRequiresBinaryContentType()
    {
        await using var app = await RunningApp.StartAsync();
        using var content = new ByteArrayContent(CreateOfflineArchive("2026.07.29.offline"));
        content.Headers.ContentType = new("application/json");

        using var response = await app.Client.PostAsync(
            "/api/v1/data-update/offline/import",
            content);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(
            "data_offline_content_type_invalid",
            result.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task EmptyOfflineZipIsRejectedBeforeStorage()
    {
        await using var app = await RunningApp.StartAsync();
        using var content = ZipContent([]);

        using var response = await app.Client.PostAsync(
            "/api/v1/data-update/offline/import",
            content);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "data_offline_archive_size_invalid",
            result.RootElement.GetProperty("code").GetString());
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

    private static ByteArrayContent ZipContent(byte[] value)
    {
        var content = new ByteArrayContent(value);
        content.Headers.ContentType = new("application/zip");
        return content;
    }

    private static StreamContent ZipStreamContent(byte[] value)
    {
        var content = new StreamContent(new MemoryStream(value, writable: false));
        content.Headers.ContentType = new("application/zip");
        return content;
    }

    private static byte[] CreateOfflineArchive(string version, string? fault = null)
    {
        var subjects = Gzip(
            """
            {"id":51,"name":"CLANNAD","name_cn":"CLANNAD","air_date":"2007-10-05","episode_count":1}

            """);
        var episodes = Gzip(
            """
            {"id":1423,"subject_id":51,"sort":1,"episode":"1","air_date":"2007-10-05"}

            """);
        var subjectName = $"subjects-{version}.jsonl.gz";
        var episodeName = $"episodes-{version}.jsonl.gz";
        var manifest = Encoding.UTF8.GetBytes(
            $$"""
            {
              "schema_version":1,
              "data_version":"{{version}}",
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
                  "file_name":"{{subjectName}}",
                  "url":"https://updates.test/{{subjectName}}",
                  "size_bytes":{{subjects.LongLength}},
                  "sha256":"{{Sha256(subjects)}}",
                  "record_count":1,
                  "subject_id_min":1,
                  "subject_id_max":100
                },
                {
                  "kind":"episodes",
                  "file_name":"{{episodeName}}",
                  "url":"https://updates.test/{{episodeName}}",
                  "size_bytes":{{episodes.LongLength}},
                  "sha256":"{{(fault == "hash" ? new string('b', 64) : Sha256(episodes))}}",
                  "record_count":1,
                  "subject_id_min":1,
                  "subject_id_max":100
                }
              ],
              "totals":{"subjects":1,"episodes":1}
            }
            """);
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(
            output,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            WriteEntry(archive, "manifest.json", manifest);
            WriteEntry(archive, subjectName, subjects);
            WriteEntry(archive, episodeName, episodes);
            if (fault == "extra")
            {
                WriteEntry(archive, "unexpected.txt", "unexpected"u8.ToArray());
            }
            else if (fault == "path")
            {
                WriteEntry(archive, "../unexpected.txt", "unexpected"u8.ToArray());
            }
        }
        return output.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] value)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.SmallestSize);
        using var stream = entry.Open();
        stream.Write(value);
    }

    private static byte[] Gzip(string value)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(
            output,
            CompressionLevel.SmallestSize,
            leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(value));
        }
        return output.ToArray();
    }

    private static string Sha256(byte[] value) =>
        Convert.ToHexStringLower(SHA256.HashData(value));
}
