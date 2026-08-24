using System.Net;
using System.Text;
using System.Text.Json;
using AnimeGoNet.Core.Compatibility;
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
                "info": {
                  "title": "来源作品 第03话",
                  "source_item_id": "private-source-item-id",
                  "source_work_id": "3951",
                  "mikanid": 3951,
                  "bgmid": 547888,
                  "anidbid": 999,
                  "imdbid": "tt1234567"
                }
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

                INSERT INTO mikan_rss_batches (
                    id, source_profile_id, rule_revision, fingerprint, mikanid,
                    priority_enabled, entry_count, created_at_utc,
                    legacy_filter_revision, legacy_filter_enabled,
                    bangumi_subject_id, bangumi_discovery_state,
                    bangumi_discovery_failure_code)
                VALUES (
                    'rss-detail-batch', 'mikan', 8, $batch_fingerprint, 3951,
                    1, 1, $now,
                    5, 1,
                    547888, 'resolved', NULL);

                INSERT INTO mikan_rss_batch_entries (
                    batch_id, candidate_id, ordinal, title, mikan_url,
                    torrent_url_fingerprint, content_type, length_bytes,
                    published_date, source_episode_kind, source_episode,
                    decision_kind, decision_reason, winner_candidate_id,
                    legacy_filter_state, legacy_filter_reason,
                    legacy_filter_scope, legacy_filter_key,
                    identity_mikanid, identity_groupid,
                    effect_state, claim_token, claim_expires_at_utc, ingest_task_id)
                VALUES (
                    'rss-detail-batch', $candidate_id, 0, '来源作品 第03话',
                    'https://mikanani.me/Home/Episode/private-rss-passkey',
                    $torrent_fingerprint, 'application/x-bittorrent', 734003200,
                    '2026-07-01', 'normal', '3',
                    'Winner', 'PriorityWinner', $candidate_id,
                    'Accepted', 'Accepted',
                    'Filiter1', 'key_3951_370',
                    3951, 370,
                    'ingested', NULL, NULL, $task_id);

                INSERT INTO mikan_rss_decision_groups (
                    batch_id, candidate_id, position, group_id)
                VALUES ('rss-detail-batch', $candidate_id, 0, 'codec');
                """;
            command.Parameters.AddWithValue("$task_id", taskId);
            command.Parameters.AddWithValue("$now", now);
            command.Parameters.AddWithValue("$batch_fingerprint", new string('b', 64));
            command.Parameters.AddWithValue("$candidate_id", new string('a', 64));
            command.Parameters.AddWithValue("$torrent_fingerprint", new string('c', 64));
            await command.ExecuteNonQueryAsync();
        }

        var resolutionStore = app.App.Services
            .GetRequiredService<AnimeGoNet.Data.Metadata.MetadataResolutionStore>();
        var projections = await resolutionStore.ListTasksAsync();
        Assert.Contains(projections, item => item.TaskId == taskId);
        Assert.NotNull(await resolutionStore.GetTaskDetailAsync(taskId));

        using var response = await app.Client.GetAsync(
            $"/api/v1/metadata/tasks/{taskId}");
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var sourceEvidence = root.GetProperty("source_evidence");
        var files = root.GetProperty("files").EnumerateArray().ToArray();
        var rssEvidence = Assert.Single(root.GetProperty("rss_evidence").EnumerateArray());
        Assert.Equal(2, files.Length);
        var file = Assert.Single(
            files,
            value => value.GetProperty("source_name").GetString() == "Source Show - 03.mkv");
        var subtitle = Assert.Single(
            files,
            value => value.GetProperty("source_name").GetString()
                == "Source Show - 03.zh-Hans.ass");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("mikan", sourceEvidence.GetProperty("source_profile_id").GetString());
        Assert.True(sourceEvidence.GetProperty("source_profile_revision").GetInt64() > 0);
        Assert.Equal("mikan", sourceEvidence.GetProperty("source_id").GetString());
        Assert.Equal("来源作品 第03话", sourceEvidence.GetProperty("source_title").GetString());
        Assert.Equal(
            StableHash.Sha256LowerHex(
                "animegonet-source-id\0mikan\0item\0private-source-item-id"),
            sourceEvidence.GetProperty("source_item_id_fingerprint").GetString());
        Assert.Equal(
            StableHash.Sha256LowerHex("animegonet-source-id\0mikan\0work\03951"),
            sourceEvidence.GetProperty("source_work_id_fingerprint").GetString());
        Assert.Equal(3951, sourceEvidence.GetProperty("mikanid").GetInt32());
        Assert.Equal(JsonValueKind.Null, sourceEvidence.GetProperty("groupid").ValueKind);
        Assert.Equal(547888, sourceEvidence.GetProperty("bgmid").GetInt32());
        Assert.Equal(999, sourceEvidence.GetProperty("anidbid").GetInt32());
        Assert.Equal("tt1234567", sourceEvidence.GetProperty("imdbid").GetString());
        Assert.False(sourceEvidence.GetProperty("published_at_raw_available").GetBoolean());
        Assert.Equal(JsonValueKind.Null, sourceEvidence.GetProperty("published_at").ValueKind);
        Assert.Equal("rss-detail-batch", rssEvidence.GetProperty("batch_id").GetString());
        Assert.Equal(0, rssEvidence.GetProperty("entry_ordinal").GetInt32());
        Assert.Equal("mikan", rssEvidence.GetProperty("source_profile_id").GetString());
        Assert.Equal(8, rssEvidence.GetProperty("rule_revision").GetInt64());
        Assert.True(rssEvidence.GetProperty("priority_enabled").GetBoolean());
        Assert.Equal(5, rssEvidence.GetProperty("legacy_filter_revision").GetInt64());
        Assert.Equal("normal", rssEvidence.GetProperty("source_episode_kind").GetString());
        Assert.Equal("3", rssEvidence.GetProperty("source_episode").GetString());
        Assert.Equal("PriorityWinner", rssEvidence.GetProperty("decision_reason").GetString());
        Assert.Equal(
            "codec",
            Assert.Single(rssEvidence.GetProperty("evaluated_priority_groups").EnumerateArray())
                .GetString());
        Assert.Equal("Filiter1", rssEvidence.GetProperty("legacy_filter_scope").GetString());
        Assert.Equal(370, rssEvidence.GetProperty("identity_groupid").GetInt32());
        Assert.Equal("ingested", rssEvidence.GetProperty("effect_state").GetString());
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
        Assert.DoesNotContain("private-rss-passkey", body, StringComparison.Ordinal);
        Assert.DoesNotContain("private-source-item-id", body, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('a', 64), body, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('b', 64), body, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('c', 64), body, StringComparison.Ordinal);

        using var listResponse = await app.Client.GetAsync(
            $"/api/v1/metadata/tasks?search={taskId}&page=1&page_size=10");
        using var listJson = JsonDocument.Parse(await listResponse.Content.ReadAsStreamAsync());
        var listItem = Assert.Single(listJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(
            [3],
            listItem.GetProperty("episode_numbers").EnumerateArray()
                .Select(value => value.GetInt32()).ToArray());
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
