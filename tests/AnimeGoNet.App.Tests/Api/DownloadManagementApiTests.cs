using System.Net;
using System.Text;
using System.Text.Json;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Data.Downloads;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Api;

public sealed class DownloadManagementApiTests
{
    [Fact]
    public async Task FiltersPagesAndReturnsLiveFilesWithoutAbsolutePaths()
    {
        var client = new FakeDownloadClient
        {
            Files =
            [
                new DownloadFileSnapshot(0, "episode.mkv", 5, 0.6, 1),
            ],
        };
        await using var fixture = await DownloadApiFixture.CreateAsync(client);

        using var response = await fixture.App.Client.GetAsync(
            "/api/v1/downloads?page=1&page_size=10"
            + "&search=Download%20management&state=downloading"
            + "&business_status=downloading"
            + "&downloader_id=bt&source=mikan");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, json.RootElement.GetProperty("total_items").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(
            1,
            json.RootElement.GetProperty("summary").GetProperty("active_jobs").GetInt32());
        Assert.Equal(
            1,
            json.RootElement.GetProperty("summary")
                .GetProperty("connected_download_speed_bytes_per_second")
                .GetInt64());
        Assert.Equal(fixture.JobId, item.GetProperty("job_id").GetString());
        Assert.Equal("not_required", item.GetProperty("seeding_state").GetString());
        Assert.Equal(0, item.GetProperty("seeding_target_minutes").GetInt32());
        Assert.Equal(0, item.GetProperty("seeding_elapsed_seconds").GetInt64());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("seeding_completed_at_utc").ValueKind);
        Assert.Equal("pending", item.GetProperty("dynamic_tag_state").GetString());
        Assert.Empty(item.GetProperty("dynamic_tags").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("dynamic_tag_failure_code").ValueKind);

