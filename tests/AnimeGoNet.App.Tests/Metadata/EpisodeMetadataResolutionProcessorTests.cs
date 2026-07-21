using System.Text;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Downloads;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Mikan;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Metadata;

public sealed class EpisodeMetadataResolutionProcessorTests
{
    private static readonly TmdbSeries Series =
        new(72517, "来自深渊", "メイドインアビス", new DateOnly(2017, 7, 7));
    private static readonly TmdbSeason Season =
        new(204984, 72517, 2, "烈日的黄金乡", new DateOnly(2022, 7, 6), 12);

    [Fact]
    public async Task VideoAndSubtitleWithSameCandidateShareVerifiedTmdbEpisode()
    {
        var tmdb = new FakeTmdbClient
        {
            EpisodeFactory = number => new TmdbEpisode(9000 + number, 72517, 2, number, $"Episode {number}", null),
        };
        await using var app = await StartSeasonResolvedTaskAsync(tmdb, episodeOffset: null);
        var taskId = await PrepareFilesAsync(app, ("Show EP04.mkv", "4", "4"), ("Show EP04.zh-Hans.ass", "4", "4"));
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services.GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        var files = await ReadFilesAsync(app, taskId);
        Assert.Equal(2, files.Length);
        Assert.All(files, file =>
        {
            Assert.Equal("episode", file.Disposition);
            Assert.Equal(4, file.EpisodeNumber);
            Assert.Null(file.OtherReason);
        });
        Assert.Equal([4, 4], tmdb.EpisodeRequests);
        Assert.Equal("metadata_resolved", await ReadTaskStatusAsync(app, taskId));
    }

    [Theory]
    [InlineData("Show [48.5].mkv", "48.5", "fractional_episode")]
    [InlineData("Show [SP01].mkv", "sp01", "special_episode")]
    [InlineData("poster.jpg", null, "episode_not_parsed")]
    public async Task NonIntegerOrUnknownFileGoesToOtherWithoutTmdbRequest(
        string path,
        string? sourceEpisode,
        string expectedReason)
    {
        var tmdb = new FakeTmdbClient();
        await using var app = await StartSeasonResolvedTaskAsync(tmdb, episodeOffset: null);
        var taskId = await PrepareFilesAsync(app, (path, sourceEpisode, null));
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services.GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        var file = Assert.Single(await ReadFilesAsync(app, taskId));
        Assert.Equal("other", file.Disposition);
        Assert.Null(file.EpisodeNumber);
        Assert.Equal(expectedReason, file.OtherReason);
        Assert.Empty(tmdb.EpisodeRequests);
    }

    [Fact]
    public async Task ManualEpisodeOffsetIsAppliedBeforeOfficialValidation()
    {
        var tmdb = new FakeTmdbClient
        {
            EpisodeFactory = number => number == 13
                ? new TmdbEpisode(9013, 72517, 2, 13, "Episode 13", null)
                : null,
        };
        await using var app = await StartSeasonResolvedTaskAsync(tmdb, episodeOffset: 12);
        var taskId = await PrepareFilesAsync(app, ("Show EP01.mkv", "1", "1"));
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services.GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        var file = Assert.Single(await ReadFilesAsync(app, taskId));
        Assert.Equal(13, file.EpisodeNumber);
        Assert.Equal([13], tmdb.EpisodeRequests);
    }

    [Fact]
    public async Task MissingAutomaticTmdbEpisodeGoesToOtherInConfirmedSeason()
    {
        var tmdb = new FakeTmdbClient { EpisodeFactory = _ => null };
        await using var app = await StartSeasonResolvedTaskAsync(tmdb, episodeOffset: null);
        var taskId = await PrepareFilesAsync(app, ("Show EP12.mkv", "12", "12"));
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services.GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        var file = Assert.Single(await ReadFilesAsync(app, taskId));
        Assert.Equal("other", file.Disposition);
        Assert.Equal("tmdb_episode_not_found", file.OtherReason);
        Assert.Equal("metadata_resolved", await ReadTaskStatusAsync(app, taskId));
    }

