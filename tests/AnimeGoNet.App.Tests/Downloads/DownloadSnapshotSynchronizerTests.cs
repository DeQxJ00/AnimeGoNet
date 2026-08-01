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

    [Fact]
    public async Task OpenCircuitSkipsRepeatedSnapshotNetworkCallsAndPersistsReason()
    {
        var root = Path.Combine(Path.GetTempPath(), "animegonet-sync-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var database = new AnimeGoSqliteDatabase(Path.Combine(root, "animegonet.db"));
            await database.InitializeAsync();
            var jobs = new DownloadJobStore(database);
            var client = new FakeClient(new HttpRequestException("private qB URL"));
            var synchronizer = new DownloadSnapshotSynchronizer(
                jobs,
                new DownloadClientOperationCoordinator(
                    new FakeRegistry(new Dictionary<string, IDownloadClient> { ["bt"] = client }),
                    new MutableTimeProvider(new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero))));

            await synchronizer.SyncOnceAsync();
            await synchronizer.SyncOnceAsync();

            Assert.Equal(1, client.ConnectCount);
            await using var connection = await database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT connected, failure_code
                FROM downloader_runtime_state WHERE downloader_id = 'bt';
                """;
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(0, reader.GetInt32(0));
            Assert.Equal("qbittorrent_circuit_open", reader.GetString(1));
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
    public async Task CoordinatorBackoffIsExponentialAndIsolatedPerInstance()
    {
        var now = new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero);
        var clock = new MutableTimeProvider(now);
        var coordinator = new DownloadClientOperationCoordinator(
            new FakeRegistry(new Dictionary<string, IDownloadClient>
            {
                ["bt"] = new FakeClient(),
                ["pt"] = new FakeClient(),
            }),
            clock);
        var btAttempts = 0;
        var ptAttempts = 0;

        Task<int> FailBt(IDownloadClient _, CancellationToken __)
        {
            btAttempts++;
            return Task.FromException<int>(new HttpRequestException("private endpoint"));
        }

        await Assert.ThrowsAsync<HttpRequestException>(() => coordinator.ExecuteAsync("bt", FailBt));
        var first = coordinator.GetCircuitSnapshot("bt");
        Assert.NotNull(first);
        Assert.Equal(DownloadClientCircuitStatus.Open, first.Status);
        Assert.Equal(1, first.ConsecutiveFailures);
        Assert.Equal(now + TimeSpan.FromSeconds(2), first.RetryAtUtc);

        var open = await Assert.ThrowsAsync<DownloadClientCircuitOpenException>(
            () => coordinator.ExecuteAsync("bt", FailBt));
        Assert.Equal(first.RetryAtUtc, open.RetryAtUtc);
        Assert.Equal(1, btAttempts);

        var ptResult = await coordinator.ExecuteAsync(
            "pt",
            (_, _) =>
            {
                ptAttempts++;
                return Task.FromResult(7);
            });
        Assert.Equal(7, ptResult);
        Assert.Equal(1, ptAttempts);
        Assert.Equal(DownloadClientCircuitStatus.Closed, coordinator.GetCircuitSnapshot("pt")!.Status);

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(DownloadClientCircuitStatus.HalfOpen, coordinator.GetCircuitSnapshot("bt")!.Status);
        await Assert.ThrowsAsync<HttpRequestException>(() => coordinator.ExecuteAsync("bt", FailBt));
        var second = coordinator.GetCircuitSnapshot("bt");
        Assert.Equal(2, second!.ConsecutiveFailures);
        Assert.Equal(clock.GetUtcNow() + TimeSpan.FromSeconds(4), second.RetryAtUtc);
        Assert.Equal(2, btAttempts);

        clock.Advance(TimeSpan.FromSeconds(4));
        var recovered = await coordinator.ExecuteAsync("bt", (_, _) => Task.FromResult(11));
        Assert.Equal(11, recovered);
        var closed = coordinator.GetCircuitSnapshot("bt");
        Assert.Equal(DownloadClientCircuitStatus.Closed, closed!.Status);
        Assert.Equal(0, closed.ConsecutiveFailures);
        Assert.Null(closed.RetryAtUtc);
    }

    [Fact]
    public async Task ExplicitProbeBypassesOpenWindowAndSuccessfulProbeResetsCircuit()
    {
        var coordinator = new DownloadClientOperationCoordinator(
            new FakeRegistry(new Dictionary<string, IDownloadClient> { ["bt"] = new FakeClient() }),
            new MutableTimeProvider(new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero)));

        await Assert.ThrowsAsync<HttpRequestException>(() => coordinator.ExecuteAsync<int>(
            "bt", (_, _) => Task.FromException<int>(new HttpRequestException("offline"))));
        Assert.Equal(DownloadClientCircuitStatus.Open, coordinator.GetCircuitSnapshot("bt")!.Status);

        var result = await coordinator.ExecuteProbeAsync("bt", (_, _) => Task.FromResult(42));

        Assert.Equal(42, result);
        Assert.Equal(DownloadClientCircuitStatus.Closed, coordinator.GetCircuitSnapshot("bt")!.Status);
    }

    [Fact]
    public async Task CallerCancellationDoesNotTripCircuit()
    {
        var coordinator = new DownloadClientOperationCoordinator(
            new FakeRegistry(new Dictionary<string, IDownloadClient> { ["bt"] = new FakeClient() }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.ExecuteAsync<int>(
            "bt",
            (_, token) => Task.FromCanceled<int>(token),
            cancellation.Token));

        Assert.Equal(DownloadClientCircuitStatus.Closed, coordinator.GetCircuitSnapshot("bt")!.Status);
    }

    private sealed class FakeRegistry(IReadOnlyDictionary<string, IDownloadClient> clients) : IDownloadClientRegistry
    {
        public IReadOnlyCollection<string> InstanceIds => clients.Keys.ToArray();

        public IDownloadClient GetRequired(string instanceId) => clients[instanceId];
    }

    private sealed class FakeClient(Exception? connectFailure = null) : IDownloadClient
    {
        public int ConnectCount { get; private set; }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            ConnectCount++;
            return connectFailure is null ? Task.CompletedTask : Task.FromException(connectFailure);
        }

        public Task<IReadOnlyList<DownloadTaskSnapshot>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DownloadTaskSnapshot>>([]);

        public Task AddTorrentAsync(AddTorrentCommand command, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

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

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan value) => _utcNow += value;
    }
}
