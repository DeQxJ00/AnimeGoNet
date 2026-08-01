using AnimeGoNet.App.Downloads;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Torrents;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Sources;
using AnimeGoNet.Data.Sqlite;

namespace AnimeGoNet.App.Tests.Downloads;

public sealed class StagedTorrentDispatcherTests
{
    [Fact]
    public async Task AddsPausedTorrentConfirmsHashAndCommitsDownloadJobBeforeDeletingStage()
    {
        await using var fixture = await DispatchFixture.CreateAsync();
        var client = new FakeDownloadClient(fixture.InfoHash);
        var dispatcher = fixture.CreateDispatcher(client);

        var result = await dispatcher.DispatchNextAsync();

        Assert.Equal(StagedDispatchResult.Completed, result);
        Assert.Equal(1, client.ConnectCalls);
        var command = Assert.Single(client.Added);
        Assert.True(command.StartPaused);
        Assert.Equal(fixture.Options.Downloaders["bt"].DownloadPath, command.SavePath);
        Assert.Equal("animegonet", command.Category);
        Assert.Contains("animegonet", command.Tags);
        Assert.Contains("mikan", command.Tags);
        Assert.Contains("move", command.Tags);
        Assert.Equal(0, command.SeedingTimeMinutes);
        Assert.Equal([fixture.InfoHash], client.PausedHashes);
        Assert.False(File.Exists(fixture.StagingFilePath));
        var state = await fixture.ReadLifecycleAsync();
        Assert.Equal("download_preparing", state.TaskStatus);
        Assert.Equal(0, state.StagedCount);
        Assert.Equal(1, state.DownloadJobCount);
        Assert.Equal(fixture.InfoHash, state.JobHash);
        Assert.Equal(fixture.Options.Downloaders["bt"].DownloadPath, state.DownloadRootPath);
        Assert.Equal(fixture.Options.Paths.SavePath, state.SaveRootPath);
    }

    [Fact]
    public async Task ExistingHashIsConfirmedWithoutAddingDuplicateTorrent()
    {
        await using var fixture = await DispatchFixture.CreateAsync();
        var client = new FakeDownloadClient(fixture.InfoHash, alreadyExists: true);

        var result = await fixture.CreateDispatcher(client).DispatchNextAsync();

        Assert.Equal(StagedDispatchResult.Completed, result);
        Assert.Empty(client.Added);
        Assert.Equal([fixture.InfoHash], client.PausedHashes);
        Assert.Equal(1, (await fixture.ReadLifecycleAsync()).DownloadJobCount);
    }

