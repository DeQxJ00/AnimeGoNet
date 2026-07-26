using System.Text;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Downloads;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Mikan;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Metadata;

public sealed class AutomaticMetadataResolutionProcessorTests
{
    private static readonly TmdbSeries Series =
        new(72517, "来自深渊", "メイドインアビス", new DateOnly(2017, 7, 7));
    private static readonly TmdbSeason SeasonOne =
        new(100, 72517, 1, "Season 1", new DateOnly(2017, 7, 7), 13);
    private static readonly TmdbSeason SeasonTwo =
        new(200, 72517, 2, "Season 2", new DateOnly(2022, 7, 6), 12);

    [Fact]
    public async Task BangumiAirDateSelectsCanonicalTmdbSeason()
    {
        var tmdb = new FakeTmdbClient(Series, [SeasonOne, SeasonTwo]);
        var bangumi = new FakeBangumiClient(new BangumiSubject(
            547888, "メイドインアビス 烈日の黄金郷", "来自深渊 烈日的黄金乡", new DateOnly(2022, 7, 6), 12));
        await using var app = await RunningApp.StartAsync(tmdbClient: tmdb, bangumiSubjectClient: bangumi);
        var taskId = await AddDownloadedTaskAsync(app, "来自深渊 第二季");

        Assert.True(await app.App.Services.GetRequiredService<AutomaticMetadataResolutionProcessor>().RunOnceAsync());

        var run = Assert.IsType<MetadataRunProjection>(await app.App.Services
            .GetRequiredService<MetadataResolutionStore>().GetLatestAsync(taskId));
        Assert.Equal("season_resolved", run.Status);
        Assert.Equal(72517, run.TmdbSeriesId);
        Assert.Equal(2, run.TmdbSeasonNumber);
        Assert.Equal(["来自深渊 烈日的黄金乡"], tmdb.SearchTitles);
    }

    [Fact]
    public async Task TitleSeasonRunsBeforeFirstSeasonAfterDirectDateFailure()
    {
        var tmdb = new FakeTmdbClient(Series, [SeasonOne, SeasonTwo]);
        var bangumi = new FakeBangumiClient(new BangumiSubject(
            547888, "Made in Abyss", "来自深渊", new DateOnly(2020, 1, 1), 12));
        await using var app = await RunningApp.StartAsync(
            configure: options => options with
            {
                Metadata = options.Metadata with
                {
                    SeasonFailure = options.Metadata.SeasonFailure with
                    {
                        UseTitleSeason = true,
                        UseFirstSeason = true,
                    },
                },
            },
            tmdbClient: tmdb,
            bangumiSubjectClient: bangumi);
        var taskId = await AddDownloadedTaskAsync(app, "来自深渊 Season 2");

        Assert.True(await app.App.Services.GetRequiredService<AutomaticMetadataResolutionProcessor>().RunOnceAsync());

        var run = Assert.IsType<MetadataRunProjection>(await app.App.Services
            .GetRequiredService<MetadataResolutionStore>().GetLatestAsync(taskId));
        Assert.Equal(2, run.TmdbSeasonNumber);
        var strategies = await ReadStrategiesAsync(app, taskId);
        Assert.Contains("title_season", strategies);
        Assert.DoesNotContain("first_season", strategies);
    }

    [Fact]
    public async Task SkipStopsLowerPrioritySeasonFallbacks()
    {
        var tmdb = new FakeTmdbClient(Series, [SeasonOne, SeasonTwo]);
        var bangumi = new FakeBangumiClient(new BangumiSubject(
            547888, "Made in Abyss", "来自深渊", new DateOnly(2020, 1, 1), 12));
        await using var app = await RunningApp.StartAsync(
            configure: options => options with
            {
                Metadata = options.Metadata with
                {
                    SeasonFailure = options.Metadata.SeasonFailure with
                    {
                        Skip = true,
                        Backtrace = true,
                        UseTitleSeason = true,
                        UseFirstSeason = true,
                    },
                },
            },
            tmdbClient: tmdb,
            bangumiSubjectClient: bangumi);
        var taskId = await AddDownloadedTaskAsync(app, "来自深渊 Season 2");

        Assert.True(await app.App.Services.GetRequiredService<AutomaticMetadataResolutionProcessor>().RunOnceAsync());

        var run = Assert.IsType<MetadataRunProjection>(await app.App.Services
            .GetRequiredService<MetadataResolutionStore>().GetLatestAsync(taskId));
        Assert.Equal("failed", run.Status);
        var strategies = await ReadStrategiesAsync(app, taskId);
        Assert.Contains("skip", strategies);
        Assert.DoesNotContain("title_season", strategies);
        Assert.DoesNotContain("first_season", strategies);
    }

