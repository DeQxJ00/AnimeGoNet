using System.Text;
using System.Text.Json;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Downloads;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Mikan;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Metadata;

public sealed class ManualMetadataResolutionProcessorTests
{
    [Fact]
    public async Task CompleteManualOverrideIsValidatedAndWinsBeforeTitleSearch()
    {
        var tmdb = new FakeTmdbClient
        {
            Series = new TmdbSeries(72517, "来自深渊", "メイドインアビス", new DateOnly(2017, 7, 7)),
            Season = new TmdbSeason(204984, 72517, 2, "烈日的黄金乡", new DateOnly(2022, 7, 6), 12),
        };
        await using var app = await RunningApp.StartAsync(tmdbClient: tmdb);
        await AddManualRuleAsync(app);
        var taskId = await AddDownloadedTaskAsync(app);

        Assert.True(await app.App.Services.GetRequiredService<ManualMetadataResolutionProcessor>().RunOnceAsync());

        var run = Assert.IsType<MetadataRunProjection>(await app.App.Services
            .GetRequiredService<MetadataResolutionStore>()
            .GetLatestAsync(taskId));
        Assert.Equal("season_resolved", run.Status);
        Assert.Equal(72517, run.TmdbSeriesId);
        Assert.Equal(2, run.TmdbSeasonNumber);
        Assert.True(run.TmdbAccessConfirmed);
        Assert.Equal(0, tmdb.SearchCalls);
        Assert.Equal(1, tmdb.SeriesCalls);
        Assert.Equal(1, tmdb.SeasonCalls);
    }

    [Fact]
    public async Task InvalidManualOverrideBlocksAutomaticMatching()
    {
        var tmdb = new FakeTmdbClient { Series = null };
        await using var app = await RunningApp.StartAsync(tmdbClient: tmdb);
        await AddManualRuleAsync(app);
        var taskId = await AddDownloadedTaskAsync(app);

        Assert.True(await app.App.Services.GetRequiredService<ManualMetadataResolutionProcessor>().RunOnceAsync());

        var run = Assert.IsType<MetadataRunProjection>(await app.App.Services
            .GetRequiredService<MetadataResolutionStore>()
            .GetLatestAsync(taskId));
        Assert.Equal("failed", run.Status);
        Assert.Equal(MetadataFailureKind.SemanticNoMatch, run.FailureKind);
        Assert.True(run.TmdbAccessConfirmed);
        Assert.False(run.FallbackEligible);
        Assert.Equal("manual_override_active", run.FallbackDenialReason);
        Assert.Equal(0, tmdb.SearchCalls);
    }

    [Fact]
    public async Task NetworkFailureIsAuditedAsRetryableWithoutFallback()
    {
        var tmdb = new FakeTmdbClient
        {
            SeriesFailure = new TmdbClientException(
                MetadataFailureKind.Network,
                "tmdb_network_error",
                tmdbAccessConfirmed: false),
        };
        await using var app = await RunningApp.StartAsync(tmdbClient: tmdb);
        await AddManualRuleAsync(app);
        var taskId = await AddDownloadedTaskAsync(app);

        Assert.True(await app.App.Services.GetRequiredService<ManualMetadataResolutionProcessor>().RunOnceAsync());

        var database = app.App.Services.GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT run.failure_kind, run.fallback_eligible, attempt.retryable,
                   attempt.strategy, attempt.priority, attempt.error_code
            FROM metadata_resolution_runs AS run
            JOIN metadata_resolution_attempts AS attempt ON attempt.run_id = run.id
            WHERE run.task_id = $task_id;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("Network", reader.GetString(0));
        Assert.Equal(0, reader.GetInt64(1));
        Assert.Equal(1, reader.GetInt64(2));
        Assert.Equal("manual_mikan_override", reader.GetString(3));
        Assert.Equal(ManualMetadataResolutionProcessor.ManualOverridePriority, reader.GetInt32(4));
        Assert.Equal("tmdb_network_error", reader.GetString(5));
    }