    [Fact]
    public async Task TmdbEpisodeNetworkFailureIsRetryableAndLeavesFilesPending()
    {
        var tmdb = new FakeTmdbClient
        {
            EpisodeFailure = new TmdbClientException(
                MetadataFailureKind.Network,
                "tmdb_network_error",
                tmdbAccessConfirmed: false),
        };
        await using var app = await StartSeasonResolvedTaskAsync(tmdb, episodeOffset: null);
        var taskId = await PrepareFilesAsync(app, ("Show EP04.mkv", "4", "4"));
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services.GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        Assert.Equal("metadata_failed", await ReadTaskStatusAsync(app, taskId));
        var file = Assert.Single(await ReadFilesAsync(app, taskId));
        Assert.Equal("pending", file.Disposition);
        var database = app.App.Services.GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT retryable, error_code
            FROM metadata_resolution_attempts
            WHERE run_id = (SELECT id FROM metadata_resolution_runs WHERE task_id = $task_id ORDER BY attempt_number DESC LIMIT 1);
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt64(0));
        Assert.Equal("tmdb_network_error", reader.GetString(1));
    }

    [Fact]
    public async Task InvalidManualOffsetTargetFailsInsteadOfFallingBackToOther()
    {
        var tmdb = new FakeTmdbClient { EpisodeFactory = _ => null };
        await using var app = await StartSeasonResolvedTaskAsync(tmdb, episodeOffset: 12);
        var taskId = await PrepareFilesAsync(app, ("Show EP01.mkv", "1", "1"));
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services.GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        Assert.Equal("metadata_failed", await ReadTaskStatusAsync(app, taskId));
        var file = Assert.Single(await ReadFilesAsync(app, taskId));
        Assert.Equal("pending", file.Disposition);
        var run = Assert.IsType<MetadataRunProjection>(await app.App.Services
            .GetRequiredService<MetadataResolutionStore>().GetLatestAsync(taskId));
        Assert.Equal(MetadataFailureKind.SemanticNoMatch, run.FailureKind);
        Assert.Equal([13], tmdb.EpisodeRequests);
    }

    private static async Task<RunningApp> StartSeasonResolvedTaskAsync(FakeTmdbClient tmdb, int? episodeOffset)
    {
        var app = await RunningApp.StartAsync(tmdbClient: tmdb);
        await app.App.Services.GetRequiredService<MikanWorkMetadataRuleStore>().SaveAsync(
            new MikanWorkMetadataRuleUpdate(3951, 547888, 72517, 2, episodeOffset),
            expectedRevision: 0,
            DateTimeOffset.UtcNow);
        return app;
    }

    private static async Task<string> PrepareFilesAsync(
        RunningApp app,
        params (string Path, string? SourceEpisode, string? Candidate)[] files)
    {
        const string payload = """
            {
              "source": "mikan",
              "data": [{
                "torrent": "https://mikanani.me/passkey/episode-resolution.torrent",
                "info": { "title": "Episode resolution", "mikanid": 3951, "bgmid": 547888 }
              }]
            }
            """;
        using var response = await app.Client.PostAsync(
            "/api/v1/ingest",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var item = json.RootElement.GetProperty("items")[0];
        var taskId = item.GetProperty("ingest_id").GetString()!;
        var hash = item.GetProperty("info_hash").GetString()!;
        var tasks = app.App.Services.GetRequiredService<IngestTaskStore>();
        var dispatch = Assert.IsType<ClaimedStagedTorrentRecord>(await tasks.TryClaimNextStagedAsync(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1)));
        await tasks.CompleteDispatchAsync(
            dispatch,
            new DownloadTaskSnapshot(hash, "Episode resolution", DownloadTaskState.Waiting, 0, 0, 5, 0, null),
            DateTimeOffset.UtcNow);
        await app.App.Services.GetRequiredService<DownloadJobStore>().ApplyInstanceSnapshotAsync(
            "bt",
            [new DownloadTaskSnapshot(hash, "Episode resolution", DownloadTaskState.Complete, 1, 5, 5, 0, 0)],
            DateTimeOffset.UtcNow);

        var database = app.App.Services.GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using (var delete = connection.CreateCommand())
        {
            delete.CommandText = "DELETE FROM task_files WHERE task_id = $task_id;";
            delete.Parameters.AddWithValue("$task_id", taskId);
            await delete.ExecuteNonQueryAsync();
        }

        foreach (var file in files)
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO task_files (
                    id, task_id, relative_path, size_bytes, source_episode,
                    file_episode_candidate, disposition)
                VALUES ($id, $task_id, $path, 5, $source_episode, $candidate, 'pending');
                """;
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            insert.Parameters.AddWithValue("$task_id", taskId);
            insert.Parameters.AddWithValue("$path", file.Path);
            insert.Parameters.AddWithValue("$source_episode", (object?)file.SourceEpisode ?? DBNull.Value);
            insert.Parameters.AddWithValue("$candidate", (object?)file.Candidate ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync();
        }

        return taskId;
    }

    private static async Task ResolveSeasonAsync(RunningApp app) =>
        Assert.True(await app.App.Services.GetRequiredService<ManualMetadataResolutionProcessor>().RunOnceAsync());

    private static async Task<FileState[]> ReadFilesAsync(RunningApp app, string taskId)
    {
        var database = app.App.Services.GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT relative_path, disposition, tmdb_episode_number, other_reason
            FROM task_files WHERE task_id = $task_id ORDER BY relative_path;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        var values = new List<FileState>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(new FileState(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return values.ToArray();
    }

    private static async Task<string> ReadTaskStatusAsync(RunningApp app, string taskId)
    {
        var database = app.App.Services.GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT status FROM ingest_tasks WHERE id = $task_id;";
        command.Parameters.AddWithValue("$task_id", taskId);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private sealed record FileState(string Path, string Disposition, int? EpisodeNumber, string? OtherReason);

    private sealed class FakeTmdbClient : ITmdbClient
    {
        public Func<int, TmdbEpisode?> EpisodeFactory { get; init; } = _ => null;

        public TmdbClientException? EpisodeFailure { get; init; }

        public List<int> EpisodeRequests { get; } = [];

        public Task<IReadOnlyList<TmdbSeries>> SearchSeriesAsync(string title, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TmdbSeries>>([Series]);

        public Task<TmdbSeries?> GetSeriesAsync(int seriesId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeries?>(Series);

        public Task<TmdbSeriesDetails?> GetSeriesDetailsAsync(int seriesId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeriesDetails?>(new TmdbSeriesDetails(Series, [Season]));

        public Task<TmdbSeason?> GetSeasonAsync(int seriesId, int seasonNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeason?>(Season);

        public Task<TmdbEpisode?> GetEpisodeAsync(
            int seriesId,
            int seasonNumber,
            int episodeNumber,
            CancellationToken cancellationToken = default)
        {
            EpisodeRequests.Add(episodeNumber);
            return EpisodeFailure is null
                ? Task.FromResult(EpisodeFactory(episodeNumber))
                : Task.FromException<TmdbEpisode?>(EpisodeFailure);
        }
    }
}
