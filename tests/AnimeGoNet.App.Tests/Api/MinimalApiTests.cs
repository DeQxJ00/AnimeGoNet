using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.Data.Downloads;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Api;

public sealed class MinimalApiTests
{
    [Fact]
    public async Task DockerModeRequiresAccessKey()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "animegonet-app-tests", Guid.NewGuid().ToString("N"));
        var options = AnimeGoNet.Core.Configuration.AnimeGoDefaults.CreateNative(rootPath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AnimeGoApplication.BuildAsync([], options, accessKey: null, runningInContainer: true));

        Assert.Contains("requires a non-empty access_key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PingPreservesLegacyEnvelope()
    {
        await using var app = await RunningApp.StartAsync();

        using var response = await app.Client.GetAsync("/ping");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(200, json.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("pong", json.RootElement.GetProperty("msg").GetString());
        Assert.True(json.RootElement.GetProperty("data").GetProperty("time").GetInt64() > 0);
    }

    [Fact]
    public async Task StatusReportsDatabaseAndEffectivePaths()
    {
        await using var app = await RunningApp.StartAsync();

        using var response = await app.Client.GetAsync("/api/v1/status");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.True(
            json.RootElement.TryGetProperty("database_schema_version", out var schemaVersion),
            json.RootElement.GetRawText());
        Assert.Equal(DatabaseSchema.CurrentVersion, schemaVersion.GetInt32());
        Assert.Equal(Path.Combine(app.RootPath, "data"), json.RootElement.GetProperty("paths").GetProperty("data_path").GetString());
        Assert.True(File.Exists(Path.Combine(app.RootPath, "data", "animegonet.db")));
    }

    [Fact]
    public async Task DirectoryDatabaseStatusAndExplicitRefreshAreExposed()
    {
        await using var app = await RunningApp.StartAsync();

        using var initial = await app.Client.GetAsync("/api/v1/library/directory-database");
        initial.EnsureSuccessStatusCode();
        using var initialJson = JsonDocument.Parse(await initial.Content.ReadAsStringAsync());
        Assert.Equal("0 0 6 * * *", initialJson.RootElement.GetProperty("refresh_cron").GetString());
        Assert.Equal("completed", initialJson.RootElement.GetProperty("last_run_status").GetString());
        Assert.Equal(0, initialJson.RootElement.GetProperty("entry_count").GetInt32());

        using var refresh = await app.Client.PostAsync(
            "/api/v1/library/directory-database/refresh",
            content: null);
        refresh.EnsureSuccessStatusCode();
        using var refreshJson = JsonDocument.Parse(await refresh.Content.ReadAsStringAsync());
        Assert.Equal("completed", refreshJson.RootElement.GetProperty("last_run_status").GetString());
        Assert.Equal(0, refreshJson.RootElement.GetProperty("last_rejected_count").GetInt32());
        Assert.NotEqual(
            initialJson.RootElement.GetProperty("last_run_id").GetString(),
            refreshJson.RootElement.GetProperty("last_run_id").GetString());
    }

    [Fact]
    public async Task StatusReportsConfiguredTmdbWithoutEchoingCredential()
    {
        await using var app = await RunningApp.StartAsync(configure: options => options with
        {
            Metadata = options.Metadata with
            {
                Tmdb = options.Metadata.Tmdb with { ReadAccessToken = "private-tmdb-token" },
            },
        });

        using var response = await app.Client.GetAsync("/api/v1/status");
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        Assert.True(json.RootElement.GetProperty("capabilities").GetProperty("tmdb").GetBoolean());
        Assert.DoesNotContain("private-tmdb-token", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MetadataTaskListShowsPipelineStateWithoutSecretTorrentUrl()
    {
        await using var app = await RunningApp.StartAsync();
        const string payload = """
            {
              "source": "mikan",
              "data": [{
                "torrent": "https://mikanani.me/private-passkey/metadata-list.torrent",
                "info": { "title": "Metadata list", "mikanid": 3951, "bgmid": 547888 }
              }]
            }
            """;
        using var ingest = await app.Client.PostAsync(
            "/api/v1/ingest",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, ingest.StatusCode);

        using var response = await app.Client.GetAsync("/api/v1/metadata/tasks");
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Metadata list", item.GetProperty("title").GetString());
        Assert.Equal("staged", item.GetProperty("status").GetString());
        Assert.Equal(3951, item.GetProperty("mikanid").GetInt32());
        Assert.Equal(0, item.GetProperty("duplicate_file_count").GetInt32());
        Assert.Equal(1, item.GetProperty("pending_file_count").GetInt32());
        Assert.DoesNotContain("private-passkey", body, StringComparison.Ordinal);
        Assert.DoesNotContain("metadata-list.torrent", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PendingTmdbApiShowsFallbackStateAndScopeWithoutFakeEpisodeProgress()
    {
        await using var app = await RunningApp.StartAsync();
        const string payload = """
            {
              "source": "mikan",
              "data": [{
                "torrent": "https://mikanani.me/private-passkey/pending-tmdb.torrent",
                "info": { "title": "Fallback title", "mikanid": 3951, "bgmid": 547888 }
              }]
            }
            """;
        using var ingest = await app.Client.PostAsync(
            "/api/v1/ingest",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        using var ingestJson = JsonDocument.Parse(await ingest.Content.ReadAsStreamAsync());
        var taskId = ingestJson.RootElement.GetProperty("items")[0].GetProperty("ingest_id").GetString()!;
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using (var connection = await database.OpenConnectionAsync())
        await using (var setup = connection.CreateCommand())
        {
            setup.CommandText = """
                INSERT INTO anime_series (
                    id, tmdb_series_id, bangumi_subject_id, canonical_name, original_name,
                    needs_tmdb_completion, created_at_utc, updated_at_utc)
                VALUES ('pending-series', 0, 547888, '兜底动画', 'Fallback Anime', 1, $now, $now);
                INSERT INTO anime_seasons (
                    id, series_id, season_number, canonical_name,
                    created_at_utc, updated_at_utc)
                VALUES ('pending-season', 'pending-series', 2, 'Season 2', $now, $now);
                UPDATE ingest_tasks
                SET status = 'metadata_resolved', failure_kind = 'tmdb_completion_pending',
                    failure_reason = 'tmdb_series_not_found', updated_at_utc = $now
                WHERE id = $task_id;
                UPDATE task_files
                SET source_episode = '1', tmdb_season_number = 2,
                    disposition = 'other',
                    other_reason = 'tmdb_fallback_pending_completion'
                WHERE task_id = $task_id;
                INSERT INTO fallback_claims (
                    id, scope_kind, scope_key, task_file_id,
                    state, claimed_at_utc, expires_at_utc)
                SELECT 'pending-claim', 'mikan_episode', '3951:source:1',
                       id, 'active', $now, NULL
                FROM task_files WHERE task_id = $task_id;
                """;
            setup.Parameters.AddWithValue("$task_id", taskId);
            setup.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            Assert.Equal(5, await setup.ExecuteNonQueryAsync());
        }

        using var listResponse = await app.Client.GetAsync("/api/v1/metadata/pending-tmdb");
        var listBody = await listResponse.Content.ReadAsStringAsync();
        using var listJson = JsonDocument.Parse(listBody);
        var summary = Assert.Single(listJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(547888, summary.GetProperty("bgmid").GetInt32());
        Assert.Equal("兜底动画", summary.GetProperty("fallback_name").GetString());
        Assert.Equal([2], summary.GetProperty("season_numbers").EnumerateArray().Select(value => value.GetInt32()));
        Assert.Equal(1, summary.GetProperty("task_count").GetInt32());
        Assert.Equal(1, summary.GetProperty("active_claim_count").GetInt32());
        Assert.Equal(0, summary.GetProperty("fallback_record_count").GetInt32());
        Assert.False(summary.TryGetProperty("tmdb_series_id", out _));
        Assert.False(summary.TryGetProperty("episode_progress", out _));
        Assert.DoesNotContain("private-passkey", listBody, StringComparison.Ordinal);

        using var detailResponse = await app.Client.GetAsync("/api/v1/metadata/pending-tmdb/547888");
        using var detailJson = JsonDocument.Parse(await detailResponse.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var task = Assert.Single(detailJson.RootElement.GetProperty("tasks").EnumerateArray());
        Assert.Equal("Fallback title", task.GetProperty("title").GetString());
        Assert.Equal(2, task.GetProperty("season_number").GetInt32());
        var scope = Assert.Single(detailJson.RootElement.GetProperty("scopes").EnumerateArray());
        Assert.Equal("mikan_episode", scope.GetProperty("kind").GetString());
        Assert.Equal("仅同一 mikanid", scope.GetProperty("dedup_boundary").GetString());
        Assert.True(scope.GetProperty("cross_source_duplicate_risk").GetBoolean());
        Assert.False(scope.TryGetProperty("key", out _));
        Assert.Empty(detailJson.RootElement.GetProperty("recovery_candidates").EnumerateArray());
    }

    [Fact]
    public async Task PendingTmdbDetailReturnsNotFoundForCanonicalOrMissingSeries()
    {
        await using var app = await RunningApp.StartAsync();

        using var response = await app.Client.GetAsync("/api/v1/metadata/pending-tmdb/547888");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PendingTmdbRecoveryVerifiesTmdbAndCommitsCanonicalCompletion()
    {
        var tmdb = new RecoveryTmdbClient(episodeExists: true);
        await using var app = await RunningApp.StartAsync(tmdbClient: tmdb);
        const string payload = """
            {
              "source": "mikan",
              "data": [{
                "torrent": "https://mikanani.me/private-passkey/recover-tmdb.torrent",
                "info": { "title": "Recover title", "mikanid": 3951, "bgmid": 547888 }
              }]
            }
            """;
        using var ingest = await app.Client.PostAsync(
            "/api/v1/ingest",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        using var ingestJson = JsonDocument.Parse(await ingest.Content.ReadAsStreamAsync());
        var taskId = ingestJson.RootElement.GetProperty("items")[0].GetProperty("ingest_id").GetString()!;
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using (var connection = await database.OpenConnectionAsync())
        await using (var setup = connection.CreateCommand())
        {
            setup.CommandText = """
                INSERT INTO anime_series (
                    id, tmdb_series_id, bangumi_subject_id, canonical_name, original_name,
                    needs_tmdb_completion, created_at_utc, updated_at_utc)
                VALUES ('recover-series', 0, 547888, '兜底动画', 'Fallback Anime', 1, $now, $now);
                INSERT INTO anime_seasons (
                    id, series_id, season_number, canonical_name,
                    created_at_utc, updated_at_utc)
                VALUES ('recover-season', 'recover-series', 2, 'Season 2', $now, $now);
                UPDATE ingest_tasks
                SET status = 'organized', failure_kind = 'tmdb_completion_pending',
                    failure_reason = 'tmdb_series_not_found', updated_at_utc = $now
                WHERE id = $task_id;
                UPDATE task_files
                SET source_episode = '1', tmdb_season_number = 2,
                    disposition = 'other',
                    other_reason = 'tmdb_fallback_pending_completion'
                WHERE task_id = $task_id;
                INSERT INTO download_jobs (
                    id, task_id, downloader_id, state, progress,
                    downloaded_bytes, total_bytes, speed_bytes_per_second,
                    download_root_path, save_root_path, created_at_utc, updated_at_utc)
                VALUES (
                    'recover-job', $task_id, 'bt', 'complete', 1,
                    100, 100, 0, $download_root, $save_root, $now, $now);
                INSERT INTO fallback_claims (
                    id, scope_kind, scope_key, task_file_id,
                    state, claimed_at_utc, expires_at_utc)
                SELECT 'recover-claim', 'mikan_episode', '3951:source:1',
                       id, 'completed', $now, NULL
                FROM task_files WHERE task_id = $task_id;
                INSERT INTO fallback_completion_records (
                    id, anime_series_id, bangumi_subject_id, scope_kind, scope_key,
                    source_id, source_episode, media_path, completed_at_utc)
                VALUES (
                    'recover-record', 'recover-series', 547888,
                    'mikan_episode', '3951:source:1', 'mikan', '1',
                    '/private/media/fallback.mkv', $now);
                """;
            setup.Parameters.AddWithValue("$task_id", taskId);
            setup.Parameters.AddWithValue("$download_root", Path.Combine(app.RootPath, "download"));
            setup.Parameters.AddWithValue("$save_root", Path.Combine(app.RootPath, "save"));
            setup.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            Assert.True(await setup.ExecuteNonQueryAsync() >= 6);
        }

        using (var detail = await app.Client.GetAsync("/api/v1/metadata/pending-tmdb/547888"))
        {
            var body = await detail.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(body);
            var candidate = Assert.Single(
                json.RootElement.GetProperty("recovery_candidates").EnumerateArray());
            Assert.Equal("recover-record", candidate.GetProperty("fallback_record_id").GetString());
            Assert.Equal("1", candidate.GetProperty("source_episode").GetString());
            Assert.Equal("仅同一 mikanid", candidate.GetProperty("dedup_boundary").GetString());
            Assert.DoesNotContain("3951:source:1", body, StringComparison.Ordinal);
            Assert.DoesNotContain("/private/media", body, StringComparison.Ordinal);
        }

        const string recoveryPayload = """
            {
              "tmdb_series_id": 700,
              "mappings": [{
                "fallback_record_id": "recover-record",
                "tmdb_season_number": 2,
                "tmdb_episode_number": 1
              }]
            }
            """;
        using var response = await app.Client.PostAsync(
            "/api/v1/metadata/pending-tmdb/547888/recover",
            new StringContent(recoveryPayload, Encoding.UTF8, "application/json"));
        var responseBody = await response.Content.ReadAsStringAsync();
        using var responseJson = JsonDocument.Parse(responseBody);

        Assert.True(response.StatusCode == HttpStatusCode.OK, responseBody);
        Assert.Equal(700, responseJson.RootElement.GetProperty("tmdb_series_id").GetInt32());
        Assert.False(responseJson.RootElement.GetProperty("has_pending_fallback_records").GetBoolean());
        Assert.Equal(
            "Resolved",
            responseJson.RootElement.GetProperty("items")[0].GetProperty("state").GetString());
        Assert.Equal([(700, 2, 1)], tmdb.EpisodeRequests);
        Assert.DoesNotContain("private-passkey", responseBody, StringComparison.Ordinal);

        await using (var connection = await database.OpenConnectionAsync())
        await using (var verify = connection.CreateCommand())
        {
            verify.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM completion_records
                     WHERE tmdb_series_id = 700
                       AND tmdb_season_number = 2
                       AND tmdb_episode_number = 1),
                    (SELECT COUNT(*) FROM completion_aliases
                     WHERE fallback_scope_kind = 'mikan_episode'),
                    (SELECT COUNT(*) FROM fallback_completion_records
                     WHERE resolution_state = 'resolved'
                       AND resolution_source = 'manual'),
                    (SELECT COUNT(*) FROM pending_tmdb_nfo_rewrite_jobs
                     WHERE state = 'pending' AND tmdb_series_id = 700);
                """;
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.Equal(1, reader.GetInt32(1));
            Assert.Equal(1, reader.GetInt32(2));
            Assert.Equal(1, reader.GetInt32(3));
        }

        using (var taskDetail = await app.Client.GetAsync(
                   $"/api/v1/metadata/tasks/{taskId}"))
        {
            var detailBody = await taskDetail.Content.ReadAsStringAsync();
            using var detailJson = JsonDocument.Parse(detailBody);
            Assert.Equal(HttpStatusCode.OK, taskDetail.StatusCode);
            var rewrite = Assert.Single(
                detailJson.RootElement.GetProperty("nfo_rewrites").EnumerateArray());
            Assert.Equal("pending", rewrite.GetProperty("state").GetString());
            Assert.Equal(0, rewrite.GetProperty("attempt_count").GetInt32());
            Assert.Equal(547888, rewrite.GetProperty("bgmid").GetInt32());
            Assert.Equal(700, rewrite.GetProperty("tmdb_series_id").GetInt32());
            Assert.DoesNotContain("save_root_path", detailBody, StringComparison.Ordinal);
            Assert.DoesNotContain("/private/media", detailBody, StringComparison.Ordinal);
            Assert.DoesNotContain(app.RootPath, detailBody, StringComparison.OrdinalIgnoreCase);
        }

        using var missing = await app.Client.GetAsync("/api/v1/metadata/pending-tmdb/547888");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task PendingTmdbRecoveryRejectsUnverifiedEpisodeBeforeDatabaseMutation()
    {
        var tmdb = new RecoveryTmdbClient(episodeExists: false);
        await using var app = await RunningApp.StartAsync(tmdbClient: tmdb);

        const string request = """
            {
              "tmdb_series_id": 700,
              "mappings": [{
                "fallback_record_id": "missing-record",
                "tmdb_season_number": 2,
                "tmdb_episode_number": 1
              }]
            }
            """;
        using var response = await app.Client.PostAsync(
            "/api/v1/metadata/pending-tmdb/547888/recover",
            new StringContent(request, Encoding.UTF8, "application/json"));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "pending_tmdb_episode_not_found",
            json.RootElement.GetProperty("code").GetString());
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM completion_records;";
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task ProtectedApiAcceptsDirectAndLegacyHashedAccessKeys()
    {
        const string accessKey = "test-secret";
        await using var app = await RunningApp.StartAsync(accessKey);

        using var denied = await app.Client.GetAsync("/api/v1/status");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        using var directRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/status");
        directRequest.Headers.Add("X-AnimeGo-Access-Key", accessKey);
        using var direct = await app.Client.SendAsync(directRequest);
        Assert.Equal(HttpStatusCode.OK, direct.StatusCode);

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(accessKey)));
        using var legacyRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/status");
        legacyRequest.Headers.Add("Access-Key", hash);
        using var legacy = await app.Client.SendAsync(legacyRequest);
        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
    }

    [Fact]
    public async Task UnifiedIngestRoutesSourcesAndReportsEveryRejectedItem()
    {
        await using var app = await RunningApp.StartAsync(configure: options => options with
        {
            InitialSourceProfiles =
            [
                .. options.InitialSourceProfiles,
                new SourceProfileSeed
                {
                    Id = "u2",
                    Adapter = "u2",
                    DownloaderId = "pt",
                    FileStrategy = FileStrategy.Link,
                    AllowedTorrentHosts = ["u2.invalid"],
                },
            ],
        });
        const string payload = """
            {
              "source": "mikan",
              "data": [
                {
                  "torrent": "https://tracker.invalid/personal-passkey/one.torrent",
                  "info": { "title": "Episode 1", "mikanid": 3951, "bgmid": 547888 },
                  "source_evidence": {
                    "published_at_raw": "2099-01-01T00:00:00+08:00",
                    "published_at": "2099-01-01T00:00:00+08:00"
                  }
                },
                {
                  "torrent": "https://tracker.invalid/personal-passkey/two.torrent",
                  "info": { "title": "Episode 2", "mikanid": 3951 }
                },
                {
                  "torrent": "https://tracker.invalid/personal-passkey/three.torrent",
                  "info": null
                }
              ]
            }
            """;

        using var response = await app.Client.PostAsync(
            "/api/v1/ingest",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, json.RootElement.GetProperty("accepted_count").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("rejected_count").GetInt32());
        Assert.Equal("bt", json.RootElement.GetProperty("items")[0].GetProperty("downloader_id").GetString());
        Assert.Equal("staged", json.RootElement.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.Equal(40, json.RootElement.GetProperty("items")[0].GetProperty("info_hash").GetString()!.Length);
        Assert.Equal(1, json.RootElement.GetProperty("items")[0].GetProperty("file_count").GetInt32());
        Assert.Equal("rejected", json.RootElement.GetProperty("items")[1].GetProperty("status").GetString());
        Assert.Equal("info is required", json.RootElement.GetProperty("items")[2].GetProperty("errors")[0].GetString());
        Assert.DoesNotContain("personal-passkey", body, StringComparison.Ordinal);

        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using (var connection = await database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT source_published_at_raw, source_published_at
                FROM ingest_tasks
                WHERE title = 'Episode 1';
                """;
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.True(reader.IsDBNull(0));
            Assert.True(reader.IsDBNull(1));
        }

        const string u2Payload = """
            {
              "source": "u2",
              "data": [
                {
                  "torrent": "https://u2.invalid/passkey/item.torrent",
                  "info": { "title": "U2 item", "source_work_id": "u2-100" }
                }
              ]
            }
            """;
        using var u2Response = await app.Client.PostAsync(
            "/api/v1/ingest",
            new StringContent(u2Payload, Encoding.UTF8, "application/json"));
        using var u2Json = JsonDocument.Parse(await u2Response.Content.ReadAsStreamAsync());
        Assert.Equal("pt", u2Json.RootElement.GetProperty("items")[0].GetProperty("downloader_id").GetString());
    }

    private sealed class RecoveryTmdbClient(bool episodeExists) : ITmdbClient
    {
        public List<(int SeriesId, int SeasonNumber, int EpisodeNumber)> EpisodeRequests { get; } = [];

        public Task<IReadOnlyList<TmdbSeries>> SearchSeriesAsync(
            string title,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TmdbSeries>>([]);

        public Task<TmdbSeries?> GetSeriesAsync(
            int seriesId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeries?>(
                seriesId == 700
                    ? new TmdbSeries(700, "Canonical Anime", "Canonical Anime", null)
                    : null);

        public Task<TmdbSeriesDetails?> GetSeriesDetailsAsync(
            int seriesId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeriesDetails?>(null);

        public Task<TmdbSeason?> GetSeasonAsync(
            int seriesId,
            int seasonNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeason?>(
                seriesId == 700 && seasonNumber == 2
                    ? new TmdbSeason(800, 700, 2, "Season 2", null, 12)
                    : null);

        public Task<TmdbEpisode?> GetEpisodeAsync(
            int seriesId,
            int seasonNumber,
            int episodeNumber,
            CancellationToken cancellationToken = default)
        {
            EpisodeRequests.Add((seriesId, seasonNumber, episodeNumber));
            return Task.FromResult<TmdbEpisode?>(
                episodeExists && seriesId == 700 && seasonNumber == 2 && episodeNumber == 1
                    ? new TmdbEpisode(9001, 700, 2, 1, "Episode 1", null)
                    : null);
        }
    }

    [Fact]
    public async Task StagingFailureIsPerItemAndDoesNotEchoSecretUrl()
    {
        await using var app = await RunningApp.StartAsync(stagingService: new RejectingStagingService());
        const string payload = """
            {
              "source": "mikan",
              "data": [
                {
                  "torrent": "https://mikanani.me/private-passkey/file.torrent?token=secret",
                  "info": { "title": "Episode 1", "mikanid": 3951, "bgmid": 547888 }
                }
              ]
            }
            """;

        using var response = await app.Client.PostAsync(
            "/api/v1/ingest",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        Assert.Equal(0, json.RootElement.GetProperty("accepted_count").GetInt32());
        Assert.Equal("rejected", json.RootElement.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.Contains("HostNotAllowed", json.RootElement.GetProperty("items")[0].GetProperty("errors")[0].GetString());
        Assert.DoesNotContain("private-passkey", body, StringComparison.Ordinal);
        Assert.DoesNotContain("token=secret", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadListReturnsCanonicalSnapshotWithoutSecretPaths()
    {
        await using var app = await RunningApp.StartAsync();
        const string payload = """
            {
              "source": "mikan",
              "data": [{
                "torrent": "https://mikanani.me/private-passkey/file.torrent?token=secret",
                "info": { "title": "Episode 1", "mikanid": 3951, "bgmid": 547888 }
              }]
            }
            """;
        using var ingestResponse = await app.Client.PostAsync(
            "/api/v1/ingest",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        using var ingestJson = JsonDocument.Parse(await ingestResponse.Content.ReadAsStreamAsync());
        var hash = ingestJson.RootElement.GetProperty("items")[0].GetProperty("info_hash").GetString()!;
        var tasks = app.App.Services.GetRequiredService<IngestTaskStore>();
        var claim = Assert.IsType<ClaimedStagedTorrentRecord>(await tasks.TryClaimNextStagedAsync(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1)));
        await tasks.CompleteDispatchAsync(
            claim,
            new DownloadTaskSnapshot(hash, "Episode", DownloadTaskState.Waiting, 0, 0, 100, 0, null),
            Path.Combine(app.RootPath, "download", "bt"),
            Path.Combine(app.RootPath, "save"),
            DateTimeOffset.UtcNow);
        var database = app.App.Services.GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using (var connection = await database.OpenConnectionAsync())
        await using (var ready = connection.CreateCommand())
        {
            ready.CommandText = """
                UPDATE download_jobs SET preparation_state = 'completed' WHERE task_id = $task_id;
                UPDATE ingest_tasks SET status = 'download_queued' WHERE id = $task_id;
                """;
            ready.Parameters.AddWithValue("$task_id", claim.TaskId);
            Assert.Equal(2, await ready.ExecuteNonQueryAsync());
        }
        await app.App.Services.GetRequiredService<DownloadJobStore>().ApplyInstanceSnapshotAsync(
            "bt",
            [new DownloadTaskSnapshot(hash, "Episode", DownloadTaskState.Downloading, 0.4, 40, 100, 8, 7, 2, 4)],
            DateTimeOffset.UtcNow);

        using var response = await app.Client.GetAsync("/api/v1/downloads");
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        var item = json.RootElement.GetProperty("items")[0];
        Assert.Equal("Episode 1", item.GetProperty("title").GetString());
        Assert.Equal("bt", item.GetProperty("downloader_id").GetString());
        Assert.Equal("downloading", item.GetProperty("state").GetString());
        Assert.Equal(0.4, item.GetProperty("progress").GetDouble());
        Assert.Equal(2, item.GetProperty("seeds").GetInt32());
        Assert.Equal(4, item.GetProperty("peers").GetInt32());
        Assert.Equal("not_required", item.GetProperty("seeding_state").GetString());
        Assert.Equal(0, item.GetProperty("seeding_target_minutes").GetInt32());
        Assert.Equal(0, item.GetProperty("seeding_elapsed_seconds").GetInt64());
        Assert.False(item.GetProperty("is_stale").GetBoolean());
        Assert.DoesNotContain("private-passkey", body, StringComparison.Ordinal);
        Assert.DoesNotContain("token=secret", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LegacyDownloadManagerUsesSameMikanRouteAndEnvelope()
    {
        await using var app = await RunningApp.StartAsync();
        const string payload = """
            {
              "source": "mikan",
              "data": [
                {
                  "torrent": "https://tracker.invalid/passkey/legacy.torrent",
                  "info": { "name": "Legacy episode", "url": "https://mikanani.me/Home/Bangumi/3951" }
                }
              ]
            }
            """;

        using var response = await app.Client.PostAsync(
            "/api/download/manager",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(200, json.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("开始处理1个下载项", json.RootElement.GetProperty("msg").GetString());
        var data = json.RootElement.GetProperty("data");
        Assert.Equal("bt", data.GetProperty("items")[0].GetProperty("downloader_id").GetString());
        Assert.Equal(1, data.GetProperty("accepted_count").GetInt32());
    }

    private sealed class RejectingStagingService : ITorrentStagingService
    {
        public Task<StagedTorrent> StageAsync(
            Uri secretUrl,
            TorrentSourcePolicy sourcePolicy,
            CancellationToken cancellationToken = default) =>
            throw new TorrentStagingException(
                TorrentStagingFailureCode.HostNotAllowed,
                "Torrent host is not allowed by the source profile.");

        public Task<bool> DeleteAsync(string stagingFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public FileStream OpenRead(string stagingFileName) => throw new FileNotFoundException();

        public Task<int> CleanupExpiredAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }
}
