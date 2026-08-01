using System.Text;
using System.Text.Json;
using AnimeGoNet.App.Downloads;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Data.Ingest;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Downloads;

public sealed class DownloadPreparationProcessorTests
{
    [Fact]
    public async Task MixedTorrentDisablesOnlyDuplicateAndIgnoredFilesThenResumes()
    {
        var client = new FakeDownloadClient
        {
            Files =
            [
                new DownloadFileSnapshot(4, "Show/EP02.mkv", 200, 0, 1),
                new DownloadFileSnapshot(2, "Show/EP01.zh-Hans.ass", 10, 0, 1),
                new DownloadFileSnapshot(1, "Show/EP01.mkv", 100, 0, 1),
            ],
        };
        await using var app = await RunningApp.StartAsync(downloadClientRegistry: new FakeRegistry(client));
        var taskId = await PrepareTaskAsync(
            app,
            [
                ("Show/EP02.mkv", 200L, "episode"),
                ("Show/EP01.zh-Hans.ass", 10L, "duplicate"),
                ("Show/EP01.mkv", 100L, "ignored"),
            ]);

        var result = await app.App.Services.GetRequiredService<DownloadPreparationProcessor>().RunOnceAsync();

        Assert.Equal(DownloadPreparationResult.Completed, result);
        Assert.Equal([[1, 2], [4]], client.PriorityCalls.Select(call => call.Indexes).ToArray());
        Assert.Equal([0, 1], client.PriorityCalls.Select(call => call.Priority).ToArray());
        Assert.Single(client.Paused);
        Assert.Single(client.Resumed);
        Assert.Empty(client.Deleted);
        var state = await ReadStateAsync(app, taskId);
        Assert.Equal("download_queued", state.TaskStatus);
        Assert.Equal("completed", state.PreparationPhase);
        Assert.Collection(
            state.Files.OrderBy(file => file.Index),
            file => Assert.Equal((1, 0, false), (file.Index, file.Priority, file.Wanted)),
            file => Assert.Equal((2, 0, false), (file.Index, file.Priority, file.Wanted)),
            file => Assert.Equal((4, 1, true), (file.Index, file.Priority, file.Wanted)));
    }

    [Fact]
    public async Task AllDuplicateTorrentStaysStoppedAndIsRemovedWithoutDeletingFiles()
    {
        var client = new FakeDownloadClient
        {
            Files = [new DownloadFileSnapshot(0, "episode.mkv", 5, 0, 1)],
        };
        await using var app = await RunningApp.StartAsync(downloadClientRegistry: new FakeRegistry(client));
        var taskId = await PrepareTaskAsync(app, [("episode.mkv", 5L, "duplicate")]);

        var result = await app.App.Services.GetRequiredService<DownloadPreparationProcessor>().RunOnceAsync();

        Assert.Equal(DownloadPreparationResult.SkippedDuplicate, result);
        Assert.Single(client.Paused);
        Assert.Empty(client.Resumed);
        var priority = Assert.Single(client.PriorityCalls);
        Assert.Equal([0], priority.Indexes);
        Assert.Equal(0, priority.Priority);
        var deleted = Assert.Single(client.Deleted);
        Assert.False(deleted.DeleteFiles);
        var state = await ReadStateAsync(app, taskId);
        Assert.Equal("download_skipped_duplicate", state.TaskStatus);
        Assert.Equal("skipped_duplicate", state.JobState);
        Assert.Equal("completed", state.PreparationPhase);
    }

    [Fact]
    public async Task ManifestMismatchKeepsTorrentStoppedAndSchedulesSafeRetry()
    {
        var client = new FakeDownloadClient
        {
            Files = [new DownloadFileSnapshot(0, "episode.mkv", 999, 0, 1)],
        };
        await using var app = await RunningApp.StartAsync(downloadClientRegistry: new FakeRegistry(client));
        var taskId = await PrepareTaskAsync(app, [("episode.mkv", 5L, "episode")]);

        var result = await app.App.Services.GetRequiredService<DownloadPreparationProcessor>().RunOnceAsync();

        Assert.Equal(DownloadPreparationResult.RetryScheduled, result);
        Assert.Equal(2, client.Paused.Count);
        Assert.Empty(client.PriorityCalls);
        Assert.Empty(client.Resumed);
        var state = await ReadStateAsync(app, taskId);
        Assert.Equal("metadata_resolved", state.TaskStatus);
        Assert.Equal("pending", state.PreparationPhase);
        Assert.Equal("download_file_manifest_mismatch", state.PreparationFailureCode);
        Assert.All(state.Files, file => Assert.Null(file.Index));
    }

