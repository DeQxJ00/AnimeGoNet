using System.Text;
using System.Text.Json;
using AnimeGoNet.App.Deletion;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Data.Deletion;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Deletion;

public sealed class DeleteExecutionProcessorTests
{
    [Fact]
    public async Task ExecutesFourIndependentTargetsWithBusinessRecordLast()
    {
        var client = new FakeDownloadClient();
        await using var app = await RunningApp.StartAsync(downloadClientRegistry: new FakeRegistry(client));
        var prepared = await PreparePlanAsync(app, new DeleteSelection(true, true, true, true));

        var result = await app.App.Services.GetRequiredService<DeleteExecutionProcessor>().RunOnceAsync();

        Assert.Equal(DeleteExecutionResult.Completed, result);
        var deleted = Assert.Single(client.Deleted);
        Assert.False(deleted.DeleteFiles);
        Assert.False(File.Exists(prepared.SourcePath));
        Assert.False(File.Exists(prepared.MediaPath));
        var state = await ReadStateAsync(app, prepared.ExecutionId);
        Assert.Equal("completed", state.ExecutionState);
        Assert.Equal(4, state.CompletedItems);
        Assert.Equal(1, state.CompletionRecords);
        Assert.Equal(1, state.EpisodeClaims);
    }

    [Fact]
    public async Task DownloaderFailureRetriesBeforeFilesOrBusinessRecordAreDeleted()
    {
        var client = new FakeDownloadClient { FailDelete = true };
        await using var app = await RunningApp.StartAsync(downloadClientRegistry: new FakeRegistry(client));
        var prepared = await PreparePlanAsync(app, new DeleteSelection(true, true, true, true));

        var result = await app.App.Services.GetRequiredService<DeleteExecutionProcessor>().RunOnceAsync();

        Assert.Equal(DeleteExecutionResult.RetryScheduled, result);
        Assert.True(File.Exists(prepared.SourcePath));
        Assert.True(File.Exists(prepared.MediaPath));
        var state = await ReadStateAsync(app, prepared.ExecutionId);
        Assert.Equal("pending", state.ExecutionState);
        Assert.Equal("qbittorrent_http_error", state.FailureReason);
        Assert.Equal(2, state.CompletionRecords);
        Assert.Equal(2, state.EpisodeClaims);
    }

    [Fact]
    public async Task TaskRecordIsDeletedLastAfterExplicitSelection()
    {
        var client = new FakeDownloadClient();
        await using var app = await RunningApp.StartAsync(downloadClientRegistry: new FakeRegistry(client));
        var prepared = await PreparePlanAsync(
            app,
            new DeleteSelection(true, true, true, true, DeleteTaskRecord: true));

        var result = await app.App.Services.GetRequiredService<DeleteExecutionProcessor>().RunOnceAsync();

        Assert.Equal(DeleteExecutionResult.Completed, result);
        var state = await ReadStateAsync(app, prepared.ExecutionId);
        Assert.Equal("completed", state.ExecutionState);
        Assert.Equal(5, state.CompletedItems);
        Assert.Equal(1, state.CompletionRecords);
        Assert.Equal(0, state.EpisodeClaims);
        Assert.False(await TaskExistsAsync(app, prepared.TaskId));
    }

    [Fact]
    public async Task FrozenDuplicateSourceTargetIsSkippedAndMediaDeletionCompletes()
    {
        var client = new FakeDownloadClient();
        await using var app = await RunningApp.StartAsync(downloadClientRegistry: new FakeRegistry(client));
        var prepared = await PreparePlanAsync(
            app,
            new DeleteSelection(true, true, true, true, DeleteTaskRecord: true),
            duplicateFrozenSourceTarget: true);

        var result = await app.App.Services.GetRequiredService<DeleteExecutionProcessor>().RunOnceAsync();

        Assert.Equal(DeleteExecutionResult.Completed, result);
        Assert.True(File.Exists(prepared.SourcePath));
        Assert.False(File.Exists(prepared.MediaPath));
        var state = await ReadStateAsync(app, prepared.ExecutionId);
        Assert.Equal("completed", state.ExecutionState);
        Assert.Equal(4, state.CompletedItems);
        Assert.Equal(1, state.SkippedItems);
        Assert.False(await TaskExistsAsync(app, prepared.TaskId));
    }

