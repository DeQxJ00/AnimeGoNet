using System.Net;
using System.Text;
using System.Text.Json;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

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

    [Fact]
    public async Task SampleEpisodeIsVerifiedBeforeRuleIsSaved()
    {
        var tmdb = new SampleTmdbClient(episodeExists: true);
        await using var app = await RunningApp.StartAsync(tmdbClient: tmdb);
        const string request = """
            {
              "bgmid": 547888,
              "tmdb_series_id": 72517,
              "tmdb_season_number": 2,
              "episode_offset": 13,
              "sample_source_episode": 4,
              "enabled": true,
              "expected_revision": 0
            }
            """;

        using var response = await app.Client.PutAsync(
            "/api/v1/mikan/work-rules/3951",
            Json(request));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([(72517, 2, 17)], tmdb.EpisodeRequests);
    }

    [Fact]
    public async Task FailedSampleEpisodeValidationDoesNotPersistRule()
    {
        await using var app = await RunningApp.StartAsync(
            tmdbClient: new SampleTmdbClient(episodeExists: false));
        const string request = """
            {
              "tmdb_series_id": 72517,
              "tmdb_season_number": 2,
              "episode_offset": 13,
              "sample_source_episode": 4,
              "enabled": true,
              "expected_revision": 0
            }
            """;

        using var response = await app.Client.PutAsync(
            "/api/v1/mikan/work-rules/3951",
            Json(request));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        using var missing = await app.Client.GetAsync("/api/v1/mikan/work-rules/3951");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "mikan_rule_tmdb_episode_not_found",
            json.RootElement.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task ImpactAndExplicitRematchProtectCompletedTasksAndRequireRuleRevision()
    {
        await using var app = await RunningApp.StartAsync();
        const string createRule = """
            {
              "bgmid": 547888,
              "enabled": true,
              "expected_revision": 0
            }
            """;
        using var createdRule = await app.Client.PutAsync(
            "/api/v1/mikan/work-rules/3951",
            Json(createRule));
        Assert.Equal(HttpStatusCode.OK, createdRule.StatusCode);

        var failedTaskId = await AddTaskAsync(app, "impact-failed");
        var completedTaskId = await AddTaskAsync(app, "impact-completed");
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await SetStatusAsync(database, failedTaskId, "metadata_failed");
        await SetStatusAsync(database, completedTaskId, "organized");

        using var impact = await app.Client.GetAsync(
            "/api/v1/mikan/work-rules/3951/impact");
        using var impactJson = JsonDocument.Parse(await impact.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, impact.StatusCode);
        Assert.Equal(2, impactJson.RootElement.GetProperty("total_task_count").GetInt32());
        Assert.Equal(1, impactJson.RootElement.GetProperty("retryable_failed_task_count").GetInt32());
        Assert.Equal(1, impactJson.RootElement.GetProperty("completed_protected_task_count").GetInt32());

        using var rematched = await app.Client.PostAsync(
            "/api/v1/mikan/work-rules/3951/rematch",
            Json("""{ "expected_rule_revision": 1 }"""));
        using var rematchedJson = JsonDocument.Parse(await rematched.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, rematched.StatusCode);
        Assert.Equal(1, rematchedJson.RootElement.GetProperty("retried_task_count").GetInt32());
        Assert.Equal("downloaded", await ReadStatusAsync(database, failedTaskId));
        Assert.Equal("organized", await ReadStatusAsync(database, completedTaskId));

        using var stale = await app.Client.PostAsync(
            "/api/v1/mikan/work-rules/3951/rematch",
            Json("""{ "expected_rule_revision": 0 }"""));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    private static async Task<string> AddTaskAsync(RunningApp app, string sourceItemId)
    {
        var request = $$"""
            {
              "source": "mikan",
              "data": [{
                "torrent": "https://mikanani.me/{{sourceItemId}}.torrent",
                "info": {
                  "title": "{{sourceItemId}}",
                  "source_item_id": "{{sourceItemId}}",
                  "mikanid": 3951,
                  "bgmid": 547888
                }
              }]
            }
            """;
        using var response = await app.Client.PostAsync("/api/v1/ingest", Json(request));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<string>(
            json.RootElement.GetProperty("items")[0].GetProperty("ingest_id").GetString());
    }

    private static async Task SetStatusAsync(
        AnimeGoSqliteDatabase database,
        string taskId,
        string status)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ingest_tasks
            SET status = $status,
                failure_kind = CASE WHEN $status = 'metadata_failed' THEN 'SemanticNoMatch' ELSE NULL END,
                failure_reason = CASE WHEN $status = 'metadata_failed' THEN 'fixture_failure' ELSE NULL END
            WHERE id = $task_id;
            """;
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$task_id", taskId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<string> ReadStatusAsync(
        AnimeGoSqliteDatabase database,
        string taskId)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT status FROM ingest_tasks WHERE id = $task_id;";
        command.Parameters.AddWithValue("$task_id", taskId);
        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }

    private static StringContent Json(string value) => new(value, Encoding.UTF8, "application/json");

    private sealed class SampleTmdbClient(bool episodeExists) : ITmdbClient
    {
        public List<(int SeriesId, int SeasonNumber, int EpisodeNumber)> EpisodeRequests { get; } = [];

        public Task<IReadOnlyList<TmdbSeries>> SearchSeriesAsync(
            string title,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TmdbSeries>>([]);

        public Task<TmdbSeries?> GetSeriesAsync(
            int seriesId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeries?>(new TmdbSeries(72517, "来自深渊", "メイドインアビス", null));

        public Task<TmdbSeriesDetails?> GetSeriesDetailsAsync(
            int seriesId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeriesDetails?>(null);

        public Task<TmdbSeason?> GetSeasonAsync(
            int seriesId,
            int seasonNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeason?>(new TmdbSeason(200, 72517, 2, "Season 2", null, 12));

        public Task<TmdbEpisode?> GetEpisodeAsync(
            int seriesId,
            int seasonNumber,
            int episodeNumber,
            CancellationToken cancellationToken = default)
        {
            EpisodeRequests.Add((seriesId, seasonNumber, episodeNumber));
            return Task.FromResult<TmdbEpisode?>(episodeExists
                ? new TmdbEpisode(9017, 72517, 2, 17, "Episode 17", null)
                : null);
        }
    }
}
