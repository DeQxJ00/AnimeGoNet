using System.Net;
using System.Text;
using System.Text.Json;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Api;

public sealed class MetadataTaskDetailApiTests
{
    [Fact]
    public async Task ShowsSourceToVerifiedTmdbFileMappingAndAiTrustBasis()
    {
        await using var app = await RunningApp.StartAsync();
        const string payload = """
            {
              "source": "mikan",
              "data": [{
                "torrent": "https://mikanani.me/private-passkey/task-detail.torrent",
                "info": { "title": "来源作品 第03话", "mikanid": 3951, "bgmid": 547888 }
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
        var now = DateTimeOffset.UtcNow.ToString("O");
        await using (var connection = await database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO anime_series (
                    id, tmdb_series_id, bangumi_subject_id, canonical_name,
                    original_name, poster_path, needs_tmdb_completion,
                    created_at_utc, updated_at_utc)
                VALUES (
                    'tmdb-900', 900, 547888, 'TMDB 规范动画名',
                    'TMDB Original', NULL, 0, $now, $now);

                INSERT INTO anime_seasons (
                    id, series_id, season_number, canonical_name, poster_path,
                    created_at_utc, updated_at_utc)
                VALUES (
                    'tmdb-900-s2', 'tmdb-900', 2, 'Season 2', NULL, $now, $now);

                INSERT INTO tmdb_episodes (
                    tmdb_episode_id, series_id, season_number, episode_number,
                    name, air_date, runtime_minutes, fetched_at_utc)
                VALUES (
                    900203, 'tmdb-900', 2, 3, 'TMDB 第三集',
                    '2026-07-01', 24, $now);

                UPDATE ingest_tasks
                SET status = 'metadata_resolved', updated_at_utc = $now
                WHERE id = $task_id;

                UPDATE task_files
                SET relative_path = 'Source Show - 03.mkv',
                    size_bytes = 734003200,
                    source_episode = '3',
                    file_episode_candidate = '3',
                    tmdb_series_id = 900,
                    tmdb_season_number = 2,
                    tmdb_episode_number = 3,
                    tmdb_episode_id = 900203,
                    disposition = 'episode',
                    other_reason = NULL
                WHERE task_id = $task_id;

                INSERT INTO metadata_resolution_runs (
                    id, task_id, status, tmdb_access_confirmed, failure_kind,
                    fallback_eligible, fallback_denial_reason,
                    started_at_utc, completed_at_utc, attempt_number,
                    tmdb_series_id, tmdb_season_number)
                VALUES (
                    'run-detail', $task_id, 'completed', 1, NULL,
                    0, NULL, $now, $now, 1, 900, 2);

                INSERT INTO metadata_resolution_attempts (
                    id, run_id, stage, strategy, priority, result, error_code,
                    reason, retryable, attempt_number, duration_ms, created_at_utc)
                VALUES (
                    'attempt-detail', 'run-detail', 'season', 'ai_metadata', NULL,
                    'matched', NULL, 'validated by TMDB', 0, 1, 321, $now);
                """;
            command.Parameters.AddWithValue("$task_id", taskId);
            command.Parameters.AddWithValue("$now", now);
            await command.ExecuteNonQueryAsync();
        }

        using var response = await app.Client.GetAsync(
            $"/api/v1/metadata/tasks/{taskId}");
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var file = Assert.Single(root.GetProperty("files").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("来源作品 第03话", root.GetProperty("summary").GetProperty("title").GetString());
        Assert.Equal("matched", root.GetProperty("ai").GetProperty("status").GetString());
        Assert.Equal(
            "tmdb_verified",
            root.GetProperty("ai").GetProperty("confidence_basis").GetString());
        Assert.Equal("Source Show - 03.mkv", file.GetProperty("source_name").GetString());
        Assert.Equal("3", file.GetProperty("source_episode").GetString());
        Assert.Equal("TMDB 规范动画名", file.GetProperty("tmdb_series_name").GetString());
        Assert.Equal(2, file.GetProperty("tmdb_season_number").GetInt32());
        Assert.Equal(3, file.GetProperty("tmdb_episode_number").GetInt32());
        Assert.Equal("TMDB 第三集", file.GetProperty("tmdb_episode_name").GetString());
        Assert.DoesNotContain("private-passkey", body, StringComparison.Ordinal);
        Assert.DoesNotContain("task-detail.torrent", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnattemptedTaskHasNoInventedAiConfidenceAndMissingTaskIsNotFound()
    {
        await using var app = await RunningApp.StartAsync();
        const string payload = """
            {
              "source": "mikan",
              "data": [{
                "torrent": "https://mikanani.me/private-passkey/unattempted.torrent",
                "info": { "title": "尚未匹配", "mikanid": 3951, "bgmid": 547888 }
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

        using var response = await app.Client.GetAsync($"/api/v1/metadata/tasks/{taskId}");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        using var missing = await app.Client.GetAsync("/api/v1/metadata/tasks/missing");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("not_attempted", json.RootElement.GetProperty("ai").GetProperty("status").GetString());
        Assert.Equal(
            "not_established",
            json.RootElement.GetProperty("ai").GetProperty("confidence_basis").GetString());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
