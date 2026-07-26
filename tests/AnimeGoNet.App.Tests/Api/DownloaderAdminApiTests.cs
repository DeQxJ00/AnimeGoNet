using System.Net;
using System.Text.Json;
using AnimeGoNet.Core.Downloads;

namespace AnimeGoNet.App.Tests.Api;

public sealed class DownloaderAdminApiTests
{
    [Fact]
    public async Task ListProjectsConfiguredInstancesUsageAndNeverCredentials()
    {
        await using var app = await RunningApp.StartAsync(configure: options => options with
        {
            Downloaders = options.Downloaders.ToDictionary(
                pair => pair.Key,
                pair => pair.Value with { Username = "admin", Password = "private-password" }),
        });

        using var response = await app.Client.GetAsync("/api/v1/downloaders");
        var text = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(text);
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, items.Length);
        var bt = Assert.Single(items, item => item.GetProperty("id").GetString() == "bt");
        Assert.Equal("qbittorrent", bt.GetProperty("type").GetString());
        Assert.True(bt.GetProperty("credentials_configured").GetBoolean());
        Assert.Equal(1, bt.GetProperty("source_profile_count").GetInt64());
        Assert.Equal(0, bt.GetProperty("download_job_count").GetInt64());
        Assert.Equal(JsonValueKind.Null, bt.GetProperty("connected").ValueKind);
        Assert.DoesNotContain("private-password", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"username\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectionTestAuthenticatesListsAndPersistsHealth()
    {
        var client = new FakeDownloadClient();
        await using var app = await RunningApp.StartAsync(
            downloadClientRegistry: new FakeRegistry(client));

        using var test = await app.Client.PostAsync("/api/v1/downloaders/bt/test", null);
        using var tested = JsonDocument.Parse(await test.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, test.StatusCode);
        Assert.True(tested.RootElement.GetProperty("connected").GetBoolean());
        Assert.Equal(1, tested.RootElement.GetProperty("task_count").GetInt32());
        Assert.Equal(1, client.ConnectCount);
        Assert.Equal(1, client.ListCount);

        using var list = await app.Client.GetAsync("/api/v1/downloaders");
        using var listed = JsonDocument.Parse(await list.Content.ReadAsStreamAsync());
        var bt = Assert.Single(
            listed.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "bt");
        Assert.True(bt.GetProperty("connected").GetBoolean());
        Assert.Equal(JsonValueKind.String, bt.GetProperty("last_success_at_utc").ValueKind);
    }

    [Fact]
    public async Task AuthenticationFailureIsSafeAndUnknownInstanceIsNotFound()
    {
        await using var app = await RunningApp.StartAsync(
            downloadClientRegistry: new FakeRegistry(new FakeDownloadClient(failAuthentication: true)));

        using var failed = await app.Client.PostAsync("/api/v1/downloaders/bt/test", null);
        var text = await failed.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(text);
        Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
        Assert.False(json.RootElement.GetProperty("connected").GetBoolean());
        Assert.Equal("authentication_failed", json.RootElement.GetProperty("failure_code").GetString());
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);

        using var missing = await app.Client.PostAsync("/api/v1/downloaders/missing/test", null);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task StaticWebUiContainsMultiInstanceHealthAndConnectionTestPanel()
    {
        await using var app = await RunningApp.StartAsync();
        var html = await app.Client.GetStringAsync("/");
        var script = await app.Client.GetStringAsync("/app.js");

        Assert.Contains("id=\"downloader-list\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"downloader-reload\"", html, StringComparison.Ordinal);
        Assert.Contains("loadDownloaders", script, StringComparison.Ordinal);
        Assert.Contains("testDownloader", script, StringComparison.Ordinal);
        Assert.Contains("/api/v1/downloaders/", script, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
    }

    private sealed class FakeRegistry(IDownloadClient client) : IDownloadClientRegistry
    {
        public IReadOnlyCollection<string> InstanceIds => ["bt", "pt"];

        public IDownloadClient GetRequired(string instanceId) =>
            instanceId is "bt" or "pt" ? client : throw new KeyNotFoundException();
    }

    private sealed class FakeDownloadClient(bool failAuthentication = false) : IDownloadClient
    {
        public int ConnectCount { get; private set; }
        public int ListCount { get; private set; }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectCount++;
            return failAuthentication
                ? Task.FromException(new InvalidOperationException("secret"))
                : Task.CompletedTask;
        }

        public Task<IReadOnlyList<DownloadTaskSnapshot>> ListAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ListCount++;
            IReadOnlyList<DownloadTaskSnapshot> tasks =
            [
                new(new string('a', 40), "fixture", DownloadTaskState.Paused, 0, 0, 1, 0, null),
            ];
            return Task.FromResult(tasks);
        }

        public Task AddTorrentAsync(AddTorrentCommand command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadFileSnapshot>> ListFilesAsync(
            string hash, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetFilePriorityAsync(
            string hash, IReadOnlyList<int> fileIndexes, int priority,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task PauseAsync(
            IReadOnlyList<string> hashes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ResumeAsync(
            IReadOnlyList<string> hashes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(
            IReadOnlyList<string> hashes, bool deleteFiles,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
