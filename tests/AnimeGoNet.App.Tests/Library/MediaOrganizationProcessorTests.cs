using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using AnimeGoNet.App.Library;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Library;

public sealed class MediaOrganizationProcessorTests
{
    [Fact]
    public async Task MoveWritesNfoAndCompletionBeforeSafeDownloaderCleanup()
    {
        var client = new FakeDownloadClient();
        await using var app = await RunningApp.StartAsync(downloadClientRegistry: new FakeRegistry(client));
        var paths = AnimeGoDefaults.CreateNative(app.RootPath).Paths;
        var taskId = await PrepareDownloadedTaskAsync(app, paths);
        var processor = app.App.Services.GetRequiredService<MediaOrganizationProcessor>();

        Assert.Equal(MediaOrganizationResult.FilesCompleted, await processor.RunOnceAsync());

        var target = Path.Combine(paths.SavePath, "Series", "S01", "E001.mkv");
        var nfo = Path.Combine(paths.SavePath, "Series", "tvshow.nfo");
        Assert.True(File.Exists(target));
        Assert.True(File.Exists(nfo));
        var document = XDocument.Load(nfo);
        Assert.Equal("100", document.Root?.Element("tmdbid")?.Value);
        Assert.Equal("547888", document.Root?.Element("bangumiid")?.Value);
        Assert.Empty(client.Deleted);
        var intermediate = await ReadStateAsync(app, taskId);
        Assert.Equal(("organizing_cleanup", "cleanup", 1), intermediate);

        Assert.Equal(MediaOrganizationResult.CleanupCompleted, await processor.RunOnceAsync());

        var deleted = Assert.Single(client.Deleted);
        Assert.False(deleted.DeleteFiles);
        Assert.Equal(("organized", "completed", 1), await ReadStateAsync(app, taskId));
        Assert.False(File.Exists(Path.Combine(paths.DownloadPath, "bt", "episode.mkv")));
        Assert.NotEmpty(client.Paused);
    }

    private static async Task<string> PrepareDownloadedTaskAsync(RunningApp app, PathOptions paths)
    {
        const string payload = """
            {
              "source": "mikan",
              "data": [{
                "torrent": "https://mikanani.me/passkey/organization-worker.torrent",
                "info": { "title": "Organization", "mikanid": 3951, "bgmid": 547888 }
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
        var downloadRoot = Path.Combine(paths.DownloadPath, "bt");
        await tasks.CompleteDispatchAsync(
            dispatch,
            new DownloadTaskSnapshot(hash, "Organization", DownloadTaskState.Complete, 1, 5, 5, 0, null),
            downloadRoot,
            paths.SavePath,
            DateTimeOffset.UtcNow);

        var source = Path.Combine(downloadRoot, "episode.mkv");
        Directory.CreateDirectory(downloadRoot);
        await File.WriteAllBytesAsync(source, [1, 2, 3, 4, 5]);
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var setup = connection.CreateCommand();
        setup.CommandText = """
            INSERT INTO anime_series (
                id, tmdb_series_id, bangumi_subject_id, canonical_name, original_name,
                needs_tmdb_completion, created_at_utc, updated_at_utc)
            VALUES ('series', 100, 547888, 'Series', 'Series', 0, $now, $now);
            UPDATE task_files SET disposition = 'episode', tmdb_series_id = 100,
                tmdb_season_number = 1, tmdb_episode_number = 1,
                tmdb_episode_id = 1001, download_wanted = 1
            WHERE task_id = $task_id;
            UPDATE download_jobs SET preparation_state = 'completed', state = 'complete', progress = 1
            WHERE task_id = $task_id;
            UPDATE ingest_tasks SET status = 'downloaded' WHERE id = $task_id;
            """;
        setup.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        setup.Parameters.AddWithValue("$task_id", taskId);
        Assert.Equal(4, await setup.ExecuteNonQueryAsync());
        return taskId;
    }

    private static async Task<(string Task, string Organization, int Completions)> ReadStateAsync(
        RunningApp app,
        string taskId)
    {
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT task.status, job.organization_state, (SELECT COUNT(*) FROM completion_records)
            FROM ingest_tasks AS task JOIN download_jobs AS job ON job.task_id = task.id
            WHERE task.id = $task_id;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetString(1), reader.GetInt32(2));
    }

    private sealed class FakeRegistry(IDownloadClient client) : IDownloadClientRegistry
    {
        public IReadOnlyCollection<string> InstanceIds => ["bt"];

        public IDownloadClient GetRequired(string instanceId) =>
            instanceId == "bt" ? client : throw new KeyNotFoundException();
    }

    private sealed class FakeDownloadClient : IDownloadClient
    {
        public List<string> Paused { get; } = [];

        public List<(string[] Hashes, bool DeleteFiles)> Deleted { get; } = [];

        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DownloadTaskSnapshot>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DownloadTaskSnapshot>>([]);
        public Task AddTorrentAsync(AddTorrentCommand command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadFileSnapshot>> ListFilesAsync(string hash, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DownloadFileSnapshot>>([]);
        public Task SetFilePriorityAsync(string hash, IReadOnlyList<int> fileIndexes, int priority, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task PauseAsync(IReadOnlyList<string> hashes, CancellationToken cancellationToken = default)
        {
            Paused.AddRange(hashes);
            return Task.CompletedTask;
        }
        public Task ResumeAsync(IReadOnlyList<string> hashes, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(IReadOnlyList<string> hashes, bool deleteFiles, CancellationToken cancellationToken = default)
        {
            Deleted.Add((hashes.ToArray(), deleteFiles));
            return Task.CompletedTask;
        }
    }
}
