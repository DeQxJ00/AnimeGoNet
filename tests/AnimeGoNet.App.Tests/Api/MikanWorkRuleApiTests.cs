using System.Net;
using System.Text;
using System.Text.Json;

namespace AnimeGoNet.App.Tests.Api;

public sealed class MikanWorkRuleApiTests
{
    [Fact]
    public async Task CreatesReadsUpdatesAndDeletesRuleWithRevisionProtection()
    {
        await using var app = await RunningApp.StartAsync();
        const string create = """
            {
              "bgmid": 547888,
              "tmdb_series_id": 72517,
              "tmdb_season_number": 2,
              "episode_offset": -12,
              "enabled": true,
              "expected_revision": 0
            }
            """;

        using var created = await app.Client.PutAsync(
            "/api/v1/mikan/work-rules/3951",
            Json(create));
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal(3951, createdJson.RootElement.GetProperty("mikanid").GetInt32());
        Assert.Equal(-12, createdJson.RootElement.GetProperty("episode_offset").GetInt32());
        Assert.Equal(1, createdJson.RootElement.GetProperty("revision").GetInt64());

        using var fetched = await app.Client.GetAsync("/api/v1/mikan/work-rules/3951");
        using var fetchedJson = JsonDocument.Parse(await fetched.Content.ReadAsStreamAsync());
        Assert.Equal(72517, fetchedJson.RootElement.GetProperty("tmdb_series_id").GetInt32());

        const string update = """
            {
              "bgmid": 547888,
              "tmdb_series_id": 72517,
              "tmdb_season_number": 3,
              "episode_offset": -24,
              "enabled": false,
              "expected_revision": 1
            }
            """;
        using var updated = await app.Client.PutAsync(
            "/api/v1/mikan/work-rules/3951",
            Json(update));
        using var updatedJson = JsonDocument.Parse(await updated.Content.ReadAsStreamAsync());
        Assert.Equal(2, updatedJson.RootElement.GetProperty("revision").GetInt64());
        Assert.False(updatedJson.RootElement.GetProperty("enabled").GetBoolean());

        using var stale = await app.Client.PutAsync(
            "/api/v1/mikan/work-rules/3951",
            Json(update));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using var deleted = await app.Client.DeleteAsync(
            "/api/v1/mikan/work-rules/3951?expected_revision=2");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var missing = await app.Client.GetAsync("/api/v1/mikan/work-rules/3951");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task RejectsOffsetWithoutCompleteTmdbIdentity()
    {
        await using var app = await RunningApp.StartAsync();
        const string invalid = """
            {
              "bgmid": 547888,
              "episode_offset": 1,
              "enabled": true,
              "expected_revision": 0
            }
            """;

        using var response = await app.Client.PutAsync(
            "/api/v1/mikan/work-rules/3951",
            Json(invalid));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("mikan_rule_invalid", json.RootElement.GetProperty("code").GetString());
    }

    private static StringContent Json(string value) => new(value, Encoding.UTF8, "application/json");
}
