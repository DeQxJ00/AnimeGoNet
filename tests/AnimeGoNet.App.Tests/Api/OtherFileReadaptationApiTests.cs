using System.Net;
using System.Text;
using System.Text.Json;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Api;

public sealed class OtherFileReadaptationApiTests
{
    [Fact]
    public async Task PreviewAndStartRequeuesOtherWithoutRedownloading()
    {
        var transport = new MikanPageTransport();
        await using var app = await RunningApp.StartAsync(
            tmdbClient: new ReviewTmdbClient(),
            rssDnsResolver: new PublicDnsResolver(),
            rssHttpTransport: transport);
        const string payload = """
            {
              "source": "mikan",
              "data": [{
                "torrent": "https://mikanani.me/private-passkey/readapt-api.torrent",
                "info": {
                  "title": "Other 重新适配样本 第12话",
                  "source_item_id": "readapt-api-item",
                  "source_work_id": "3951",
                  "mikan_url": "https://mikanime.tv/Home/Episode/readapt-api",
                  "mikanid": 3951,
                  "bgmid": 547888
                }
              }]
            }
            """;
        using var ingest = await app.Client.PostAsync(
            "/api/v1/ingest",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        using var ingestJson = JsonDocument.Parse(await ingest.Content.ReadAsStreamAsync());
        var taskId = ingestJson.RootElement.GetProperty("items")[0]
            .GetProperty("ingest_id").GetString()!;

        var tasks = app.App.Services.GetRequiredService<IngestTaskStore>();
        var dispatch = Assert.IsType<ClaimedStagedTorrentRecord>(await tasks.TryClaimNextStagedAsync(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1)));
        await tasks.CompleteDispatchAsync(
            dispatch,
            new DownloadTaskSnapshot(
                dispatch.InfoHash,
                "Other 重新适配样本",
                DownloadTaskState.Complete,
                1,
                5,
                5,
                0,
                null),
            Path.Combine(app.RootPath, "download"),
            Path.Combine(app.RootPath, "library"),
            DateTimeOffset.UtcNow);

        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        var target = Path.Combine(app.RootPath, "library", "Series", "S01", "Other", "episode.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllBytesAsync(target, [1, 2, 3, 4, 5]);
        await using (var connection = await database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO anime_series (
                    id, tmdb_series_id, canonical_name, original_name,
                    needs_tmdb_completion, created_at_utc, updated_at_utc)
                VALUES ('readapt-api-series', 65942, 'Series', 'Series', 0, $now, $now);
                UPDATE task_files
                SET disposition = 'other', other_reason = 'ai_tmdb_season_changed',
                    tmdb_series_id = 65942, tmdb_season_number = 1,
                    download_wanted = 1
                WHERE task_id = $task_id;
                UPDATE download_jobs
                SET preparation_state = 'completed', organization_state = 'completed',
                    organization_phase = 'completed', organization_total_units = 1,
                    organization_completed_units = 1, state = 'complete', progress = 1
                WHERE task_id = $task_id;
                UPDATE ingest_tasks SET status = 'organized' WHERE id = $task_id;
                INSERT INTO file_operations (
                    id, task_file_id, strategy, source_path, target_path, state,
                    bytes_verified, failure_reason, created_at_utc, updated_at_utc)
                SELECT 'readapt-api-operation', id, 'move', $source, $target,
                       'completed', 5, NULL, $now, $now
                FROM task_files WHERE task_id = $task_id;
                """;
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$task_id", taskId);
            command.Parameters.AddWithValue("$source", Path.Combine(app.RootPath, "download", "episode.mkv"));
            command.Parameters.AddWithValue("$target", target);
            Assert.Equal(5, await command.ExecuteNonQueryAsync());
        }

        await using (var connection = await database.OpenConnectionAsync())
        await using (var candidate = connection.CreateCommand())
        {
            candidate.CommandText = """
                INSERT INTO ai_series_change_reviews (
                    id, task_id, task_file_id, state,
                    expected_tmdb_series_id, expected_tmdb_season_number,
                    proposed_tmdb_series_id, proposed_series_name, proposed_original_name,
                    proposed_series_first_air_date, proposed_series_poster_path,
                    proposed_tmdb_season_id, proposed_tmdb_season_number,
                    proposed_season_name, proposed_season_air_date,
                    proposed_season_episode_count, proposed_season_poster_path,
                    proposed_tmdb_episode_id, proposed_tmdb_episode_number,
                    proposed_episode_name, proposed_episode_air_date,
                    requested_at_utc, reviewed_at_utc)
                SELECT 'ai-series-review-api', $task_id, id, 'pending',
                       65942, 1, 70000, 'AI Candidate', 'AI Candidate',
                       '2026-07-05', NULL, 70001, 1, 'Season 1', '2026-07-05',
                       12, NULL, 70006, 6, 'Episode 6', '2026-08-09', $now, NULL
                FROM task_files WHERE task_id = $task_id;
                UPDATE ingest_tasks
                SET readaptation_review_state = 'pending',
                    readaptation_review_requested_at_utc = $now,
                    readaptation_reviewed_at_utc = NULL
                WHERE id = $task_id;
                """;
            candidate.Parameters.AddWithValue("$task_id", taskId);
            candidate.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            Assert.Equal(2, await candidate.ExecuteNonQueryAsync());
        }

        using (var aiReview = await app.Client.GetAsync(
                   $"/api/v1/metadata/tasks/{taskId}/other-readaptation/review"))
        {
            Assert.Equal(HttpStatusCode.OK, aiReview.StatusCode);
            using var aiReviewJson = JsonDocument.Parse(await aiReview.Content.ReadAsStreamAsync());
            Assert.Equal("ai_series_change", aiReviewJson.RootElement
                .GetProperty("review_kind").GetString());
            Assert.Equal("pending", aiReviewJson.RootElement
                .GetProperty("review_decision").GetString());
            Assert.Equal(70000, aiReviewJson.RootElement.GetProperty("files")[0]
                .GetProperty("after_tmdb_series_id").GetInt32());
        }

        using (var taskListResponse = await app.Client.GetAsync(
                   "/api/v1/metadata/tasks?page=1&page_size=100&review_state=pending"))
        {
            Assert.Equal(HttpStatusCode.OK, taskListResponse.StatusCode);
            using var tasksJson = JsonDocument.Parse(await taskListResponse.Content.ReadAsStreamAsync());
            var task = Assert.Single(
                tasksJson.RootElement.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("task_id").GetString() == taskId);
            Assert.Equal("ai_series_change", task.GetProperty("review_kind").GetString());
        }

        using (var reject = await app.Client.PostAsync(
                   $"/api/v1/metadata/tasks/{taskId}/ai-series-change-review/reject",
                   null))
        {
            Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
            using var rejectJson = JsonDocument.Parse(await reject.Content.ReadAsStreamAsync());
            Assert.Equal("rejected", rejectJson.RootElement.GetProperty("decision").GetString());
            Assert.Equal("kept_in_other", rejectJson.RootElement.GetProperty("result").GetString());
        }

        using var preview = await app.Client.GetAsync(
            $"/api/v1/metadata/tasks/{taskId}/other-readaptation/preview");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        using var previewJson = JsonDocument.Parse(await preview.Content.ReadAsStreamAsync());
        Assert.True(previewJson.RootElement.GetProperty("eligible").GetBoolean());
        Assert.True(previewJson.RootElement.GetProperty("files")[0]
            .GetProperty("source_available").GetBoolean());

        using var start = await app.Client.PostAsync(
            $"/api/v1/metadata/tasks/{taskId}/other-readaptation",
            null);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.True(File.Exists(target));

        await using var verify = await database.OpenConnectionAsync();
        await using var state = verify.CreateCommand();
        state.CommandText = """
            SELECT task.status, file.disposition, file.tmdb_series_id,
                   file.tmdb_season_number, job.preparation_state,
                   job.organization_state
            FROM ingest_tasks AS task
            JOIN task_files AS file ON file.task_id = task.id
            JOIN download_jobs AS job ON job.task_id = task.id
            WHERE task.id = $task_id;
            """;
        state.Parameters.AddWithValue("$task_id", taskId);
        await using var reader = await state.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("download_preparing", reader.GetString(0));
        Assert.Equal("pending", reader.GetString(1));
        Assert.True(reader.IsDBNull(2));
        Assert.True(reader.IsDBNull(3));
        Assert.Equal("completed", reader.GetString(4));
        Assert.Equal("pending", reader.GetString(5));
        Assert.Equal(2, transport.Requests.Count);
        await reader.DisposeAsync();

        await using (var complete = verify.CreateCommand())
        {
            complete.CommandText = """
                UPDATE other_file_readaptation_jobs SET state = 'completed', completed_at_utc = $now
                WHERE task_id = $task_id AND state = 'pending';
                UPDATE task_files
                SET disposition = 'other', other_reason = 'ai_tmdb_episode_unresolved',
                    tmdb_series_id = 65942, tmdb_season_number = 1,
                    tmdb_episode_number = NULL
                WHERE task_id = $task_id;
                UPDATE ingest_tasks SET status = 'organized' WHERE id = $task_id;
                UPDATE download_jobs
                SET organization_state = 'completed', organization_phase = 'completed',
                    organization_total_units = 1, organization_completed_units = 1
                WHERE task_id = $task_id;
                INSERT INTO file_operations (
                    id, task_file_id, strategy, source_path, target_path, state,
                    bytes_verified, failure_reason, created_at_utc, updated_at_utc)
                SELECT 'readapt-api-operation-final', id, 'move', $target, $target,
                       'completed', 5, NULL, $now, $now
                FROM task_files WHERE task_id = $task_id;
                INSERT INTO completion_records (
                    id, tmdb_series_id, tmdb_season_number, tmdb_episode_number,
                    source_id, source_item_id, media_path, completed_at_utc)
                VALUES ('readapt-api-existing-e12', 65942, 1, 12,
                        'test', 'existing-e12', $completed_target, $now);
                """;
            complete.Parameters.AddWithValue("$task_id", taskId);
            complete.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            complete.Parameters.AddWithValue("$target", target);
            complete.Parameters.AddWithValue(
                "$completed_target",
                Path.Combine(app.RootPath, "library", "Series", "S01", "Series - S01E12.mkv"));
            Assert.Equal(6, await complete.ExecuteNonQueryAsync());
        }

        using var manual = await app.Client.PostAsync(
            $"/api/v1/metadata/tasks/{taskId}/other-readaptation/review/files/"
                + $"{await ReadFileIdAsync(database, taskId)}/manual-override",
            new StringContent(
                """{"tmdb_series_id":65942,"tmdb_season_number":1,"tmdb_episode_number":12}""",
                Encoding.UTF8,
                "application/json"));
        Assert.True(
            manual.StatusCode == HttpStatusCode.OK,
            $"Expected OK, received {manual.StatusCode}: {await manual.Content.ReadAsStringAsync()}");
        using var manualJson = JsonDocument.Parse(await manual.Content.ReadAsStreamAsync());
        Assert.Equal("duplicate_kept_in_other", manualJson.RootElement.GetProperty("result").GetString());
        Assert.Equal("kept_in_other_no_auto_delete", manualJson.RootElement.GetProperty("other_action").GetString());

        using var reviewPreview = await app.Client.GetAsync(
            $"/api/v1/metadata/tasks/{taskId}/other-readaptation/review");
        Assert.Equal(HttpStatusCode.OK, reviewPreview.StatusCode);
        using var reviewJson = JsonDocument.Parse(await reviewPreview.Content.ReadAsStreamAsync());
        Assert.Equal("pending", reviewJson.RootElement.GetProperty("review_state").GetString());
        Assert.Equal("awaiting_review", reviewJson.RootElement.GetProperty("completion_status").GetString());
        Assert.Equal(JsonValueKind.Null, reviewJson.RootElement.GetProperty("reviewed_at_utc").ValueKind);
        var comparison = reviewJson.RootElement.GetProperty("files")[0];
        Assert.Equal("other", comparison.GetProperty("before_disposition").GetString());
        Assert.Equal("ai_tmdb_episode_unresolved", comparison.GetProperty("before_other_reason").GetString());
        Assert.Equal(65942, comparison.GetProperty("before_tmdb_series_id").GetInt32());
        Assert.Equal(1, comparison.GetProperty("before_tmdb_season_number").GetInt32());
        Assert.Equal("other", comparison.GetProperty("after_disposition").GetString());
        Assert.Equal("episode_already_completed", comparison.GetProperty("after_other_reason").GetString());
        Assert.Equal(12, comparison.GetProperty("after_tmdb_episode_number").GetInt32());
        Assert.Equal("manual_review_override", comparison.GetProperty("after_episode_strategy").GetString());
        Assert.Equal(target, comparison.GetProperty("before_media_path").GetString());
        Assert.Equal(JsonValueKind.Null, comparison.GetProperty("after_media_path").ValueKind);

        using var approve = await app.Client.PostAsync(
            $"/api/v1/metadata/tasks/{taskId}/other-readaptation/review",
            null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        using var completedReview = await app.Client.GetAsync(
            $"/api/v1/metadata/tasks/{taskId}/other-readaptation/review");
        using var completedReviewJson = JsonDocument.Parse(
            await completedReview.Content.ReadAsStreamAsync());
        Assert.Equal("approved", completedReviewJson.RootElement.GetProperty("review_state").GetString());
        Assert.Equal("review_completed", completedReviewJson.RootElement
            .GetProperty("completion_status").GetString());
        Assert.Equal(JsonValueKind.String, completedReviewJson.RootElement
            .GetProperty("reviewed_at_utc").ValueKind);
        using var deletePreview = await app.Client.GetAsync($"/api/v1/delete/tasks/{taskId}/preview");
        using var deleteJson = JsonDocument.Parse(await deletePreview.Content.ReadAsStreamAsync());
        Assert.True(deleteJson.RootElement.GetProperty("task_record_deletion_allowed").GetBoolean());

        await using (var restoreOther = await database.OpenConnectionAsync())
        await using (var restore = restoreOther.CreateCommand())
        {
            restore.CommandText = """
                UPDATE task_files SET disposition = 'other' WHERE task_id = $task_id;
                UPDATE ingest_tasks SET status = 'organized' WHERE id = $task_id;
                """;
            restore.Parameters.AddWithValue("$task_id", taskId);
            Assert.Equal(2, await restore.ExecuteNonQueryAsync());
        }

        using var ignore = await app.Client.PostAsync(
            $"/api/v1/metadata/tasks/{taskId}/other-attention/ignore",
            null);
        Assert.Equal(HttpStatusCode.OK, ignore.StatusCode);
        using var ignoreJson = JsonDocument.Parse(await ignore.Content.ReadAsStreamAsync());
        Assert.Equal("ignored", ignoreJson.RootElement.GetProperty("result").GetString());
        Assert.Equal(1, ignoreJson.RootElement.GetProperty("ignored_file_count").GetInt32());

        await using var ignoredConnection = await database.OpenConnectionAsync();
        await using var ignored = ignoredConnection.CreateCommand();
        ignored.CommandText = "SELECT disposition, other_reason FROM task_files WHERE task_id = $task_id;";
        ignored.Parameters.AddWithValue("$task_id", taskId);
        await using var ignoredReader = await ignored.ExecuteReaderAsync();
        Assert.True(await ignoredReader.ReadAsync());
        Assert.Equal("ignored", ignoredReader.GetString(0));
        Assert.Equal("episode_already_completed", ignoredReader.GetString(1));
    }

    private static async Task<string> ReadFileIdAsync(AnimeGoSqliteDatabase database, string taskId)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM task_files WHERE task_id = $task_id;";
        command.Parameters.AddWithValue("$task_id", taskId);
        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }

    private sealed class PublicDnsResolver : ITorrentDnsResolver
    {
        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(
            string host,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("1.1.1.1")]);
    }

    private sealed class MikanPageTransport : ITorrentHttpTransport
    {
        public List<Uri> Requests { get; } = [];

        public ValueTask<TorrentHttpResponse> SendAsync(
            Uri uri,
            IReadOnlyList<IPAddress> validatedAddresses,
            CancellationToken cancellationToken)
        {
            Requests.Add(uri);
            var html = uri.AbsolutePath.StartsWith("/Home/Episode/", StringComparison.OrdinalIgnoreCase)
                ? "<a class='mikan-rss' href='/RSS/Bangumi?bangumiId=3951&amp;subgroupid=370'>RSS</a>"
                : "<p class='bangumi-info'><a href='https://bgm.tv/subject/547888'>Bangumi</a></p>";
            var bytes = Encoding.UTF8.GetBytes(html);
            return ValueTask.FromResult(new TorrentHttpResponse(
                HttpStatusCode.OK, null, bytes.Length, new MemoryStream(bytes, writable: false)));
        }
    }

    private sealed class ReviewTmdbClient : ITmdbClient
    {
        public Task<IReadOnlyList<TmdbSeries>> SearchSeriesAsync(
            string title,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TmdbSeries>>([]);

        public Task<TmdbSeries?> GetSeriesAsync(
            int seriesId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeries?>(seriesId == 65942
                ? new TmdbSeries(65942, "Re：从零开始的异世界生活", "Re:Zero", new DateOnly(2016, 4, 4))
                : null);

        public Task<TmdbSeriesDetails?> GetSeriesDetailsAsync(
            int seriesId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeriesDetails?>(null);

        public Task<TmdbSeason?> GetSeasonAsync(
            int seriesId,
            int seasonNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeason?>(seriesId == 65942 && seasonNumber == 1
                ? new TmdbSeason(70001, 65942, 1, "Season 1", new DateOnly(2016, 4, 4), 78)
                : null);

        public Task<TmdbEpisode?> GetEpisodeAsync(
            int seriesId,
            int seasonNumber,
            int episodeNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbEpisode?>(seriesId == 65942 && seasonNumber == 1 && episodeNumber == 12
                ? new TmdbEpisode(70012, 65942, 1, 12, "Episode 12", new DateOnly(2016, 6, 20))
                : null);
    }
}