    [Fact]
    public async Task TaskWithoutCompleteManualOverrideRemainsForAutomaticPipeline()
    {
        await using var app = await RunningApp.StartAsync(tmdbClient: new FakeTmdbClient());
        var taskId = await AddDownloadedTaskAsync(app);

        Assert.False(await app.App.Services.GetRequiredService<ManualMetadataResolutionProcessor>().RunOnceAsync());
        Assert.Null(await app.App.Services.GetRequiredService<MetadataResolutionStore>().GetLatestAsync(taskId));

        var database = app.App.Services.GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT status FROM ingest_tasks WHERE id = $task_id;";
        command.Parameters.AddWithValue("$task_id", taskId);
        Assert.Equal("downloaded", await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task DisablingInvalidManualOverrideThenRetryReturnsTaskToAutomaticQueue()
    {
        await using var app = await RunningApp.StartAsync(tmdbClient: new FakeTmdbClient { Series = null });
        await AddManualRuleAsync(app);
        var taskId = await AddDownloadedTaskAsync(app);
        var processor = app.App.Services.GetRequiredService<ManualMetadataResolutionProcessor>();
        Assert.True(await processor.RunOnceAsync());

        var rules = app.App.Services.GetRequiredService<MikanWorkMetadataRuleStore>();
        var current = Assert.IsType<MikanWorkMetadataRule>(await rules.GetAsync(3951));
        await rules.SaveAsync(
            new MikanWorkMetadataRuleUpdate(
                current.MikanId,
                current.BangumiSubjectId,
                current.TmdbSeriesId,
                current.TmdbSeasonNumber,
                current.EpisodeOffset,
                Enabled: false),
            current.Revision,
            DateTimeOffset.UtcNow);

        using var response = await app.Client.PostAsync($"/api/v1/metadata/tasks/{taskId}/retry", null);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal(taskId, json.RootElement.GetProperty("task_id").GetString());
        Assert.Equal("downloaded", json.RootElement.GetProperty("status").GetString());
        Assert.False(await processor.RunOnceAsync());

        var database = app.App.Services.GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT status, failure_kind, failure_reason FROM ingest_tasks WHERE id = $task_id;";
        command.Parameters.AddWithValue("$task_id", taskId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("downloaded", reader.GetString(0));
        Assert.True(reader.IsDBNull(1));
        Assert.True(reader.IsDBNull(2));
    }

    private static async Task AddManualRuleAsync(RunningApp app)
    {
        await app.App.Services.GetRequiredService<MikanWorkMetadataRuleStore>().SaveAsync(
            new MikanWorkMetadataRuleUpdate(3951, 547888, 72517, 2, null),
            expectedRevision: 0,
            DateTimeOffset.UtcNow);
    }

    private static async Task<string> AddDownloadedTaskAsync(RunningApp app)
    {
        const string payload = """
            {
              "source": "mikan",
              "data": [{
                "torrent": "https://mikanani.me/passkey/manual-metadata.torrent",
                "info": { "title": "Manual metadata", "mikanid": 3951, "bgmid": 547888 }
              }]
            }
            """;
        using var response = await app.Client.PostAsync(
            "/api/v1/ingest",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var item = json.RootElement.GetProperty("items")[0];
        var taskId = item.GetProperty("ingest_id").GetString()!;
        var hash = item.GetProperty("info_hash").GetString()!;
        var tasks = app.App.Services.GetRequiredService<IngestTaskStore>();
        var claim = Assert.IsType<ClaimedStagedTorrentRecord>(await tasks.TryClaimNextStagedAsync(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1)));
        await tasks.CompleteDispatchAsync(
            claim,
            new DownloadTaskSnapshot(hash, "Manual metadata", DownloadTaskState.Waiting, 0, 0, 5, 0, null),
            DateTimeOffset.UtcNow);
        await app.App.Services.GetRequiredService<DownloadJobStore>().ApplyInstanceSnapshotAsync(
            "bt",
            [new DownloadTaskSnapshot(hash, "Manual metadata", DownloadTaskState.Complete, 1, 5, 5, 0, 0)],
            DateTimeOffset.UtcNow);
        return taskId;
    }

    private sealed class FakeTmdbClient : ITmdbClient
    {
        public TmdbSeries? Series { get; init; }

        public TmdbSeason? Season { get; init; }

        public TmdbClientException? SeriesFailure { get; init; }

        public int SearchCalls { get; private set; }

        public int SeriesCalls { get; private set; }

        public int SeasonCalls { get; private set; }

        public Task<IReadOnlyList<TmdbSeries>> SearchSeriesAsync(
            string title,
            CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            return Task.FromResult<IReadOnlyList<TmdbSeries>>(
                Series is null ? [] : [Series]);
        }

        public Task<TmdbSeries?> GetSeriesAsync(
            int seriesId,
            CancellationToken cancellationToken = default)
        {
            SeriesCalls++;
            return SeriesFailure is null
                ? Task.FromResult(Series)
                : Task.FromException<TmdbSeries?>(SeriesFailure);
        }

        public Task<TmdbSeriesDetails?> GetSeriesDetailsAsync(
            int seriesId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Series is null ? null : new TmdbSeriesDetails(Series, Season is null ? [] : [Season]));

        public Task<TmdbSeason?> GetSeasonAsync(
            int seriesId,
            int seasonNumber,
            CancellationToken cancellationToken = default)
        {
            SeasonCalls++;
            return Task.FromResult(Season);
        }

        public Task<TmdbEpisode?> GetEpisodeAsync(
            int seriesId,
            int seasonNumber,
            int episodeNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbEpisode?>(null);
    }
}
