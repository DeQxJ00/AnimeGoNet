using System.Text;
using System.Net.WebSockets;
using System.Globalization;
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
        Assert.Equal("tmdb_episode_number", video.ResolutionSource);
        Assert.Equal("subtitle_association", subtitle.ResolutionSource);
        Assert.NotNull(video.ResolutionRunId);
        Assert.Equal(video.ResolutionRunId, subtitle.ResolutionRunId);
        Assert.NotNull(video.ResolutionAttemptId);
        Assert.NotNull(subtitle.ResolutionAttemptId);
        Assert.NotEqual(video.ResolutionAttemptId, subtitle.ResolutionAttemptId);
        Assert.Equal("metadata_resolved", await ReadTaskStatusAsync(app, taskId));
        Assert.Equal(1, await CountEpisodeClaimsAsync(app, taskId));
    }

    [Fact]
    public async Task MikanBangumiAirDateOverridesConflictingSameNumberTmdbEpisode()
    {
        var tmdb = new FakeTmdbClient
        {
            SeasonValue = new TmdbSeason(
                100,
                72517,
                2,
                "Season 2",
                new DateOnly(2024, 10, 2),
                2,
                Episodes:
                [
                    new TmdbEpisode(6, 72517, 2, 6, "Old episode", new DateOnly(2016, 5, 9)),
                    new TmdbEpisode(56, 72517, 2, 56, "Current episode", new DateOnly(2024, 11, 6)),
                ]),
            EpisodeFactory = number => number == 56
                ? new TmdbEpisode(56, 72517, 2, 56, "Current episode", new DateOnly(2024, 11, 6))
                : null,
        };
        var bangumi = new FakeBangumiEpisodeClient(
        [
            new BangumiEpisode(42599806, 0, 6, new DateOnly(2024, 11, 6)),
        ]);
        await using var app = await StartSeasonResolvedTaskAsync(
            tmdb,
            episodeOffset: null,
            bangumiEpisodeClient: bangumi);
        var taskId = await PrepareFilesAsync(app, ("Show EP06.mkv", "6", "6"));
        await SetTrustedPublicationEvidenceAsync(app, taskId);
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        var file = Assert.Single(await ReadFilesAsync(app, taskId));
        Assert.Equal("episode", file.Disposition);
        Assert.Equal(56, file.EpisodeNumber);
        Assert.Equal("tmdb_episode_bangumi_date", file.ResolutionSource);
        Assert.Equal([56], tmdb.EpisodeRequests);
        Assert.Equal([547888], bangumi.SubjectIds);
    }

    [Fact]
    public async Task MikanMissingTargetAirDateRefreshesEpisodesBeforeDateMatching()
    {
        var tmdb = new FakeTmdbClient
        {
            SeasonValue = new TmdbSeason(
                100,
                72517,
                2,
                "Season 2",
                new DateOnly(2024, 10, 2),
                2,
                Episodes:
                [
                    new TmdbEpisode(6, 72517, 2, 6, "Old episode", new DateOnly(2016, 5, 9)),
                    new TmdbEpisode(56, 72517, 2, 56, "Current episode", new DateOnly(2024, 11, 6)),
                ]),
            EpisodeFactory = number => number == 56
                ? new TmdbEpisode(56, 72517, 2, 56, "Current episode", new DateOnly(2024, 11, 6))
                : null,
        };
        var bangumi = new RefreshingBangumiEpisodeClient(
            cached: [new BangumiEpisode(42599806, 0, 6, null)],
            refreshed: [new BangumiEpisode(42599806, 0, 6, new DateOnly(2024, 11, 6))]);
        await using var app = await StartSeasonResolvedTaskAsync(
            tmdb,
            episodeOffset: null,
            bangumiEpisodeClient: bangumi);
        var taskId = await PrepareFilesAsync(app, ("Show EP06.mkv", "6", "6"));
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        var file = Assert.Single(await ReadFilesAsync(app, taskId));
        Assert.Equal("episode", file.Disposition);
        Assert.Equal(56, file.EpisodeNumber);
        Assert.Equal("tmdb_episode_bangumi_date", file.ResolutionSource);
        Assert.Equal([547888], bangumi.CachedSubjectIds);
        Assert.Equal([547888], bangumi.RefreshedSubjectIds);
    }

    [Fact]
    public async Task MikanAvailableTargetAirDateDoesNotRefreshEpisodes()
    {
        var tmdb = new FakeTmdbClient
        {
            SeasonValue = new TmdbSeason(
                100,
                72517,
                2,
                "Season 2",
                new DateOnly(2024, 10, 2),
                1,
                Episodes:
                [new TmdbEpisode(56, 72517, 2, 56, "Current episode", new DateOnly(2024, 11, 6))]),
            EpisodeFactory = number => number == 56
                ? new TmdbEpisode(56, 72517, 2, 56, "Current episode", new DateOnly(2024, 11, 6))
                : null,
        };
        var bangumi = new RefreshingBangumiEpisodeClient(
            cached: [new BangumiEpisode(42599806, 0, 6, new DateOnly(2024, 11, 6))],
            refreshed: []);
        await using var app = await StartSeasonResolvedTaskAsync(
            tmdb,
            episodeOffset: null,
            bangumiEpisodeClient: bangumi);
        var taskId = await PrepareFilesAsync(app, ("Show EP06.mkv", "6", "6"));
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        Assert.Equal(56, Assert.Single(await ReadFilesAsync(app, taskId)).EpisodeNumber);
        Assert.Empty(bangumi.RefreshedSubjectIds);
    }

    [Fact]
    public async Task MikanStaleTmdbSeasonSnapshotRefreshesBeforeEpisodeEightDateMatch()
    {
        var staleEpisodes = Enumerable.Range(1, 7)
            .Select(number => new TmdbEpisode(
                29610100 + number,
                296101,
                1,
                number,
                $"Episode {number}",
                new DateOnly(2026, 7, 4).AddDays((number - 1) * 7)))
            .ToArray();
        var episodeEight = new TmdbEpisode(
            29610108,
            296101,
            1,
            8,
            "Episode 8",
            new DateOnly(2026, 8, 22));
        var tmdb = new FakeTmdbClient
        {
            SeriesValue = new TmdbSeries(296101, "Grow Up Show", "Grow Up Show", new DateOnly(2026, 7, 4)),
            SeasonValue = new TmdbSeason(
                29610101,
                296101,
                1,
                "Season 1",
                new DateOnly(2026, 7, 4),
                7,
                Episodes: staleEpisodes),
            RefreshedSeasonValue = new TmdbSeason(
                29610101,
                296101,
                1,
                "Season 1",
                new DateOnly(2026, 7, 4),
                8,
                Episodes: [.. staleEpisodes, episodeEight]),
            EpisodeFactory = number => number == 8 ? episodeEight : null,
        };
        var bangumi = new FakeBangumiEpisodeClient(
        [
            new BangumiEpisode(57058308, 0, 8, new DateOnly(2026, 8, 22)),
        ]);
        await using var app = await StartSeasonResolvedTaskAsync(
            tmdb,
            episodeOffset: null,
            bangumiEpisodeClient: bangumi,
            tmdbSeriesId: 296101,
            tmdbSeasonNumber: 1);
        var taskId = await PrepareFilesAsync(app, ("Grow Up Show - 08.mkv", "8", "8"));
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        var file = Assert.Single(await ReadFilesAsync(app, taskId));
        Assert.Equal("episode", file.Disposition);
        Assert.Equal(8, file.EpisodeNumber);
        Assert.Equal("tmdb_episode_bangumi_date", file.ResolutionSource);
        Assert.Equal(1, tmdb.SeasonRefreshCalls);
        Assert.Equal([8], tmdb.EpisodeRequests);
    }

    [Fact]
    public async Task MikanTargetAirDateRefreshFailureRemainsRetryable()
    {
        var bangumi = new RefreshingBangumiEpisodeClient(
            cached: [new BangumiEpisode(42599806, 0, 6, null)],
            refreshed: [],
            refreshFailure: new BangumiClientException(
                MetadataFailureKind.Network,
                "bangumi_network_error"));
        await using var app = await StartSeasonResolvedTaskAsync(
            new FakeTmdbClient(),
            episodeOffset: null,
            bangumiEpisodeClient: bangumi);
        var taskId = await PrepareFilesAsync(app, ("Show EP06.mkv", "6", "6"));
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        Assert.Equal("metadata_failed", await ReadTaskStatusAsync(app, taskId));
        Assert.Equal("pending", Assert.Single(await ReadFilesAsync(app, taskId)).Disposition);
        Assert.Equal(
            "bangumi_network_error",
            await ReadLatestAttemptErrorAsync(app, taskId, "tmdb_episode_bangumi_date"));
        Assert.Equal([547888], bangumi.RefreshedSubjectIds);
    }

    [Fact]
    public async Task MikanDirectSequelGlobalSortMapsByEpisodeAirDate()
    {
        var tmdb = new FakeTmdbClient
        {
            SeasonValue = new TmdbSeason(
                200,
                72517,
                2,
                "Season 2",
                new DateOnly(2021, 1, 12),
                1,
                Episodes:
                [new TmdbEpisode(21, 72517, 2, 21, "Episode 21", new DateOnly(2021, 8, 31))]),
            EpisodeFactory = number => number == 21
                ? new TmdbEpisode(21, 72517, 2, 21, "Episode 21", new DateOnly(2021, 8, 31))
                : null,
        };
        var episodes = new FakeBangumiEpisodeClient(
            new Dictionary<int, IReadOnlyList<BangumiEpisode>>
            {
                [547888] = [new BangumiEpisode(12, 0, 12, new DateOnly(2021, 3, 30), 36)],
                [302523] = [new BangumiEpisode(9, 0, 9, new DateOnly(2021, 8, 31), 45)],
            });
        var subjects = new FakeBangumiSubjectClient(
            new Dictionary<int, IReadOnlyList<BangumiSubjectRelation>>
            {
                [547888] = [new BangumiSubjectRelation(302523, 2, "Part 2", "第二部分", "续集")],
            });
        await using var app = await StartSeasonResolvedTaskAsync(
            tmdb,
            episodeOffset: null,
            bangumiEpisodeClient: episodes,
            bangumiSubjectClient: subjects);
        var taskId = await PrepareFilesAsync(app, ("Show - 45.mkv", "45", "45"));
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        var file = Assert.Single(await ReadFilesAsync(app, taskId));
        Assert.Equal("episode", file.Disposition);
        Assert.Equal(21, file.EpisodeNumber);
        Assert.Equal("tmdb_episode_bangumi_date", file.ResolutionSource);
        Assert.Equal([547888, 302523], episodes.SubjectIds);
    }

    [Fact]
    public async Task MikanSingleFileUsesFilenameConfirmedNearestDateWithinSevenDays()
    {
        var tmdb = new FakeTmdbClient
        {
            SeasonValue = new TmdbSeason(
                300,
                72517,
                2,
                "Season 2",
                new DateOnly(2026, 7, 3),
                1,
                Episodes:
                [new TmdbEpisode(606, 72517, 2, 6, "Episode 6", new DateOnly(2026, 8, 10))]),
            EpisodeFactory = number => number == 6
                ? new TmdbEpisode(606, 72517, 2, 6, "Episode 6", new DateOnly(2026, 8, 10))
                : null,
        };
        var bangumi = new FakeBangumiEpisodeClient(
        [
            new BangumiEpisode(600, 0, 6, new DateOnly(2026, 8, 7)),
        ]);
        await using var app = await StartSeasonResolvedTaskAsync(
            tmdb,
            episodeOffset: null,
            bangumiEpisodeClient: bangumi);
        var taskId = await PrepareFilesAsync(app, ("Kokoore - 06.mkv", "6", "6"));
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        var file = Assert.Single(await ReadFilesAsync(app, taskId));
        Assert.Equal("episode", file.Disposition);
        Assert.Equal(6, file.EpisodeNumber);
        Assert.Equal("tmdb_episode_bangumi_nearest_date", file.ResolutionSource);
        Assert.Equal([6], tmdb.EpisodeRequests);
    }

    [Fact]
    public async Task MikanMultipleFilesWithoutPrimaryDateMatchesUseOneTaskLevelAiCall()
    {
        var tmdb = new FakeTmdbClient
        {
            SeasonValue = new TmdbSeason(
                301,
                72517,
                2,
                "Season 2",
                new DateOnly(2026, 7, 3),
                2,
                Episodes:
                [
                    new TmdbEpisode(605, 72517, 2, 5, "Episode 5", new DateOnly(2026, 8, 3)),
                    new TmdbEpisode(606, 72517, 2, 6, "Episode 6", new DateOnly(2026, 8, 10)),
                ]),
            EpisodeFactory = number => number is 5 or 6
                ? new TmdbEpisode(600 + number, 72517, 2, number, $"Episode {number}",
                    number == 5 ? new DateOnly(2026, 8, 3) : new DateOnly(2026, 8, 10))
                : null,
        };
        var bangumi = new FakeBangumiEpisodeClient(
        [
            new BangumiEpisode(500, 0, 5, new DateOnly(2026, 7, 31)),
            new BangumiEpisode(600, 0, 6, new DateOnly(2026, 8, 7)),
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
                    file.Name.Contains("05", StringComparison.Ordinal) ? 5 : 6,
                    null)).ToArray(),
                null),
        };
        await using var app = await StartSeasonResolvedTaskAsync(
            tmdb,
            episodeOffset: null,
            aiMatcher: ai,
            enableEpisodeAi: true,
            bangumiEpisodeClient: bangumi);
        var taskId = await PrepareFilesAsync(
            app,
            ("Kokoore - 05.mkv", "5", "5"),
            ("Kokoore - 06.mkv", "6", "6"));
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        Assert.Single(ai.Requests);
        var files = await ReadFilesAsync(app, taskId);
        Assert.Collection(
            files,
            file => Assert.Equal(5, file.EpisodeNumber),
            file => Assert.Equal(6, file.EpisodeNumber));
        Assert.All(files, file => Assert.Equal("ai_metadata", file.ResolutionSource));
    }

    [Fact]
    public async Task AiNaturalLanguageOtherReasonDoesNotBlockEpisodeCompletion()
    {
        var tmdb = new FakeTmdbClient();
        var ai = new FakeAiMetadataMatcher
        {
            ResultFactory = input => new AiMetadataMatchCandidate(
                true,
                72517,
                [new(input.Files[0].Name, false, 2, null, "该文件是特典，不是正片 Episode。")],
                null),
        };
        await using var app = await StartSeasonResolvedTaskAsync(
            tmdb,
            episodeOffset: null,
            aiMatcher: ai,
            enableEpisodeAi: true);
        var taskId = await PrepareFilesAsync(app, ("Show bonus.mkv", null, null));
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        var file = Assert.Single(await ReadFilesAsync(app, taskId));
        Assert.Equal("extras", file.Disposition);
        Assert.Equal("episode_not_parsed", file.OtherReason);
        Assert.Equal("metadata_resolved", await ReadTaskStatusAsync(app, taskId));
    }

    [Fact]
    public async Task MikanSingleFileNearestDateBeyondSevenDaysUsesAi()
    {
        var tmdb = new FakeTmdbClient
        {
            SeasonValue = new TmdbSeason(
                302,
                72517,
                2,
                "Season 2",
                new DateOnly(2026, 7, 3),
                1,
                Episodes:
                [new TmdbEpisode(606, 72517, 2, 6, "Episode 6", new DateOnly(2026, 8, 16))]),
            EpisodeFactory = number => number == 6
                ? new TmdbEpisode(606, 72517, 2, 6, "Episode 6", new DateOnly(2026, 8, 16))
                : null,
        };
        var bangumi = new FakeBangumiEpisodeClient(
        [
            new BangumiEpisode(600, 0, 6, new DateOnly(2026, 8, 7)),
        ]);
        var ai = new FakeAiMetadataMatcher
        {
            ResultFactory = input => new AiMetadataMatchCandidate(
                true,
                72517,
                [new(input.Files[0].Name, true, 2, 6, null)],
                null),
        };
        await using var app = await StartSeasonResolvedTaskAsync(
            tmdb,
            episodeOffset: null,
            aiMatcher: ai,
            enableEpisodeAi: true,
            bangumiEpisodeClient: bangumi);
        var taskId = await PrepareFilesAsync(app, ("Kokoore - 06.mkv", "6", "6"));
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        Assert.Single(ai.Requests);
        var file = Assert.Single(await ReadFilesAsync(app, taskId));
        Assert.Equal(6, file.EpisodeNumber);
        Assert.Equal("ai_metadata", file.ResolutionSource);
        Assert.Equal(
            "tmdb_episode_bangumi_nearest_date_too_distant",
            await ReadLatestAttemptErrorAsync(
                app,
                taskId,
                "tmdb_episode_bangumi_nearest_date"));
        Assert.Equal(
            "episode_unresolved:tmdb_episode_bangumi_nearest_date_too_distant",
            await ReadLatestAiTriggerReasonAsync(app, taskId));
    }

    [Fact]
    public async Task MikanInRangeMarkerConflictRecordsPreciseAiTriggerReason()
    {
        var tmdb = new FakeTmdbClient
        {
            EpisodeFactory = number => number == 7
                ? new TmdbEpisode(7007, 72517, 2, 7, "Episode 7", null)
                : null,
        };
        var ai = new FakeAiMetadataMatcher
        {
            ResultFactory = input => new AiMetadataMatchCandidate(
                true,
                72517,
                [new(input.Files[0].Name, true, 2, 7, null)],
                null),
        };
        await using var app = await StartSeasonResolvedTaskAsync(
            tmdb,
            episodeOffset: null,
            aiMatcher: ai,
            enableEpisodeAi: true);
        var taskId = await PrepareFilesAsync(
            app,
            ("[Dynamis One] Kokoore - 07 (CR 1920x1080 AVC AAC MKV) [08].mkv", "7", null));
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        Assert.Single(ai.Requests);
        Assert.Equal(
            "episode_unresolved:ambiguous_episode_markers",
            await ReadLatestAiTriggerReasonAsync(app, taskId));
        var file = Assert.Single(await ReadFilesAsync(app, taskId));
        Assert.Equal(7, file.EpisodeNumber);
        Assert.Equal("ai_metadata", file.ResolutionSource);
    }

    [Fact]
    public async Task KokooreOversizedChecksumResolvesTmdbEpisodeWithoutAi()
    {
        const string title =
            "[黒ネズミたち] 说出这边交给我你们先走以后十年过去成了传说。 / Kokoore - 07 (CR 1920x1080 AVC AAC MKV)";
        const string fileName =
            "[Dynamis One] Kokoore - 07 (CR 1920x1080 AVC AAC MKV) [13335833].mkv";
        var parsed = FileEpisodeCandidateResolver.Resolve("mikan", fileName);
        Assert.Equal(7, parsed.Episode);

        var series = new TmdbSeries(
            302051,
            "ここは俺に任せて先に行けと言ってから10年がたったら伝説になっていた。",
            "ここは俺に任せて先に行けと言ってから10年がたったら伝説になっていた。",
            null);
        var season = new TmdbSeason(500001, 302051, 1, "第 1 季", null, 12);
        var tmdb = new FakeTmdbClient
        {
            SeriesValue = series,
            SeasonValue = season,
            EpisodeFactory = number => number == 7
                ? new TmdbEpisode(7007, 302051, 1, 7, "第 7 集", null)
                : null,
        };
        var ai = new FakeAiMetadataMatcher();
        await using var app = await StartSeasonResolvedTaskAsync(
            tmdb,
            episodeOffset: null,
            aiMatcher: ai,
            enableEpisodeAi: true,
            tmdbSeriesId: 302051,
            tmdbSeasonNumber: 1);
        var taskId = await PrepareFilesAsync(
            app,
            (fileName, "7", parsed.Episode?.ToString(CultureInfo.InvariantCulture)));
        await SetTaskTitleAsync(app, taskId, title);
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        Assert.Empty(ai.Requests);
        var file = Assert.Single(await ReadFilesAsync(app, taskId));
        Assert.Equal("episode", file.Disposition);
        Assert.Equal(7, file.EpisodeNumber);
        Assert.Equal("tmdb_episode_number", file.ResolutionSource);
        Assert.Null(await ReadLatestAiTriggerReasonAsync(app, taskId));
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
        using var logSocket = new ClientWebSocket();
        await logSocket.ConnectAsync(WebSocketUri(app), CancellationToken.None);

        Assert.True(await app.App.Services.GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        var duplicateLog = await ReceiveUntilAsync(
            logSocket,
            value => value.Contains("(4301)", StringComparison.Ordinal));
        Assert.Contains("tmdb:72517:s2:e4", duplicateLog, StringComparison.Ordinal);
        Assert.Contains("episode_claimed_by_another_task", duplicateLog, StringComparison.Ordinal);

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
    [InlineData("poster.jpg", null, "non_video_attachment")]
    public async Task NonIntegerOrUnknownFileGoesToExtrasWithoutTmdbRequest(
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
        Assert.Equal(path.EndsWith(".jpg", StringComparison.Ordinal) ? "other" : "extras", file.Disposition);
        Assert.Null(file.EpisodeNumber);
        Assert.Equal(expectedReason, file.OtherReason);
        Assert.Empty(tmdb.EpisodeRequests);

        var attempts = await app.App.Services
            .GetRequiredService<MetadataResolutionStore>()
            .ListAttemptsAsync(taskId);
        var attempt = Assert.Single(attempts, value => value.ErrorCode == expectedReason);
        Assert.Contains(path, attempt.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Show 劇場版.mkv")]
    [InlineData("Show 剧场版.mkv")]
    [InlineData("SHOW MOVIE.mkv")]
    public async Task MovieHintInsideTvTaskRemainsOtherForMixedMediaPostprocessing(
        string path)
    {
        var tmdb = new FakeTmdbClient();
        await using var app = await StartSeasonResolvedTaskAsync(tmdb, episodeOffset: null);
        var taskId = await PrepareFilesAsync(app, (path, null, null));
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        var file = Assert.Single(await ReadFilesAsync(app, taskId));
        Assert.Equal("other", file.Disposition);
        Assert.Equal("episode_not_parsed", file.OtherReason);
        Assert.Empty(tmdb.EpisodeRequests);
    }

    [Fact]
    public async Task MatchedVideoMakesUnmatchedNonVideoFilesExtrasWithoutOtherAttention()
    {
        var tmdb = new FakeTmdbClient
        {
            EpisodeFactory = number => new TmdbEpisode(
                9000 + number,
                72517,
                2,
                number,
                $"Episode {number}",
                null),
        };
        await using var app = await StartSeasonResolvedTaskAsync(tmdb, episodeOffset: null);
        var taskId = await PrepareFilesAsync(
            app,
            ("Medalist 14.mkv", "14", "14"),
            ("Medalist 14 [Fonts].7z", "14", "14"),
            ("Medalist [Subtitles].7z", null, null));
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        var files = await ReadFilesAsync(app, taskId);
        var video = files.Single(file => file.Path.EndsWith(".mkv", StringComparison.Ordinal));
        Assert.Equal("episode", video.Disposition);
        Assert.Equal(14, video.EpisodeNumber);
        Assert.All(files.Where(file => file.Path.EndsWith(".7z", StringComparison.Ordinal)), file =>
        {
            Assert.Equal("extras", file.Disposition);
            Assert.Equal("non_video_attachment", file.OtherReason);
            Assert.Null(file.EpisodeNumber);
        });
        Assert.Equal([14], tmdb.EpisodeRequests);

        var task = Assert.Single(
            await app.App.Services.GetRequiredService<MetadataResolutionStore>()
                .ListTasksAsync(),
            item => item.TaskId == taskId);
        Assert.Equal(0, task.OtherFileCount);
        Assert.Equal(1, task.EpisodeFileCount);
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
        Assert.Equal(0, tmdb.EpisodeRefreshCalls);
    }

    [Fact]
    public async Task MissingAutomaticTmdbEpisodeGoesToExtrasInConfirmedSeason()
    {
        var tmdb = new FakeTmdbClient { EpisodeFactory = _ => null };
        await using var app = await StartSeasonResolvedTaskAsync(tmdb, episodeOffset: null);
        var taskId = await PrepareFilesAsync(app, ("Show EP12.mkv", "12", "12"));
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services.GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        var file = Assert.Single(await ReadFilesAsync(app, taskId));
        Assert.Equal("extras", file.Disposition);
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
        Assert.Equal([13, 13], tmdb.EpisodeRequests);
        Assert.Equal(1, tmdb.EpisodeRefreshCalls);
    }

    [Fact]
    public async Task EpisodeAiResolvesUnparsedVideoAndPreservesWorkLevelIds()
    {
        var tmdb = new FakeTmdbClient
        {
            EpisodeFactory = number => new TmdbEpisode(
                9000 + number,
                72517,
                2,
                number,
                $"Episode {number}",
                null),
        };
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
        await using var app = await StartSeasonResolvedTaskAsync(
            tmdb,
            episodeOffset: null,
            aiMatcher: ai,
            enableEpisodeAi: true,
            bangumiEpisodeClient: new FakeBangumiEpisodeClient([]));
        var taskId = await PrepareFilesAsync(
            app,
            ("Show unknown.mkv", null, null),
            ("Show unknown.zh-Hans.ass", null, null));
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        var files = await ReadFilesAsync(app, taskId);
        Assert.Equal(2, files.Length);
        Assert.All(files, file =>
        {
            Assert.Equal("episode", file.Disposition);
            Assert.Equal(7, file.EpisodeNumber);
        });
        var video = files.Single(file => file.Path.EndsWith(".mkv", StringComparison.Ordinal));
        var subtitle = files.Single(file => file.Path.EndsWith(".ass", StringComparison.Ordinal));
        Assert.Equal(video.FileId, subtitle.AssociatedFileId);
        Assert.Equal(".zh-Hans.ass", subtitle.RenameSuffix);
        var request = Assert.Single(ai.Requests);
        Assert.Equal(547888, request.BangumiSubjectId);
        Assert.Equal(999, request.AniDbAnimeId);
        Assert.Equal("tt1234567", request.ImdbTitleId);
        Assert.Equal(2, request.TorrentFileCount);
        Assert.Single(request.Files);
        Assert.False(request.UseBangumiPubDateFirst);
        Assert.Null(request.PublishedAt);
        Assert.Equal([7], tmdb.EpisodeRequests);
    }

    [Fact]
    public async Task U2CompleteSeasonSetPassesLocallyAndUnnumberedExtraDoesNotTriggerAi()
    {
        var tmdb = new FakeTmdbClient
        {
            SeasonValue = U2Season(1, 2, 3),
            EpisodeFactory = number => new TmdbEpisode(
                9000 + number, 72517, 2, number, $"Episode {number}", null),
        };
        var ai = new FakeAiMetadataMatcher();
        await using var app = await StartSeasonResolvedTaskAsync(
            tmdb,
            episodeOffset: null,
            aiMatcher: ai,
            enableEpisodeAi: true);
        var taskId = await PrepareFilesAsync(
            app,
            ("Show 01.mkv", "1", "1"),
            ("Show 02.mkv", "2", "2"),
            ("Show 03.mkv", "3", "3"),
            ("Show NCOP.mkv", null, null));
        await SetSourceAdapterAsync(app, taskId, "u2");
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        Assert.Empty(ai.Requests);
        var files = await ReadFilesAsync(app, taskId);
        Assert.Equal([1, 2, 3], files.Where(file => file.Disposition == "episode")
            .Select(file => file.EpisodeNumber).ToArray());
        var extra = files.Single(file => file.Path.EndsWith("NCOP.mkv", StringComparison.Ordinal));
        Assert.Equal("extras", extra.Disposition);
    }

    [Fact]
    public async Task U2IncompleteTorrentInvokesUnifiedAiExactlyOnce()
    {
        var tmdb = new FakeTmdbClient
        {
            SeasonValue = U2Season(1, 2, 3),
            EpisodeFactory = number => new TmdbEpisode(
                9000 + number, 72517, 2, number, $"Episode {number}", null),
        };
        var ai = new FakeAiMetadataMatcher
        {
            ResultFactory = input => new AiMetadataMatchCandidate(
                true,
                72517,
                [
                    new(input.Files[0].Name, true, 2, 2, null),
                    new(input.Files[1].Name, true, 2, 1, null),
                ],
                null),
        };
        await using var app = await StartSeasonResolvedTaskAsync(
            tmdb,
            episodeOffset: null,
            aiMatcher: ai,
            enableEpisodeAi: true);
        var taskId = await PrepareFilesAsync(
            app,
            ("Show 01.mkv", "1", "1"),
            ("Show 02.mkv", "2", "2"));
        await SetSourceAdapterAsync(app, taskId, "u2");
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        Assert.Single(ai.Requests);
        Assert.Equal("metadata_resolved", await ReadTaskStatusAsync(app, taskId));
        var files = await ReadFilesAsync(app, taskId);
        Assert.All(files, file => Assert.Equal("episode", file.Disposition));
        Assert.Equal(2, files.Single(file => file.Path == "Show 01.mkv").EpisodeNumber);
        Assert.Equal(1, files.Single(file => file.Path == "Show 02.mkv").EpisodeNumber);
    }

    [Fact]
    public async Task U2OverallAiMatchPlacesIndividuallyUnmatchedFilesInExtras()
    {
        var tmdb = new FakeTmdbClient
        {
            SeasonValue = U2Season(1, 2, 3),
            EpisodeFactory = number => number == 1
                ? new TmdbEpisode(9001, 72517, 2, 1, "Episode 1", null)
                : null,
        };
        var ai = new FakeAiMetadataMatcher
        {
            ResultFactory = input => new AiMetadataMatchCandidate(
                true,
                72517,
                input.Files.Select(file => file.Name.Contains("Movie", StringComparison.Ordinal)
                    ? new AiMetadataFileCandidate(
                        file.Name,
                        false,
                        2,
                        null,
                        "movie for postprocessing")
                    : new AiMetadataFileCandidate(file.Name, true, 2, 1, null)).ToArray(),
                null),
        };
        await using var app = await StartSeasonResolvedTaskAsync(
            tmdb,
            episodeOffset: null,
            aiMatcher: ai,
            enableEpisodeAi: true);
        var taskId = await PrepareFilesAsync(
            app,
            ("TV/Show 01.mkv", "1", "1"),
            ("Movie/Show Movie.mkv", null, null));
        await SetSourceAdapterAsync(app, taskId, "u2");
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        Assert.Single(ai.Requests);
        Assert.Equal("metadata_resolved", await ReadTaskStatusAsync(app, taskId));
        var files = await ReadFilesAsync(app, taskId);
        var episode = files.Single(file => file.Path == "TV/Show 01.mkv");
        Assert.Equal("episode", episode.Disposition);
        Assert.Equal(1, episode.EpisodeNumber);
        var extra = files.Single(file => file.Path == "Movie/Show Movie.mkv");
        Assert.Equal("extras", extra.Disposition);
        Assert.Null(extra.EpisodeNumber);
        Assert.Equal("u2_explicit_extra", extra.OtherReason);
    }

    [Fact]
    public async Task U2AiFailureDoesNotRelabelTmdbValidatedLocalCandidatesAsParseFailures()
    {
        var tmdb = new FakeTmdbClient
        {
            SeasonValue = U2Season(1, 2, 3),
        };
        var ai = new FakeAiMetadataMatcher
        {
            Failure = new AiMetadataMatcherException(
                MetadataFailureKind.Configuration,
                "ai_provider_not_configured"),
        };
        await using var app = await StartSeasonResolvedTaskAsync(
            tmdb,
            episodeOffset: null,
            aiMatcher: ai,
            enableEpisodeAi: true);
        var taskId = await PrepareFilesAsync(
            app,
            ("Show 01.mkv", "1", "1"),
            ("Show unknown.mkv", null, null));
        await SetSourceAdapterAsync(app, taskId, "u2");
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        Assert.Single(ai.Requests);
        var files = await ReadFilesAsync(app, taskId);
        Assert.Equal(
            "u2_episode_candidate_tmdb_verified_ai_required",
            files.Single(file => file.Path == "Show 01.mkv").OtherReason);
        Assert.Equal(
            "ai_provider_not_configured",
            files.Single(file => file.Path == "Show unknown.mkv").OtherReason);
        Assert.Equal(
            "ai_provider_not_configured",
            await ReadLatestAttemptErrorAsync(app, taskId, "ai_metadata"));
    }

    [Fact]
    public async Task U2AiCannotHideOrdinaryVideoAsUnmatchedExtra()
    {
        var tmdb = new FakeTmdbClient
        {
            SeasonValue = U2Season(1, 2, 3),
            EpisodeFactory = number => new TmdbEpisode(
                9000 + number, 72517, 2, number, $"Episode {number}", null),
        };
        var ai = new FakeAiMetadataMatcher
        {
            ResultFactory = input => new AiMetadataMatchCandidate(
                true,
                72517,
                input.Files.Select(file => new AiMetadataFileCandidate(
                    file.Name, false, 2, null, "not an episode")).ToArray(),
                null),
        };
        await using var app = await StartSeasonResolvedTaskAsync(
            tmdb,
            episodeOffset: null,
            aiMatcher: ai,
            enableEpisodeAi: true);
        var taskId = await PrepareFilesAsync(app, ("Show unknown.mkv", null, null));
        await SetSourceAdapterAsync(app, taskId, "u2");
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        Assert.Single(ai.Requests);
        var file = Assert.Single(await ReadFilesAsync(app, taskId));
        Assert.Equal("other", file.Disposition);
        Assert.Equal("ai_u2_main_video_unmatched", file.OtherReason);
    }

    [Fact]
    public async Task U2AiCanExplicitlyClassifyOrdinaryVideoAsExtras()
    {
        var tmdb = new FakeTmdbClient
        {
            SeasonValue = U2Season(1, 2, 3),
        };
        var ai = new FakeAiMetadataMatcher
        {
            ResultFactory = input => new AiMetadataMatchCandidate(
                true,
                72517,
                input.Files.Select(file => new AiMetadataFileCandidate(
                    file.Name,
                    false,
                    2,
                    AiMetadataFileCandidate.ExtrasEpisodeSentinel,
                    "AI classified this file as Extras.")).ToArray(),
                null),
        };
        await using var app = await StartSeasonResolvedTaskAsync(
            tmdb,
            episodeOffset: null,
            aiMatcher: ai,
            enableEpisodeAi: true);
        var taskId = await PrepareFilesAsync(app, ("Show unknown.mkv", null, null));
        await SetSourceAdapterAsync(app, taskId, "u2");
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        Assert.Single(ai.Requests);
        var file = Assert.Single(await ReadFilesAsync(app, taskId));
        Assert.Equal("extras", file.Disposition);
        Assert.Equal("ai_episode_extra", file.OtherReason);
        Assert.Null(file.EpisodeNumber);
    }

    [Fact]
    public async Task EpisodeAiResultOverridesLocalEpisodeCandidates()
    {
        var tmdb = new FakeTmdbClient
        {
            EpisodeFactory = number => new TmdbEpisode(
                9000 + number,
                72517,
                2,
                number,
                $"Episode {number}",
                null),
        };
        var ai = new FakeAiMetadataMatcher
        {
            ResultFactory = input => new AiMetadataMatchCandidate(
                true,
                72517,
                [
                    new(input.Files[0].Name, true, 2, 9, null),
                    new(input.Files[1].Name, true, 2, 5, null),
                ],
                null),
        };
        await using var app = await StartSeasonResolvedTaskAsync(
            tmdb,
            episodeOffset: null,
            aiMatcher: ai,
            enableEpisodeAi: true);
        var taskId = await PrepareFilesAsync(
            app,
            ("A EP04.mkv", "4", "4"),
            ("B unknown.mkv", null, null));
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        var files = await ReadFilesAsync(app, taskId);
        Assert.Equal(9, files[0].EpisodeNumber);
        Assert.Equal(5, files[1].EpisodeNumber);
        Assert.Equal([4, 9, 5], tmdb.EpisodeRequests);
        Assert.Null(await ReadLatestAttemptErrorAsync(app, taskId, "ai_metadata"));
    }

    [Fact]
    public async Task EpisodeAiConfigurationFailureFallsThroughToConfirmedSeasonOther()
    {
        var ai = new FakeAiMetadataMatcher
        {
            Failure = new AiMetadataMatcherException(
                MetadataFailureKind.Configuration,
                "ai_provider_not_configured"),
        };
        await using var app = await StartSeasonResolvedTaskAsync(
            new FakeTmdbClient(),
            episodeOffset: null,
            aiMatcher: ai,
            enableEpisodeAi: true);
        var taskId = await PrepareFilesAsync(app, ("Show unknown.mkv", null, null));
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        var file = Assert.Single(await ReadFilesAsync(app, taskId));
        Assert.Equal("extras", file.Disposition);
        Assert.Equal("ai_provider_not_configured", file.OtherReason);
        Assert.Equal("metadata_resolved", await ReadTaskStatusAsync(app, taskId));
        Assert.Equal(
            "ai_provider_not_configured",
            await ReadLatestAttemptErrorAsync(app, taskId, "ai_metadata"));
    }

    [Fact]
    public async Task EpisodeAiNetworkFailureKeepsFilesPendingForRetry()
    {
        var ai = new FakeAiMetadataMatcher
        {
            Failure = new AiMetadataMatcherException(
                MetadataFailureKind.Network,
                "ai_network_error"),
        };
        await using var app = await StartSeasonResolvedTaskAsync(
            new FakeTmdbClient(),
            episodeOffset: null,
            aiMatcher: ai,
            enableEpisodeAi: true);
        var taskId = await PrepareFilesAsync(app, ("Show unknown.mkv", null, null));
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        var file = Assert.Single(await ReadFilesAsync(app, taskId));
        Assert.Equal("pending", file.Disposition);
        Assert.Equal("metadata_failed", await ReadTaskStatusAsync(app, taskId));
        Assert.Equal(
            "ai_network_error",
            await ReadLatestAttemptErrorAsync(app, taskId, "ai_metadata"));
    }

    [Fact]
    public async Task ManualOffsetRuleSuppressesEpisodeAi()
    {
        var ai = new FakeAiMetadataMatcher();
        await using var app = await StartSeasonResolvedTaskAsync(
            new FakeTmdbClient(),
            episodeOffset: 12,
            aiMatcher: ai,
            enableEpisodeAi: true);
        var taskId = await PrepareFilesAsync(app, ("Show unknown.mkv", null, null));
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        Assert.Empty(ai.Requests);
        Assert.Equal("extras", Assert.Single(await ReadFilesAsync(app, taskId)).Disposition);
    }

    [Fact]
    public async Task EpisodeAiUsesTrustedMikanPublicationEvidence()
    {
        var tmdb = new FakeTmdbClient
        {
            EpisodeFactory = number => new TmdbEpisode(
                9000 + number,
                72517,
                2,
                number,
                $"Episode {number}",
                null),
        };
        var episodes = new FakeBangumiEpisodeClient(
        [
            new BangumiEpisode(100, 0, 7, new DateOnly(2026, 7, 22)),
        ]);
        var ai = new FakeAiMetadataMatcher
        {
            ResultFactory = input => new AiMetadataMatchCandidate(
                true,
                72517,
                [new(input.Files[0].Name, true, 2, 7, null)],
                null),
        };
        await using var app = await StartSeasonResolvedTaskAsync(
            tmdb,
            episodeOffset: null,
            aiMatcher: ai,
            enableEpisodeAi: true,
            bangumiEpisodeClient: episodes);
        var taskId = await PrepareFilesAsync(app, ("Show unknown.mkv", null, null));
        await SetTrustedPublicationEvidenceAsync(app, taskId);
        await ResolveSeasonAsync(app);

        Assert.True(await app.App.Services
            .GetRequiredService<EpisodeMetadataResolutionProcessor>().RunOnceAsync());

        var request = Assert.Single(ai.Requests);
        Assert.True(request.UseBangumiPubDateFirst);
        Assert.Equal(7, request.BangumiEpisodeCandidate);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 22, 12, 34, 56, 123, TimeSpan.FromHours(8)),
            request.PublishedAt);
        Assert.Equal([547888], episodes.SubjectIds);
        var file = Assert.Single(await ReadFilesAsync(app, taskId));
        Assert.Equal("episode", file.Disposition);
        Assert.Equal(7, file.EpisodeNumber);
        Assert.Equal(
            "matched",
            await ReadLatestAttemptResultAsync(app, taskId, "ai_pubdate"));
    }

    private static async Task<RunningApp> StartSeasonResolvedTaskAsync(
        FakeTmdbClient tmdb,
        int? episodeOffset,
        IAiMetadataMatcher? aiMatcher = null,
        bool enableEpisodeAi = false,
        IBangumiEpisodeClient? bangumiEpisodeClient = null,
        IBangumiSubjectClient? bangumiSubjectClient = null,
        int tmdbSeriesId = 72517,
        int tmdbSeasonNumber = 2)
    {
        var app = await RunningApp.StartAsync(
            configure: options => options with
            {
                Metadata = options.Metadata with
                {
                    Ai = options.Metadata.Ai with { UseMetadataMatch = enableEpisodeAi },
                },
            },
            tmdbClient: tmdb,
            bangumiSubjectClient: bangumiSubjectClient ?? new FakeBangumiSubjectClient(
                Array.Empty<BangumiSubjectRelation>()),
            bangumiEpisodeClient: bangumiEpisodeClient ?? new FakeBangumiEpisodeClient([]),
            aiMetadataMatcher: aiMatcher);
        await app.App.Services.GetRequiredService<MikanWorkMetadataRuleStore>().SaveAsync(
            new MikanWorkMetadataRuleUpdate(
                3951,
                bangumiEpisodeClient is null ? null : 547888,
                tmdbSeriesId,
                tmdbSeasonNumber,
                episodeOffset),
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
                "info": {
                  "title": "Episode resolution",
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
        await using (var alignBangumi = connection.CreateCommand())
        {
            alignBangumi.CommandText = """
                UPDATE ingest_tasks
                SET bangumi_subject_id = (
                    SELECT bangumi_subject_id
                    FROM mikan_work_rules
                    WHERE mikanid = 3951 AND enabled = 1)
                WHERE id = $task_id;
                """;
            alignBangumi.Parameters.AddWithValue("$task_id", taskId);
            Assert.Equal(1, await alignBangumi.ExecuteNonQueryAsync());
        }
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

    private static async Task SetTaskTitleAsync(RunningApp app, string taskId, string title)
    {
        var database = app.App.Services
            .GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ingest_tasks SET title = $title WHERE id = $task_id;";
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$task_id", taskId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task SetSourceAdapterAsync(
        RunningApp app,
        string taskId,
        string adapter)
    {
        var database = app.App.Services
            .GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE source_profiles
            SET adapter = $adapter
            WHERE id = (SELECT source_profile_id FROM ingest_tasks WHERE id = $task_id);
            """;
        command.Parameters.AddWithValue("$adapter", adapter);
        command.Parameters.AddWithValue("$task_id", taskId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static TmdbSeason U2Season(params int[] episodes) =>
        new(
            204984,
            72517,
            2,
            "Season 2",
            null,
            episodes.Length,
            Episodes: episodes.Select(number => new TmdbEpisode(
                9000 + number,
                72517,
                2,
                number,
                $"Episode {number}",
                null)).ToArray());

    private static async Task SetTrustedPublicationEvidenceAsync(
        RunningApp app,
        string taskId)
    {
        var database = app.App.Services
            .GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ingest_tasks
            SET source_published_at_raw = '2026-07-22T12:34:56.123',
                source_published_at = '2026-07-22T12:34:56.123+08:00'
            WHERE id = $task_id;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
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
                   associated_task_file_id, rename_suffix,
                   episode_resolution_source, episode_resolution_run_id,
                   episode_resolution_attempt_id
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
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)));
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

    private static async Task<string?> ReadLatestAttemptErrorAsync(
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

    private static async Task<string?> ReadLatestAttemptResultAsync(
        RunningApp app,
        string taskId,
        string strategy)
    {
        var database = app.App.Services
            .GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT attempt.result
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

    private static async Task<string?> ReadLatestAiTriggerReasonAsync(
        RunningApp app,
        string taskId)
    {
        var database = app.App.Services
            .GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT attempt.ai_trigger_reason
            FROM metadata_resolution_attempts AS attempt
            JOIN metadata_resolution_runs AS run ON run.id = attempt.run_id
            WHERE run.task_id = $task_id
              AND attempt.strategy = 'ai_metadata'
            ORDER BY attempt.created_at_utc DESC, attempt.id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        return await command.ExecuteScalarAsync() as string;
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

    private static Uri WebSocketUri(RunningApp app) => new UriBuilder(app.Client.BaseAddress!)
    {
        Scheme = "ws",
        Path = "/websocket/log",
    }.Uri;

    private static async Task<string> ReceiveUntilAsync(
        ClientWebSocket socket,
        Func<string, bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var frame = await ReceiveTextAsync(socket, timeout.Token);
            if (predicate(frame))
            {
                return frame;
            }
        }
        throw new Xunit.Sdk.XunitException(
            "Expected duplicate notification WebSocket frame was not received.");
    }

    private static async Task<string> ReceiveTextAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        using var payload = new MemoryStream();
        ValueWebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new Xunit.Sdk.XunitException(
                    "WebSocket closed before the duplicate notification frame.");
            }
            Assert.Equal(WebSocketMessageType.Text, result.MessageType);
            payload.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);
        return Encoding.UTF8.GetString(payload.GetBuffer(), 0, (int)payload.Length);
    }

    private sealed record FileState(
        string FileId,
        string Path,
        string Disposition,
        int? EpisodeNumber,
        string? OtherReason,
        string? AssociatedFileId,
        string? RenameSuffix,
        string? ResolutionSource,
        string? ResolutionRunId,
        string? ResolutionAttemptId);

    private sealed class FakeTmdbClient : ITmdbRefreshClient
    {
        public TmdbSeries SeriesValue { get; init; } = Series;

        public TmdbSeason SeasonValue { get; init; } = Season;

        public TmdbSeason? RefreshedSeasonValue { get; init; }

        public Func<int, TmdbEpisode?> EpisodeFactory { get; init; } = _ => null;

        public TmdbClientException? EpisodeFailure { get; init; }

        public List<int> EpisodeRequests { get; } = [];

        public int SeasonRefreshCalls { get; private set; }

        public int EpisodeRefreshCalls { get; private set; }

        public Task<IReadOnlyList<TmdbSeries>> RefreshSeriesSearchAsync(
            string title,
            CancellationToken cancellationToken = default) =>
            SearchSeriesAsync(title, cancellationToken);

        public Task<TmdbSeriesDetails?> RefreshSeriesDetailsAsync(
            int seriesId,
            CancellationToken cancellationToken = default) =>
            GetSeriesDetailsAsync(seriesId, cancellationToken);

        public Task<TmdbSeason?> RefreshSeasonAsync(
            int seriesId,
            int seasonNumber,
            CancellationToken cancellationToken = default)
        {
            SeasonRefreshCalls++;
            return Task.FromResult<TmdbSeason?>(
                CompleteSnapshot(RefreshedSeasonValue ?? SeasonValue));
        }

        public Task<TmdbEpisode?> RefreshEpisodeAsync(
            int seriesId,
            int seasonNumber,
            int episodeNumber,
            CancellationToken cancellationToken = default)
        {
            EpisodeRefreshCalls++;
            return GetEpisodeAsync(seriesId, seasonNumber, episodeNumber, cancellationToken);
        }

        public Task<IReadOnlyList<TmdbSeries>> SearchSeriesAsync(string title, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TmdbSeries>>([SeriesValue]);

        public Task<TmdbSeries?> GetSeriesAsync(int seriesId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeries?>(SeriesValue);

        public Task<TmdbSeriesDetails?> GetSeriesDetailsAsync(int seriesId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeriesDetails?>(new TmdbSeriesDetails(SeriesValue, [SeasonValue]));

        public Task<TmdbSeason?> GetSeasonAsync(int seriesId, int seasonNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeason?>(CompleteSnapshot(SeasonValue));

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

        private TmdbSeason CompleteSnapshot(TmdbSeason season)
        {
            if (season.Episodes is not null)
            {
                return season;
            }

            var episodes = Enumerable.Range(1, season.EpisodeCount)
                .Select(number => EpisodeFactory(number)
                    ?? new TmdbEpisode(
                        8_000_000 + (season.SeasonNumber * 100_000) + number,
                        season.SeriesId,
                        season.SeasonNumber,
                        number,
                        $"Episode {number}",
                        null))
                .ToArray();
            return season with { Episodes = episodes };
        }
    }

    private sealed class FakeAiMetadataMatcher : IAiMetadataMatcher
    {
        public Func<AiMetadataMatchInput, AiMetadataMatchCandidate>? ResultFactory { get; init; }

        public AiMetadataMatcherException? Failure { get; init; }

        public List<AiMetadataMatchInput> Requests { get; } = [];

        public Task<AiMetadataMatchResponse> MatchAsync(
            AiMetadataMatchInput input,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(input);
            if (Failure is not null)
            {
                return Task.FromException<AiMetadataMatchResponse>(Failure);
            }

            return Task.FromResult(new AiMetadataMatchResponse(
                ResultFactory?.Invoke(input)
                    ?? new AiMetadataMatchCandidate(false, null, [], "not configured"),
                null));
        }
    }

    private sealed class FakeBangumiEpisodeClient : IBangumiEpisodeClient
    {
        private readonly IReadOnlyList<BangumiEpisode>? _episodes;
        private readonly IReadOnlyDictionary<int, IReadOnlyList<BangumiEpisode>>? _episodesBySubject;

        public FakeBangumiEpisodeClient(IReadOnlyList<BangumiEpisode> episodes) =>
            _episodes = episodes;

        public FakeBangumiEpisodeClient(
            IReadOnlyDictionary<int, IReadOnlyList<BangumiEpisode>> episodesBySubject) =>
            _episodesBySubject = episodesBySubject;

        public List<int> SubjectIds { get; } = [];

        public Task<IReadOnlyList<BangumiEpisode>> GetEpisodesAsync(
            int subjectId,
            CancellationToken cancellationToken = default)
        {
            SubjectIds.Add(subjectId);
            return Task.FromResult(_episodesBySubject is null
                ? _episodes ?? []
                : _episodesBySubject.GetValueOrDefault(subjectId) ?? []);
        }
    }

    private sealed class RefreshingBangumiEpisodeClient(
        IReadOnlyList<BangumiEpisode> cached,
        IReadOnlyList<BangumiEpisode> refreshed,
        BangumiClientException? refreshFailure = null)
        : IBangumiEpisodeRefreshClient
    {
        public List<int> CachedSubjectIds { get; } = [];

        public List<int> RefreshedSubjectIds { get; } = [];

        public Task<IReadOnlyList<BangumiEpisode>> GetEpisodesAsync(
            int subjectId,
            CancellationToken cancellationToken = default)
        {
            CachedSubjectIds.Add(subjectId);
            return Task.FromResult(cached);
        }

        public Task<IReadOnlyList<BangumiEpisode>> RefreshEpisodesAsync(
            int subjectId,
            CancellationToken cancellationToken = default)
        {
            RefreshedSubjectIds.Add(subjectId);
            return refreshFailure is null
                ? Task.FromResult(refreshed)
                : Task.FromException<IReadOnlyList<BangumiEpisode>>(refreshFailure);
        }
    }

    private sealed class FakeBangumiSubjectClient(
        IReadOnlyDictionary<int, IReadOnlyList<BangumiSubjectRelation>> relations)
        : IBangumiSubjectClient
    {
        public FakeBangumiSubjectClient(IReadOnlyList<BangumiSubjectRelation> relations)
            : this(new Dictionary<int, IReadOnlyList<BangumiSubjectRelation>>
            {
                [547888] = relations,
            })
        {
        }

        public Task<BangumiSubject?> GetSubjectAsync(
            int subjectId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BangumiSubject?>(null);

        public Task<IReadOnlyList<BangumiSubjectRelation>> GetRelatedSubjectsAsync(
            int subjectId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(relations.GetValueOrDefault(subjectId) ?? []);
    }
}
