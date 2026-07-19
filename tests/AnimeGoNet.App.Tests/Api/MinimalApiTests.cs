using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AnimeGoNet.App.Tests.Api;

public sealed class MinimalApiTests
{
    [Fact]
    public async Task DockerModeRequiresAccessKey()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "animegonet-app-tests", Guid.NewGuid().ToString("N"));
        var options = AnimeGoNet.Core.Configuration.AnimeGoDefaults.CreateNative(rootPath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AnimeGoApplication.BuildAsync([], options, accessKey: null, runningInContainer: true));

        Assert.Contains("requires a non-empty access_key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PingPreservesLegacyEnvelope()
    {
        await using var app = await RunningApp.StartAsync();

        using var response = await app.Client.GetAsync("/ping");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(200, json.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("pong", json.RootElement.GetProperty("msg").GetString());
        Assert.True(json.RootElement.GetProperty("data").GetProperty("time").GetInt64() > 0);
    }

    [Fact]
    public async Task StatusReportsDatabaseAndEffectivePaths()
    {
        await using var app = await RunningApp.StartAsync();

        using var response = await app.Client.GetAsync("/api/v1/status");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.True(
            json.RootElement.TryGetProperty("database_schema_version", out var schemaVersion),
            json.RootElement.GetRawText());
        Assert.Equal(1, schemaVersion.GetInt32());
        Assert.Equal(Path.Combine(app.RootPath, "data"), json.RootElement.GetProperty("paths").GetProperty("data_path").GetString());
        Assert.True(File.Exists(Path.Combine(app.RootPath, "data", "animegonet.db")));
    }

    [Fact]
    public async Task ProtectedApiAcceptsDirectAndLegacyHashedAccessKeys()
    {
        const string accessKey = "test-secret";
        await using var app = await RunningApp.StartAsync(accessKey);

        using var denied = await app.Client.GetAsync("/api/v1/status");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        using var directRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/status");
        directRequest.Headers.Add("X-AnimeGo-Access-Key", accessKey);
        using var direct = await app.Client.SendAsync(directRequest);
        Assert.Equal(HttpStatusCode.OK, direct.StatusCode);

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(accessKey)));
        using var legacyRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/status");
        legacyRequest.Headers.Add("Access-Key", hash);
        using var legacy = await app.Client.SendAsync(legacyRequest);
        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
    }
}
