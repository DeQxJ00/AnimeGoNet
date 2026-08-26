using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Tests.Api;

public sealed class U2PluginApiTests
{
    private const string Endpoint = "/api/v1/plugins/inner_plugin_u2/ingest";

    [Fact]
    public async Task DedicatedAccessKeyProtectsU2PluginAndAcceptedItemIsAudited()
    {
        await using var app = await RunningApp.StartAsync(
            webUiAccessKey: string.Empty,
            u2AccessKey: "u2-secret",
            configure: AddU2Source);
        var payload = ValidPayload();

        using var unauthorized = await app.Client.PostAsJsonAsync(Endpoint, payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add("X-AnimeGo-Access-Key", "u2-secret");
        using var accepted = await app.Client.SendAsync(request);
        using var body = JsonDocument.Parse(await accepted.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal("inner_plugin_u2", body.RootElement.GetProperty("plugin").GetString());
        Assert.Equal(1, body.RootElement.GetProperty("accepted_count").GetInt32());
        var item = body.RootElement.GetProperty("items")[0];
        Assert.Equal(65893, item.GetProperty("u2id").GetInt32());
        Assert.Equal("u2", item.GetProperty("source_profile_id").GetString());
        Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("ingest_id").GetString()));

        using var logs = await app.Client.GetAsync("/api/v1/logs/u2-plugin-calls");
        using var logBody = JsonDocument.Parse(await logs.Content.ReadAsStreamAsync());
        var call = Assert.Single(logBody.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("success", call.GetProperty("result").GetString());
        var auditItem = Assert.Single(call.GetProperty("items").EnumerateArray());
        Assert.Equal(65893, auditItem.GetProperty("u2id").GetInt32());
        Assert.Equal(9125, auditItem.GetProperty("anidbid").GetInt32());
        Assert.Equal("BDRip", auditItem.GetProperty("category_name").GetString());
        Assert.Equal(
            "https://u2.dmhy.org/details.php?id=65893",
            auditItem.GetProperty("details_url").GetString());
        Assert.DoesNotContain("u2-test-passkey", await logs.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsMismatchedIdsAndUnsupportedMediaTypeWithoutStaging()
    {
        await using var app = await RunningApp.StartAsync(
            webUiAccessKey: string.Empty,
            u2AccessKey: "u2-secret",
            configure: AddU2Source);
        var payload = new
        {
            schema_version = 1,
            source_profile_id = "u2",
            items = new[]
            {
                new
                {
                    u2id = 65893,
                    title = "Invalid U2 item",
                    details_url = "https://u2.dmhy.org/details.php?id=65894&hit=1",
                    torrent_url = "https://u2.dmhy.org/download.php?id=65893&passkey=secret&https=1",
                    anidbid = 9125,
                    category = new { id = 12, name = "BDRip" },
                    media_type = "auto",
                },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add("X-AnimeGo-Access-Key", "u2-secret");
        using var response = await app.Client.SendAsync(request);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var item = body.RootElement.GetProperty("items")[0];
        var errors = item.GetProperty("errors").EnumerateArray().Select(value => value.GetString()).ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body.RootElement.GetProperty("accepted_count").GetInt32());
        Assert.Contains("u2_details_url_invalid", errors);
        Assert.Contains("u2_media_type_invalid", errors);
    }

    private static object ValidPayload() => new
    {
        schema_version = 1,
        source_profile_id = "u2",
        items = new[]
        {
            new
            {
                u2id = 65893,
                title = "U2 API contract fixture",
                details_url = "https://u2.dmhy.org/details.php?id=65893&hit=1&passkey=u2-test-passkey",
                torrent_url = "https://u2.dmhy.org/download.php?id=65893&passkey=u2-test-passkey&https=1",
                anidbid = 9125,
                category = new { id = 12, name = "BDRip" },
                media_type = "tv",
            },
        },
    };

    private static AnimeGoOptions AddU2Source(AnimeGoOptions options) => options with
    {
        InitialSourceProfiles =
        [
            .. options.InitialSourceProfiles,
            new SourceProfileSeed
            {
                Id = "u2",
                DisplayName = "U2",
                Adapter = "u2",
                MediaType = "tv",
                DownloaderId = "pt",
                FileStrategy = FileStrategy.Link,
                AllowedTorrentHosts = ["u2.dmhy.org"],
            },
        ],
    };
}