    [Fact]
    public async Task AppliesRenderedCanonicalMetadataTagsBeforeResumeAndAuditsResult()
    {
        var client = new FakeDownloadClient
        {
            Files = [new DownloadFileSnapshot(0, "episode.mkv", 5, 0, 1)],
        };
        await using var app = await RunningApp.StartAsync(downloadClientRegistry: new FakeRegistry(client));
        var taskId = await PrepareTaskAsync(app, [("episode.mkv", 5L, "episode")]);
        await ConfigureDynamicTagFixtureAsync(
            app,
            taskId,
            "{year}年{quarter}月新番,EP{ep},{week_name}",
            new DateOnly(2026, 4, 6),
            3);

        var result = await app.App.Services.GetRequiredService<DownloadPreparationProcessor>().RunOnceAsync();

        Assert.Equal(DownloadPreparationResult.Completed, result);
        var tags = Assert.Single(client.TagCalls);
        Assert.Equal(["2026年4月新番", "EP3", "星期一"], tags.Tags);
        Assert.True(client.Events.IndexOf("tags") < client.Events.IndexOf("resume"));
        var state = await ReadStateAsync(app, taskId);
        Assert.Equal("applied", state.DynamicTagState);
        Assert.Equal(["2026年4月新番", "EP3", "星期一"], state.DynamicTags);
        Assert.Null(state.DynamicTagFailureCode);
        Assert.Equal(1, state.DynamicTagEventCount);
    }

    [Fact]
    public async Task MissingCanonicalAirDateSkipsDynamicTagWithoutBlockingDownload()
    {
        var client = new FakeDownloadClient
        {
            Files = [new DownloadFileSnapshot(0, "episode.mkv", 5, 0, 1)],
        };
        await using var app = await RunningApp.StartAsync(downloadClientRegistry: new FakeRegistry(client));
        var taskId = await PrepareTaskAsync(app, [("episode.mkv", 5L, "episode")]);
        await ConfigureDynamicTagFixtureAsync(app, taskId, "{year}年新番", null, 3);

        var result = await app.App.Services.GetRequiredService<DownloadPreparationProcessor>().RunOnceAsync();

        Assert.Equal(DownloadPreparationResult.Completed, result);
        Assert.Empty(client.TagCalls);
        Assert.Single(client.Resumed);
        var state = await ReadStateAsync(app, taskId);
        Assert.Equal("skipped", state.DynamicTagState);
        Assert.Equal("dynamic_tag_air_date_unavailable", state.DynamicTagFailureCode);
        Assert.Equal(1, state.DynamicTagEventCount);
    }

    [Fact]
    public async Task QbittorrentTagFailureLeavesPreparationRetryableAndStopped()
    {
        var client = new FakeDownloadClient
        {
            Files = [new DownloadFileSnapshot(0, "episode.mkv", 5, 0, 1)],
            AddTagsFailure = new HttpRequestException("fixture"),
        };
        await using var app = await RunningApp.StartAsync(downloadClientRegistry: new FakeRegistry(client));
        var taskId = await PrepareTaskAsync(app, [("episode.mkv", 5L, "episode")]);
        await ConfigureDynamicTagFixtureAsync(
            app,
            taskId,
            "{year}年新番",
            new DateOnly(2026, 4, 6),
            3);

        var result = await app.App.Services.GetRequiredService<DownloadPreparationProcessor>().RunOnceAsync();

        Assert.Equal(DownloadPreparationResult.RetryScheduled, result);
        Assert.Empty(client.Resumed);
        var state = await ReadStateAsync(app, taskId);
        Assert.Equal("pending", state.PreparationPhase);
        Assert.Equal("qbittorrent_http_error", state.PreparationFailureCode);
        Assert.Equal("pending", state.DynamicTagState);
        Assert.Empty(state.DynamicTags);
        Assert.Equal(0, state.DynamicTagEventCount);
    }

