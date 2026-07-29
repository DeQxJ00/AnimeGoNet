using System.Net;
using System.Text;
using System.Text.Json;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Api;

public sealed class MetadataAttemptApiTests
{
    [Fact]
    public async Task ListsCompleteSafeTimelineWithoutTorrentSecret()
    {
        await using var app = await RunningApp.StartAsync();
        const string payload = """
            {
              "source": "mikan",
              "data": [{
                "torrent": "https://mikanani.me/private-passkey/attempt-timeline.torrent",
                "info": { "title": "Attempt timeline", "mikanid": 3951, "bgmid": 547888 }
              }]
            }
            """;
        using var ingest = await app.Client.PostAsync(
            "/api/v1/ingest",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        using var ingestJson = JsonDocument.Parse(await ingest.Content.ReadAsStreamAsync());
        var taskId = ingestJson.RootElement
            .GetProperty("items")[0]
            .GetProperty("ingest_id")
            .GetString()!;
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using (var connection = await database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE ingest_tasks
                SET status = 'downloaded', updated_at_utc = $now
                WHERE id = $task_id;
                """;
            command.Parameters.AddWithValue("$task_id", taskId);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        var store = app.App.Services.GetRequiredService<MetadataResolutionStore>();
        var now = DateTimeOffset.UtcNow;
        var claim = Assert.IsType<MetadataTaskClaim>(
            await store.TryClaimNextDownloadedAsync(now, TimeSpan.FromMinutes(1)));
        await store.RecordAttemptAsync(
            claim,
            new MetadataAttempt(
                "series",
                "tmdb_title",
                4,
                "failed",
                "tmdb_series_not_found",
                false,
                claim.AttemptNumber,
                12),
            now.AddSeconds(1));
        await store.RecordAttemptAsync(
            claim,
            new MetadataAttempt(
                "season",
                "tmdb_fail_first_season",
                1,
                "matched",
                null,
                false,
                claim.AttemptNumber,
                3,
                "local S01 selected"),
            now.AddSeconds(2));

        using var response = await app.Client.GetAsync(
            $"/api/v1/metadata/tasks/{taskId}/attempts");
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(taskId, json.RootElement.GetProperty("task_id").GetString());
        Assert.Equal(2, items.Length);
        Assert.Equal("season", items[0].GetProperty("stage").GetString());
        Assert.Equal("local S01 selected", items[0].GetProperty("reason").GetString());
        Assert.Equal("series", items[1].GetProperty("stage").GetString());
        Assert.Equal("tmdb_series_not_found", items[1].GetProperty("reason").GetString());
        Assert.False(items[1].GetProperty("retryable").GetBoolean());
        Assert.Equal(12, items[1].GetProperty("duration_ms").GetInt64());
        Assert.DoesNotContain("private-passkey", body, StringComparison.Ordinal);
        Assert.DoesNotContain("attempt-timeline.torrent", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingTaskAndInvalidLimitUseStableErrors()
    {
        await using var app = await RunningApp.StartAsync();

        using var missing = await app.Client.GetAsync(
            "/api/v1/metadata/tasks/missing/attempts");
        using var invalid = await app.Client.GetAsync(
            "/api/v1/metadata/tasks/missing/attempts?limit=501");
        using var missingJson = JsonDocument.Parse(await missing.Content.ReadAsStreamAsync());
        using var invalidJson = JsonDocument.Parse(await invalid.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(
            "metadata_task_not_found",
            missingJson.RootElement.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(
            "metadata_attempt_limit_invalid",
            invalidJson.RootElement.GetProperty("code").GetString());
    }
}
