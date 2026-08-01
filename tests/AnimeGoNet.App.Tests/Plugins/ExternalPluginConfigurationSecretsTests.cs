using System.Text.Json;
using AnimeGoNet.App.Plugins;

namespace AnimeGoNet.App.Tests.Plugins;

public sealed class ExternalPluginConfigurationSecretsTests
{
    [Fact]
    public void ProjectionOnlyReturnsDeclaredNonSecretValues()
    {
        var schema = Json("""
            {
              "type":"object",
              "properties":{
                "label":{"type":"string"},
                "token":{"type":"string","writeOnly":true},
                "nested":{"type":"object","properties":{
                  "visible":{"type":"boolean"},
                  "password":{"type":"string","writeOnly":true}
                }}
              }
            }
            """);

        var projected = ExternalPluginConfigurationSecrets.ProjectSafe(
            schema,
            Json("{\"label\":\"demo\",\"token\":\"secret\",\"nested\":{\"visible\":true,\"password\":\"hidden\",\"undeclared\":\"drop\"},\"unknown\":\"drop\"}"));

        Assert.Equal("demo", projected.Value.GetProperty("label").GetString());
        Assert.True(projected.Value.GetProperty("nested").GetProperty("visible").GetBoolean());
        Assert.False(projected.Value.TryGetProperty("token", out _));
        Assert.False(projected.Value.TryGetProperty("unknown", out _));
        Assert.False(projected.Value.GetProperty("nested").TryGetProperty("password", out _));
        Assert.Equal(["/nested/password", "/token"], projected.ConfiguredWriteOnlyPaths);
    }

    [Fact]
    public void OmittedWriteOnlyValuesAreRetainedWhilePublicValuesReplace()
    {
        var schema = Schema();

        var merged = ExternalPluginConfigurationSecrets.MergeWriteOnly(
            schema,
            Json("{\"label\":\"old\",\"token\":\"secret\"}"),
            Json("{\"label\":\"new\"}"),
            []);

        Assert.Equal("new", merged.GetProperty("label").GetString());
        Assert.Equal("secret", merged.GetProperty("token").GetString());
    }

    [Fact]
    public void WriteOnlyValueCanBeReplacedOrExplicitlyCleared()
    {
        var schema = Schema();

        var replaced = ExternalPluginConfigurationSecrets.MergeWriteOnly(
            schema,
            Json("{\"token\":\"old\"}"),
            Json("{\"token\":\"new\"}"),
            []);
        var cleared = ExternalPluginConfigurationSecrets.MergeWriteOnly(
            schema,
            Json("{\"token\":\"old\"}"),
            Json("{}"),
            ["/token"]);

        Assert.Equal("new", replaced.GetProperty("token").GetString());
        Assert.False(cleared.TryGetProperty("token", out _));
    }

    [Fact]
    public void InvalidOrConflictingClearPathsFailClosed()
    {
        var schema = Schema();

        var invalid = Assert.Throws<ExternalPluginConfigurationValidationException>(() =>
            ExternalPluginConfigurationSecrets.MergeWriteOnly(
                schema,
                Json("{}"),
                Json("{}"),
                ["/missing"]));
        var conflict = Assert.Throws<ExternalPluginConfigurationValidationException>(() =>
            ExternalPluginConfigurationSecrets.MergeWriteOnly(
                schema,
                Json("{}"),
                Json("{\"token\":\"new\"}"),
                ["/token"]));

        Assert.Equal("plugin_config_clear_path_invalid", invalid.Code);
        Assert.Equal("plugin_config_clear_path_conflict", conflict.Code);
    }

    [Fact]
    public void ArrayWithWriteOnlyDescendantIsRedactedAsOneValue()
    {
        var schema = Json("""
            {"type":"object","properties":{"accounts":{"type":"array","items":{
              "type":"object","properties":{"name":{"type":"string"},"token":{"type":"string","writeOnly":true}}
            }}}}
            """);

        var projected = ExternalPluginConfigurationSecrets.ProjectSafe(
            schema,
            Json("{\"accounts\":[{\"name\":\"one\",\"token\":\"secret\"}]}"));

        Assert.False(projected.Value.TryGetProperty("accounts", out _));
        Assert.Equal("/accounts", Assert.Single(projected.ConfiguredWriteOnlyPaths));
    }

    [Fact]
    public void SafeSchemaRemovesDefaultsFromWriteOnlyNodesOnly()
    {
        var projected = ExternalPluginConfigurationSecrets.ProjectSafeSchema(Json("""
            {"type":"object","properties":{
              "label":{"type":"string","default":"public"},
              "token":{"type":"string","writeOnly":true,"default":"secret","example":"hidden","const":"fixed"}
            }}
            """));
        var properties = projected.GetProperty("properties");

        Assert.Equal("public", properties.GetProperty("label").GetProperty("default").GetString());
        Assert.False(properties.GetProperty("token").TryGetProperty("default", out _));
        Assert.False(properties.GetProperty("token").TryGetProperty("example", out _));
        Assert.False(properties.GetProperty("token").TryGetProperty("const", out _));
        Assert.True(properties.GetProperty("token").GetProperty("writeOnly").GetBoolean());
    }

    private static JsonElement Schema() => Json("""
        {
          "type":"object",
          "additionalProperties":false,
          "properties":{
            "label":{"type":"string"},
            "token":{"type":"string","writeOnly":true}
          }
        }
        """);

    private static JsonElement Json(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();
}
