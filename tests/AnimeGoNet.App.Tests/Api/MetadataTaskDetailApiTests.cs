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
                VALUES
                    (
                        'attempt-detail-series', 'run-detail', 'series',
                        'ai_metadata', NULL, 'matched', NULL,
                        'validated by TMDB', 0, 1, 300, $now),
                    (
                        'attempt-detail-season', 'run-detail', 'season',
                        'ai_metadata', NULL, 'matched', NULL,
                        'validated by TMDB', 0, 1, 321, $now),
                    (
                        'attempt-detail-episode', 'run-detail', 'episode',
                        'tmdb_episode_number', NULL, 'matched', NULL,
                        'validated by TMDB', 0, 1, 25, $now),
                    (
                        'attempt-detail-subtitle', 'run-detail', 'episode',
                        'subtitle_association', NULL, 'matched', NULL,
                        'associated with verified episode', 0, 1, 5, $now);

                UPDATE metadata_resolution_runs
                SET series_resolution_source = 'ai_metadata',
                    series_resolution_attempt_id = 'attempt-detail-series',
                    season_resolution_source = 'ai_metadata',
                    season_resolution_attempt_id = 'attempt-detail-season'
                WHERE id = 'run-detail';

                UPDATE task_files
                SET episode_resolution_source = 'tmdb_episode_number',
                    episode_resolution_run_id = 'run-detail',
                    episode_resolution_attempt_id = 'attempt-detail-episode'
                WHERE task_id = $task_id;

                INSERT INTO task_files (
                    id, task_id, relative_path, size_bytes,
                    source_episode, file_episode_candidate,
                    tmdb_series_id, tmdb_season_number,
                    tmdb_episode_number, tmdb_episode_id,
                    disposition, other_reason, associated_task_file_id,
                    rename_suffix)
                VALUES (
                    'file-detail-subtitle', $task_id,
                    'Source Show - 03.zh-Hans.ass', 4096,
                    '3', '3', 900, 2, 3, 900203,
                    'episode', NULL,
                    (SELECT id FROM task_files
                     WHERE task_id = $task_id
                       AND relative_path = 'Source Show - 03.mkv'),
                    '.zh-hans.ass');

                UPDATE task_files
                SET episode_resolution_source = 'subtitle_association',
                    episode_resolution_run_id = 'run-detail',
                    episode_resolution_attempt_id = 'attempt-detail-subtitle'
                WHERE id = 'file-detail-subtitle';
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
        var files = root.GetProperty("files").EnumerateArray().ToArray();
        Assert.Equal(2, files.Length);
        var file = Assert.Single(
            files,
            value => value.GetProperty("source_name").GetString() == "Source Show - 03.mkv");
        var subtitle = Assert.Single(
            files,
            value => value.GetProperty("source_name").GetString()
                == "Source Show - 03.zh-Hans.ass");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("来源作品 第03话", root.GetProperty("summary").GetProperty("title").GetString());
        Assert.Equal(
            "ai_metadata",
            root.GetProperty("summary").GetProperty("series_strategy").GetString());
        Assert.Equal(
            "attempt-detail-series",
            root.GetProperty("summary").GetProperty("series_attempt_id").GetString());
        Assert.Equal(
            "run-detail",
            root.GetProperty("summary").GetProperty("series_run_id").GetString());
        Assert.Equal(
            "ai_metadata",
            root.GetProperty("summary").GetProperty("season_strategy").GetString());
        Assert.Equal(
            "attempt-detail-season",
            root.GetProperty("summary").GetProperty("season_attempt_id").GetString());
        Assert.Equal(
            "run-detail",
            root.GetProperty("summary").GetProperty("season_run_id").GetString());
        Assert.Equal(
            "mixed",
            root.GetProperty("summary").GetProperty("episode_strategy").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("summary").GetProperty("episode_attempt_id").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("summary").GetProperty("episode_run_id").ValueKind);
        Assert.True(
            root.GetProperty("summary").GetProperty("episode_resolution_mixed").GetBoolean());
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
        Assert.Equal(
            "tmdb_episode_number",
            file.GetProperty("episode_strategy").GetString());
        Assert.Equal("run-detail", file.GetProperty("episode_run_id").GetString());
        Assert.Equal(
            "attempt-detail-episode",
            file.GetProperty("episode_attempt_id").GetString());
        Assert.Equal(
            "subtitle_association",
            subtitle.GetProperty("episode_strategy").GetString());
        Assert.Equal(
            "attempt-detail-subtitle",
            subtitle.GetProperty("episode_attempt_id").GetString());
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

    [Theory]
    [InlineData("SemanticNoMatch", true, "bangumi_fallback_disabled")]
    [InlineData("Network", false, "tmdb_access_not_confirmed")]
    public async Task ListAndDetailExposeLatestAuthoritativeFallbackDecision(
        string failureKind,
        bool accessConfirmed,
        string denialReason)
    {
        await using var app = await RunningApp.StartAsync();
        const string payload = """
            {
              "source": "mikan",
              "data": [{
                "torrent": "https://mikanani.me/private-passkey/fallback-decision.torrent",
                "info": { "title": "Fallback decision", "mikanid": 3951, "bgmid": 547888 }
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
                UPDATE ingest_tasks
                SET status = 'metadata_failed', failure_kind = $failure_kind,
                    failure_reason = 'safe_failure', updated_at_utc = $now
                WHERE id = $task_id;

                INSERT INTO metadata_resolution_runs (
                    id, task_id, status, tmdb_access_confirmed, failure_kind,
                    fallback_eligible, fallback_denial_reason,
                    started_at_utc, completed_at_utc, attempt_number)
                VALUES (
                    'run-fallback-decision', $task_id, 'failed', $access_confirmed,
                    $failure_kind, 0, $denial_reason, $now, $now, 1);
                """;
            command.Parameters.AddWithValue("$task_id", taskId);
            command.Parameters.AddWithValue("$failure_kind", failureKind);
            command.Parameters.AddWithValue("$access_confirmed", accessConfirmed ? 1 : 0);
            command.Parameters.AddWithValue("$denial_reason", denialReason);
            command.Parameters.AddWithValue("$now", now);
            await command.ExecuteNonQueryAsync();
        }

        using var listResponse = await app.Client.GetAsync("/api/v1/metadata/tasks");
        using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStreamAsync());
        var listItem = Assert.Single(
            list.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("task_id").GetString() == taskId);
        AssertFallbackDecision(listItem);

        using var detailResponse = await app.Client.GetAsync(
            $"/api/v1/metadata/tasks/{taskId}");
        using var detail = JsonDocument.Parse(await detailResponse.Content.ReadAsStreamAsync());
        AssertFallbackDecision(detail.RootElement.GetProperty("summary"));

        void AssertFallbackDecision(JsonElement item)
        {
            Assert.Equal("failed", item.GetProperty("latest_run_status").GetString());
            Assert.Equal(
                accessConfirmed,
                item.GetProperty("tmdb_access_confirmed").GetBoolean());
            Assert.False(item.GetProperty("bangumi_fallback_eligible").GetBoolean());
            Assert.Equal(
                denialReason,
                item.GetProperty("bangumi_fallback_denial_reason").GetString());
        }
    }
}
