using System.Diagnostics;
using System.Net.WebSockets;
using AnimeGoNet.Core.Downloads;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AnimeGoNet.App.Tests.Lifecycle;

public sealed class GracefulShutdownTests
{
    [Fact]
    public async Task HostUsesFiveSecondShutdownDeadline()
    {
        await using var app = await RunningApp.StartAsync();

        var options = app.App.Services
            .GetRequiredService<IOptions<HostOptions>>()
            .Value;

        Assert.Equal(TimeSpan.FromSeconds(5), options.ShutdownTimeout);
    }

    [Fact]
    public async Task StopCancelsActiveDownloaderWorkerAndClosesWebSocket()
    {
        var downloadClient = new BlockingDownloadClient();
        await using var app = await RunningApp.StartAsync(
            downloadClientRegistry: new SingleDownloadClientRegistry(downloadClient),
            startBackgroundWorkers: true);
        await downloadClient.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(WebSocketUri(app), CancellationToken.None);

        var stopwatch = Stopwatch.StartNew();
        await app.App.StopAsync().WaitAsync(TimeSpan.FromSeconds(7));
        stopwatch.Stop();

        Assert.True(downloadClient.CancellationObserved.Task.IsCompletedSuccessfully);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(7),
            $"Host stop took {stopwatch.Elapsed}.");

        var buffer = new byte[64];
        using var receiveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            var receive = await socket.ReceiveAsync(
                buffer.AsMemory(),
                receiveTimeout.Token);
            Assert.Equal(WebSocketMessageType.Close, receive.MessageType);
        }
        catch (WebSocketException)
        {
            Assert.True(socket.State is WebSocketState.Aborted or WebSocketState.Closed);
        }
    }

    private static Uri WebSocketUri(RunningApp app)
    {
        var builder = new UriBuilder(app.Client.BaseAddress!)
        {
            Scheme = "ws",
            Path = "/websocket/log",
        };
        return builder.Uri;
    }

    private sealed class SingleDownloadClientRegistry(
        IDownloadClient client) : IDownloadClientRegistry
    {
        public IReadOnlyCollection<string> InstanceIds { get; } = ["shutdown-test"];

        public IDownloadClient GetRequired(string instanceId) =>
            string.Equals(instanceId, "shutdown-test", StringComparison.Ordinal)
                ? client
                : throw new KeyNotFoundException(instanceId);
    }

    private sealed class BlockingDownloadClient : IDownloadClient
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved.TrySetResult();
                throw;
            }
        }

        public Task<IReadOnlyList<DownloadTaskSnapshot>> ListAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddTorrentAsync(
            AddTorrentCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DownloadFileSnapshot>> ListFilesAsync(
            string hash,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetFilePriorityAsync(
            string hash,
            IReadOnlyList<int> fileIndexes,
            int priority,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddTagsAsync(
            IReadOnlyList<string> hashes,
            IReadOnlyList<string> tags,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task PauseAsync(
            IReadOnlyList<string> hashes,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ResumeAsync(
            IReadOnlyList<string> hashes,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            IReadOnlyList<string> hashes,
            bool deleteFiles,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
