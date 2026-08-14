using System.Net;
using System.Text;
using System.Text.Json;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Data.Deletion;
using AnimeGoNet.Data.Ingest;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Api;

public sealed class DeleteApiTests
{
    [Fact]
    public async Task PreviewConfirmAndStatusExposeAuditableDeletePlan()
    {
        await using var app = await RunningApp.StartAsync();
        var taskId = await PrepareDispatchedTaskAsync(app);

        using var previewResponse = await app.Client.GetAsync($"/api/v1/delete/tasks/{taskId}/preview");
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        using var preview = JsonDocument.Parse(await previewResponse.Content.ReadAsStreamAsync());
        var fingerprint = preview.RootElement.GetProperty("fingerprint").GetString()!;
        Assert.Equal(64, fingerprint.Length);
        Assert.Single(preview.RootElement.GetProperty("downloader_tasks").EnumerateArray());
        Assert.Empty(preview.RootElement.GetProperty("business_records").EnumerateArray());

        using var stale = await app.Client.PostAsync(
            $"/api/v1/delete/tasks/{taskId}",
            Json(new
            {
                fingerprint = new string('0', 64),
                delete_business_record = false,
                delete_downloader_task = true,
                delete_source_files = false,
                delete_media_files = false,
            }));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using var create = await app.Client.PostAsync(
            $"/api/v1/delete/tasks/{taskId}",
            Json(new
            {
                fingerprint,
                delete_business_record = false,
                delete_downloader_task = true,
                delete_source_files = false,
                delete_media_files = false,
            }));
        Assert.Equal(HttpStatusCode.Accepted, create.StatusCode);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStreamAsync());
        var executionId = created.RootElement.GetProperty("execution_id").GetString()!;
        Assert.Equal(1, created.RootElement.GetProperty("selected_target_count").GetInt32());

        using var statusResponse = await app.Client.GetAsync($"/api/v1/delete/executions/{executionId}");
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        using var status = JsonDocument.Parse(await statusResponse.Content.ReadAsStreamAsync());
        Assert.Equal("pending", status.RootElement.GetProperty("state").GetString());
        var item = Assert.Single(status.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("downloader_task", item.GetProperty("kind").GetString());
        Assert.Equal("pending", item.GetProperty("state").GetString());

        using var duplicate = await app.Client.PostAsync(
            $"/api/v1/delete/tasks/{taskId}",
            Json(new
            {
                fingerprint,
                delete_business_record = false,
                delete_downloader_task = true,
                delete_source_files = false,
                delete_media_files = false,
            }));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task UnknownTaskAndEmptySelectionAreRejected()
    {
        await using var app = await RunningApp.StartAsync();
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await app.Client.GetAsync("/api/v1/delete/tasks/missing/preview")).StatusCode);
        var taskId = await PrepareDispatchedTaskAsync(app);
        using var previewResponse = await app.Client.GetAsync($"/api/v1/delete/tasks/{taskId}/preview");
        using var preview = JsonDocument.Parse(await previewResponse.Content.ReadAsStreamAsync());