    [Fact]
    public async Task BacktraceMatchesBeforeTitleAndFirstFallbacks()
    {
        var tmdb = new FakeTmdbClient(Series, [SeasonOne, SeasonTwo]);
        var bangumi = new GraphBangumiClient(
            new Dictionary<int, BangumiSubject>
            {
                [547888] = new(547888, "Current", "来自深渊", new DateOnly(2020, 1, 1), 12),
                [1000] = new(1000, "Previous", "来自深渊前作", new DateOnly(2022, 7, 6), 12),
            },
            new Dictionary<int, IReadOnlyList<BangumiSubjectRelation>>
            {
                [547888] = [new BangumiSubjectRelation(1000, 2, "Previous", "来自深渊前作", "前传")],
            });
        await using var app = await RunningApp.StartAsync(
            configure: options => options with
            {
                Metadata = options.Metadata with
                {
                    SeasonFailure = options.Metadata.SeasonFailure with
                    {
                        Backtrace = true,
                        UseTitleSeason = true,
                        UseFirstSeason = true,
                    },
                },
            },
            tmdbClient: tmdb,
            bangumiSubjectClient: bangumi);
        var taskId = await AddDownloadedTaskAsync(app, "来自深渊 Season 1");

        Assert.True(await app.App.Services.GetRequiredService<AutomaticMetadataResolutionProcessor>().RunOnceAsync());

        var run = Assert.IsType<MetadataRunProjection>(await app.App.Services
            .GetRequiredService<MetadataResolutionStore>().GetLatestAsync(taskId));
        Assert.Equal(2, run.TmdbSeasonNumber);
        var strategies = await ReadStrategiesAsync(app, taskId);
        Assert.Contains("backtrace", strategies);
        Assert.DoesNotContain("title_season", strategies);
        Assert.DoesNotContain("first_season", strategies);
    }

