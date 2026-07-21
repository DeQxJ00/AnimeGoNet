using System.Text;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Core.Library;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Downloads;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Library;
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
        Assert.Equal([4], tmdb.EpisodeRequests);
        var video = files.Single(file => file.Path.EndsWith(".mkv", StringComparison.Ordinal));
        var subtitle = files.Single(file => file.Path.EndsWith(".ass", StringComparison.Ordinal));
        Assert.Null(video.AssociatedFileId);
        Assert.Equal(video.FileId, subtitle.AssociatedFileId);
        Assert.Equal(".zh-Hans.ass", subtitle.RenameSuffix);
        Assert.Equal("metadata_resolved", await ReadTaskStatusAsync(app, taskId));
        Assert.Equal(1, await CountEpisodeClaimsAsync(app, taskId));
    }

    [Fact]
    public async Task CompletedEpisodeIsSkippedWithoutSuppressingAnotherEpisode()
    {
        var tmdb = new FakeTmdbClient
        {
            EpisodeFactory = number => new TmdbEpisode(9000 + number, 72517, 2, number, $"Episode {number}", null),
        };
        await using var app = await StartSeasonResolvedTaskAsync(tmdb, episodeOffset: null);
        var taskId = await PrepareFilesAsync(app, ("Show EP04.mkv", "4", "4"), ("Show EP05.mkv", "5", "5"));
        var completions = app.App.Services.GetRequiredService<CompletionRecordStore>();
        Assert.True(await completions.TryAddAsync(new CompletionRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Episode = new TmdbEpisodeIdentity(72517, 2, 4),
            SourceId = "u2",
            SourceItemId = "completed-elsewhere",
            CompletedAtUtc = DateTimeOffset.UtcNow,
        }));
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services.GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        var files = await ReadFilesAsync(app, taskId);
        Assert.Collection(
            files,
            file =>
            {
                Assert.Equal(4, file.EpisodeNumber);
                Assert.Equal("duplicate", file.Disposition);
                Assert.Equal("episode_already_completed", file.OtherReason);
            },
            file =>
            {
                Assert.Equal(5, file.EpisodeNumber);
                Assert.Equal("episode", file.Disposition);
                Assert.Null(file.OtherReason);
            });
        Assert.Equal(1, await CountEpisodeClaimsAsync(app, taskId));
    }

    [Fact]
    public async Task CompletionFinalizesOwnedEpisodeClaim()
    {
        var tmdb = new FakeTmdbClient
        {
            EpisodeFactory = number => new TmdbEpisode(9000 + number, 72517, 2, number, $"Episode {number}", null),
        };
        await using var app = await StartSeasonResolvedTaskAsync(tmdb, episodeOffset: null);
        var taskId = await PrepareFilesAsync(app, ("Show EP04.mkv", "4", "4"));
        await ResolveSeasonAsync(app);
        Assert.True(await app.App.Services.GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        var file = Assert.Single(await ReadFilesAsync(app, taskId));
        var completions = app.App.Services.GetRequiredService<CompletionRecordStore>();
        Assert.True(await completions.TryAddAsync(new CompletionRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Episode = new TmdbEpisodeIdentity(72517, 2, 4),
            SourceId = "mikan",
            SourceItemId = taskId,
            CompletedAtUtc = DateTimeOffset.UtcNow,
        }));

        Assert.Equal("completed", await ReadEpisodeClaimStateAsync(app, file.FileId));
        Assert.False(await completions.ReleaseClaimAsync(new TmdbEpisodeIdentity(72517, 2, 4), file.FileId));
    }

    [Fact]
    public async Task ActiveClaimFromAnotherTaskSkipsOnlyMatchingEpisode()
    {
        var tmdb = new FakeTmdbClient
        {
            EpisodeFactory = number => new TmdbEpisode(9000 + number, 72517, 2, number, $"Episode {number}", null),
        };
        await using var app = await StartSeasonResolvedTaskAsync(tmdb, episodeOffset: null);
        var ownerTaskId = await PrepareFilesAsync(app, ("Owner EP04.mkv", "4", "4"));
        await ResolveSeasonAsync(app);
        Assert.True(await app.App.Services.GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());
        var competingTaskId = await CloneSeasonResolvedTaskAsync(app, ownerTaskId, "Competing EP04.mkv");

        Assert.True(await app.App.Services.GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        var competingFile = Assert.Single(await ReadFilesAsync(app, competingTaskId));
        Assert.Equal("duplicate", competingFile.Disposition);
        Assert.Equal(4, competingFile.EpisodeNumber);
        Assert.Equal("episode_claimed_by_another_task", competingFile.OtherReason);
        Assert.Equal(1, await CountEpisodeClaimsAsync(app, ownerTaskId));
        Assert.Equal(0, await CountEpisodeClaimsAsync(app, competingTaskId));
    }

    [Fact]
    public async Task FailedOrganizerCanReleaseClaimForAnotherTask()
    {
        var tmdb = new FakeTmdbClient
        {
            EpisodeFactory = number => new TmdbEpisode(9000 + number, 72517, 2, number, $"Episode {number}", null),
        };
        await using var app = await StartSeasonResolvedTaskAsync(tmdb, episodeOffset: null);
        var taskId = await PrepareFilesAsync(app, ("Show EP04.mkv", "4", "4"));
        await ResolveSeasonAsync(app);
        Assert.True(await app.App.Services.GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        var file = Assert.Single(await ReadFilesAsync(app, taskId));
        var completions = app.App.Services.GetRequiredService<CompletionRecordStore>();
        Assert.True(await completions.ReleaseClaimAsync(new TmdbEpisodeIdentity(72517, 2, 4), file.FileId));
        Assert.Equal("released", await ReadEpisodeClaimStateAsync(app, file.FileId));

        var nextTaskId = await CloneSeasonResolvedTaskAsync(app, taskId, "Retry elsewhere EP04.mkv");
        Assert.True(await app.App.Services.GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());
        var nextFile = Assert.Single(await ReadFilesAsync(app, nextTaskId));
        Assert.Equal("episode", nextFile.Disposition);
        Assert.Null(nextFile.OtherReason);
        Assert.Equal("active", await ReadEpisodeClaimStateAsync(app, nextFile.FileId));
        Assert.Equal(0, await CountEpisodeClaimsAsync(app, taskId));
        Assert.Equal(1, await CountEpisodeClaimsAsync(app, nextTaskId));
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
    public async Task OrphanSubtitleGoesToConfirmedSeasonOtherWithoutTmdbRequest()
    {
        var tmdb = new FakeTmdbClient();
        await using var app = await StartSeasonResolvedTaskAsync(tmdb, episodeOffset: null);
        var taskId = await PrepareFilesAsync(app, ("orphan.zh-Hans.ass", "4", "4"));
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services.GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        var file = Assert.Single(await ReadFilesAsync(app, taskId));
        Assert.Equal("other", file.Disposition);
        Assert.Equal("subtitle_unmatched", file.OtherReason);
        Assert.Equal(".ass", file.RenameSuffix);
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
            Path.Combine(app.RootPath, "download", "bt"),
            Path.Combine(app.RootPath, "save"),
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
            SELECT id, relative_path, disposition, tmdb_episode_number, other_reason,
                   associated_task_file_id, rename_suffix
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
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
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

    private static async Task<string> CloneSeasonResolvedTaskAsync(
        RunningApp app,
        string sourceTaskId,
        string relativePath)
    {
        var taskId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        var database = app.App.Services.GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using (var task = connection.CreateCommand())
        {
            task.CommandText = """
                INSERT INTO ingest_tasks (
                    id, source_profile_id, source_profile_revision, source_id, source_item_id,
                    source_work_id, mikanid, groupid, bangumi_subject_id, anidb_id, imdb_id,
                    title, torrent_url_fingerprint, downloader_id, route_snapshot_json,
                    status, failure_kind, failure_reason, created_at_utc, updated_at_utc)
                SELECT $task_id, source_profile_id, source_profile_revision, source_id, $source_item_id,
                       source_work_id, mikanid, groupid, bangumi_subject_id, anidb_id, imdb_id,
                       'Competing task', $fingerprint, downloader_id, route_snapshot_json,
                       'metadata_season_resolved', NULL, NULL, $now, $now
                FROM ingest_tasks WHERE id = $source_task_id;
                """;
            task.Parameters.AddWithValue("$task_id", taskId);
            task.Parameters.AddWithValue("$source_task_id", sourceTaskId);
            task.Parameters.AddWithValue("$source_item_id", $"competing-{taskId}");
            task.Parameters.AddWithValue("$fingerprint", $"competing-{taskId}");
            task.Parameters.AddWithValue("$now", now);
            Assert.Equal(1, await task.ExecuteNonQueryAsync());
        }

        await using (var file = connection.CreateCommand())
        {
            file.CommandText = """
                INSERT INTO task_files (
                    id, task_id, relative_path, size_bytes, source_episode,
                    file_episode_candidate, tmdb_series_id, tmdb_season_number, disposition)
                VALUES ($id, $task_id, $relative_path, 5, '4', '4', 72517, 2, 'pending');
                """;
            file.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            file.Parameters.AddWithValue("$task_id", taskId);
            file.Parameters.AddWithValue("$relative_path", relativePath);
            Assert.Equal(1, await file.ExecuteNonQueryAsync());
        }

        return taskId;
    }

    private static async Task<int> CountEpisodeClaimsAsync(RunningApp app, string taskId)
    {
        var database = app.App.Services.GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM episode_claims AS claim
            JOIN task_files AS file ON file.id = claim.task_file_id
            WHERE file.task_id = $task_id;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<string> ReadEpisodeClaimStateAsync(RunningApp app, string taskFileId)
    {
        var database = app.App.Services.GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT state FROM episode_claims WHERE task_file_id = $task_file_id;";
        command.Parameters.AddWithValue("$task_file_id", taskFileId);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private sealed record FileState(
        string FileId,
        string Path,
        string Disposition,
        int? EpisodeNumber,
        string? OtherReason,
        string? AssociatedFileId,
        string? RenameSuffix);

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
