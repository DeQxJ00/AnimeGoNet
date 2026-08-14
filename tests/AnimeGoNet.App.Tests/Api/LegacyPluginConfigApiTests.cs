using System.Net;
using System.Text;
using System.Text.Json;
using AnimeGoNet.Core.Rules;
using AnimeGoNet.Data.Rules;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Api;

public sealed class LegacyPluginConfigApiTests
{
    private const string LegacyJson = """
        {
          "Filiter0": {
            "second": {
              "is_enable_whitelist": true,
              "whitelist": ["", "简体", "简体"],
              "is_enable_blacklist": false,
              "blacklist": []
            },
            "FIRST": {
              "is_enable_whitelist": false,
              "whitelist": [],
              "is_enable_blacklist": true,
              "blacklist": ["WebRip"]
            }
          },
          "Filiter1": {},
          "Filiter2": {},
          "Filiter3": {},
          "Filiter4": {}
        }
        """;

    [Fact]
    public async Task UnmodifiedLegacyClientCanUploadAndDownloadConfiguration()
    {
        await using var app = await RunningApp.StartAsync();
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(LegacyJson));
        var payload = JsonSerializer.Serialize(new
        {
            name = "filter/mikan_tool.py",
            data = encoded,
        });

        using var post = await app.Client.PostAsync(
            "/api/plugin/config",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        using var postJson = JsonDocument.Parse(await post.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        Assert.Equal(200, postJson.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("写入插件配置文件成功", postJson.RootElement.GetProperty("msg").GetString());
        Assert.Equal("filter/mikan_tool.py", postJson.RootElement.GetProperty("data").GetProperty("name").GetString());

        using var get = await app.Client.GetAsync("/api/plugin/config?name=filter%2Fmikan_tool.py");
        using var getJson = JsonDocument.Parse(await get.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(200, getJson.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("读取插件配置文件成功", getJson.RootElement.GetProperty("msg").GetString());
        var downloaded = Convert.FromBase64String(
            getJson.RootElement.GetProperty("data").GetProperty("data").GetString()!);
        var config = LegacyMikanFilterCodec.Parse(downloaded);
        Assert.Equal(["second", "FIRST"], config.Filiter0.Select(pair => pair.Key));
        Assert.Equal(["", "简体", "简体"], config.Filiter0[0].Value.Whitelist);
        Assert.Equal("WebRip", config.Filiter0[1].Value.Blacklist[0]);

        var snapshot = await app.App.Services.GetRequiredService<LegacyMikanFilterStore>().GetAsync("mikan");
        Assert.NotNull(snapshot);
        Assert.Equal(2, snapshot.Revision);
        Assert.Equal("legacy_api", snapshot.UpdatedSource);
        Assert.Empty(Directory.EnumerateFiles(app.RootPath, "*.py", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("inner_plugin_mikan")]
    [InlineData("filter/mikan_tool")]
    [InlineData("mikan_tool.py")]
    [InlineData("mikan_tool")]
    [InlineData("plugin/filter/mikan_tool.py")]
    [InlineData("filter\\mikan_tool.py")]
    public async Task EquivalentAliasesResolveToBuiltInFilter(string alias)
    {
        await using var app = await RunningApp.StartAsync();
        using var response = await app.Client.GetAsync(
            "/api/plugin/config?name=" + Uri.EscapeDataString(alias));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(200, json.RootElement.GetProperty("code").GetInt32());
        Assert.Equal(alias, json.RootElement.GetProperty("data").GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("not-base64")]
    [InlineData("W10=")]
    [InlineData("eyJGaWxpdGVyMCI6W119")]
    public async Task InvalidBase64OrJsonReturnsLegacyFailureEnvelope(string data)
    {
        await using var app = await RunningApp.StartAsync();
        var payload = JsonSerializer.Serialize(new { name = "filter/mikan_tool.py", data });

        using var response = await app.Client.PostAsync(
            "/api/plugin/config",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(300, json.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("配置解析错误", json.RootElement.GetProperty("msg").GetString());
    }

    [Fact]
    public async Task UnknownPluginReturnsLegacyFailureEnvelopeWithoutTouchingStorage()
    {
        await using var app = await RunningApp.StartAsync();
        var payload = JsonSerializer.Serialize(new { name = "../other.py", data = "e30=" });

        using var post = await app.Client.PostAsync(
            "/api/plugin/config",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        using var postJson = JsonDocument.Parse(await post.Content.ReadAsStreamAsync());
        using var get = await app.Client.GetAsync("/api/plugin/config?name=other.py");
        using var getJson = JsonDocument.Parse(await get.Content.ReadAsStreamAsync());

        Assert.Equal(300, postJson.RootElement.GetProperty("code").GetInt32());
        Assert.Equal(300, getJson.RootElement.GetProperty("code").GetInt32());
        var snapshot = await app.App.Services.GetRequiredService<LegacyMikanFilterStore>().GetAsync("mikan");
        Assert.Equal(1, snapshot!.Revision);
    }

    [Fact]
    public async Task ConcurrentLegacyUploadsAllCommitAsFullReplacements()
    {
        await using var app = await RunningApp.StartAsync();
        var requests = Enumerable.Range(0, 8).Select(async index =>
        {
            var json = $"{{\"Filiter0\":{{\"rule-{index}\":{{}}}},\"Filiter1\":{{}},\"Filiter2\":{{}},\"Filiter3\":{{}},\"Filiter4\":{{}}}}";
            var payload = JsonSerializer.Serialize(new
            {
                name = "filter/mikan_tool.py",
                data = Convert.ToBase64String(Encoding.UTF8.GetBytes(json)),
            });
            using var response = await app.Client.PostAsync(
                "/api/plugin/config",
                new StringContent(payload, Encoding.UTF8, "application/json"));
            using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            return (response.StatusCode, Code: body.RootElement.GetProperty("code").GetInt32());
        });

        var results = await Task.WhenAll(requests);

        Assert.All(results, result =>
        {
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Equal(200, result.Code);
        });
        var snapshot = await app.App.Services.GetRequiredService<LegacyMikanFilterStore>().GetAsync("mikan");
        Assert.Equal(9, snapshot!.Revision);
        Assert.Single(snapshot.Config.Filiter0);
    }

    [Fact]
    public async Task LegacyDownloadManagerBypassesConfiguredMikanFilter()
    {
        await using var app = await RunningApp.StartAsync();
        const string rejectAll = """
            {
              "Filiter0": {
                "reject-all": {
                  "is_enable_whitelist": true,
                  "whitelist": ["never-match"],
                  "is_enable_blacklist": false,
                  "blacklist": []
                }
              },
              "Filiter1": {}, "Filiter2": {}, "Filiter3": {}, "Filiter4": {}
            }
            """;
        var upload = JsonSerializer.Serialize(new
        {
            name = "filter/mikan_tool.py",
            data = Convert.ToBase64String(Encoding.UTF8.GetBytes(rejectAll)),
        });
        using var uploaded = await app.Client.PostAsync(
            "/api/plugin/config",
            new StringContent(upload, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        const string request = """
            {
              "source": "mikan",
              "data": [{
                "torrent": "https://mikanani.me/Download/fast.torrent",
                "info": { "name": "Fast download", "url": "https://mikanani.me/Home/Bangumi/3951" }
              }]
            }
            """;

        using var response = await app.Client.PostAsync(
            "/api/download/manager",
            new StringContent(request, Encoding.UTF8, "application/json"));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(200, json.RootElement.GetProperty("code").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("data").GetProperty("accepted_count").GetInt32());
        Assert.Equal("staged", json.RootElement.GetProperty("data").GetProperty("items")[0]
            .GetProperty("status").GetString());
    }
}