        using var response = await app.Client.PostAsync(
            $"/api/v1/delete/tasks/{taskId}",
            Json(new
            {
                fingerprint = preview.RootElement.GetProperty("fingerprint").GetString(),
                delete_business_record = false,
                delete_downloader_task = false,
                delete_source_files = false,
                delete_media_files = false,
            }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SynchronousDeleteJoinsActiveExecutionAndReturnsFinalItemStates()
    {
        await using var app = await RunningApp.StartAsync(
            downloadClientRegistry: new DeleteRegistry(new DeleteClient()));
        var taskId = await PrepareDispatchedTaskAsync(app);
        var plans = app.App.Services.GetRequiredService<DeletePlanStore>();
        var preview = Assert.IsType<DeletePlanPreview>(await plans.GetPreviewAsync(taskId));
        var active = await plans.CreateAsync(
            taskId,
            preview.Fingerprint,
            new DeleteSelection(false, true, false, false),
            DateTimeOffset.UtcNow);

        using var response = await app.Client.PostAsync(
            $"/api/v1/delete/tasks/{taskId}/execute",
            Json(new
            {
                fingerprint = preview.Fingerprint,
                delete_business_record = false,
                delete_downloader_task = true,
                delete_source_files = false,
                delete_media_files = false,
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal(active.ExecutionId, result.RootElement.GetProperty("execution_id").GetString());
        Assert.True(result.RootElement.GetProperty("reused_existing_execution").GetBoolean());
        Assert.Equal("completed", result.RootElement.GetProperty("state").GetString());
        Assert.Equal(1, result.RootElement.GetProperty("attempt_count").GetInt32());
        var item = Assert.Single(result.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("completed", item.GetProperty("state").GetString());
    }

    [Fact]
    public async Task StaticWebUiContainsPreviewFirstDeleteCenter()
    {
        await using var app = await RunningApp.StartAsync();
        var html = await app.Client.GetStringAsync("/");
        var script = await app.Client.GetStringAsync("/app.js");

        Assert.Contains("id=\"delete-dialog\"", html, StringComparison.Ordinal);
        Assert.Contains("确认删除并等待结果", html, StringComparison.Ordinal);
        Assert.Contains("openDeletePreview", script, StringComparison.Ordinal);
        Assert.Contains("/execute", script, StringComparison.Ordinal);
        Assert.Contains("正在删除，请等待", script, StringComparison.Ordinal);
        Assert.Contains("重试并等待结果", script, StringComparison.Ordinal);
        Assert.Contains("delete_business_record", script, StringComparison.Ordinal);
        Assert.Contains("delete_downloader_task", script, StringComparison.Ordinal);
        Assert.Contains("delete_source_files", script, StringComparison.Ordinal);
        Assert.Contains("delete_media_files", script, StringComparison.Ordinal);
    }

    private static async Task<string> PrepareDispatchedTaskAsync(RunningApp app)
    {
        const string payload = """
            {
              "source": "mikan",
              "data": [{
                "torrent": "https://mikanani.me/passkey/delete-api.torrent",
                "info": { "title": "Delete API", "mikanid": 3951, "bgmid": 547888 }
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
        await tasks.CompleteDispatchAsync(
            dispatch,
            new DownloadTaskSnapshot(hash, "Delete API", DownloadTaskState.Paused, 0, 0, 5, 0, null),
            Path.Combine(paths.DownloadPath, "bt"), paths.SavePath, DateTimeOffset.UtcNow);
        return taskId;
    }

    private static StringContent Json(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private sealed class DeleteRegistry(IDownloadClient client) : IDownloadClientRegistry
    {
        public IReadOnlyCollection<string> InstanceIds => ["bt"];

        public IDownloadClient GetRequired(string instanceId) =>
            instanceId == "bt" ? client : throw new KeyNotFoundException();
    }

    private sealed class DeleteClient : IDownloadClient
    {
        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<DownloadTaskSnapshot>> ListAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DownloadTaskSnapshot>>([]);

        public Task AddTorrentAsync(
            AddTorrentCommand command,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DownloadFileSnapshot>> ListFilesAsync(
            string hash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DownloadFileSnapshot>>([]);

        public Task SetFilePriorityAsync(
            string hash,
            IReadOnlyList<int> fileIndexes,
            int priority,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task AddTagsAsync(
            IReadOnlyList<string> hashes,
            IReadOnlyList<string> tags,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task PauseAsync(
            IReadOnlyList<string> hashes,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ResumeAsync(
            IReadOnlyList<string> hashes,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(
            IReadOnlyList<string> hashes,
            bool deleteFiles,
            CancellationToken cancellationToken = default)
        {
            Assert.False(deleteFiles);
            return Task.CompletedTask;
        }
    }
}
