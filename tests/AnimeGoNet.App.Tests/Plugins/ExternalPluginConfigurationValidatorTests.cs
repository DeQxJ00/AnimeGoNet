using System.Text.Json;
using AnimeGoNet.App.Plugins;

namespace AnimeGoNet.App.Tests.Plugins;

public sealed class ExternalPluginConfigurationValidatorTests
{
    [Fact]
    public async Task SupportedSchemaAcceptsValidVars()
    {
        using var fixture = new ValidatorFixture("""
            {
              "type": "object",
              "required": ["quality", "attempts"],
              "additionalProperties": false,
              "properties": {
                "quality": { "type": "string", "enum": ["720p", "1080p"] },
                "attempts": { "type": "integer", "minimum": 1, "maximum": 5 },
                "labels": {
                  "type": "array",
                  "minItems": 1,
                  "items": { "type": "string", "minLength": 1 }
                }
              }
            }
            """);

        await fixture.Validator.ValidateVarsAsync(
            fixture.Package,
            Json("{\"quality\":\"1080p\",\"attempts\":3,\"labels\":[\"anime\"]}"));
    }

    [Fact]
    public async Task MissingRequiredValueReturnsStablePath()
    {
        using var fixture = new ValidatorFixture("""
            {"type":"object","required":["token"],"properties":{"token":{"type":"string"}}}
            """);

        var error = await Assert.ThrowsAsync<ExternalPluginConfigurationValidationException>(
            () => fixture.Validator.ValidateVarsAsync(fixture.Package, Json("{}")));

        Assert.Equal("plugin_config_invalid", error.Code);
        Assert.Equal("vars/token", error.Path);
    }

    [Fact]
    public async Task AdditionalAndWrongTypedValuesAreRejected()
    {
        using var fixture = new ValidatorFixture("""
            {
              "type":"object",
              "additionalProperties":false,
              "properties":{"enabled":{"type":"boolean"}}
            }
            """);

        var additional = await Assert.ThrowsAsync<ExternalPluginConfigurationValidationException>(
            () => fixture.Validator.ValidateVarsAsync(
                fixture.Package,
                Json("{\"extra\":true}")));
        var typed = await Assert.ThrowsAsync<ExternalPluginConfigurationValidationException>(
            () => fixture.Validator.ValidateVarsAsync(
                fixture.Package,
                Json("{\"enabled\":\"yes\"}")));

        Assert.Equal("vars/extra", additional.Path);
        Assert.Equal("vars/enabled", typed.Path);
    }

    [Fact]
    public async Task StringPatternIsValidatedWithoutBacktrackingEngine()
    {
        using var fixture = new ValidatorFixture("""
            {"type":"object","properties":{"tag":{"type":"string","pattern":"^[a-z0-9-]+$"}}}
            """);

        await fixture.Validator.ValidateVarsAsync(
            fixture.Package,
            Json("{\"tag\":\"anime-1080p\"}"));
        var error = await Assert.ThrowsAsync<ExternalPluginConfigurationValidationException>(
            () => fixture.Validator.ValidateVarsAsync(
                fixture.Package,
                Json("{\"tag\":\"Anime 1080p\"}")));

        Assert.Equal("vars/tag", error.Path);
    }

    [Fact]
    public async Task MalformedSchemaFailsAsPackageConfigurationError()
    {
        using var fixture = new ValidatorFixture("{\"type\":[\"object\"]}");

        var error = await Assert.ThrowsAsync<ExternalPluginConfigurationValidationException>(
            () => fixture.Validator.ValidateVarsAsync(fixture.Package, Json("{}")));

        Assert.Equal("plugin_config_schema_invalid", error.Code);
        Assert.Equal("schema", error.Path);
    }

    private static JsonElement Json(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    private sealed class ValidatorFixture : IDisposable
    {
        public ValidatorFixture(string schema)
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                $"animegonet-plugin-schema-{Guid.NewGuid():N}");
            Directory.CreateDirectory(RootPath);
            var schemaPath = Path.Combine(RootPath, "config.schema.json");
            File.WriteAllText(schemaPath, schema);
            Package = new ExternalPluginPackage(
                new ExternalPluginManifest(
                    "com.example.filter",
                    "Example",
                    "1.0.0",
                    1,
                    "filter",
                    "win-x64",
                    "plugin.exe",
                    "config.schema.json",
                    []),
                RootPath,
                Path.Combine(RootPath, "plugin.json"),
                Path.Combine(RootPath, "plugin.exe"),
                schemaPath);
        }

        public string RootPath { get; }

        public ExternalPluginPackage Package { get; }

        public ExternalPluginConfigurationValidator Validator { get; } = new();

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
