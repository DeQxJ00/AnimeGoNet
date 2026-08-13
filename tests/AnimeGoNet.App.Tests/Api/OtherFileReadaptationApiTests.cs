using System.Net;
using System.Text;
using System.Text.Json;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.Core.Downloads;
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
                SET disposition = 'episode', other_reason = NULL,
                    tmdb_series_id = 65942, tmdb_season_number = 1,
                    tmdb_episode_number = 12
                WHERE task_id = $task_id;
                UPDATE ingest_tasks SET status = 'organized' WHERE id = $task_id;
                """;
            complete.Parameters.AddWithValue("$task_id", taskId);
            complete.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            Assert.Equal(3, await complete.ExecuteNonQueryAsync());
        }

        using var reviewPreview = await app.Client.GetAsync(
            $"/api/v1/metadata/tasks/{taskId}/other-readaptation/review");
        Assert.Equal(HttpStatusCode.OK, reviewPreview.StatusCode);
        using var reviewJson = JsonDocument.Parse(await reviewPreview.Content.ReadAsStreamAsync());
        Assert.Equal("pending", reviewJson.RootElement.GetProperty("review_state").GetString());
        var comparison = reviewJson.RootElement.GetProperty("files")[0];
        Assert.Equal("other", comparison.GetProperty("before_disposition").GetString());
        Assert.Equal("ai_tmdb_season_changed", comparison.GetProperty("before_other_reason").GetString());
        Assert.Equal(65942, comparison.GetProperty("before_tmdb_series_id").GetInt32());
        Assert.Equal(1, comparison.GetProperty("before_tmdb_season_number").GetInt32());
        Assert.Equal("episode", comparison.GetProperty("after_disposition").GetString());
        Assert.Equal(12, comparison.GetProperty("after_tmdb_episode_number").GetInt32());
        Assert.Equal(JsonValueKind.Null, comparison.GetProperty("after_episode_strategy").ValueKind);
        Assert.Equal(target, comparison.GetProperty("before_media_path").GetString());
        Assert.Equal(JsonValueKind.Null, comparison.GetProperty("after_media_path").ValueKind);

        using var approve = await app.Client.PostAsync(
            $"/api/v1/metadata/tasks/{taskId}/other-readaptation/review",
            null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        using var deletePreview = await app.Client.GetAsync($"/api/v1/delete/tasks/{taskId}/preview");
        using var deleteJson = JsonDocument.Parse(await deletePreview.Content.ReadAsStreamAsync());
        Assert.True(deleteJson.RootElement.GetProperty("task_record_deletion_allowed").GetBoolean());
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
}
