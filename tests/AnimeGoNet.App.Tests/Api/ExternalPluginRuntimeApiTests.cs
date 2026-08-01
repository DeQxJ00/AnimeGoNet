using System.Net;
using System.Text.Json;

namespace AnimeGoNet.App.Tests.Api;

public sealed class ExternalPluginRuntimeApiTests
{
    [Fact]
    public async Task StatusReturnsExplicitEmptyRuntimeCollection()
    {
        await using var app = await RunningApp.StartAsync();

        using var response = await app.Client.GetAsync("/api/v1/status");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            JsonValueKind.Array,
            body.RootElement.GetProperty("external_plugins")
                .GetProperty("runtimes").ValueKind);
        Assert.Empty(body.RootElement.GetProperty("external_plugins")
            .GetProperty("runtimes").EnumerateArray());
    }

    [Fact]
    public async Task ResetMissingPluginReturnsStableNotFoundWithoutStartingProcess()
    {
        await using var app = await RunningApp.StartAsync();

        using var response = await app.Client.PostAsync(
            "/api/v1/plugins/com.example.missing/reset",
            content: null);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "external_plugin_not_found",
            body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ResetRejectsNonCanonicalPluginId()
    {
        await using var app = await RunningApp.StartAsync();

        using var response = await app.Client.PostAsync(
            "/api/v1/plugins/Com.Example.Plugin/reset",
            content: null);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "external_plugin_id_invalid",
            body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ResetMutationRequiresConfiguredAccessKey()
    {
        await using var app = await RunningApp.StartAsync(accessKey: "plugin-test-key");

        using var response = await app.Client.PostAsync(
            "/api/v1/plugins/com.example.missing/reset",
            content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
