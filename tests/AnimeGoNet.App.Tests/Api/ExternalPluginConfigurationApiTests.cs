using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Tests.Api;

public sealed class ExternalPluginConfigurationApiTests
{
    private const string PluginId = "com.example.configuration";
    private const string SecretValue = "external-plugin-secret-value";

    [Fact]
    public async Task ListReturnsEditableDisabledDefaultAndSchema()
    {
        await using var app = await StartAsync();

        using var response = await app.Client.GetAsync("/api/v1/plugins");
        var text = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(text);
        var item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, body.RootElement.GetProperty("revision").GetInt64());
        Assert.Equal(PluginId, item.GetProperty("id").GetString());
        Assert.False(item.GetProperty("configured").GetBoolean());
        Assert.False(item.GetProperty("enabled").GetBoolean());
        Assert.Equal(0, item.GetProperty("entry_revision").GetInt64());
        Assert.Equal("object", item.GetProperty("schema").GetProperty("type").GetString());
        Assert.Equal(
            SecretValue,
            item.GetProperty("schema").GetProperty("properties")
                .GetProperty("token").GetProperty("default").GetString());
        Assert.DoesNotContain(app.RootPath, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveReturnsWriteOnlyValueToConfigurationApiButNotRuntimeStatus()
    {
        await using var app = await StartAsync();

        using var saved = await PutAsync(
            app,
            0,
            enabled: true,
            vars: $"{{\"label\":\"demo\",\"token\":\"{SecretValue}\"}}");
        var savedText = await saved.Content.ReadAsStringAsync();
        using var savedBody = JsonDocument.Parse(savedText);
        using var listed = await app.Client.GetAsync("/api/v1/plugins");
        var listedText = await listed.Content.ReadAsStringAsync();
        using var listedBody = JsonDocument.Parse(listedText);
        using var statusResponse = await app.Client.GetAsync("/api/v1/status");
        var statusText = await statusResponse.Content.ReadAsStringAsync();
        using var statusBody = JsonDocument.Parse(statusText);
        var item = Assert.Single(listedBody.RootElement.GetProperty("items").EnumerateArray());
        var statusPackage = Assert.Single(statusBody.RootElement
            .GetProperty("external_plugins").GetProperty("packages").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Contains(SecretValue, savedText, StringComparison.Ordinal);
        Assert.Contains(SecretValue, listedText, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretValue, statusText, StringComparison.Ordinal);
        Assert.Equal(
            "/token",
            Assert.Single(savedBody.RootElement.GetProperty("item")
                .GetProperty("configured_write_only_paths").EnumerateArray()).GetString());
        Assert.True(item.GetProperty("configured").GetBoolean());
        Assert.True(item.GetProperty("enabled").GetBoolean());
        Assert.True(statusPackage.GetProperty("configured").GetBoolean());
        Assert.True(statusPackage.GetProperty("enabled").GetBoolean());
        Assert.Equal(SecretValue, item.GetProperty("vars").GetProperty("token").GetString());
        Assert.Contains(
            SecretValue,
            await File.ReadAllTextAsync(ConfigurationPath(app)),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task OmittedSecretIsRetainedAndStaleRevisionDoesNotWrite()
    {
        await using var app = await StartAsync();
        using var first = await PutAsync(
            app,
            0,
            enabled: true,
            vars: $"{{\"label\":\"first\",\"token\":\"{SecretValue}\"}}");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var second = await PutAsync(
            app,
            1,
            enabled: true,
            vars: "{\"label\":\"second\"}");
        var beforeConflict = await File.ReadAllBytesAsync(ConfigurationPath(app));

        using var conflict = await PutAsync(
            app,
            1,
            enabled: false,
            vars: "{\"label\":\"stale\"}");
        using var conflictBody = JsonDocument.Parse(
            await conflict.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(
            "external_plugin_configuration_revision_conflict",
            conflictBody.RootElement.GetProperty("code").GetString());
        Assert.Equal(beforeConflict, await File.ReadAllBytesAsync(ConfigurationPath(app)));
        Assert.Contains(
            SecretValue,
            await File.ReadAllTextAsync(ConfigurationPath(app)),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplicitClearRemovesWriteOnlyValue()
    {
        await using var app = await StartAsync();
        using var first = await PutAsync(
            app,
            0,
            enabled: true,
            vars: $"{{\"token\":\"{SecretValue}\"}}");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var cleared = await PutAsync(
            app,
            1,
            enabled: true,
            vars: "{}",
            clearWriteOnlyPaths: "[\"/token\"]");
        using var body = JsonDocument.Parse(await cleared.Content.ReadAsStreamAsync());
        using var file = JsonDocument.Parse(await File.ReadAllTextAsync(ConfigurationPath(app)));
        var vars = file.RootElement.GetProperty("plugins")
            .GetProperty(PluginId).GetProperty("vars");

        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Empty(body.RootElement.GetProperty("item")
            .GetProperty("configured_write_only_paths").EnumerateArray());
        Assert.False(vars.TryGetProperty("token", out _));
    }

    [Fact]
    public async Task ValidationFailureDoesNotCreatePrivateFile()
    {
        await using var app = await StartAsync();

        using var response = await PutAsync(
            app,
            0,
            enabled: true,
            vars: "{\"label\":42,\"extra\":true}");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("plugin_config_invalid", body.RootElement.GetProperty("code").GetString());
        Assert.False(File.Exists(ConfigurationPath(app)));
    }

    [Fact]
    public async Task DeleteRestoresUnconfiguredDisabledDefault()
    {
        await using var app = await StartAsync();
        using var saved = await PutAsync(
            app,
            0,
            enabled: true,
            vars: "{\"label\":\"demo\"}");
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        using var deleted = await app.Client.DeleteAsync(
            $"/api/v1/plugins/{PluginId}/configuration?expected_revision=1");
        using var listed = await app.Client.GetAsync("/api/v1/plugins");
        using var body = JsonDocument.Parse(await listed.Content.ReadAsStreamAsync());
        var item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.False(item.GetProperty("configured").GetBoolean());
        Assert.False(item.GetProperty("enabled").GetBoolean());
        Assert.Equal(2, body.RootElement.GetProperty("revision").GetInt64());
    }

    [Fact]
    public async Task ConfigurationEndpointsUseCommonAccessKeyBoundary()
    {
        await using var app = await StartAsync(accessKey: "plugin-configuration-key");

        using var listed = await app.Client.GetAsync("/api/v1/plugins");
        using var saved = await PutAsync(
            app,
            0,
            enabled: true,
            vars: "{}",
            includeAccessKey: false);

        Assert.Equal(HttpStatusCode.Unauthorized, listed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, saved.StatusCode);
        Assert.False(File.Exists(ConfigurationPath(app)));
    }

    private static Task<RunningApp> StartAsync(string? accessKey = null) =>
        RunningApp.StartAsync(
            accessKey: accessKey,
            prepareData: PreparePlugin);

    private static async Task<HttpResponseMessage> PutAsync(
        RunningApp app,
        long expectedRevision,
        bool enabled,
        string vars,
        string clearWriteOnlyPaths = "[]",
        bool includeAccessKey = true)
    {
        var body = $$"""
            {
              "expected_revision": {{expectedRevision}},
              "enabled": {{enabled.ToString().ToLowerInvariant()}},
              "args": {"fallback":true},
              "vars": {{vars}},
              "clear_write_only_paths": {{clearWriteOnlyPaths}}
            }
            """;
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/plugins/{PluginId}/configuration")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (includeAccessKey)
        {
            request.Headers.TryAddWithoutValidation(
                "X-AnimeGo-WebUI-Access-Key",
                "plugin-configuration-key");
        }
        return await app.Client.SendAsync(request);
    }

    private static string ConfigurationPath(RunningApp app) =>
        Path.Combine(
            app.RootPath,
            "data",
            "config",
            "external-plugins.private.json");

    private static void PreparePlugin(DirectoryLayout layout)
    {
        var package = Path.Combine(layout.PluginsPath, "configuration");
        Directory.CreateDirectory(package);
        var entryName = OperatingSystem.IsWindows() ? "plugin.exe" : "plugin";
        var entry = Path.Combine(package, entryName);
        File.WriteAllBytes(entry, [0x00]);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                entry,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        File.WriteAllText(
            Path.Combine(package, "config.schema.json"),
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"label\":{\"type\":\"string\"},\"token\":{\"type\":\"string\",\"writeOnly\":true,\"default\":\""
            + SecretValue
            + "\"}}}");
        var manifest = new JsonObject
        {
            ["id"] = PluginId,
            ["name"] = "Configuration test",
            ["version"] = "1.0.0",
            ["apiVersion"] = 1,
            ["type"] = "filter",
            ["rid"] = CurrentRid(),
            ["entryPoint"] = entryName,
            ["configSchema"] = "config.schema.json",
            ["capabilities"] = new JsonArray("filter.test"),
        };
        File.WriteAllText(
            Path.Combine(package, "plugin.json"),
            manifest.ToJsonString());
    }

    private static string CurrentRid()
    {
        var os = OperatingSystem.IsWindows()
            ? "win"
            : OperatingSystem.IsLinux()
                ? "linux"
                : "osx";
        var architecture = System.Runtime.InteropServices.RuntimeInformation
            .ProcessArchitecture.ToString().ToLowerInvariant();
        return $"{os}-{architecture}";
    }
}
