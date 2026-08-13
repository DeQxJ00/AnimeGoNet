using System.Net;
using System.Text;
using System.Text.Json;
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
        await using var app = await RunningApp.StartAsync();
        const string payload = """
            {
              "source": "mikan",
              "data": [{
                "torrent": "https://mikanani.me/private-passkey/readapt-api.torrent",
                "info": {
                  "title": "Other 重新适配样本 第12话",
                  "source_item_id": "readapt-api-item",
                  "source_work_id": "3951",
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
        Assert.Equal("metadata_season_resolved", reader.GetString(0));
        Assert.Equal("pending", reader.GetString(1));
        Assert.Equal(65942, reader.GetInt32(2));
        Assert.Equal(1, reader.GetInt32(3));
        Assert.Equal("completed", reader.GetString(4));
        Assert.Equal("pending", reader.GetString(5));
    }
}