    [Fact]
    public async Task BacktraceNetworkErrorIsAuditedThenTitleFallbackContinues()
    {
        var tmdb = new FakeTmdbClient(Series, [SeasonOne, SeasonTwo]);
        var bangumi = new GraphBangumiClient(
            new Dictionary<int, BangumiSubject>
            {
                [547888] = new(547888, "Current", "来自深渊", new DateOnly(2020, 1, 1), 12),
            },
            new Dictionary<int, IReadOnlyList<BangumiSubjectRelation>>(),
            new BangumiClientException(MetadataFailureKind.Network, "bangumi_network_error"));
        await using var app = await RunningApp.StartAsync(
            configure: options => options with
            {
                Metadata = options.Metadata with
                {
                    SeasonFailure = options.Metadata.SeasonFailure with
                    {
                        Backtrace = true,
                        UseTitleSeason = true,
                    },
                },
            },
            tmdbClient: tmdb,
            bangumiSubjectClient: bangumi);
        var taskId = await AddDownloadedTaskAsync(app, "来自深渊 Season 2");

        Assert.True(await app.App.Services.GetRequiredService<AutomaticMetadataResolutionProcessor>().RunOnceAsync());

        var run = Assert.IsType<MetadataRunProjection>(await app.App.Services
            .GetRequiredService<MetadataResolutionStore>().GetLatestAsync(taskId));
        Assert.Equal(2, run.TmdbSeasonNumber);
        var strategies = await ReadStrategiesAsync(app, taskId);
        Assert.Contains("backtrace", strategies);
        Assert.Contains("title_season", strategies);
        var database = app.App.Services.GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT retryable, result, error_code
            FROM metadata_resolution_attempts
            WHERE strategy = 'backtrace';
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt64(0));
        Assert.Equal("error", reader.GetString(1));
        Assert.Equal("bangumi_network_error", reader.GetString(2));
    }

    [Fact]
    public async Task AutomaticProcessorCannotClaimTaskWithCompleteManualOverride()
    {
        var tmdb = new FakeTmdbClient(Series, [SeasonOne, SeasonTwo]);
        await using var app = await RunningApp.StartAsync(
            tmdbClient: tmdb,
            bangumiSubjectClient: new FakeBangumiClient((BangumiSubject?)null));
        await app.App.Services.GetRequiredService<MikanWorkMetadataRuleStore>().SaveAsync(
            new MikanWorkMetadataRuleUpdate(3951, 547888, 72517, 2, null),
            expectedRevision: 0,
            DateTimeOffset.UtcNow);
        await AddDownloadedTaskAsync(app, "来自深渊 第二季");

        Assert.False(await app.App.Services.GetRequiredService<AutomaticMetadataResolutionProcessor>().RunOnceAsync());
        Assert.Empty(tmdb.SearchTitles);
    }

    [Fact]
    public async Task BangumiNetworkFailureIsAuditedAsRetryableWithoutTmdbCall()
    {
        var tmdb = new FakeTmdbClient(Series, [SeasonOne]);
        var bangumi = new FakeBangumiClient(
            new BangumiClientException(MetadataFailureKind.Network, "bangumi_network_error"));
        await using var app = await RunningApp.StartAsync(tmdbClient: tmdb, bangumiSubjectClient: bangumi);
        var taskId = await AddDownloadedTaskAsync(app, "来自深渊");

        Assert.True(await app.App.Services.GetRequiredService<AutomaticMetadataResolutionProcessor>().RunOnceAsync());

        Assert.Empty(tmdb.SearchTitles);
        var database = app.App.Services.GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT attempt.retryable, attempt.error_code, run.failure_kind
            FROM metadata_resolution_attempts AS attempt
            JOIN metadata_resolution_runs AS run ON run.id = attempt.run_id
            WHERE run.task_id = $task_id;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt64(0));
        Assert.Equal("bangumi_network_error", reader.GetString(1));
        Assert.Equal("Network", reader.GetString(2));
    }

    [Fact]
    public async Task SeasonAiRunsBeforeTitleFallbackAndSeedsVerifiedEpisode()
    {
        var tmdb = new FakeTmdbClient(
            Series,
            [SeasonOne, SeasonTwo],
            number => new TmdbEpisode(
                9000 + number,
                72517,
                2,
                number,
                $"Episode {number}",
                null));
        var bangumi = new FakeBangumiClient(new BangumiSubject(
            547888,
            "Made in Abyss",
            "来自深渊",
            new DateOnly(2020, 1, 1),
            12));
        var ai = new FakeAiMetadataMatcher
        {
            ResultFactory = input => new AiMetadataMatchCandidate(
                true,
                72517,
                input.Files.Select(file => new AiMetadataFileCandidate(
                    file.Name,
                    true,
                    2,
                    7,
                    null)).ToArray(),
                null),
        };
        await using var app = await RunningApp.StartAsync(
            configure: options => options with
            {
                Metadata = options.Metadata with
                {
                    SeasonFailure = options.Metadata.SeasonFailure with
                    {
                        UseTitleSeason = true,
                        UseFirstSeason = true,
                    },
                    Ai = options.Metadata.Ai with
                    {
                        UseSeasonMatch = true,
                        UseEpisodeMatch = true,
                    },
                },
            },
            tmdbClient: tmdb,
            bangumiSubjectClient: bangumi,
            aiMetadataMatcher: ai);
        var taskId = await AddDownloadedTaskAsync(app, "来自深渊 Season 2");

        Assert.True(await app.App.Services
            .GetRequiredService<AutomaticMetadataResolutionProcessor>().RunOnceAsync());

        var run = Assert.IsType<MetadataRunProjection>(await app.App.Services
            .GetRequiredService<MetadataResolutionStore>().GetLatestAsync(taskId));
        Assert.Equal(72517, run.TmdbSeriesId);
        Assert.Equal(2, run.TmdbSeasonNumber);
        var strategies = await ReadStrategiesAsync(app, taskId);
        Assert.Contains("ai_season", strategies);
        Assert.DoesNotContain("title_season", strategies);
        Assert.DoesNotContain("first_season", strategies);
        var request = Assert.Single(ai.Requests);
        Assert.Equal(547888, request.BangumiSubjectId);
        Assert.Equal(999, request.AniDbAnimeId);
        Assert.Equal("tt1234567", request.ImdbTitleId);
        Assert.Single(request.Files);
        Assert.False(request.UseBangumiPubDateFirst);
        Assert.Equal(7, await ReadTaskFileEpisodeAsync(app, taskId));

        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        Assert.Equal("metadata_resolved", await ReadTaskStatusAsync(app, taskId));
        Assert.Equal(7, await ReadTaskFileEpisodeAsync(app, taskId));
        Assert.Single(ai.Requests);
        Assert.True(tmdb.EpisodeRequests.Count >= 2);
        Assert.All(tmdb.EpisodeRequests, episode => Assert.Equal(7, episode));
    }

    [Fact]
    public async Task SeasonAiConfigurationFailureIsAuditedThenTitleFallbackContinues()
    {
        var tmdb = new FakeTmdbClient(Series, [SeasonOne, SeasonTwo]);
        var bangumi = new FakeBangumiClient(new BangumiSubject(
            547888,
            "Made in Abyss",
            "来自深渊",
            new DateOnly(2020, 1, 1),
            12));
        var ai = new FakeAiMetadataMatcher
        {
            Failure = new AiMetadataMatcherException(
                MetadataFailureKind.Configuration,
                "ai_provider_not_configured"),
        };
        await using var app = await RunningApp.StartAsync(
            configure: options => options with
            {
                Metadata = options.Metadata with
                {
                    SeasonFailure = options.Metadata.SeasonFailure with
                    {
                        UseTitleSeason = true,
                    },
                    Ai = options.Metadata.Ai with { UseSeasonMatch = true },
                },
            },
            tmdbClient: tmdb,
            bangumiSubjectClient: bangumi,
            aiMetadataMatcher: ai);
        var taskId = await AddDownloadedTaskAsync(app, "来自深渊 Season 2");

        Assert.True(await app.App.Services
            .GetRequiredService<AutomaticMetadataResolutionProcessor>().RunOnceAsync());

        var run = Assert.IsType<MetadataRunProjection>(await app.App.Services
            .GetRequiredService<MetadataResolutionStore>().GetLatestAsync(taskId));
        Assert.Equal(2, run.TmdbSeasonNumber);
        var strategies = await ReadStrategiesAsync(app, taskId);
        Assert.True(
            Array.IndexOf(strategies, "ai_season")
            < Array.IndexOf(strategies, "title_season"));
        Assert.Equal(
            "ai_provider_not_configured",
            await ReadAttemptErrorAsync(app, taskId, "ai_season"));
    }

    [Fact]
    public async Task SeasonAiKnownSeasonOtherDoesNotInvokeEpisodeAiAgain()
    {
        var tmdb = new FakeTmdbClient(Series, [SeasonOne, SeasonTwo]);
        var bangumi = new FakeBangumiClient(new BangumiSubject(
            547888,
            "Made in Abyss",
            "来自深渊",
            new DateOnly(2020, 1, 1),
            12));
        var ai = new FakeAiMetadataMatcher
        {
            ResultFactory = input => new AiMetadataMatchCandidate(
                true,
                72517,
                [new(input.Files[0].Name, false, 2, null, "NCOP is not an Episode.")],
                null),
        };
        await using var app = await RunningApp.StartAsync(
            configure: options => options with
            {
                Metadata = options.Metadata with
                {
                    Ai = options.Metadata.Ai with
                    {
                        UseSeasonMatch = true,
                        UseEpisodeMatch = true,
                    },
                },
            },
            tmdbClient: tmdb,
            bangumiSubjectClient: bangumi,
            aiMetadataMatcher: ai);
        var taskId = await AddDownloadedTaskAsync(app, "来自深渊 NCOP");

        Assert.True(await app.App.Services
            .GetRequiredService<AutomaticMetadataResolutionProcessor>().RunOnceAsync());
        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        var state = await ReadTaskFileStateAsync(app, taskId);
        Assert.Equal("other", state.Disposition);
        Assert.Equal("ai_episode_unmatched", state.OtherReason);
        Assert.Null(state.EpisodeNumber);
        Assert.Single(ai.Requests);
        Assert.Empty(tmdb.EpisodeRequests);
    }

    [Fact]
    public async Task SkipSuppressesSeasonAi()
    {
        var ai = new FakeAiMetadataMatcher();
        var tmdb = new FakeTmdbClient(Series, [SeasonOne, SeasonTwo]);
        var bangumi = new FakeBangumiClient(new BangumiSubject(
            547888,
            "Made in Abyss",
            "来自深渊",
            new DateOnly(2020, 1, 1),
            12));
        await using var app = await RunningApp.StartAsync(
            configure: options => options with
            {
                Metadata = options.Metadata with
                {
                    SeasonFailure = options.Metadata.SeasonFailure with { Skip = true },
                    Ai = options.Metadata.Ai with { UseSeasonMatch = true },
                },
            },
            tmdbClient: tmdb,
            bangumiSubjectClient: bangumi,
            aiMetadataMatcher: ai);
        var taskId = await AddDownloadedTaskAsync(app, "来自深渊 Season 2");

        Assert.True(await app.App.Services
            .GetRequiredService<AutomaticMetadataResolutionProcessor>().RunOnceAsync());

        Assert.Empty(ai.Requests);
        Assert.DoesNotContain("ai_season", await ReadStrategiesAsync(app, taskId));
    }

    private static async Task<string> AddDownloadedTaskAsync(RunningApp app, string title)
    {
        var payload = $$"""
            {
              "source": "mikan",
              "data": [{
                "torrent": "https://mikanani.me/passkey/automatic-metadata.torrent",
                "info": {
                  "title": "{{title}}",
                  "mikanid": 3951,
                  "bgmid": 547888,
                  "anidbid": 999,
                  "imdbid": "tt1234567"
                }
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
        var claim = Assert.IsType<ClaimedStagedTorrentRecord>(await tasks.TryClaimNextStagedAsync(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1)));
        await tasks.CompleteDispatchAsync(
            claim,
            new DownloadTaskSnapshot(hash, title, DownloadTaskState.Waiting, 0, 0, 5, 0, null),
            Path.Combine(app.RootPath, "download", "bt"),
            Path.Combine(app.RootPath, "save"),
            DateTimeOffset.UtcNow);
        await app.App.Services.GetRequiredService<DownloadJobStore>().ApplyInstanceSnapshotAsync(
            "bt",
            [new DownloadTaskSnapshot(hash, title, DownloadTaskState.Complete, 1, 5, 5, 0, 0)],
            DateTimeOffset.UtcNow);
        return taskId;
    }

    private static async Task<string[]> ReadStrategiesAsync(RunningApp app, string taskId)
    {
        var database = app.App.Services.GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT attempt.strategy
            FROM metadata_resolution_attempts AS attempt
            JOIN metadata_resolution_runs AS run ON run.id = attempt.run_id
            WHERE run.task_id = $task_id
            ORDER BY attempt.created_at_utc, attempt.rowid;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values.ToArray();
    }

    private static async Task<string?> ReadAttemptErrorAsync(
        RunningApp app,
        string taskId,
        string strategy)
    {
        var database = app.App.Services
            .GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT attempt.error_code
            FROM metadata_resolution_attempts AS attempt
            JOIN metadata_resolution_runs AS run ON run.id = attempt.run_id
            WHERE run.task_id = $task_id AND attempt.strategy = $strategy
            ORDER BY attempt.created_at_utc DESC, attempt.id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        command.Parameters.AddWithValue("$strategy", strategy);
        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task<int?> ReadTaskFileEpisodeAsync(RunningApp app, string taskId)
    {
        var database = app.App.Services
            .GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT tmdb_episode_number FROM task_files WHERE task_id = $task_id;";
        command.Parameters.AddWithValue("$task_id", taskId);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull
            ? null
            : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<TaskFileState> ReadTaskFileStateAsync(
        RunningApp app,
        string taskId)
    {
        var database = app.App.Services
            .GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT disposition, tmdb_episode_number, other_reason
            FROM task_files WHERE task_id = $task_id;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new TaskFileState(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    private static async Task<string> ReadTaskStatusAsync(RunningApp app, string taskId)
    {
        var database = app.App.Services
            .GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT status FROM ingest_tasks WHERE id = $task_id;";
        command.Parameters.AddWithValue("$task_id", taskId);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private sealed class FakeBangumiClient : IBangumiSubjectClient
    {
        private readonly BangumiSubject? _subject;
        private readonly Exception? _exception;

        public FakeBangumiClient(BangumiSubject? subject) => _subject = subject;

        public FakeBangumiClient(Exception exception) => _exception = exception;

        public Task<BangumiSubject?> GetSubjectAsync(int subjectId, CancellationToken cancellationToken = default) =>
            _exception is null
                ? Task.FromResult(_subject)
                : Task.FromException<BangumiSubject?>(_exception);

        public Task<IReadOnlyList<BangumiSubjectRelation>> GetRelatedSubjectsAsync(
            int subjectId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BangumiSubjectRelation>>([]);
    }

    private sealed record TaskFileState(
        string Disposition,
        int? EpisodeNumber,
        string? OtherReason);

    private sealed class FakeTmdbClient(
        TmdbSeries series,
        IReadOnlyList<TmdbSeason> seasons,
        Func<int, TmdbEpisode?>? episodeFactory = null) : ITmdbClient
    {
        public List<string> SearchTitles { get; } = [];

        public List<int> EpisodeRequests { get; } = [];

        public Task<IReadOnlyList<TmdbSeries>> SearchSeriesAsync(string title, CancellationToken cancellationToken = default)
        {
            SearchTitles.Add(title);
            return Task.FromResult<IReadOnlyList<TmdbSeries>>([series]);
        }

        public Task<TmdbSeries?> GetSeriesAsync(int seriesId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeries?>(series);

        public Task<TmdbSeriesDetails?> GetSeriesDetailsAsync(int seriesId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeriesDetails?>(new TmdbSeriesDetails(series, seasons));

        public Task<TmdbSeason?> GetSeasonAsync(int seriesId, int seasonNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult(seasons.FirstOrDefault(value => value.SeasonNumber == seasonNumber));

        public Task<TmdbEpisode?> GetEpisodeAsync(
            int seriesId,
            int seasonNumber,
            int episodeNumber,
            CancellationToken cancellationToken = default)
        {
            EpisodeRequests.Add(episodeNumber);
            return Task.FromResult(episodeFactory?.Invoke(episodeNumber));
        }
    }

    private sealed class FakeAiMetadataMatcher : IAiMetadataMatcher
    {
        public Func<AiMetadataMatchInput, AiMetadataMatchCandidate>? ResultFactory { get; init; }

        public AiMetadataMatcherException? Failure { get; init; }

        public List<AiMetadataMatchInput> Requests { get; } = [];

        public Task<AiMetadataMatchCandidate> MatchAsync(
            AiMetadataMatchInput input,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(input);
            if (Failure is not null)
            {
                return Task.FromException<AiMetadataMatchCandidate>(Failure);
            }

            return Task.FromResult(
                ResultFactory?.Invoke(input)
                ?? new AiMetadataMatchCandidate(false, null, [], "not matched"));
        }
    }

    private sealed class GraphBangumiClient(
        IReadOnlyDictionary<int, BangumiSubject> subjects,
        IReadOnlyDictionary<int, IReadOnlyList<BangumiSubjectRelation>> relations,
        Exception? relationFailure = null) : IBangumiSubjectClient
    {
        public Task<BangumiSubject?> GetSubjectAsync(int subjectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(subjects.TryGetValue(subjectId, out var subject) ? subject : null);

        public Task<IReadOnlyList<BangumiSubjectRelation>> GetRelatedSubjectsAsync(
            int subjectId,
            CancellationToken cancellationToken = default) =>
            relationFailure is null
                ? Task.FromResult(relations.TryGetValue(subjectId, out var values)
                    ? values
                    : (IReadOnlyList<BangumiSubjectRelation>)[])
                : Task.FromException<IReadOnlyList<BangumiSubjectRelation>>(relationFailure);
    }
}
