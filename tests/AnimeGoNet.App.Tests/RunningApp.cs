using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.Core.Torrents;
using AnimeGoNet.Core.Metadata;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests;

public sealed class RunningApp : IAsyncDisposable
{
    private RunningApp(WebApplication app, HttpClient client, string rootPath)
    {
        App = app;
        Client = client;
        RootPath = rootPath;
    }

    public WebApplication App { get; }

    public HttpClient Client { get; }

    public string RootPath { get; }

    public static async Task<RunningApp> StartAsync(
        string? accessKey = null,
        Func<AnimeGoOptions, AnimeGoOptions>? configure = null,
        ITorrentStagingService? stagingService = null,
        IDownloadClientRegistry? downloadClientRegistry = null,
        ITmdbClient? tmdbClient = null,
        IBangumiSubjectClient? bangumiSubjectClient = null)
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "animegonet-app-tests", Guid.NewGuid().ToString("N"));
        var options = AnimeGoDefaults.CreateNative(rootPath);
        options = configure?.Invoke(options) ?? options;
        stagingService ??= new TestTorrentStagingService(
            DirectoryLayout.From(options.Paths).StagingPath);
        var app = await AnimeGoApplication.BuildAsync(
            [],
            options,
            accessKey,
            torrentStagingService: stagingService,
            downloadClientRegistry: downloadClientRegistry,
            tmdbClient: tmdbClient,
            bangumiSubjectClient: bangumiSubjectClient,
            startBackgroundWorkers: false);
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        var server = app.Services.GetRequiredService<IServer>();
        var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);
        return new RunningApp(app, new HttpClient { BaseAddress = new Uri(address) }, rootPath);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }

    private sealed class TestTorrentStagingService(string stagingPath) : ITorrentStagingService
    {
        private static readonly byte[] TorrentBytes = Encoding.UTF8.GetBytes(
            "d8:announce20:https://secret/token4:infod6:lengthi5e4:name11:episode.mkv12:piece lengthi16384e6:pieces20:aaaaaaaaaaaaaaaaaaaaee");

        public async Task<StagedTorrent> StageAsync(
            Uri secretUrl,
            TorrentSourcePolicy sourcePolicy,
            CancellationToken cancellationToken = default)
        {
            _ = secretUrl;
            _ = sourcePolicy;
            Directory.CreateDirectory(stagingPath);
            var path = Path.Combine(stagingPath, $"test-{Guid.NewGuid():N}.torrent");
            await File.WriteAllBytesAsync(path, TorrentBytes, cancellationToken);
            return new StagedTorrent(path, TorrentMetainfoParser.Parse(TorrentBytes));
        }

        public Task<bool> DeleteAsync(string stagingFileName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(stagingPath, stagingFileName);
            var existed = File.Exists(path);
            File.Delete(path);
            return Task.FromResult(existed);
        }

        public FileStream OpenRead(string stagingFileName) => File.OpenRead(Path.Combine(stagingPath, stagingFileName));

        public Task<int> CleanupExpiredAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(0);
        }
    }
}