        using var detailResponse = await fixture.App.Client.GetAsync(
            $"/api/v1/downloads/{fixture.JobId}");
        var body = await detailResponse.Content.ReadAsStringAsync();
        using var detail = JsonDocument.Parse(body);
        var file = Assert.Single(detail.RootElement.GetProperty("files").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.Equal("live", detail.RootElement.GetProperty("file_snapshot_state").GetString());
        Assert.Equal(
            "not_required",
            detail.RootElement.GetProperty("summary").GetProperty("seeding_state").GetString());
        Assert.Equal(
            "pending",
            detail.RootElement.GetProperty("summary").GetProperty("dynamic_tag_state").GetString());
        var preparation = detail.RootElement.GetProperty("preparation");
        Assert.Equal(JsonValueKind.Null, preparation.GetProperty("phase").ValueKind);
        Assert.Equal(JsonValueKind.Null, preparation.GetProperty("progress").ValueKind);
        var organization = detail.RootElement.GetProperty("organization");
        Assert.Equal("not_started", organization.GetProperty("phase").GetString());
        Assert.Equal(0, organization.GetProperty("completed_units").GetInt32());
        Assert.Equal(0, organization.GetProperty("total_units").GetInt32());
        Assert.Equal(0, organization.GetProperty("progress").GetDouble());
        Assert.Equal("episode.mkv", file.GetProperty("relative_path").GetString());
        Assert.Equal(0.6, file.GetProperty("progress").GetDouble());
        Assert.True(file.GetProperty("wanted").GetBoolean());
        Assert.NotEmpty(detail.RootElement.GetProperty("timeline").EnumerateArray());
        Assert.DoesNotContain(fixture.App.RootPath, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-passkey", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PauseAndResumeUseRevisionAndWriteTimeline()
    {
        var client = new FakeDownloadClient();
        await using var fixture = await DownloadApiFixture.CreateAsync(client);
        var revision = await fixture.RevisionAsync();

        using var paused = await PostControlAsync(
            fixture.App.Client,
            fixture.JobId,
            "pause",
            revision);
        using var pausedJson = JsonDocument.Parse(await paused.Content.ReadAsStreamAsync());
        var pausedRevision = pausedJson.RootElement.GetProperty("revision").GetInt64();

        Assert.Equal(HttpStatusCode.OK, paused.StatusCode);
        Assert.Equal("paused", pausedJson.RootElement.GetProperty("state").GetString());
        Assert.Equal([fixture.InfoHash], client.PausedHashes);

        using var conflict = await PostControlAsync(
            fixture.App.Client,
            fixture.JobId,
            "resume",
            revision);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Empty(client.ResumedHashes);

        using var resumed = await PostControlAsync(
            fixture.App.Client,
            fixture.JobId,
            "resume",
            pausedRevision);
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        Assert.Equal([fixture.InfoHash], client.ResumedHashes);

        using var detail = await fixture.App.Client.GetAsync(
            $"/api/v1/downloads/{fixture.JobId}");
        using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStreamAsync());
        var kinds = detailJson.RootElement.GetProperty("timeline")
            .EnumerateArray()
            .Select(item => item.GetProperty("kind").GetString())
            .ToArray();
        Assert.Contains("pause", kinds);
        Assert.Contains("resume", kinds);
    }

    [Fact]
    public async Task BusinessRetryReschedulesPreparationWithoutCallingDownloader()
    {
        var client = new FakeDownloadClient();
        await using var fixture = await DownloadApiFixture.CreateAsync(client);
        await fixture.FailPreparationAsync();
        var revision = await fixture.RevisionAsync();

        using var response = await PostControlAsync(
            fixture.App.Client,
            fixture.JobId,
            "retry",
            revision);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("retry", json.RootElement.GetProperty("action").GetString());
        Assert.Empty(client.ResumedHashes);
        Assert.Null(await fixture.PreparationFailureAsync());
    }

    [Fact]
    public async Task OfflineDetailFallsBackToStoredFileAssignmentAndControlUsesSafeError()
    {
        var client = new FakeDownloadClient
        {
            Failure = new HttpRequestException("private upstream detail"),
        };
        await using var fixture = await DownloadApiFixture.CreateAsync(client);

        using var detail = await fixture.App.Client.GetAsync(
            $"/api/v1/downloads/{fixture.JobId}");
        var detailBody = await detail.Content.ReadAsStringAsync();
        using var detailJson = JsonDocument.Parse(detailBody);

        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(
            "unavailable",
            detailJson.RootElement.GetProperty("file_snapshot_state").GetString());
        Assert.Equal(
            "downloader_unavailable",
            detailJson.RootElement.GetProperty("file_snapshot_failure_code").GetString());
        Assert.DoesNotContain("private upstream detail", detailBody, StringComparison.Ordinal);

        using var pause = await PostControlAsync(
            fixture.App.Client,
            fixture.JobId,
            "pause",
            await fixture.RevisionAsync());
        var pauseBody = await pause.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, pause.StatusCode);
        Assert.Contains("downloader_circuit_open", pauseBody, StringComparison.Ordinal);
        Assert.DoesNotContain("private upstream detail", pauseBody, StringComparison.Ordinal);
    }

    private static Task<HttpResponseMessage> PostControlAsync(
        HttpClient client,
        string jobId,
        string action,
        long revision) =>
        client.PostAsync(
            $"/api/v1/downloads/{jobId}/{action}",
            new StringContent(
                $$"""{"expected_revision":{{revision}}}""",
                Encoding.UTF8,
                "application/json"));

    private sealed class DownloadApiFixture : IAsyncDisposable
    {
        private readonly AnimeGoSqliteDatabase _database;

        private DownloadApiFixture(
            RunningApp app,
            AnimeGoSqliteDatabase database,
            string jobId,
            string infoHash)
        {
            App = app;
            _database = database;
            JobId = jobId;
            InfoHash = infoHash;
        }

        public RunningApp App { get; }

        public string JobId { get; }

        public string InfoHash { get; }