    private static async Task<PreparedPlan> PreparePlanAsync(
        RunningApp app,
        DeleteSelection selection,
        bool duplicateFrozenSourceTarget = false)
    {
        const string payload = """
            {
              "source": "mikan",
              "data": [{
                "torrent": "https://mikanani.me/passkey/delete-execution.torrent",
                "info": { "title": "Delete execution", "mikanid": 3951, "bgmid": 547888 }
              }]
            }
            """;
        using var response = await app.Client.PostAsync(
            "/api/v1/ingest", new StringContent(payload, Encoding.UTF8, "application/json"));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var taskId = json.RootElement.GetProperty("items")[0].GetProperty("ingest_id").GetString()!;
        var hash = json.RootElement.GetProperty("items")[0].GetProperty("info_hash").GetString()!;
        var tasks = app.App.Services.GetRequiredService<IngestTaskStore>();
        var dispatch = Assert.IsType<ClaimedStagedTorrentRecord>(await tasks.TryClaimNextStagedAsync(
            DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)));
        var paths = AnimeGoDefaults.CreateNative(app.RootPath).Paths;
        var downloadRoot = Path.Combine(paths.DownloadPath, "bt");
        await tasks.CompleteDispatchAsync(
            dispatch,
            new DownloadTaskSnapshot(hash, "Delete execution", DownloadTaskState.Complete, 1, 5, 5, 0, 0),
            downloadRoot, paths.SavePath, DateTimeOffset.UtcNow);

