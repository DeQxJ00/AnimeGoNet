using System.Net;
using System.Text;
using System.Text.Json;

namespace AnimeGoNet.App.Tests.Api;

public sealed class LegacyMikanFilterApiTests
{
    private static readonly string[] ChsKeyword = ["CHS"];

    [Fact]
    public async Task GetPutPreviewAndRevisionConflictPreserveLegacySemantics()
    {
        await using var app = await RunningApp.StartAsync();
        using var initialResponse = await app.Client.GetAsync("/api/v1/mikan/legacy-filter");
        Assert.Equal(HttpStatusCode.OK, initialResponse.StatusCode);
        using var initial = JsonDocument.Parse(await initialResponse.Content.ReadAsStreamAsync());
        Assert.Equal(1, initial.RootElement.GetProperty("revision").GetInt64());
        Assert.Equal("migration", initial.RootElement.GetProperty("updated_source").GetString());

        var rules = new object[]
        {
            new
            {
                tier = 0, position = 0, key = "first",
                whitelist_enabled = true, blacklist_enabled = false,
                whitelist = new[] { "CHS" }, blacklist = Array.Empty<string>(),
            },
            new
            {
                tier = 0, position = 1, key = "last-wins",
                whitelist_enabled = false, blacklist_enabled = true,
                whitelist = Array.Empty<string>(), blacklist = new[] { "720P" },
            },
            new
            {
                tier = 1, position = 0, key = "key_3951_12",
                whitelist_enabled = true, blacklist_enabled = false,
                whitelist = new[] { "1080P", "1080P" }, blacklist = Array.Empty<string>(),
            },
            new
            {
                tier = 2, position = 0, key = "3951",
                whitelist_enabled = false, blacklist_enabled = true,
                whitelist = Array.Empty<string>(), blacklist = new[] { "fallback-must-not-run" },
            },
            new
            {
                tier = 4, position = 0, key = "Group",
                whitelist_enabled = false, blacklist_enabled = true,
                whitelist = Array.Empty<string>(), blacklist = new[] { "HEVC" },
            },
        };
        using var put = await app.Client.PutAsync(
            "/api/v1/mikan/legacy-filter",
            Json(new { expected_revision = 1, rules }));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        using var saved = JsonDocument.Parse(await put.Content.ReadAsStreamAsync());
        Assert.Equal(2, saved.RootElement.GetProperty("revision").GetInt64());
        Assert.Contains("\"Filiter0\"", saved.RootElement.GetProperty("legacy_json").GetString());
        Assert.Equal(
            ["CHS"],
            saved.RootElement.GetProperty("rules")[0].GetProperty("whitelist")
                .EnumerateArray().Select(item => item.GetString()));

        using var preview = await app.Client.PostAsync(
            "/api/v1/mikan/legacy-filter/preview",
            Json(new
            {
                title = "[Group] Show CHS 1080P",
                mikanid = 3951,
                groupid = 12,
                rules,
            }));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        using var result = JsonDocument.Parse(await preview.Content.ReadAsStreamAsync());
        Assert.True(result.RootElement.GetProperty("accepted").GetBoolean());
        Assert.Equal("Group", result.RootElement.GetProperty("derived_group_name").GetString());
        var steps = result.RootElement.GetProperty("steps").EnumerateArray().ToArray();
        Assert.Equal(2, steps.Count(step => step.GetProperty("tier").GetString() == "Filiter0"));
        Assert.Contains(steps, step =>
            step.GetProperty("tier").GetString() == "Filiter2"
            && step.GetProperty("reason").GetString() == "HigherTierMatched");

        using var stale = await app.Client.PutAsync(
            "/api/v1/mikan/legacy-filter",
            Json(new { expected_revision = 1, rules }));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task LegacyJsonImportExportAndRollbackKeepEmptyDuplicateAndCaseSensitiveValues()
    {
        await using var app = await RunningApp.StartAsync();
        const string legacyJson = """
            {"Filiter0":{"case":{"is_enable_whitelist":true,"whitelist":["","CHS","CHS"],"is_enable_blacklist":false,"blacklist":[]}},"Filiter1":{},"Filiter2":{},"Filiter3":{},"Filiter4":{}}
            """;
        using var importedResponse = await app.Client.PostAsync(
            "/api/v1/mikan/legacy-filter/import",
            Json(new { expected_revision = 1, legacy_json = legacyJson }));
        Assert.Equal(HttpStatusCode.OK, importedResponse.StatusCode);
        using var imported = JsonDocument.Parse(await importedResponse.Content.ReadAsStreamAsync());
        Assert.Equal(2, imported.RootElement.GetProperty("revision").GetInt64());
        Assert.Equal(
            ["", "CHS", "CHS"],
            imported.RootElement.GetProperty("rules")[0].GetProperty("whitelist")
                .EnumerateArray().Select(item => item.GetString()));

        using var preview = await app.Client.PostAsync(
            "/api/v1/mikan/legacy-filter/preview",
            Json(new
            {
                title = "chs",
                rules = new[]
                {
                    new
                    {
                        tier = 0, position = 0, key = "case",
                        whitelist_enabled = true, blacklist_enabled = false,
                        whitelist = ChsKeyword, blacklist = Array.Empty<string>(),
                    },
                },
            }));
        using var previewDocument = JsonDocument.Parse(await preview.Content.ReadAsStreamAsync());
        Assert.False(previewDocument.RootElement.GetProperty("accepted").GetBoolean());

        using var rollbackResponse = await app.Client.PostAsync(
            "/api/v1/mikan/legacy-filter/rollback",
            Json(new { expected_revision = 2, target_revision = 1 }));
        Assert.Equal(HttpStatusCode.OK, rollbackResponse.StatusCode);
        using var rolled = JsonDocument.Parse(await rollbackResponse.Content.ReadAsStreamAsync());
        Assert.Equal(3, rolled.RootElement.GetProperty("revision").GetInt64());
        Assert.Equal("rollback", rolled.RootElement.GetProperty("updated_source").GetString());
        Assert.Empty(rolled.RootElement.GetProperty("rules").EnumerateArray());
        Assert.Equal(
            [3L, 2L, 1L],
            rolled.RootElement.GetProperty("snapshots").EnumerateArray()
                .Select(item => item.GetProperty("revision").GetInt64()));
    }

    [Fact]
    public async Task InvalidStructuredAndImportRequestsReturnStableErrors()
    {
        await using var app = await RunningApp.StartAsync();
        using var invalidRules = await app.Client.PutAsync(
            "/api/v1/mikan/legacy-filter",
            Json(new
            {
                expected_revision = 1,
                rules = new[]
                {
                    new
                    {
                        tier = 5, position = 0, key = "invalid",
                        whitelist_enabled = false, blacklist_enabled = false,
                        whitelist = Array.Empty<string>(), blacklist = Array.Empty<string>(),
                    },
                },
            }));
        Assert.Equal(HttpStatusCode.BadRequest, invalidRules.StatusCode);
        Assert.Equal(
            "mikan_legacy_filter_invalid",
            (await DocumentAsync(invalidRules)).RootElement.GetProperty("code").GetString());

        using var invalidImport = await app.Client.PostAsync(
            "/api/v1/mikan/legacy-filter/import",
            Json(new { expected_revision = 1, legacy_json = "[]" }));
        Assert.Equal(HttpStatusCode.BadRequest, invalidImport.StatusCode);
        Assert.Equal(
            "mikan_legacy_filter_import_invalid",
            (await DocumentAsync(invalidImport)).RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task StaticWebUiContainsFiveTierEditorImportRollbackAndExplainablePreview()
    {
        await using var app = await RunningApp.StartAsync();
        var html = await app.Client.GetStringAsync("/");
        var script = await app.Client.GetStringAsync("/app.js");

        for (var tier = 0; tier <= 4; tier++)
        {
            Assert.Contains($"id=\"legacy-filter-tier-{tier}\"", html, StringComparison.Ordinal);
        }
        Assert.Contains("id=\"legacy-filter-enabled\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"legacy-filter-json\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"legacy-filter-rollback\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"legacy-filter-preview-run\"", html, StringComparison.Ordinal);
        Assert.Contains("saveLegacyMikanFilter", script, StringComparison.Ordinal);
        Assert.Contains("importLegacyMikanFilter", script, StringComparison.Ordinal);
        Assert.Contains("rollbackLegacyMikanFilter", script, StringComparison.Ordinal);
        Assert.Contains("previewLegacyMikanFilter", script, StringComparison.Ordinal);
    }

    private static StringContent Json(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static async Task<JsonDocument> DocumentAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
}