    [Fact]
    public async Task TransportFailureReleasesLeaseWithSafeRetryCodeAndKeepsStage()
    {
        await using var fixture = await DispatchFixture.CreateAsync();
        var client = new FakeDownloadClient(fixture.InfoHash)
        {
            ConnectFailure = new HttpRequestException(
                "Failed http://admin:private-password@qb.invalid/api/v2/auth/login"),
        };

        var result = await fixture.CreateDispatcher(client).DispatchNextAsync();

        Assert.Equal(StagedDispatchResult.RetryScheduled, result);
        Assert.True(File.Exists(fixture.StagingFilePath));
        var state = await fixture.ReadLifecycleAsync();
        Assert.Equal("staged", state.TaskStatus);
        Assert.Equal("qbittorrent_http_error", state.FailureReason);
        Assert.Equal("ready", state.DispatchState);
        Assert.Equal(1, state.AttemptCount);
        Assert.Equal(0, state.DownloadJobCount);
        Assert.DoesNotContain("private-password", state.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RelativeOrganizationRootRejectsDispatchTransactionAndKeepsStage()
    {
        await using var fixture = await DispatchFixture.CreateAsync();
        var client = new FakeDownloadClient(fixture.InfoHash);
        var invalidOptions = fixture.Options with
        {
            Paths = fixture.Options.Paths with { SavePath = "relative-library" },
        };

        var result = await fixture.CreateDispatcher(client, invalidOptions).DispatchNextAsync();

        Assert.Equal(StagedDispatchResult.RetryScheduled, result);
        Assert.True(File.Exists(fixture.StagingFilePath));
        var state = await fixture.ReadLifecycleAsync();
        Assert.Equal(0, state.DownloadJobCount);
        Assert.Null(state.SaveRootPath);
    }

    private sealed class FakeRegistry(IDownloadClient client) : IDownloadClientRegistry
    {
        public IReadOnlyCollection<string> InstanceIds => ["bt"];

        public IDownloadClient GetRequired(string instanceId) =>
            instanceId == "bt" ? client : throw new KeyNotFoundException();
    }

    private sealed class FakeDownloadClient(string infoHash, bool alreadyExists = false) : IDownloadClient
    {
        private bool _exists = alreadyExists;

        public int ConnectCalls { get; private set; }

        public Exception? ConnectFailure { get; init; }

        public List<AddedCommand> Added { get; } = [];

        public List<string> PausedHashes { get; } = [];

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            ConnectCalls++;
            return ConnectFailure is null ? Task.CompletedTask : Task.FromException(ConnectFailure);
        }

        public Task<IReadOnlyList<DownloadTaskSnapshot>> ListAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<DownloadTaskSnapshot> result = _exists
                ? [Snapshot()]
                : [];
            return Task.FromResult(result);
        }

        public async Task AddTorrentAsync(AddTorrentCommand command, CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await command.Torrent.CopyToAsync(buffer, cancellationToken);
            Added.Add(new AddedCommand(
                command.FileName,
                command.SavePath,
                command.Category,
                command.Tags,
                command.StartPaused,
                command.SeedingTimeMinutes,
                buffer.ToArray()));
            _exists = true;
        }

        public Task<IReadOnlyList<DownloadFileSnapshot>> ListFilesAsync(
            string hash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DownloadFileSnapshot>>([]);

        public Task SetFilePriorityAsync(
            string hash,
            IReadOnlyList<int> fileIndexes,
            int priority,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task AddTagsAsync(
            IReadOnlyList<string> hashes,
            IReadOnlyList<string> tags,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task PauseAsync(IReadOnlyList<string> hashes, CancellationToken cancellationToken = default)
        {
            PausedHashes.AddRange(hashes);
            return Task.CompletedTask;
        }

        public Task ResumeAsync(IReadOnlyList<string> hashes, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(
            IReadOnlyList<string> hashes,
            bool deleteFiles,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        private DownloadTaskSnapshot Snapshot() => new(
            infoHash,
            "Episode",
            DownloadTaskState.Downloading,
            0,
            0,
            5,
            0,
            null);
    }

    private sealed record AddedCommand(
        string FileName,
        string SavePath,
        string? Category,
        IReadOnlyList<string> Tags,
        bool StartPaused,
        int SeedingTimeMinutes,
        byte[] Bytes);

    private sealed class FileStagingService(string stagingPath) : ITorrentStagingService
    {
        public Task<StagedTorrent> StageAsync(
            Uri secretUrl,
            TorrentSourcePolicy sourcePolicy,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(string stagingFileName, CancellationToken cancellationToken = default)
        {
            var path = Path.Combine(stagingPath, stagingFileName);
            var existed = File.Exists(path);
            File.Delete(path);
            return Task.FromResult(existed);
        }

        public FileStream OpenRead(string stagingFileName) => File.OpenRead(Path.Combine(stagingPath, stagingFileName));

        public Task<int> CleanupExpiredAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class DispatchFixture : IAsyncDisposable
    {
        private readonly string _root;
        private readonly AnimeGoSqliteDatabase _database;

        private DispatchFixture(
            string root,
            AnimeGoOptions options,
            AnimeGoSqliteDatabase database,
            IngestTaskStore store,
            string taskId,
            string stagingFilePath,
            string infoHash)
        {
            _root = root;
            Options = options;
            _database = database;
            Store = store;
            TaskId = taskId;
            StagingFilePath = stagingFilePath;
            InfoHash = infoHash;
        }

        public AnimeGoOptions Options { get; }

        public IngestTaskStore Store { get; }

        public string TaskId { get; }

        public string StagingFilePath { get; }

        public string InfoHash { get; }

        public static async Task<DispatchFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "animegonet-dispatch-tests", Guid.NewGuid().ToString("N"));
            var options = AnimeGoDefaults.CreateNative(root);
            var layout = DirectoryLayout.From(options.Paths);
            layout.CreateDataDirectories();
            var database = new AnimeGoSqliteDatabase(layout.DatabaseFile);
            await database.InitializeAsync();
            var profiles = new SourceProfileStore(database);
            await profiles.EnsureSeedsAsync(options.InitialSourceProfiles);
            var profile = Assert.IsType<SourceProfileRecord>(await profiles.GetEnabledAsync("mikan"));
            var normalized = Assert.IsType<NormalizedIngestItem>(IngestCommandNormalizer.Normalize(
                "mikan",
                new IngestItemCommand(
                    "https://mikanani.me/passkey/item.torrent",
                    new IngestItemInfo("Episode", null, "one", "3951", null, null, 3951, 547888, null, null))).Item);
            var infoHash = new string('a', 40);
            var stagingFileName = $"{infoHash}-{Guid.NewGuid():N}.torrent";
            var stagingFilePath = Path.Combine(layout.StagingPath, stagingFileName);
            await File.WriteAllBytesAsync(stagingFilePath, [1, 2, 3, 4]);
            var store = new IngestTaskStore(database);
            var task = await store.AddStagedAsync(
                normalized,
                profile,
                new TorrentMetadata("episode.mkv", infoHash, 5, [new TorrentFile("episode.mkv", 5, false)]),
                stagingFileName,
                DateTimeOffset.UtcNow.AddMinutes(15));
            return new DispatchFixture(root, options, database, store, task.Id, stagingFilePath, infoHash);
        }

        public StagedTorrentDispatcher CreateDispatcher(
            IDownloadClient client,
            AnimeGoOptions? options = null) => new(
            Store,
            new FileStagingService(Path.GetDirectoryName(StagingFilePath)!),
            new DownloadClientOperationCoordinator(new FakeRegistry(client)),
            options ?? Options);

        public async Task<LifecycleState> ReadLifecycleAsync()
        {
            await using var connection = await _database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT ingest_tasks.status, ingest_tasks.failure_reason,
                       COALESCE(staged_torrents.dispatch_state, ''),
                       COALESCE(staged_torrents.attempt_count, 0),
                       (SELECT COUNT(*) FROM staged_torrents WHERE task_id = $task_id),
                       (SELECT COUNT(*) FROM download_jobs WHERE task_id = $task_id),
                       (SELECT info_hash FROM download_jobs WHERE task_id = $task_id),
                       (SELECT download_root_path FROM download_jobs WHERE task_id = $task_id),
                       (SELECT save_root_path FROM download_jobs WHERE task_id = $task_id)
                FROM ingest_tasks
                LEFT JOIN staged_torrents ON staged_torrents.task_id = ingest_tasks.id
                WHERE ingest_tasks.id = $task_id;
                """;
            command.Parameters.AddWithValue("$task_id", TaskId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return new LifecycleState(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8));
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed record LifecycleState(
        string TaskStatus,
        string? FailureReason,
        string DispatchState,
        int AttemptCount,
        int StagedCount,
        int DownloadJobCount,
        string? JobHash,
        string? DownloadRootPath,
        string? SaveRootPath);
}