        public static async Task<DownloadApiFixture> CreateAsync(
            FakeDownloadClient client)
        {
            var app = await RunningApp.StartAsync(
                downloadClientRegistry: new FakeRegistry(client));
            const string payload = """
                {
                  "source": "mikan",
                  "data": [{
                    "torrent": "https://mikanani.me/private-passkey/download-management.torrent",
                    "info": {
                      "title": "Download management",
                      "mikanid": 3951,
                      "bgmid": 547888
                    }
                  }]
                }
                """;
            using var ingest = await app.Client.PostAsync(
                "/api/v1/ingest",
                new StringContent(payload, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, ingest.StatusCode);

            var tasks = app.App.Services.GetRequiredService<IngestTaskStore>();
            var now = DateTimeOffset.UtcNow;
            var claim = Assert.IsType<ClaimedStagedTorrentRecord>(
                await tasks.TryClaimNextStagedAsync(now, TimeSpan.FromMinutes(1)));
            await tasks.CompleteDispatchAsync(
                claim,
                new DownloadTaskSnapshot(
                    claim.InfoHash,
                    "Download management",
                    DownloadTaskState.Waiting,
                    0,
                    0,
                    5,
                    0,
                    null),
                app.RootPath,
                app.RootPath,
                now);
            var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
            await using (var connection = await database.OpenConnectionAsync())
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    UPDATE download_jobs
                    SET preparation_state = 'completed'
                    WHERE task_id = $task_id;
                    UPDATE ingest_tasks
                    SET status = 'download_queued'
                    WHERE id = $task_id;
                    """;
                command.Parameters.AddWithValue("$task_id", claim.TaskId);
                Assert.Equal(2, await command.ExecuteNonQueryAsync());
            }

            var jobs = app.App.Services.GetRequiredService<DownloadJobStore>();
            await jobs.ApplyInstanceSnapshotAsync(
                "bt",
                [new DownloadTaskSnapshot(
                    claim.InfoHash,
                    "Download management",
                    DownloadTaskState.Downloading,
                    0.4,
                    2,
                    5,
                    1,
                    3,
                    1,
                    2)],
                now.AddSeconds(1));
            var job = Assert.Single(await jobs.ListAsync());
            return new DownloadApiFixture(app, database, job.JobId, claim.InfoHash);
        }

        public async Task<long> RevisionAsync()
        {
            await using var connection = await _database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT revision FROM download_jobs WHERE id = $job_id;";
            command.Parameters.AddWithValue("$job_id", JobId);
            return Convert.ToInt64(
                await command.ExecuteScalarAsync(),
                System.Globalization.CultureInfo.InvariantCulture);
        }

        public async Task FailPreparationAsync()
        {
            await using var connection = await _database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE download_jobs
                SET preparation_state = 'pending',
                    preparation_failure_code = 'download_files_not_ready',
                    preparation_next_attempt_at_utc = $later,
                    revision = revision + 1
                WHERE id = $job_id;
                """;
            command.Parameters.AddWithValue("$job_id", JobId);
            command.Parameters.AddWithValue(
                "$later",
                DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        public async Task<string?> PreparationFailureAsync()
        {
            await using var connection = await _database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT preparation_failure_code FROM download_jobs WHERE id = $job_id;";
            command.Parameters.AddWithValue("$job_id", JobId);
            return await command.ExecuteScalarAsync() as string;
        }

        public async ValueTask DisposeAsync() => await App.DisposeAsync();
    }

    private sealed class FakeRegistry(IDownloadClient client) : IDownloadClientRegistry
    {
        public IReadOnlyCollection<string> InstanceIds => ["bt"];

        public IDownloadClient GetRequired(string instanceId) =>
            instanceId == "bt"
                ? client
                : throw new KeyNotFoundException(instanceId);
    }

    private sealed class FakeDownloadClient : IDownloadClient
    {
        public IReadOnlyList<DownloadFileSnapshot> Files { get; init; } = [];

        public Exception? Failure { get; init; }

        public List<string> PausedHashes { get; } = [];

        public List<string> ResumedHashes { get; } = [];

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                throw Failure;
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DownloadTaskSnapshot>> ListAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DownloadTaskSnapshot>>([]);

        public Task AddTorrentAsync(
            AddTorrentCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DownloadFileSnapshot>> ListFilesAsync(
            string hash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Files);

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
            Task.CompletedTask;

        public Task PauseAsync(
            IReadOnlyList<string> hashes,
            CancellationToken cancellationToken = default)
        {
            PausedHashes.AddRange(hashes);
            return Task.CompletedTask;
        }

        public Task ResumeAsync(
            IReadOnlyList<string> hashes,
            CancellationToken cancellationToken = default)
        {
            ResumedHashes.AddRange(hashes);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            IReadOnlyList<string> hashes,
            bool deleteFiles,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
