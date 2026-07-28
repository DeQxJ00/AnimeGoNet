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
    public async Task AuthoritativeSeriesNoMatchCanCreateBangumiPendingCompletionWithoutFakeEpisode()
    {
        var tmdb = new FakeTmdbClient(
            Series,
            [SeasonOne, SeasonTwo],
            searchReturnsEmpty: true);
        var bangumi = new FakeBangumiClient(new BangumiSubject(
            547888,
            "Made in Abyss Season 2",
            "来自深渊 第二季",
            new DateOnly(2022, 7, 6),
            12));
        var bangumiEpisodes = new FakeBangumiEpisodeClient(
            [new BangumiEpisode(1001, 0, 7, new DateOnly(2022, 8, 17))]);
        await using var app = await RunningApp.StartAsync(
            configure: options => options with
            {
                Metadata = options.Metadata with
                {
                    TmdbFailureUseBangumi = true,
                    SeasonFailure = options.Metadata.SeasonFailure with
                    {
                        UseTitleSeason = true,
                    },
                },
            },
            tmdbClient: tmdb,
            bangumiSubjectClient: bangumi,
            bangumiEpisodeClient: bangumiEpisodes);
        var taskId = await AddDownloadedTaskAsync(app, "来自深渊 第二季");
        await SetMikanGroupAndEpisodeCandidateAsync(app, taskId, 77, 7);

        Assert.True(await app.App.Services
            .GetRequiredService<AutomaticMetadataResolutionProcessor>().RunOnceAsync());

        var run = Assert.IsType<MetadataRunProjection>(await app.App.Services
            .GetRequiredService<MetadataResolutionStore>().GetLatestAsync(taskId));
        Assert.Equal("fallback_resolved", run.Status);
        Assert.Null(run.TmdbSeriesId);
        Assert.Equal(2, run.TmdbSeasonNumber);
        Assert.True(run.FallbackEligible);
        Assert.Equal("metadata_resolved", await ReadTaskStatusAsync(app, taskId));
        var database = app.App.Services
            .GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT series.tmdb_series_id, series.bangumi_subject_id,
                   series.needs_tmdb_completion, file.tmdb_series_id,
                   file.tmdb_season_number, file.tmdb_episode_number,
                   file.disposition, file.other_reason,
                   (SELECT scope_kind FROM fallback_claims WHERE task_file_id = file.id),
                   (SELECT scope_key FROM fallback_claims WHERE task_file_id = file.id)
            FROM ingest_tasks AS task
            JOIN anime_series AS series
              ON series.tmdb_series_id = 0
             AND series.bangumi_subject_id = task.bangumi_subject_id
            JOIN task_files AS file ON file.task_id = task.id
            WHERE task.id = $task_id;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetInt32(0));
        Assert.Equal(547888, reader.GetInt32(1));
        Assert.Equal(1, reader.GetInt64(2));
        Assert.True(reader.IsDBNull(3));
        Assert.Equal(2, reader.GetInt32(4));
        Assert.True(reader.IsDBNull(5));
        Assert.Equal("other", reader.GetString(6));
        Assert.Equal("tmdb_fallback_pending_completion", reader.GetString(7));
        Assert.Equal("bangumi_episode", reader.GetString(8));
        Assert.Equal("1001", reader.GetString(9));
        Assert.Equal([547888], bangumiEpisodes.SubjectIds);
    }

    [Fact]
    public async Task TmdbNetworkFailureNeverCreatesBangumiFallback()
    {
        var tmdb = new FakeTmdbClient(
            Series,
            [SeasonOne, SeasonTwo],
            searchFailure: new TmdbClientException(
                MetadataFailureKind.Network,
                "tmdb_network_error",
                tmdbAccessConfirmed: false));
        await using var app = await RunningApp.StartAsync(
            configure: options => options with
            {
                Metadata = options.Metadata with
                {
                    TmdbFailureUseBangumi = true,
                    SeasonFailure = options.Metadata.SeasonFailure with { UseTitleSeason = true },
                },
            },
            tmdbClient: tmdb,
            bangumiSubjectClient: new FakeBangumiClient(new BangumiSubject(
                547888, "Made in Abyss Season 2", "来自深渊 第二季", null, 12)));
        var taskId = await AddDownloadedTaskAsync(app, "来自深渊 第二季");

        Assert.True(await app.App.Services
            .GetRequiredService<AutomaticMetadataResolutionProcessor>().RunOnceAsync());

        var run = Assert.IsType<MetadataRunProjection>(await app.App.Services
            .GetRequiredService<MetadataResolutionStore>().GetLatestAsync(taskId));
        Assert.Equal("failed", run.Status);
        Assert.Equal(MetadataFailureKind.Network, run.FailureKind);
        Assert.False(run.FallbackEligible);
        var database = app.App.Services
            .GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM anime_series WHERE tmdb_series_id = 0;";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
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
    public async Task SeasonAiUsesTrustedMikanPublicationAndSharedRuleBangumiId()
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
        var subject = new FakeBangumiClient(new BangumiSubject(
            547888,
            "Made in Abyss",
            "来自深渊",
            new DateOnly(2020, 1, 1),
            12));
        var episodes = new FakeBangumiEpisodeClient(
        [
            new BangumiEpisode(100, 0, 6, new DateOnly(2026, 7, 15)),
            new BangumiEpisode(101, 0, 7, new DateOnly(2026, 7, 22)),
        ]);
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
                    Ai = options.Metadata.Ai with
                    {
                        UseSeasonMatch = true,
                        UseBangumiPubDateFirst = true,
                    },
                },
            },
            tmdbClient: tmdb,
            bangumiSubjectClient: subject,
            bangumiEpisodeClient: episodes,
            aiMetadataMatcher: ai);
        var taskId = await AddDownloadedTaskAsync(app, "来自深渊 Season 2 EP07");
        await SetTrustedPublicationEvidenceAsync(
            app,
            taskId,
            clearTaskBangumiId: true);
        await app.App.Services.GetRequiredService<MikanWorkMetadataRuleStore>().SaveAsync(
            new MikanWorkMetadataRuleUpdate(3951, 547888, null, null, null),
            expectedRevision: 0,
            DateTimeOffset.UtcNow);

        Assert.True(await app.App.Services
            .GetRequiredService<AutomaticMetadataResolutionProcessor>().RunOnceAsync());

        var request = Assert.Single(ai.Requests);
        Assert.True(request.UseBangumiPubDateFirst);
        Assert.Equal(547888, request.BangumiSubjectId);
        Assert.Equal(7, request.BangumiEpisodeCandidate);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 22, 12, 34, 56, 123, TimeSpan.FromHours(8)),
            request.PublishedAt);
        Assert.Equal(1, request.TorrentFileCount);
        Assert.Equal([547888], episodes.SubjectIds);
        Assert.Contains("ai_pubdate", await ReadStrategiesAsync(app, taskId));
    }

    [Fact]
    public async Task SeasonAiResolvesCrossSeasonVideosAndAssociatedSubtitleEndToEnd()
    {
        var tmdb = new FakeTmdbClient(
            Series,
            [SeasonOne, SeasonTwo],
            seasonEpisodeFactory: (season, episode) => new TmdbEpisode(
                9000 + (season * 100) + episode,
                Series.Id,
                season,
                episode,
                $"S{season:00}E{episode:00}",
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
                Series.Id,
                input.Files.Select(file => new AiMetadataFileCandidate(
                    file.Name,
                    true,
                    file.Name.StartsWith("season-2/", StringComparison.Ordinal) ? 2 : 1,
                    1,
                    null)).ToArray(),
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
        var taskId = await AddDownloadedTaskAsync(app, "来自深渊 Season 1-2");
        await ReplaceWithCrossSeasonFilesAsync(app, taskId);

        Assert.True(await app.App.Services
            .GetRequiredService<AutomaticMetadataResolutionProcessor>().RunOnceAsync());

        var seasonRun = Assert.IsType<MetadataRunProjection>(await app.App.Services
            .GetRequiredService<MetadataResolutionStore>().GetLatestAsync(taskId));
        Assert.Equal("season_resolved", seasonRun.Status);
        Assert.Null(seasonRun.TmdbSeasonNumber);
        var seasonFiles = await ReadTaskFilesAsync(app, taskId);
        Assert.Equal(3, seasonFiles.Length);
        Assert.Equal(
            [1, 2, 2],
            seasonFiles.OrderBy(file => file.RelativePath)
                .Select(file => file.SeasonNumber)
                .ToArray());
        Assert.All(seasonFiles, file => Assert.Equal(1, file.EpisodeNumber));

        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        Assert.Equal("metadata_resolved", await ReadTaskStatusAsync(app, taskId));
        var completedFiles = await ReadTaskFilesAsync(app, taskId);
        Assert.All(completedFiles, file => Assert.Equal("episode", file.Disposition));
        var subtitle = Assert.Single(
            completedFiles,
            file => file.RelativePath.EndsWith(".ass", StringComparison.Ordinal));
        Assert.Equal(2, subtitle.SeasonNumber);
        Assert.Equal(1, subtitle.EpisodeNumber);
        Assert.Equal(".zh-Hans.ass", subtitle.RenameSuffix);
        Assert.Single(ai.Requests);
        Assert.Equal(2, ai.Requests[0].Files.Count);
        Assert.Null(await ReadAttemptErrorAsync(app, taskId, "ai_season"));
        Assert.Contains((1, 1), tmdb.EpisodeIdentities);
        Assert.Contains((2, 1), tmdb.EpisodeIdentities);
    }

    [Fact]
    public async Task SeasonAiRejectsCrossSeasonPackageWhenAFileCannotBeAssignedSafely()
    {
        var tmdb = new FakeTmdbClient(
            Series,
            [SeasonOne, SeasonTwo],
            seasonEpisodeFactory: (season, episode) => new TmdbEpisode(
                9000 + (season * 100) + episode,
                Series.Id,
                season,
                episode,
                $"S{season:00}E{episode:00}",
                null));
        var ai = new FakeAiMetadataMatcher
        {
            ResultFactory = input => new AiMetadataMatchCandidate(
                true,
                Series.Id,
                input.Files.Select(file => new AiMetadataFileCandidate(
                    file.Name,
                    true,
                    file.Name.StartsWith("season-2/", StringComparison.Ordinal) ? 2 : 1,
                    1,
                    null)).ToArray(),
                null),
        };
        await using var app = await RunningApp.StartAsync(
            configure: options => options with
            {
                Metadata = options.Metadata with
                {
                    Ai = options.Metadata.Ai with { UseSeasonMatch = true },
                },
            },
            tmdbClient: tmdb,
            bangumiSubjectClient: new FakeBangumiClient(new BangumiSubject(
                547888,
                "Made in Abyss",
                "来自深渊",
                new DateOnly(2020, 1, 1),
                12)),
            aiMetadataMatcher: ai);
        var taskId = await AddDownloadedTaskAsync(app, "来自深渊 Season 1-2");
        await ReplaceWithCrossSeasonFilesAsync(app, taskId);
        await AddUnassignedSubtitleAsync(app, taskId);

        Assert.True(await app.App.Services
            .GetRequiredService<AutomaticMetadataResolutionProcessor>().RunOnceAsync());

        var run = Assert.IsType<MetadataRunProjection>(await app.App.Services
            .GetRequiredService<MetadataResolutionStore>().GetLatestAsync(taskId));
        Assert.Equal("failed", run.Status);
        Assert.Equal(
            "ai_cross_season_file_unassigned",
            await ReadAttemptErrorAsync(app, taskId, "ai_season"));
        Assert.All(await ReadTaskFilesAsync(app, taskId), file => Assert.Null(file.SeasonNumber));
    }

    [Fact]
    public async Task TrustedMikanOffsetBypassesAiAndTmdbEpisodeRequests()
    {
        var tmdb = new FakeTmdbClient(Series, [SeasonOne, SeasonTwo]);
        var ai = new FakeAiMetadataMatcher();
        await using var app = await RunningApp.StartAsync(
            configure: options => options with
            {
                Metadata = options.Metadata with
                {
                    MikanTrustedOffsetCacheEnabled = true,
                    Ai = options.Metadata.Ai with
                    {
                        UseSeasonMatch = true,
                        UseEpisodeMatch = true,
                    },
                },
            },
            tmdbClient: tmdb,
            bangumiSubjectClient: new FakeBangumiClient((BangumiSubject?)null),
            aiMetadataMatcher: ai);
        await SeedCanonicalSeasonAsync(app);
        var offsets = app.App.Services.GetRequiredService<MikanTrustedOffsetStore>();
        for (var episode = 1; episode <= 3; episode++)
        {
            await offsets.ObserveAsync(
                new MikanOffsetEvidenceObservation(3951, 7, episode, Series.Id, 2, 13),
                DateTimeOffset.UtcNow.AddMinutes(episode));
        }

        var taskId = await AddDownloadedTaskAsync(app, "来自深渊 第四话");
        await SetMikanGroupAndEpisodeCandidateAsync(app, taskId, 7, 4);
        await AddTrustedOffsetSubtitleAsync(app, taskId);

        Assert.True(await app.App.Services
            .GetRequiredService<AutomaticMetadataResolutionProcessor>().RunOnceAsync());
        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        Assert.Equal("metadata_resolved", await ReadTaskStatusAsync(app, taskId));
        var files = await ReadTaskFilesAsync(app, taskId);
        Assert.Equal(2, files.Length);
        Assert.All(files, file =>
        {
            Assert.Equal(17, file.EpisodeNumber);
            Assert.Equal("episode", file.Disposition);
        });
        Assert.Equal(
            ".zh-Hans.ass",
            Assert.Single(files, file => file.RelativePath.EndsWith(".ass", StringComparison.Ordinal))
                .RenameSuffix);
        Assert.Empty(ai.Requests);
        Assert.Empty(tmdb.SearchTitles);
        Assert.Empty(tmdb.EpisodeIdentities);
        Assert.Contains("trusted_mikan_offset", await ReadStrategiesAsync(app, taskId));
    }

    [Fact]
    public async Task VerifiedEpisodeCompletesThirdTrustedOffsetObservation()
    {
        var tmdb = new FakeTmdbClient(
            Series,
            [SeasonOne, SeasonTwo],
            number => new TmdbEpisode(
                9000 + number,
                Series.Id,
                2,
                number,
                $"Episode {number}",
                null));
        var ai = new FakeAiMetadataMatcher
        {
            ResultFactory = input => new AiMetadataMatchCandidate(
                true,
                Series.Id,
                input.Files.Select(file => new AiMetadataFileCandidate(
                    file.Name,
                    true,
                    2,
                    17,
                    null)).ToArray(),
                null),
        };
        await using var app = await RunningApp.StartAsync(
            configure: options => options with
            {
                Metadata = options.Metadata with
                {
                    MikanTrustedOffsetCacheEnabled = true,
                    Ai = options.Metadata.Ai with { UseSeasonMatch = true },
                },
            },
            tmdbClient: tmdb,
            bangumiSubjectClient: new FakeBangumiClient(new BangumiSubject(
                547888,
                "Made in Abyss",
                "来自深渊",
                new DateOnly(2020, 1, 1),
                12)),
            aiMetadataMatcher: ai);
        var offsets = app.App.Services.GetRequiredService<MikanTrustedOffsetStore>();
        Assert.Null(await offsets.ObserveAsync(
            new MikanOffsetEvidenceObservation(3951, 7, 1, Series.Id, 2, 13),
            DateTimeOffset.UtcNow));
        Assert.Null(await offsets.ObserveAsync(
            new MikanOffsetEvidenceObservation(3951, 7, 2, Series.Id, 2, 13),
            DateTimeOffset.UtcNow.AddMinutes(1)));
        var taskId = await AddDownloadedTaskAsync(app, "来自深渊 第四话");
        await SetMikanGroupAndEpisodeCandidateAsync(app, taskId, 7, 4);

        Assert.True(await app.App.Services
            .GetRequiredService<AutomaticMetadataResolutionProcessor>().RunOnceAsync());
        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        var trusted = Assert.IsType<MikanTrustedOffset>(
            await offsets.GetTrustedAsync(3951, 7));
        Assert.Equal(3, trusted.DistinctEpisodeCount);
        Assert.Equal(13, trusted.EpisodeOffset);
        Assert.Single(ai.Requests);
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

    private static async Task SetTrustedPublicationEvidenceAsync(
        RunningApp app,
        string taskId,
        bool clearTaskBangumiId)
    {
        var database = app.App.Services
            .GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ingest_tasks
            SET source_published_at_raw = '2026-07-22T12:34:56.123',
                source_published_at = '2026-07-22T12:34:56.123+08:00',
                bangumi_subject_id = CASE
                    WHEN $clear_bgmid = 1 THEN NULL
                    ELSE bangumi_subject_id
                END
            WHERE id = $task_id;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        command.Parameters.AddWithValue("$clear_bgmid", clearTaskBangumiId ? 1 : 0);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task ReplaceWithCrossSeasonFilesAsync(RunningApp app, string taskId)
    {
        var database = app.App.Services
            .GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE task_files
            SET relative_path = 'season-1/episode-01.mkv',
                size_bytes = 100,
                source_episode = '1',
                file_episode_candidate = '1'
            WHERE task_id = $task_id;

            INSERT INTO task_files (
                id, task_id, relative_path, size_bytes, source_episode,
                file_episode_candidate, disposition, other_reason)
            VALUES
                ('cross-season-video', $task_id, 'season-2/episode-01.mkv', 200, '1', '1', 'pending', NULL),
                ('cross-season-subtitle', $task_id, 'season-2/episode-01.zh-Hans.ass', 20, '1', '1', 'pending', NULL);
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        Assert.Equal(3, await command.ExecuteNonQueryAsync());
    }

    private static async Task AddUnassignedSubtitleAsync(RunningApp app, string taskId)
    {
        var database = app.App.Services
            .GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO task_files (
                id, task_id, relative_path, size_bytes, source_episode,
                file_episode_candidate, disposition, other_reason)
            VALUES (
                'cross-season-unassigned', $task_id, 'extras/commentary.ass', 10,
                NULL, NULL, 'pending', NULL);
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task SetMikanGroupAndEpisodeCandidateAsync(
        RunningApp app,
        string taskId,
        int groupId,
        int episode)
    {
        var database = app.App.Services
            .GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ingest_tasks SET groupid = $groupid WHERE id = $task_id;
            UPDATE task_files
            SET relative_path = '[04].mkv',
                source_episode = $episode,
                file_episode_candidate = $episode
            WHERE task_id = $task_id;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        command.Parameters.AddWithValue("$groupid", groupId);
        command.Parameters.AddWithValue("$episode", episode.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(2, await command.ExecuteNonQueryAsync());
    }

    private static async Task SeedCanonicalSeasonAsync(RunningApp app)
    {
        var database = app.App.Services
            .GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO anime_series (
                id, tmdb_series_id, bangumi_subject_id, canonical_name,
                original_name, poster_path, needs_tmdb_completion,
                created_at_utc, updated_at_utc)
            VALUES (
                'trusted-series', 72517, NULL, '来自深渊',
                'メイドインアビス', NULL, 0, $now, $now);
            INSERT INTO anime_seasons (
                id, series_id, season_number, canonical_name, poster_path,
                created_at_utc, updated_at_utc)
            VALUES (
                'trusted-season', 'trusted-series', 2, 'Season 2', NULL,
                $now, $now);
            """;
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        Assert.Equal(2, await command.ExecuteNonQueryAsync());
    }

    private static async Task AddTrustedOffsetSubtitleAsync(RunningApp app, string taskId)
    {
        var database = app.App.Services
            .GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO task_files (
                id, task_id, relative_path, size_bytes, source_episode,
                file_episode_candidate, disposition, other_reason)
            VALUES (
                'trusted-offset-subtitle', $task_id, '[04].zh-Hans.ass', 10,
                '4', '4', 'pending', NULL);
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
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

    private static async Task<TaskFileProjection[]> ReadTaskFilesAsync(
        RunningApp app,
        string taskId)
    {
        var database = app.App.Services
            .GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT relative_path, tmdb_season_number, tmdb_episode_number,
                   disposition, rename_suffix
            FROM task_files
            WHERE task_id = $task_id
            ORDER BY relative_path;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        var files = new List<TaskFileProjection>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            files.Add(new TaskFileProjection(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return files.ToArray();
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

    private sealed class FakeBangumiEpisodeClient(
        IReadOnlyList<BangumiEpisode> episodes) : IBangumiEpisodeClient
    {
        public List<int> SubjectIds { get; } = [];

        public Task<IReadOnlyList<BangumiEpisode>> GetEpisodesAsync(
            int subjectId,
            CancellationToken cancellationToken = default)
        {
            SubjectIds.Add(subjectId);
            return Task.FromResult(episodes);
        }
    }

    private sealed record TaskFileState(
        string Disposition,
        int? EpisodeNumber,
        string? OtherReason);

    private sealed record TaskFileProjection(
        string RelativePath,
        int? SeasonNumber,
        int? EpisodeNumber,
        string Disposition,
        string? RenameSuffix);

    private sealed class FakeTmdbClient(
        TmdbSeries series,
        IReadOnlyList<TmdbSeason> seasons,
        Func<int, TmdbEpisode?>? episodeFactory = null,
        Func<int, int, TmdbEpisode?>? seasonEpisodeFactory = null,
        bool searchReturnsEmpty = false,
        TmdbClientException? searchFailure = null) : ITmdbClient
    {
        public List<string> SearchTitles { get; } = [];

        public List<int> EpisodeRequests { get; } = [];

        public List<(int SeasonNumber, int EpisodeNumber)> EpisodeIdentities { get; } = [];

        public Task<IReadOnlyList<TmdbSeries>> SearchSeriesAsync(string title, CancellationToken cancellationToken = default)
        {
            SearchTitles.Add(title);
            if (searchFailure is not null)
            {
                return Task.FromException<IReadOnlyList<TmdbSeries>>(searchFailure);
            }

            return Task.FromResult<IReadOnlyList<TmdbSeries>>(
                searchReturnsEmpty ? [] : [series]);
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
            EpisodeIdentities.Add((seasonNumber, episodeNumber));
            return Task.FromResult(
                seasonEpisodeFactory?.Invoke(seasonNumber, episodeNumber)
                ?? episodeFactory?.Invoke(episodeNumber));
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
