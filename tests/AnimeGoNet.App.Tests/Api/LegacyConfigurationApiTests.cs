using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnimeGoNet.App.Tests.Api;

public sealed class LegacyConfigurationApiTests
{
    [Fact]
    public async Task GetSupportsAllDefaultCommentAndRawBehindLegacyAccessKey()
    {
        const string accessKey = "legacy-config-test-access";
        await using var app = await RunningApp.StartAsync(accessKey: accessKey);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await app.Client.GetAsync("/api/config?key=raw")).StatusCode);

        using var raw = await SendAsync(app, HttpMethod.Get, "/api/config?key=raw", accessKey);
        using var rawJson = JsonDocument.Parse(await raw.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, raw.StatusCode);
        Assert.Equal(200, rawJson.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("配置文件", rawJson.RootElement.GetProperty("msg").GetString());
        var yaml = Encoding.UTF8.GetString(Convert.FromBase64String(
            rawJson.RootElement.GetProperty("data").GetString()!));
        Assert.Contains("version: 1.7.1", yaml, StringComparison.Ordinal);
        Assert.Contains(app.RootPath, yaml, StringComparison.Ordinal);

        using var all = await SendAsync(app, HttpMethod.Get, "/api/config?key=all", accessKey);
        using var allJson = JsonDocument.Parse(await all.Content.ReadAsStreamAsync());
        Assert.Equal(200, allJson.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("1.7.1", allJson.RootElement.GetProperty("data")
            .GetProperty("version").GetString());
        Assert.Equal(
            Path.Combine(app.RootPath, "data"),
            allJson.RootElement.GetProperty("data").GetProperty("paths")
                .GetProperty("data_path").GetString());

        using var defaults = await SendAsync(
            app,
            HttpMethod.Get,
            "/api/config?key=default",
            accessKey);
        using var defaultsJson = JsonDocument.Parse(await defaults.Content.ReadAsStreamAsync());
        Assert.Equal(200, defaultsJson.RootElement.GetProperty("code").GetInt32());
        Assert.True(defaultsJson.RootElement.GetProperty("data")
            .GetProperty("data_update").GetProperty("auto_download").GetBoolean());

        using var comments = await SendAsync(
            app,
            HttpMethod.Get,
            "/api/config?key=comment",
            accessKey);
        using var commentsJson = JsonDocument.Parse(await comments.Content.ReadAsStreamAsync());
        Assert.Equal(200, commentsJson.RootElement.GetProperty("code").GetInt32());
        Assert.Contains(
            "共享",
            commentsJson.RootElement.GetProperty("data").GetProperty("paths")
                .GetProperty("download_path").GetString(),
            StringComparison.Ordinal);

        using var unsupported = await SendAsync(
            app,
            HttpMethod.Get,
            "/api/config?key=unknown",
            accessKey);
        using var unsupportedJson = JsonDocument.Parse(
            await unsupported.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, unsupported.StatusCode);
        Assert.Equal(300, unsupportedJson.RootElement.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task RawPutValidatesBeforeAtomicReplaceBacksUpAndRequiresRestart()
    {
        await using var app = await RunningApp.StartAsync();
        var path = Path.Combine(app.RootPath, "data", "animego.yaml");
        var initial = await ReadRawAsync(app);
        var first = initial.Replace("language: zh-CN", "language: ja-JP", StringComparison.Ordinal);

        using var firstResponse = await app.Client.PutAsJsonAsync(
            "/api/config",
            new
            {
                key = "raw",
                backup = true,
                config_raw = Convert.ToBase64String(Encoding.UTF8.GetBytes(first)),
            });
        using var firstJson = JsonDocument.Parse(await firstResponse.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(200, firstJson.RootElement.GetProperty("code").GetInt32());
        Assert.Contains("需要重启", firstJson.RootElement.GetProperty("msg").GetString());
        Assert.Equal(first, await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "animego-api-*.yaml"));

        var second = first.Replace("language: ja-JP", "language: en-US", StringComparison.Ordinal);
        using var secondResponse = await app.Client.PutAsJsonAsync(
            "/api/config?backup=true",
            new
            {
                key = "raw",
                backup = false,
                config_raw = Convert.ToBase64String(Encoding.UTF8.GetBytes(second)),
            });
        using var secondJson = JsonDocument.Parse(await secondResponse.Content.ReadAsStreamAsync());
        Assert.Equal(200, secondJson.RootElement.GetProperty("code").GetInt32());
        Assert.Equal(second, await File.ReadAllTextAsync(path));
        var backup = Assert.Single(Directory.GetFiles(
            Path.GetDirectoryName(path)!,
            "animego-api-*.yaml"));
        Assert.Equal(first, await File.ReadAllTextAsync(backup));

        var beforeFailure = await File.ReadAllBytesAsync(path);
        using var invalid = await app.Client.PutAsJsonAsync(
            "/api/config",
            new
            {
                key = "raw",
                backup = true,
                config_raw = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                    "version: 1.7.1\nmetadata:\n  tmdb:\n    timeout_seconds: nope\n")),
            });
        using var invalidJson = JsonDocument.Parse(await invalid.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Equal(300, invalidJson.RootElement.GetProperty("code").GetInt32());
        Assert.Equal(beforeFailure, await File.ReadAllBytesAsync(path));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, "animego-api-*.yaml"));
    }

    [Fact]
    public async Task AllPutRoundTripsTypedJsonAndQueryOverridesBodyFlags()
    {
        await using var app = await RunningApp.StartAsync();
        using var all = await app.Client.GetAsync("/api/config?key=all");
        var envelope = JsonNode.Parse(await all.Content.ReadAsStringAsync())!.AsObject();
        var configuration = envelope["data"]!.AsObject();
        configuration["metadata"]!["tmdb"]!["language"] = "ja-JP";
        configuration["web"]!["access_key"] = "webui-animegohelper-access";
        var request = new JsonObject
        {
            ["key"] = "raw",
            ["backup"] = true,
            ["config"] = configuration.DeepClone(),
        };

        using var response = await app.Client.PutAsync(
            "/api/config?key=all&backup=false",
            new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json"));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(200, json.RootElement.GetProperty("code").GetInt32());

        using var reread = await app.Client.GetAsync("/api/config?key=all");
        using var rereadJson = JsonDocument.Parse(await reread.Content.ReadAsStreamAsync());
        Assert.Equal(
            "ja-JP",
            rereadJson.RootElement.GetProperty("data").GetProperty("metadata")
                .GetProperty("tmdb").GetProperty("language").GetString());
        Assert.Equal(
            "webui-animegohelper-access",
            rereadJson.RootElement.GetProperty("data").GetProperty("web")
                .GetProperty("access_key").GetString());
        Assert.Empty(Directory.GetFiles(
            Path.Combine(app.RootPath, "data"),
            "animego-api-*.yaml"));
    }

    [Theory]
    [InlineData("{", "参数错误")]
    [InlineData("{\"key\":\"raw\",\"config_raw\":\"%%%\"}", "参数格式错误")]
    [InlineData("{\"key\":\"all\"}", "参数错误，未传入对应数据")]
    [InlineData("{\"key\":\"all\",\"config\":{\"version\":\"1.7.1\",\"Version\":\"1.7.1\"}}", "参数格式错误")]
    public async Task InvalidRequestUsesLegacyFailureEnvelopeWithoutCreatingAFile(
        string body,
        string message)
    {
        await using var app = await RunningApp.StartAsync();

        using var response = await app.Client.PutAsync(
            "/api/config",
            new StringContent(body, Encoding.UTF8, "application/json"));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(300, json.RootElement.GetProperty("code").GetInt32());
        Assert.Equal(message, json.RootElement.GetProperty("msg").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("data").ValueKind);
        Assert.False(File.Exists(Path.Combine(app.RootPath, "data", "animego.yaml")));
    }

    private static async Task<string> ReadRawAsync(RunningApp app)
    {
        using var response = await app.Client.GetAsync("/api/config?key=raw");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return Encoding.UTF8.GetString(Convert.FromBase64String(
            json.RootElement.GetProperty("data").GetString()!));
    }

    private static Task<HttpResponseMessage> SendAsync(
        RunningApp app,
        HttpMethod method,
        string path,
        string accessKey)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-AnimeGo-Access-Key", accessKey);
        return app.Client.SendAsync(request);
    }
}
