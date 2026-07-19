using AnimeGoNet.App.Downloads;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Data.Downloads;
using AnimeGoNet.Data.Sqlite;

namespace AnimeGoNet.App.Tests.Downloads;

public sealed class DownloadSnapshotSynchronizerTests
{
    [Fact]
    public async Task OneOfflineInstanceDoesNotBlockHealthyInstanceState()
    {
        var root = Path.Combine(Path.GetTempPath(), "animegonet-sync-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var database = new AnimeGoSqliteDatabase(Path.Combine(root, "animegonet.db"));
            await database.InitializeAsync();
            var jobs = new DownloadJobStore(database);
            var registry = new FakeRegistry(new Dictionary<string, IDownloadClient>
            {
                ["bt"] = new FakeClient(),
                ["pt"] = new FakeClient(new HttpRequestException("private qB URL")),
            });
            var synchronizer = new DownloadSnapshotSynchronizer(
                jobs,
                new DownloadClientOperationCoordinator(registry));

            var active = await synchronizer.SyncOnceAsync();

            Assert.Equal(0, active);
            await using var connection = await database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT downloader_id, connected, failure_code
                FROM downloader_runtime_state ORDER BY downloader_id;
                """;
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("bt", reader.GetString(0));
            Assert.Equal(1, reader.GetInt32(1));
            Assert.True(await reader.ReadAsync());
            Assert.Equal("pt", reader.GetString(0));
            Assert.Equal(0, reader.GetInt32(1));
            Assert.Equal("qbittorrent_http_error", reader.GetString(2));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CoordinatorAllowsOnlyOneInFlightOperationPerInstance()
    {
        var coordinator = new DownloadClientOperationCoordinator(
            new FakeRegistry(new Dictionary<string, IDownloadClient> { ["bt"] = new FakeClient() }));
        var inFlight = 0;
        var maximum = 0;

        async Task<int> Operation(IDownloadClient client, CancellationToken cancellationToken)
        {
            _ = client;
            var current = Interlocked.Increment(ref inFlight);
            maximum = Math.Max(maximum, current);
            await Task.Delay(30, cancellationToken);
            Interlocked.Decrement(ref inFlight);
            return current;
        }

        await Task.WhenAll(
            coordinator.ExecuteAsync("bt", Operation),
            coordinator.ExecuteAsync("bt", Operation));

        Assert.Equal(1, maximum);
    }

    private sealed class FakeRegistry(IReadOnlyDictionary<string, IDownloadClient> clients) : IDownloadClientRegistry
    {
        public IReadOnlyCollection<string> InstanceIds => clients.Keys.ToArray();

        public IDownloadClient GetRequired(string instanceId) => clients[instanceId];
    }

    private sealed class FakeClient(Exception? connectFailure = null) : IDownloadClient
    {
        public Task ConnectAsync(CancellationToken cancellationToken = default) =>
            connectFailure is null ? Task.CompletedTask : Task.FromException(connectFailure);

        public Task<IReadOnlyList<DownloadTaskSnapshot>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DownloadTaskSnapshot>>([]);

        public Task AddTorrentAsync(AddTorrentCommand command, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task PauseAsync(IReadOnlyList<string> hashes, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ResumeAsync(IReadOnlyList<string> hashes, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(
            IReadOnlyList<string> hashes,
            bool deleteFiles,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
