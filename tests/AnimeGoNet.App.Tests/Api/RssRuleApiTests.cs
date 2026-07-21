using System.Net;
using System.Text;
using System.Text.Json;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Api;

public sealed class RssRuleApiTests
{
    [Fact]
    public async Task GetPutAndPreviewUseRevisionLowercaseAndOrderedShortCircuit()
    {
        await using var app = await RunningApp.StartAsync();
        using var initialResponse = await app.Client.GetAsync("/api/v1/rss-rules/mikan");
        Assert.Equal(HttpStatusCode.OK, initialResponse.StatusCode);
        using var initial = JsonDocument.Parse(await initialResponse.Content.ReadAsStreamAsync());
        Assert.Equal(1, initial.RootElement.GetProperty("revision").GetInt64());
        Assert.True(initial.RootElement.GetProperty("rss_priority_enabled").GetBoolean());

        var update = new
        {
            expected_revision = 1,
            whitelist = Array.Empty<object>(),
            blacklist = new[]
            {
                new { id = " BLOCKED ", name = "720p", enabled = true, values = new[] { " 720P " } },
            },
            priority_groups = new[]
            {
                new
                {
                    id = " LANGUAGE ", name = "字幕", arrays = new[]
                    {
                        new { id = " SIMPLE ", name = "简体", enabled = true, values = new[] { " CHS ", "简体" } },
                        new { id = " TRAD ", name = "繁体", enabled = true, values = new[] { " CHT ", "繁体" } },
                    },
                },
            },
        };
        using var put = await app.Client.PutAsync("/api/v1/rss-rules/mikan", Json(update));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        using var saved = JsonDocument.Parse(await put.Content.ReadAsStreamAsync());
        Assert.Equal(2, saved.RootElement.GetProperty("revision").GetInt64());
        var blacklist = Assert.Single(saved.RootElement.GetProperty("blacklist").EnumerateArray());
        Assert.Equal("blocked", blacklist.GetProperty("id").GetString());
        Assert.Equal("720p", Assert.Single(blacklist.GetProperty("values").EnumerateArray()).GetString());

        using var preview = await app.Client.PostAsync("/api/v1/rss-rules/mikan/preview", Json(new
        {
            candidates = new[]
            {
                new { id = "bad", title = "Show 720P", mikanid = 3951, source_episode_kind = "normal", source_episode = "1" },
                new { id = "trad", title = "Show CHT", mikanid = 3951, source_episode_kind = "normal", source_episode = "1" },
                new { id = "simple", title = "Show CHS", mikanid = 3951, source_episode_kind = "normal", source_episode = "1" },
            },
        }));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        using var result = JsonDocument.Parse(await preview.Content.ReadAsStreamAsync());
        var decisions = result.RootElement.GetProperty("decisions").EnumerateArray().ToArray();
        Assert.Equal("rejected_by_blacklist", decisions[0].GetProperty("decision").GetString());
        Assert.Equal("suppressed_by_higher_priority", decisions[1].GetProperty("decision").GetString());
        Assert.Equal("simple", decisions[1].GetProperty("winner_id").GetString());
        Assert.Equal("winner", decisions[2].GetProperty("decision").GetString());
        Assert.Equal(
            "language",
            Assert.Single(decisions[2].GetProperty("evaluated_priority_groups").EnumerateArray()).GetString());

        using var stale = await app.Client.PutAsync("/api/v1/rss-rules/mikan", Json(update));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task DisabledPrioritySwitchBypassesRulesWithoutErasingThem()
    {
        await using var app = await RunningApp.StartAsync();
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using (var connection = await database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE source_profiles SET rss_priority_enabled = 0 WHERE id = 'mikan';";
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        using var preview = await app.Client.PostAsync("/api/v1/rss-rules/mikan/preview", Json(new
        {
            candidates = new[]
            {
                new { id = "720", title = "Show 720P", mikanid = 3951, source_episode_kind = "normal", source_episode = "1" },
                new { id = "1080", title = "Show 1080P", mikanid = 3951, source_episode_kind = "normal", source_episode = "1" },
            },
        }));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        using var result = JsonDocument.Parse(await preview.Content.ReadAsStreamAsync());
        Assert.False(result.RootElement.GetProperty("rss_priority_enabled").GetBoolean());
        Assert.All(result.RootElement.GetProperty("decisions").EnumerateArray(), decision =>
        {
            Assert.Equal("winner", decision.GetProperty("decision").GetString());
            Assert.Equal("SkippedByConfiguration", decision.GetProperty("reason").GetString());
        });

        using var rules = await app.Client.GetAsync("/api/v1/rss-rules/mikan");
        using var stored = JsonDocument.Parse(await rules.Content.ReadAsStreamAsync());
        Assert.False(stored.RootElement.GetProperty("rss_priority_enabled").GetBoolean());
        Assert.NotEmpty(stored.RootElement.GetProperty("blacklist").EnumerateArray());
    }

    [Fact]
    public async Task UnknownProfileAndDuplicateCandidateIdsAreRejected()
    {
        await using var app = await RunningApp.StartAsync();
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await app.Client.GetAsync("/api/v1/rss-rules/missing")).StatusCode);
        using var response = await app.Client.PostAsync("/api/v1/rss-rules/mikan/preview", Json(new
        {
            candidates = new[]
            {
                new { id = "same", title = "one", mikanid = 1, source_episode_kind = "normal", source_episode = "1" },
                new { id = "same", title = "two", mikanid = 1, source_episode_kind = "normal", source_episode = "1" },
            },
        }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static StringContent Json(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
}