    private static async Task<string> PrepareTaskAsync(
        RunningApp app,
        params (string Path, long Size, string Disposition)[] files)
    {
        const string payload = """
            {
              "source": "mikan",
              "data": [{
                "torrent": "https://mikanani.me/passkey/download-preparation.torrent",
                "info": { "title": "Download preparation", "mikanid": 3951, "bgmid": 547888 }
              }]
            }
            """;
        using var response = await app.Client.PostAsync(
            "/api/v1/ingest",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var item = json.RootElement.GetProperty("items")[0];
        var taskId = item.GetProperty("ingest_id").GetString()!;
        var hash = item.GetProperty("info_hash").GetString()!;
        var tasks = app.App.Services.GetRequiredService<IngestTaskStore>();
        var claim = Assert.IsType<ClaimedStagedTorrentRecord>(await tasks.TryClaimNextStagedAsync(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1)));
        await tasks.CompleteDispatchAsync(
            claim,
            new DownloadTaskSnapshot(hash, "Download preparation", DownloadTaskState.Paused, 0, 0, 5, 0, null),
            Path.Combine(app.RootPath, "download", "bt"),
            Path.Combine(app.RootPath, "save"),
            DateTimeOffset.UtcNow);

        var database = app.App.Services.GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using (var delete = connection.CreateCommand())
        {
            delete.CommandText = "DELETE FROM task_files WHERE task_id = $task_id;";
            delete.Parameters.AddWithValue("$task_id", taskId);
            await delete.ExecuteNonQueryAsync();
        }

        foreach (var file in files)
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO task_files (id, task_id, relative_path, size_bytes, disposition)
                VALUES ($id, $task_id, $path, $size, $disposition);
                """;
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            insert.Parameters.AddWithValue("$task_id", taskId);
            insert.Parameters.AddWithValue("$path", file.Path);
            insert.Parameters.AddWithValue("$size", file.Size);
            insert.Parameters.AddWithValue("$disposition", file.Disposition);
            await insert.ExecuteNonQueryAsync();
        }

        await using (var ready = connection.CreateCommand())
        {
            ready.CommandText = "UPDATE ingest_tasks SET status = 'metadata_resolved' WHERE id = $task_id;";
            ready.Parameters.AddWithValue("$task_id", taskId);
            Assert.Equal(1, await ready.ExecuteNonQueryAsync());
        }

        return taskId;
    }

    private static async Task ConfigureDynamicTagFixtureAsync(
        RunningApp app,
        string taskId,
        string template,
        DateOnly? seasonAirDate,
        int episodeNumber)
    {
        var database = app.App.Services.GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        var now = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        await using (var series = connection.CreateCommand())
        {
            series.CommandText = """
                INSERT INTO anime_series (
                    id, tmdb_series_id, canonical_name, needs_tmdb_completion,
                    created_at_utc, updated_at_utc)
                VALUES ('dynamic-tag-series', 900001, 'Dynamic Tag Fixture', 0, $now, $now);

                INSERT INTO anime_seasons (
                    id, series_id, season_number, canonical_name, air_date,
                    created_at_utc, updated_at_utc)
                VALUES (
                    'dynamic-tag-season', 'dynamic-tag-series', 4, 'Season 4',
                    $air_date, $now, $now);
                """;
            series.Parameters.AddWithValue("$now", now);
            series.Parameters.AddWithValue(
                "$air_date",
                seasonAirDate is null
                    ? DBNull.Value
                    : seasonAirDate.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            await series.ExecuteNonQueryAsync();
        }

        await using var task = connection.CreateCommand();
        task.CommandText = """
            UPDATE ingest_tasks
            SET route_snapshot_json = json_set(
                route_snapshot_json, '$.dynamic_tag_template', $template)
            WHERE id = $task_id;

            UPDATE task_files
            SET tmdb_series_id = 900001,
                tmdb_season_number = 4,
                tmdb_episode_number = $episode
            WHERE task_id = $task_id AND disposition = 'episode';
            """;
        task.Parameters.AddWithValue("$template", template);
        task.Parameters.AddWithValue("$task_id", taskId);
        task.Parameters.AddWithValue("$episode", episodeNumber);
        Assert.Equal(2, await task.ExecuteNonQueryAsync());
    }

    private static async Task<PreparationState> ReadStateAsync(RunningApp app, string taskId)
    {
        var database = app.App.Services.GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        string taskStatus;
        string jobState;
        string preparationState;
        string? preparationFailureCode;
        string dynamicTagState;
        string[] dynamicTags;
        string? dynamicTagFailureCode;
        await using (var job = connection.CreateCommand())
        {
            job.CommandText = """
                SELECT task.status, download_jobs.state, download_jobs.preparation_state,
                       download_jobs.preparation_failure_code,
                       download_jobs.dynamic_tag_state,
                       download_jobs.dynamic_tags_json,
                       download_jobs.dynamic_tag_failure_code
                FROM ingest_tasks AS task
                JOIN download_jobs ON download_jobs.task_id = task.id
                WHERE task.id = $task_id;
                """;
            job.Parameters.AddWithValue("$task_id", taskId);
            await using var reader = await job.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            taskStatus = reader.GetString(0);
            jobState = reader.GetString(1);
            preparationState = reader.GetString(2);
            preparationFailureCode = reader.IsDBNull(3) ? null : reader.GetString(3);
            dynamicTagState = reader.GetString(4);
            dynamicTags = JsonSerializer.Deserialize<string[]>(reader.GetString(5)) ?? [];
            dynamicTagFailureCode = reader.IsDBNull(6) ? null : reader.GetString(6);
        }

        var files = new List<FileState>();
        await using (var query = connection.CreateCommand())
        {
            query.CommandText = """
                SELECT download_file_index, download_priority, download_wanted
                FROM task_files WHERE task_id = $task_id ORDER BY relative_path;
                """;
            query.Parameters.AddWithValue("$task_id", taskId);
            await using var reader = await query.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                files.Add(new FileState(
                    reader.IsDBNull(0) ? null : reader.GetInt32(0),
                    reader.IsDBNull(1) ? null : reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : reader.GetInt64(2) != 0));
            }
        }

        await using var eventCount = connection.CreateCommand();
        eventCount.CommandText = """
            SELECT COUNT(*)
            FROM download_job_events AS event
            JOIN download_jobs AS job ON job.id = event.job_id
            WHERE job.task_id = $task_id AND event.kind = 'dynamic_tag';
            """;
        eventCount.Parameters.AddWithValue("$task_id", taskId);
        var dynamicTagEventCount = Convert.ToInt32(
            await eventCount.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture);

        return new PreparationState(
            taskStatus,
            jobState,
            preparationState,
            preparationFailureCode,
            dynamicTagState,
            dynamicTags,
            dynamicTagFailureCode,
            dynamicTagEventCount,
            files);
    }

    private sealed class FakeRegistry(IDownloadClient client) : IDownloadClientRegistry
    {
        public IReadOnlyCollection<string> InstanceIds => ["bt"];

        public IDownloadClient GetRequired(string instanceId) =>
            instanceId == "bt" ? client : throw new KeyNotFoundException();
    }

    private sealed class FakeDownloadClient : IDownloadClient
    {
        public IReadOnlyList<DownloadFileSnapshot> Files { get; init; } = [];

        public List<(int[] Indexes, int Priority)> PriorityCalls { get; } = [];

        public List<(string[] Hashes, string[] Tags)> TagCalls { get; } = [];

        public List<string> Events { get; } = [];

        public Exception? AddTagsFailure { get; init; }

        public List<string> Resumed { get; } = [];

        public List<string> Paused { get; } = [];

        public List<(string[] Hashes, bool DeleteFiles)> Deleted { get; } = [];

        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<DownloadTaskSnapshot>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DownloadTaskSnapshot>>([]);

        public Task AddTorrentAsync(AddTorrentCommand command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DownloadFileSnapshot>> ListFilesAsync(
            string hash,
            CancellationToken cancellationToken = default) => Task.FromResult(Files);

        public Task SetFilePriorityAsync(
            string hash,
            IReadOnlyList<int> fileIndexes,
            int priority,
            CancellationToken cancellationToken = default)
        {
            PriorityCalls.Add((fileIndexes.ToArray(), priority));
            return Task.CompletedTask;
        }

        public Task AddTagsAsync(
            IReadOnlyList<string> hashes,
            IReadOnlyList<string> tags,
            CancellationToken cancellationToken = default)
        {
            TagCalls.Add((hashes.ToArray(), tags.ToArray()));
            Events.Add("tags");
            if (AddTagsFailure is not null)
            {
                throw AddTagsFailure;
            }
            return Task.CompletedTask;
        }

        public Task PauseAsync(IReadOnlyList<string> hashes, CancellationToken cancellationToken = default)
        {
            Paused.AddRange(hashes);
            return Task.CompletedTask;
        }

        public Task ResumeAsync(IReadOnlyList<string> hashes, CancellationToken cancellationToken = default)
        {
            Resumed.AddRange(hashes);
            Events.Add("resume");
            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            IReadOnlyList<string> hashes,
            bool deleteFiles,
            CancellationToken cancellationToken = default)
        {
            Deleted.Add((hashes.ToArray(), deleteFiles));
            return Task.CompletedTask;
        }
    }

    private sealed record FileState(int? Index, int? Priority, bool? Wanted);

    private sealed record PreparationState(
        string TaskStatus,
        string JobState,
        string PreparationPhase,
        string? PreparationFailureCode,
        string DynamicTagState,
        IReadOnlyList<string> DynamicTags,
        string? DynamicTagFailureCode,
        int DynamicTagEventCount,
        IReadOnlyList<FileState> Files);
}