        var sourcePath = Path.Combine(downloadRoot, "episode.mkv");
        var mediaPath = Path.Combine(paths.SavePath, "Series", "S01", "E001.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(mediaPath)!);
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4, 5]);
        await File.WriteAllBytesAsync(mediaPath, [1, 2, 3, 4, 5]);

        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using (var connection = await database.OpenConnectionAsync())
        await using (var setup = connection.CreateCommand())
        {
            setup.CommandText = """
                UPDATE task_files SET disposition = 'episode', tmdb_series_id = 100,
                    tmdb_season_number = 1, tmdb_episode_number = 1, tmdb_episode_id = 1001
                WHERE task_id = $task_id;
                INSERT INTO completion_records (
                    id, tmdb_series_id, tmdb_season_number, tmdb_episode_number,
                    source_id, source_item_id, media_path, completed_at_utc)
                VALUES ('completion-delete', 100, 1, 1, 'mikan', 'delete-execution', $media, $now);
                INSERT INTO completion_records (
                    id, tmdb_series_id, tmdb_season_number, tmdb_episode_number,
                    source_id, source_item_id, media_path, completed_at_utc)
                VALUES ('completion-keep', 100, 1, 2, 'u2', 'keep-other-episode', NULL, $now);
                INSERT INTO episode_claims (
                    id, tmdb_series_id, tmdb_season_number, tmdb_episode_number,
                    task_file_id, state, claimed_at_utc)
                SELECT 'claim-delete', 100, 1, 1, id, 'completed', $now
                FROM task_files WHERE task_id = $task_id;
                INSERT INTO episode_claims (
                    id, tmdb_series_id, tmdb_season_number, tmdb_episode_number,
                    task_file_id, state, claimed_at_utc)
                SELECT 'claim-keep', 100, 1, 2, id, 'completed', $now
                FROM task_files WHERE task_id = $task_id;
                INSERT INTO file_operations (
                    id, task_file_id, strategy, source_path, target_path, state,
                    bytes_verified, created_at_utc, updated_at_utc)
                SELECT 'operation-delete', id, 'move', $source, $media, 'completed', 5, $now, $now
                FROM task_files WHERE task_id = $task_id;
                """;
            setup.Parameters.AddWithValue("$task_id", taskId);
            setup.Parameters.AddWithValue("$source", sourcePath);
            setup.Parameters.AddWithValue("$media", mediaPath);
            setup.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            Assert.Equal(6, await setup.ExecuteNonQueryAsync());
        }

        var plans = app.App.Services.GetRequiredService<DeletePlanStore>();
        var preview = Assert.IsType<DeletePlanPreview>(await plans.GetPreviewAsync(taskId));
        var plan = await plans.CreateAsync(taskId, preview.Fingerprint, selection, DateTimeOffset.UtcNow);
        if (duplicateFrozenSourceTarget)
        {
            await using var connection = await database.OpenConnectionAsync();
            await using var corruptFrozenPlan = connection.CreateCommand();
            corruptFrozenPlan.CommandText = """
                UPDATE delete_execution_items
                SET target_key = $media
                WHERE execution_id = $execution_id AND item_kind = 'source_file';
                """;
            corruptFrozenPlan.Parameters.AddWithValue("$media", mediaPath);
            corruptFrozenPlan.Parameters.AddWithValue("$execution_id", plan.ExecutionId);
            Assert.Equal(1, await corruptFrozenPlan.ExecuteNonQueryAsync());
        }
        return new PreparedPlan(plan.ExecutionId, taskId, sourcePath, mediaPath);
    }

    private static async Task<bool> TaskExistsAsync(RunningApp app, string taskId)
    {
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM ingest_tasks WHERE id = $task_id);";
        command.Parameters.AddWithValue("$task_id", taskId);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<State> ReadStateAsync(RunningApp app, string executionId)
    {
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT state, failure_reason,
                   (SELECT COUNT(*) FROM delete_execution_items
                    WHERE execution_id = execution.id AND state = 'completed'),
                   (SELECT COUNT(*) FROM delete_execution_items
                    WHERE execution_id = execution.id AND state = 'skipped'),
                   (SELECT COUNT(*) FROM completion_records),
                   (SELECT COUNT(*) FROM episode_claims)
            FROM delete_executions AS execution WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", executionId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new State(
            reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5));
    }

    private sealed class FakeRegistry(IDownloadClient client) : IDownloadClientRegistry
    {
        public IReadOnlyCollection<string> InstanceIds => ["bt"];
        public IDownloadClient GetRequired(string instanceId) =>
            instanceId == "bt" ? client : throw new KeyNotFoundException();
    }

    private sealed class FakeDownloadClient : IDownloadClient
    {
        public bool FailDelete { get; init; }
        public List<(string[] Hashes, bool DeleteFiles)> Deleted { get; } = [];
        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DownloadTaskSnapshot>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DownloadTaskSnapshot>>([]);
        public Task AddTorrentAsync(AddTorrentCommand command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadFileSnapshot>> ListFilesAsync(string hash, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DownloadFileSnapshot>>([]);
        public Task SetFilePriorityAsync(string hash, IReadOnlyList<int> fileIndexes, int priority, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddTagsAsync(IReadOnlyList<string> hashes, IReadOnlyList<string> tags, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PauseAsync(IReadOnlyList<string> hashes, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResumeAsync(IReadOnlyList<string> hashes, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(IReadOnlyList<string> hashes, bool deleteFiles, CancellationToken cancellationToken = default)
        {
            Deleted.Add((hashes.ToArray(), deleteFiles));
            return FailDelete
                ? Task.FromException(new HttpRequestException("fake qB failure"))
                : Task.CompletedTask;
        }
    }

    private sealed record PreparedPlan(string ExecutionId, string TaskId, string SourcePath, string MediaPath);
    private sealed record State(
        string ExecutionState,
        string? FailureReason,
        int CompletedItems,
        int SkippedItems,
        int CompletionRecords,
        int EpisodeClaims);
}
