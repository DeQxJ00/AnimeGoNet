using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Compatibility;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Core.Torrents;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Library;
using AnimeGoNet.Data.Mikan;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Sources;

namespace AnimeGoNet.Data.Tests.Metadata;

public sealed class MetadataResolutionStoreTests
{
    [Fact]
    public async Task TaskDetailSeparatesSafeSourceEvidenceFromCanonicalProjection()
    {
        await using var fixture = await MetadataFixture.CreateAsync();

        var detail = Assert.IsType<MetadataTaskDetailProjection>(
            await fixture.Store.GetTaskDetailAsync(fixture.TaskId));

        Assert.Equal("mikan", detail.Source.SourceProfileId);
        Assert.True(detail.Source.SourceProfileRevision > 0);
        Assert.Equal("mikan", detail.Source.SourceId);
        Assert.Equal("Episode", detail.Source.SourceTitle);
        Assert.Equal(
            StableHash.Sha256LowerHex("animegonet-source-id\0mikan\0item\0one"),
            detail.Source.SourceItemIdFingerprint);
        Assert.Equal(
            StableHash.Sha256LowerHex("animegonet-source-id\0mikan\0work\03951"),
            detail.Source.SourceWorkIdFingerprint);
        Assert.Equal(3951, detail.Source.MikanId);
        Assert.Null(detail.Source.GroupId);
        Assert.Equal(547888, detail.Source.BangumiSubjectId);
        Assert.Equal(999, detail.Source.AniDbAnimeId);
        Assert.Equal("tt1234567", detail.Source.ImdbTitleId);
        Assert.True(detail.Source.SourcePublishedAtRawAvailable);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 22, 12, 34, 56, 123, TimeSpan.FromHours(8)),
            detail.Source.SourcePublishedAt);
        Assert.Null(detail.Summary.TmdbSeriesId);
        Assert.Null(detail.Summary.TmdbSeasonNumber);
    }

    [Fact]
    public async Task ConcurrentClaimsReturnDownloadedTaskAtMostOnce()
    {
        await using var fixture = await MetadataFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;

        var claims = await Task.WhenAll(
            fixture.Store.TryClaimNextDownloadedAsync(now, TimeSpan.FromMinutes(1)),
            fixture.Store.TryClaimNextDownloadedAsync(now, TimeSpan.FromMinutes(1)));

        Assert.Single(claims, claim => claim is not null);
    }

    [Fact]
    public async Task ClaimCountsEveryTorrentFileNotOnlyPendingMetadataFiles()
    {
        await using var fixture = await MetadataFixture.CreateAsync();
        await using (var connection = await fixture.Database.OpenConnectionAsync())
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO task_files (
                    id, task_id, relative_path, size_bytes, source_episode,
                    file_episode_candidate, disposition, other_reason)
                VALUES (
                    'ignored-file', $task_id, 'cover.jpg', 10, NULL,
                    NULL, 'ignored', 'not_media');
                """;
            insert.Parameters.AddWithValue("$task_id", fixture.TaskId);
            Assert.Equal(1, await insert.ExecuteNonQueryAsync());
        }

        var claim = Assert.IsType<MetadataTaskClaim>(
            await fixture.Store.TryClaimNextDownloadedAsync(
                DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(1)));

        Assert.Single(claim.Files!);
        Assert.Equal(2, claim.TorrentFileCount);
    }

    [Fact]
    public async Task AttemptTimelinePersistsSafeReasonAndIsQueryableAfterStoreRecreation()
    {
        await using var fixture = await MetadataFixture.CreateAsync();
        var started = DateTimeOffset.UtcNow;
        var claim = Assert.IsType<MetadataTaskClaim>(
            await fixture.Store.TryClaimNextDownloadedAsync(
                started,
                TimeSpan.FromMinutes(1)));
        await fixture.Store.RecordAttemptAsync(
            claim,
            new MetadataAttempt(
                "series",
                "tmdb_title",
                4,
                "failed",
                "tmdb_network_error",
                true,
                claim.AttemptNumber,
                125),
            started.AddSeconds(1));
        await fixture.Store.RecordAttemptAsync(
            claim,
            new MetadataAttempt(
                "season",
                "tmdb_fail_first_season",
                1,
                "matched",
                null,
                false,
                claim.AttemptNumber,
                5,
                "validated local S01 fallback"),
            started.AddSeconds(2));

        var recreated = new MetadataResolutionStore(fixture.Database);
        var attempts = await recreated.ListAttemptsAsync(fixture.TaskId);

        Assert.Equal(2, attempts.Count);
        Assert.Equal("season", attempts[0].Stage);
        Assert.Equal("validated local S01 fallback", attempts[0].Reason);
        Assert.Equal("tmdb_network_error", attempts[1].Reason);
        Assert.True(attempts[1].Retryable);
        Assert.Equal(125, attempts[1].DurationMilliseconds);
        Assert.Equal(claim.RunId, attempts[1].RunId);
        Assert.Equal(claim.AttemptNumber, attempts[1].RunAttemptNumber);
        Assert.Equal("running", attempts[1].RunStatus);
    }

    [Fact]
    public async Task AiUsageIsPersistedOnOneAttemptAndProjectedInTaskDetail()
    {
        await using var fixture = await MetadataFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var claim = Assert.IsType<MetadataTaskClaim>(
            await fixture.Store.TryClaimNextDownloadedAsync(
                now,
                TimeSpan.FromMinutes(1)));
        var usage = new AiMetadataProviderUsage(
            "gpt-5.4-mini",
            120,
            35,
            155,
            2,
            1);
        await fixture.Store.RecordAttemptAsync(
            claim,
            new MetadataAttempt(
                "season",
                "ai_metadata",
                null,
                "matched",
                null,
                false,
                claim.AttemptNumber,
                250,
                AiUsage: usage),
            now);

        var attempt = Assert.Single(await fixture.Store.ListAttemptsAsync(fixture.TaskId));
        Assert.Equal(usage, attempt.AiUsage);
        var detail = Assert.IsType<MetadataTaskDetailProjection>(
            await fixture.Store.GetTaskDetailAsync(fixture.TaskId));
        Assert.Equal(usage, detail.Ai!.Usage);
    }

    [Fact]
    public async Task ManualClaimRequiresEnabledCompleteTmdbOverride()
    {
        await using var fixture = await MetadataFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;

        Assert.Null(await fixture.Store.TryClaimNextManualOverrideAsync(now, TimeSpan.FromMinutes(1)));
        var rules = new MikanWorkMetadataRuleStore(fixture.Database);
        await rules.SaveAsync(
            new MikanWorkMetadataRuleUpdate(3951, 547888, 72517, 2, null),
            expectedRevision: 0,
            now);

        var claim = Assert.IsType<MetadataTaskClaim>(await fixture.Store.TryClaimNextManualOverrideAsync(
            now,
            TimeSpan.FromMinutes(1)));
        Assert.Equal(3951, claim.MikanId);
        Assert.Equal(547888, claim.BangumiSubjectId);
        Assert.Equal(999, claim.AniDbAnimeId);
        Assert.Equal("tt1234567", claim.ImdbTitleId);
        Assert.Equal("mikan", claim.SourceAdapter);
        Assert.Equal("2026-07-22T12:34:56.123", claim.SourcePublishedAtRaw);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 22, 12, 34, 56, 123, TimeSpan.FromHours(8)),
            claim.SourcePublishedAt);
        Assert.Equal(1, claim.TorrentFileCount);
    }

    [Fact]
    public async Task CompletingSeasonPersistsCanonicalLibraryAndTaskProjectionAtomically()
    {
        await using var fixture = await MetadataFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var claim = Assert.IsType<MetadataTaskClaim>(await fixture.Store.TryClaimNextDownloadedAsync(
            now,
            TimeSpan.FromMinutes(1)));
        Assert.Single(claim.Files!);
        await fixture.Store.RecordAttemptAsync(
            claim,
            new MetadataAttempt("series", "tmdb_title", null, "matched", null, false, 1, 12),
            now);
        var series = new TmdbSeries(
            72517,
            "来自深渊",
            "メイドインアビス",
            new DateOnly(2017, 7, 7),
            "/series-poster.jpg");
        var season = new TmdbSeason(
            204984,
            72517,
            2,
            "烈日的黄金乡",
            new DateOnly(2022, 7, 6),
            2,
            "/season-poster.jpg",
            [
                new TmdbEpisode(310001, 72517, 2, 1, "罗盘指向了黑暗", new DateOnly(2022, 7, 6)),
                new TmdbEpisode(310002, 72517, 2, 2, "不归之都", new DateOnly(2022, 7, 13)),
            ]);

        await fixture.Store.CompleteSeasonAsync(claim, series, season, now);

        var run = Assert.IsType<MetadataRunProjection>(await fixture.Store.GetLatestAsync(fixture.TaskId));
        Assert.Equal("season_resolved", run.Status);
        Assert.Equal(72517, run.TmdbSeriesId);
        Assert.Equal(2, run.TmdbSeasonNumber);
        Assert.True(run.TmdbAccessConfirmed);
        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ingest_tasks.status, anime_series.canonical_name,
                   anime_seasons.season_number, task_files.tmdb_series_id,
                   task_files.tmdb_season_number, anime_series.first_air_date,
                   anime_series.poster_path, anime_seasons.air_date,
                   anime_seasons.episode_count, anime_seasons.poster_path
            FROM ingest_tasks
            JOIN task_files ON task_files.task_id = ingest_tasks.id
            JOIN anime_series ON anime_series.tmdb_series_id = task_files.tmdb_series_id
            JOIN anime_seasons ON anime_seasons.series_id = anime_series.id
                              AND anime_seasons.season_number = task_files.tmdb_season_number
            WHERE ingest_tasks.id = $task_id AND task_files.disposition = 'pending';
            """;
        command.Parameters.AddWithValue("$task_id", fixture.TaskId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("metadata_season_resolved", reader.GetString(0));
        Assert.Equal("来自深渊", reader.GetString(1));
        Assert.Equal(2, reader.GetInt32(2));
        Assert.Equal(72517, reader.GetInt32(3));
        Assert.Equal(2, reader.GetInt32(4));
        Assert.Equal("2017-07-07", reader.GetString(5));
        Assert.Equal("/series-poster.jpg", reader.GetString(6));
        Assert.Equal("2022-07-06", reader.GetString(7));
        Assert.Equal(2, reader.GetInt32(8));
        Assert.Equal("/season-poster.jpg", reader.GetString(9));
        await reader.DisposeAsync();
        command.CommandText = """
            SELECT tmdb_episode_id, episode_number, name, air_date
            FROM tmdb_episodes
            ORDER BY episode_number;
            """;
        command.Parameters.Clear();
        await using var episodeReader = await command.ExecuteReaderAsync();
        Assert.True(await episodeReader.ReadAsync());
        Assert.Equal(310001, episodeReader.GetInt32(0));
        Assert.Equal(1, episodeReader.GetInt32(1));
        Assert.Equal("罗盘指向了黑暗", episodeReader.GetString(2));
        Assert.Equal("2022-07-06", episodeReader.GetString(3));
        Assert.True(await episodeReader.ReadAsync());
        Assert.Equal(310002, episodeReader.GetInt32(0));
        Assert.Equal(2, episodeReader.GetInt32(1));
        Assert.False(await episodeReader.ReadAsync());
    }

    [Fact]
    public async Task AiSeasonSeedsVerifiedEpisodeForEpisodeClaim()
    {
        await using var fixture = await MetadataFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var claim = Assert.IsType<MetadataTaskClaim>(
            await fixture.Store.TryClaimNextDownloadedAsync(
                now,
                TimeSpan.FromMinutes(1)));
        await fixture.Store.RecordAttemptAsync(
            claim,
            new MetadataAttempt(
                "season",
                "ai_metadata",
                null,
                "matched",
                null,
                false,
                claim.AttemptNumber,
                10),
            now);
        await fixture.Store.CompleteAiSeasonAsync(
            claim,
            new TmdbSeries(72517, "来自深渊", "メイドインアビス", null),
            new TmdbSeason(204984, 72517, 2, "Season 2", null, 12),
            [new MetadataSeasonFileSeed("episode.mkv", 7, null)],
            now);

        var episodeClaim = Assert.IsType<MetadataEpisodeTaskClaim>(
            await fixture.Store.TryClaimNextSeasonResolvedAsync(
                now.AddSeconds(1),
                TimeSpan.FromMinutes(1)));

        Assert.True(episodeClaim.SeasonResolvedByAi);
        Assert.True(episodeClaim.AiMetadataAttempted);
        Assert.False(episodeClaim.HasMultipleSeasons);
        var file = Assert.Single(episodeClaim.Files);
        Assert.Equal(7, file.PreResolvedEpisodeNumber);
        Assert.Null(file.PreResolvedOtherReason);
        Assert.Equal(2, file.TmdbSeasonNumber);
    }

    [Fact]
    public async Task FailedUnifiedAiAttemptIsRememberedAfterDeterministicSeasonFallback()
    {
        await using var fixture = await MetadataFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var claim = Assert.IsType<MetadataTaskClaim>(
            await fixture.Store.TryClaimNextDownloadedAsync(
                now,
                TimeSpan.FromMinutes(1)));
        await fixture.Store.RecordAttemptAsync(
            claim,
            new MetadataAttempt(
                "season",
                "ai_metadata",
                null,
                "not_matched",
                "ai_no_match",
                false,
                claim.AttemptNumber,
                10),
            now);
        await fixture.Store.CompleteSeasonAsync(
            claim,
            new TmdbSeries(72517, "来自深渊", "メイドインアビス", null),
            new TmdbSeason(204984, 72517, 2, "Season 2", null, 12),
            now);

        var episodeClaim = Assert.IsType<MetadataEpisodeTaskClaim>(
            await fixture.Store.TryClaimNextSeasonResolvedAsync(
                now.AddSeconds(1),
                TimeSpan.FromMinutes(1)));

        Assert.True(episodeClaim.AiMetadataAttempted);
        Assert.False(episodeClaim.SeasonResolvedByAi);
    }

    [Fact]
    public async Task AiCrossSeasonAssignmentPersistsAndCompletesEachFileAgainstItsOwnSeason()
    {
        await using var fixture = await MetadataFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        await using (var connection = await fixture.Database.OpenConnectionAsync())
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO task_files (
                    id, task_id, relative_path, size_bytes, source_episode,
                    file_episode_candidate, disposition, other_reason)
                VALUES (
                    'season-two-file', $task_id, 'season-2/episode-01.mkv', 200,
                    '1', '1', 'pending', NULL);
                """;
            insert.Parameters.AddWithValue("$task_id", fixture.TaskId);
            Assert.Equal(1, await insert.ExecuteNonQueryAsync());
        }

        var claim = Assert.IsType<MetadataTaskClaim>(
            await fixture.Store.TryClaimNextDownloadedAsync(
                now,
                TimeSpan.FromMinutes(1)));
        await fixture.Store.RecordAttemptAsync(
            claim,
            new MetadataAttempt(
                "season",
                "ai_metadata",
                null,
                "matched",
                null,
                false,
                claim.AttemptNumber,
                10),
            now);
        var series = new TmdbSeries(72517, "来自深渊", "メイドインアビス", null);
        var seasonOne = new TmdbSeason(100001, 72517, 1, "Season 1", null, 13);
        var seasonTwo = new TmdbSeason(204984, 72517, 2, "Season 2", null, 12);
        await fixture.Store.CompleteAiSeasonsAsync(
            claim,
            series,
            [seasonOne, seasonTwo],
            [
                new MetadataSeasonFileSeed("episode.mkv", 1, null, 1),
                new MetadataSeasonFileSeed("season-2/episode-01.mkv", 1, null, 2),
            ],
            now);

        await using (var connection = await fixture.Database.OpenConnectionAsync())
        await using (var emulateLegacySnapshot = connection.CreateCommand())
        {
            emulateLegacySnapshot.CommandText = """
                UPDATE ingest_tasks
                SET route_snapshot_json = json_remove(
                    route_snapshot_json,
                    '$.duplicate_notification_enabled')
                WHERE id = $task_id;
                """;
            emulateLegacySnapshot.Parameters.AddWithValue("$task_id", fixture.TaskId);
            Assert.Equal(1, await emulateLegacySnapshot.ExecuteNonQueryAsync());
        }

        var seasonRun = Assert.IsType<MetadataRunProjection>(
            await fixture.Store.GetLatestAsync(fixture.TaskId));
        Assert.Equal("season_resolved", seasonRun.Status);
        Assert.Null(seasonRun.TmdbSeasonNumber);
        var episodeClaim = Assert.IsType<MetadataEpisodeTaskClaim>(
            await fixture.Store.TryClaimNextSeasonResolvedAsync(
                now.AddSeconds(1),
                TimeSpan.FromMinutes(1)));
        Assert.True(episodeClaim.SeasonResolvedByAi);
        Assert.True(episodeClaim.AiMetadataAttempted);
        Assert.True(episodeClaim.HasMultipleSeasons);
        Assert.True(episodeClaim.Resolution.DuplicateNotificationEnabled);
        Assert.Equal(
            [1, 2],
            episodeClaim.Files
                .OrderBy(file => file.TmdbSeasonNumber)
                .Select(file => file.TmdbSeasonNumber)
                .ToArray());

        var episodeAttemptId = await fixture.Store.RecordAttemptAsync(
            episodeClaim.Resolution,
            new MetadataAttempt(
                "episode",
                "ai_metadata",
                null,
                "matched",
                null,
                false,
                episodeClaim.Resolution.AttemptNumber,
                10),
            now.AddSeconds(2));
        await fixture.Store.CompleteEpisodesAsync(
            episodeClaim,
            episodeClaim.Files.Select(file =>
            {
                var seasonNumber = file.TmdbSeasonNumber!.Value;
                return new MetadataEpisodeFileResolution(
                    file.FileId,
                    new TmdbEpisode(
                        900000 + seasonNumber,
                        series.Id,
                        seasonNumber,
                        1,
                        $"S{seasonNumber:00}E01",
                        null),
                    "episode",
                    null,
                    ResolutionSource: TmdbResolutionSource.AiMetadata,
                    ResolutionAttemptId: episodeAttemptId);
            }).ToArray(),
            now.AddSeconds(3));

        await using var verifyConnection = await fixture.Database.OpenConnectionAsync();
        await using var verify = verifyConnection.CreateCommand();
        verify.CommandText = """
            SELECT ingest_tasks.status, task_files.tmdb_season_number, task_files.disposition
            FROM ingest_tasks
            JOIN task_files ON task_files.task_id = ingest_tasks.id
            WHERE ingest_tasks.id = $task_id
            ORDER BY task_files.tmdb_season_number;
            """;
        verify.Parameters.AddWithValue("$task_id", fixture.TaskId);
        await using var reader = await verify.ExecuteReaderAsync();
        for (var expectedSeason = 1; expectedSeason <= 2; expectedSeason++)
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal("metadata_resolved", reader.GetString(0));
            Assert.Equal(expectedSeason, reader.GetInt32(1));
            Assert.Equal("episode", reader.GetString(2));
        }

        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task ExpiredLeaseIsAuditedAndCanBeClaimedAsNextAttempt()
    {
        await using var fixture = await MetadataFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var first = Assert.IsType<MetadataTaskClaim>(await fixture.Store.TryClaimNextDownloadedAsync(
            now,
            TimeSpan.FromSeconds(1)));

        var second = Assert.IsType<MetadataTaskClaim>(await fixture.Store.TryClaimNextDownloadedAsync(
            now.AddSeconds(2),
            TimeSpan.FromMinutes(1)));

        Assert.Equal(first.TaskId, second.TaskId);
        Assert.Equal(2, second.AttemptNumber);
        Assert.NotEqual(first.LeaseToken, second.LeaseToken);
    }

    [Fact]
    public async Task NonAuthoritativeFailureCannotBeMarkedFallbackEligible()
    {
        await using var fixture = await MetadataFixture.CreateAsync();
        var claim = Assert.IsType<MetadataTaskClaim>(await fixture.Store.TryClaimNextDownloadedAsync(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1)));
        var failure = new MetadataFailure(MetadataFailureKind.Network, "tmdb_network_error", false);

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.FailAsync(
            claim,
            failure,
            fallbackEligible: true,
            "network_failure",
            DateTimeOffset.UtcNow));

        await fixture.Store.FailAsync(
            claim,
            failure,
            fallbackEligible: false,
            "tmdb_access_not_confirmed",
            DateTimeOffset.UtcNow);
        var run = Assert.IsType<MetadataRunProjection>(await fixture.Store.GetLatestAsync(fixture.TaskId));
        Assert.Equal(MetadataFailureKind.Network, run.FailureKind);
        Assert.False(run.FallbackEligible);
        Assert.False(run.TmdbAccessConfirmed);

        var detail = Assert.IsType<MetadataTaskDetailProjection>(
            await fixture.Store.GetTaskDetailAsync(fixture.TaskId));
        Assert.Equal("failed", detail.Summary.LatestRunStatus);
        Assert.False(detail.Summary.TmdbAccessConfirmed);
        Assert.False(detail.Summary.BangumiFallbackEligible);
        Assert.Equal(
            "tmdb_access_not_confirmed",
            detail.Summary.BangumiFallbackDenialReason);
    }

    [Fact]
    public async Task AuthoritativeNoMatchAndCompletedFallbackProjectLatestDecision()
    {
        await using var fixture = await MetadataFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var failedClaim = Assert.IsType<MetadataTaskClaim>(
            await fixture.Store.TryClaimNextDownloadedAsync(
                now,
                TimeSpan.FromMinutes(1)));
        await fixture.Store.FailAsync(
            failedClaim,
            new MetadataFailure(
                MetadataFailureKind.SemanticNoMatch,
                "tmdb_series_not_found",
                true),
            fallbackEligible: false,
            "bangumi_fallback_disabled",
            now);

        var failed = Assert.IsType<MetadataTaskDetailProjection>(
            await fixture.Store.GetTaskDetailAsync(fixture.TaskId));
        Assert.Equal("failed", failed.Summary.LatestRunStatus);
        Assert.True(failed.Summary.TmdbAccessConfirmed);
        Assert.False(failed.Summary.BangumiFallbackEligible);
        Assert.Equal(
            "bangumi_fallback_disabled",
            failed.Summary.BangumiFallbackDenialReason);

        Assert.Equal(
            MetadataRetryResult.Retried,
            await fixture.Store.RetryFailedAsync(fixture.TaskId, now.AddSeconds(1)));
        var fallbackClaim = Assert.IsType<MetadataTaskClaim>(
            await fixture.Store.TryClaimNextDownloadedAsync(
                now.AddSeconds(2),
                TimeSpan.FromMinutes(1)));
        await CompleteFallbackAsync(fixture, fallbackClaim, now.AddSeconds(2));

        var completed = Assert.IsType<MetadataTaskDetailProjection>(
            await fixture.Store.GetTaskDetailAsync(fixture.TaskId));
        Assert.Equal("fallback_resolved", completed.Summary.LatestRunStatus);
        Assert.True(completed.Summary.TmdbAccessConfirmed);
        Assert.True(completed.Summary.BangumiFallbackEligible);
        Assert.Null(completed.Summary.BangumiFallbackDenialReason);
    }

    [Fact]
    public async Task FallbackClaimStopsSameMikanEpisodeButReleaseAllowsRetryAndOtherEpisodeContinues()
    {
        await using var fixture = await MetadataFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        await fixture.SetSourceEpisodeAsync(fixture.TaskId, "01");
        var first = Assert.IsType<MetadataTaskClaim>(await fixture.Store.TryClaimNextDownloadedAsync(
            now,
            TimeSpan.FromMinutes(1)));
        await CompleteFallbackAsync(fixture, first, now);
        var firstFile = await fixture.ReadFileAsync(first.TaskId);
        Assert.Equal(("other", "tmdb_fallback_pending_completion"), firstFile.State);

        var competingTaskId = await fixture.AddDownloadedTaskAsync('f', "competing");
        await fixture.SetSourceEpisodeAsync(competingTaskId, "1.0");
        var competing = Assert.IsType<MetadataTaskClaim>(await fixture.Store.TryClaimNextDownloadedAsync(
            now.AddSeconds(1),
            TimeSpan.FromMinutes(1)));
        await CompleteFallbackAsync(fixture, competing, now.AddSeconds(1));
        Assert.Equal(
            ("duplicate", "fallback_claimed_by_another_task"),
            (await fixture.ReadFileAsync(competingTaskId)).State);

        var otherEpisodeTaskId = await fixture.AddDownloadedTaskAsync('d', "other-episode");
        await fixture.SetSourceEpisodeAsync(otherEpisodeTaskId, "2");
        var otherEpisode = Assert.IsType<MetadataTaskClaim>(await fixture.Store.TryClaimNextDownloadedAsync(
            now.AddSeconds(2),
            TimeSpan.FromMinutes(1)));
        await CompleteFallbackAsync(fixture, otherEpisode, now.AddSeconds(2));
        Assert.Equal(
            ("other", "tmdb_fallback_pending_completion"),
            (await fixture.ReadFileAsync(otherEpisodeTaskId)).State);

        var completionStore = new CompletionRecordStore(fixture.Database);
        Assert.True(await completionStore.ReleaseFallbackClaimAsync(
            new FallbackDedupScope("mikan_episode", "3951:source:1"),
            firstFile.FileId));

        var retryTaskId = await fixture.AddDownloadedTaskAsync('c', "retry");
        await fixture.SetSourceEpisodeAsync(retryTaskId, "1");
        var retry = Assert.IsType<MetadataTaskClaim>(await fixture.Store.TryClaimNextDownloadedAsync(
            now.AddSeconds(3),
            TimeSpan.FromMinutes(1)));
        await CompleteFallbackAsync(fixture, retry, now.AddSeconds(3));
        Assert.Equal(
            ("other", "tmdb_fallback_pending_completion"),
            (await fixture.ReadFileAsync(retryTaskId)).State);
        Assert.Equal(2, await fixture.CountFallbackClaimsAsync());
    }

    [Fact]
    public async Task CompletedFallbackScopeStopsLaterTaskBeforeDownloadResume()
    {
        await using var fixture = await MetadataFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        await fixture.SetSourceEpisodeAsync(fixture.TaskId, "7");
        var first = Assert.IsType<MetadataTaskClaim>(await fixture.Store.TryClaimNextDownloadedAsync(
            now,
            TimeSpan.FromMinutes(1)));
        await CompleteFallbackAsync(fixture, first, now);
        await fixture.MarkFallbackCompletedAsync(
            new FallbackDedupScope("mikan_episode", "3951:source:7"),
            now.AddSeconds(1));

        var laterTaskId = await fixture.AddDownloadedTaskAsync('b', "completed-duplicate");
        await fixture.SetSourceEpisodeAsync(laterTaskId, "07.0");
        var later = Assert.IsType<MetadataTaskClaim>(await fixture.Store.TryClaimNextDownloadedAsync(
            now.AddSeconds(2),
            TimeSpan.FromMinutes(1)));
        await CompleteFallbackAsync(fixture, later, now.AddSeconds(2));

        Assert.Equal(
            ("duplicate", "fallback_already_completed"),
            (await fixture.ReadFileAsync(laterTaskId)).State);
    }

    [Fact]
    public async Task FilesInSameTaskShareOneFallbackEpisodeClaim()
    {
        await using var fixture = await MetadataFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        await fixture.SetSourceEpisodeAsync(fixture.TaskId, "3");
        await fixture.AddPendingFileAsync("episode.zh-Hans.ass", 20, "3");
        var claim = Assert.IsType<MetadataTaskClaim>(await fixture.Store.TryClaimNextDownloadedAsync(
            now,
            TimeSpan.FromMinutes(1)));

        await CompleteFallbackAsync(fixture, claim, now);

        Assert.Equal(2, await fixture.CountFilesByStateAsync("other", "tmdb_fallback_pending_completion"));
        Assert.Equal(1, await fixture.CountFallbackClaimsAsync());
    }

    [Fact]
    public async Task FailedTaskCanBeRetriedWithoutDeletingResolutionHistory()
    {
        await using var fixture = await MetadataFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var first = Assert.IsType<MetadataTaskClaim>(await fixture.Store.TryClaimNextDownloadedAsync(
            now,
            TimeSpan.FromMinutes(1)));
        await fixture.Store.FailAsync(
            first,
            new MetadataFailure(MetadataFailureKind.SemanticNoMatch, "tmdb_series_not_found", true),
            fallbackEligible: false,
            "manual_override_active",
            now);

        Assert.Equal(MetadataRetryResult.Retried, await fixture.Store.RetryFailedAsync(
            fixture.TaskId,
            now.AddSeconds(1)));
        var second = Assert.IsType<MetadataTaskClaim>(await fixture.Store.TryClaimNextDownloadedAsync(
            now.AddSeconds(2),
            TimeSpan.FromMinutes(1)));

        Assert.Equal(2, second.AttemptNumber);
        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM metadata_resolution_runs WHERE task_id = $task_id;";
        command.Parameters.AddWithValue("$task_id", fixture.TaskId);
        Assert.Equal(2L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task RetryRejectsTaskThatIsNotFailed()
    {
        await using var fixture = await MetadataFixture.CreateAsync();

        Assert.Equal(MetadataRetryResult.InvalidState, await fixture.Store.RetryFailedAsync(
            fixture.TaskId,
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task RetryRejectsTaskWithActiveResolutionLease()
    {
        await using var fixture = await MetadataFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        Assert.NotNull(await fixture.Store.TryClaimNextDownloadedAsync(now, TimeSpan.FromMinutes(1)));

        Assert.Equal(MetadataRetryResult.ActiveLease, await fixture.Store.RetryFailedAsync(
            fixture.TaskId,
            now.AddSeconds(1)));
    }

    [Fact]
    public async Task ConcurrentEpisodeClaimsReturnSeasonResolvedTaskAtMostOnce()
    {
        await using var fixture = await MetadataFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var seasonClaim = Assert.IsType<MetadataTaskClaim>(await fixture.Store.TryClaimNextDownloadedAsync(
            now,
            TimeSpan.FromMinutes(1)));
        await using (var connection = await fixture.Database.OpenConnectionAsync())
        await using (var disableNotification = connection.CreateCommand())
        {
            disableNotification.CommandText = """
                UPDATE ingest_tasks
                SET route_snapshot_json = json_set(
                    route_snapshot_json,
                    '$.duplicate_notification_enabled',
                    0)
                WHERE id = $task_id;
                """;
            disableNotification.Parameters.AddWithValue("$task_id", fixture.TaskId);
            Assert.Equal(1, await disableNotification.ExecuteNonQueryAsync());
        }
        await fixture.Store.CompleteSeasonAsync(
            seasonClaim,
            new TmdbSeries(72517, "来自深渊", "メイドインアビス", new DateOnly(2017, 7, 7)),
            new TmdbSeason(204984, 72517, 2, "烈日的黄金乡", new DateOnly(2022, 7, 6), 12),
            now);

        var claims = await Task.WhenAll(
            fixture.Store.TryClaimNextSeasonResolvedAsync(now, TimeSpan.FromMinutes(1)),
            fixture.Store.TryClaimNextSeasonResolvedAsync(now, TimeSpan.FromMinutes(1)));

        var episodeClaim = Assert.Single(claims, value => value is not null)!;
        Assert.Equal(72517, episodeClaim.TmdbSeriesId);
        Assert.Equal(2, episodeClaim.TmdbSeasonNumber);
        Assert.Single(episodeClaim.Files);
        Assert.Equal(2, episodeClaim.Resolution.AttemptNumber);
        Assert.Equal(999, episodeClaim.Resolution.AniDbAnimeId);
        Assert.Equal("tt1234567", episodeClaim.Resolution.ImdbTitleId);
        Assert.Equal("mikan", episodeClaim.Resolution.SourceAdapter);
        Assert.Equal("mikan", episodeClaim.Resolution.SourceProfileId);
        Assert.Equal("mikan", episodeClaim.Resolution.SourceId);
        Assert.False(episodeClaim.Resolution.DuplicateNotificationEnabled);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 22, 12, 34, 56, 123, TimeSpan.FromHours(8)),
            episodeClaim.Resolution.SourcePublishedAt);
        Assert.Equal(1, episodeClaim.Resolution.TorrentFileCount);
    }

    private static Task CompleteFallbackAsync(
        MetadataFixture fixture,
        MetadataTaskClaim claim,
        DateTimeOffset now) =>
        fixture.Store.CompleteBangumiFallbackAsync(
            claim,
            new BangumiSubject(547888, "Made in Abyss", "来自深渊", null, 12),
            1,
            new MetadataFailure(
                MetadataFailureKind.SemanticNoMatch,
                "tmdb_series_not_found",
                TmdbAccessConfirmed: true),
            null,
            now);

    private sealed class MetadataFixture : IAsyncDisposable
    {
        private readonly SqliteDatabaseFixture _databaseFixture;

        private MetadataFixture(SqliteDatabaseFixture databaseFixture, string taskId)
        {
            _databaseFixture = databaseFixture;
            Database = databaseFixture.Database;
            Store = new MetadataResolutionStore(Database);
            TaskId = taskId;
        }

        public AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase Database { get; }

        public MetadataResolutionStore Store { get; }

        public string TaskId { get; }

        public async Task<string> AddDownloadedTaskAsync(char hashCharacter, string sourceItemId)
        {
            var profile = Assert.IsType<SourceProfileRecord>(
                await new SourceProfileStore(Database).GetEnabledAsync("mikan"));
            var normalizedResult = IngestCommandNormalizer.Normalize(
                "mikan",
                new IngestItemCommand(
                    $"https://mikanani.me/passkey/{sourceItemId}.torrent",
                    new IngestItemInfo(
                        "Episode",
                        null,
                        sourceItemId,
                        "3951",
                        null,
                        null,
                        3951,
                        547888,
                        999,
                        "tt1234567")));
            var normalized = Assert.IsType<NormalizedIngestItem>(normalizedResult.Item);
            var hash = new string(hashCharacter, 40);
            var tasks = new IngestTaskStore(Database);
            var staged = await tasks.AddStagedAsync(
                normalized,
                profile,
                new TorrentMetadata("episode.mkv", hash, 100, [new TorrentFile("episode.mkv", 100, false)]),
                $"{sourceItemId}.torrent",
                DateTimeOffset.UtcNow.AddMinutes(15));
            var dispatch = Assert.IsType<ClaimedStagedTorrentRecord>(await tasks.TryClaimNextStagedAsync(
                DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(1)));
            await tasks.CompleteDispatchAsync(
                dispatch,
                new DownloadTaskSnapshot(hash, "Episode", DownloadTaskState.Waiting, 0, 0, 100, 0, null),
                "/download/incomplete/bt",
                "/download/anime",
                DateTimeOffset.UtcNow);
            await new AnimeGoNet.Data.Downloads.DownloadJobStore(Database).ApplyInstanceSnapshotAsync(
                "bt",
                [new DownloadTaskSnapshot(hash, "Episode", DownloadTaskState.Complete, 1, 100, 100, 0, 0)],
                DateTimeOffset.UtcNow);
            return staged.Id;
        }

        public async Task SetSourceEpisodeAsync(string taskId, string sourceEpisode)
        {
            await using var connection = await Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE task_files
                SET source_episode = $source_episode
                WHERE task_id = $task_id;
                """;
            command.Parameters.AddWithValue("$source_episode", sourceEpisode);
            command.Parameters.AddWithValue("$task_id", taskId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        public async Task AddPendingFileAsync(string relativePath, long sizeBytes, string sourceEpisode)
        {
            await using var connection = await Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO task_files (
                    id, task_id, relative_path, size_bytes,
                    source_episode, disposition)
                VALUES ($id, $task_id, $relative_path, $size_bytes, $source_episode, 'pending');
                """;
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("$task_id", TaskId);
            command.Parameters.AddWithValue("$relative_path", relativePath);
            command.Parameters.AddWithValue("$size_bytes", sizeBytes);
            command.Parameters.AddWithValue("$source_episode", sourceEpisode);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        public async Task<(string FileId, (string Disposition, string Reason) State)> ReadFileAsync(
            string taskId)
        {
            await using var connection = await Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, disposition, other_reason
                FROM task_files WHERE task_id = $task_id;
                """;
            command.Parameters.AddWithValue("$task_id", taskId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return (reader.GetString(0), (reader.GetString(1), reader.GetString(2)));
        }

        public async Task<int> CountFallbackClaimsAsync()
        {
            await using var connection = await Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM fallback_claims;";
            return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        }

        public async Task<int> CountFilesByStateAsync(string disposition, string reason)
        {
            await using var connection = await Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*) FROM task_files
                WHERE task_id = $task_id
                  AND disposition = $disposition
                  AND other_reason = $reason;
                """;
            command.Parameters.AddWithValue("$task_id", TaskId);
            command.Parameters.AddWithValue("$disposition", disposition);
            command.Parameters.AddWithValue("$reason", reason);
            return Convert.ToInt32(
                await command.ExecuteScalarAsync(),
                System.Globalization.CultureInfo.InvariantCulture);
        }

        public async Task MarkFallbackCompletedAsync(FallbackDedupScope scope, DateTimeOffset completedAt)
        {
            await using var connection = await Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO fallback_completion_records (
                    id, anime_series_id, bangumi_subject_id, scope_kind, scope_key,
                    source_id, source_episode, media_path, completed_at_utc)
                SELECT 'completed-fallback', id, 547888, $scope_kind, $scope_key,
                       'mikan', '7', '/download/anime/episode.mkv', $now
                FROM anime_series
                WHERE tmdb_series_id = 0 AND bangumi_subject_id = 547888;
                UPDATE fallback_claims
                SET state = 'completed'
                WHERE scope_kind = $scope_kind AND scope_key = $scope_key;
                """;
            command.Parameters.AddWithValue("$scope_kind", scope.Kind);
            command.Parameters.AddWithValue("$scope_key", scope.Key);
            command.Parameters.AddWithValue("$now", completedAt.ToUniversalTime().ToString("O"));
            Assert.Equal(2, await command.ExecuteNonQueryAsync());
        }

        public static async Task<MetadataFixture> CreateAsync()
        {
            var databaseFixture = await SqliteDatabaseFixture.CreateAsync();
            var profiles = new SourceProfileStore(databaseFixture.Database);
            await profiles.EnsureSeedsAsync(AnimeGoDefaults.CreateDocker().InitialSourceProfiles);
            var profile = Assert.IsType<SourceProfileRecord>(await profiles.GetEnabledAsync("mikan"));
            var normalizedResult = IngestCommandNormalizer.Normalize(
                "mikan",
                new IngestItemCommand(
                    "https://mikanani.me/passkey/file.torrent",
                    new IngestItemInfo(
                        "Episode",
                        null,
                        "one",
                        "3951",
                        null,
                        null,
                        3951,
                        547888,
                        999,
                        "tt1234567"),
                    new IngestSourceEvidence(
                        "2026-07-22T12:34:56.123",
                        new DateTimeOffset(
                            2026, 7, 22, 12, 34, 56, 123, TimeSpan.FromHours(8)))));
            var normalized = Assert.IsType<NormalizedIngestItem>(normalizedResult.Item);
            var hash = new string('e', 40);
            var tasks = new IngestTaskStore(databaseFixture.Database);
            var staged = await tasks.AddStagedAsync(
                normalized,
                profile,
                new TorrentMetadata("episode.mkv", hash, 100, [new TorrentFile("episode.mkv", 100, false)]),
                "metadata.torrent",
                DateTimeOffset.UtcNow.AddMinutes(15));
            var dispatch = Assert.IsType<ClaimedStagedTorrentRecord>(await tasks.TryClaimNextStagedAsync(
                DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(1)));
            await tasks.CompleteDispatchAsync(
                dispatch,
                new DownloadTaskSnapshot(hash, "Episode", DownloadTaskState.Waiting, 0, 0, 100, 0, null),
                "/download/incomplete/bt",
                "/download/anime",
                DateTimeOffset.UtcNow);
            await new AnimeGoNet.Data.Downloads.DownloadJobStore(databaseFixture.Database).ApplyInstanceSnapshotAsync(
                "bt",
                [new DownloadTaskSnapshot(hash, "Episode", DownloadTaskState.Complete, 1, 100, 100, 0, 0)],
                DateTimeOffset.UtcNow);
            return new MetadataFixture(databaseFixture, staged.Id);
        }

        public ValueTask DisposeAsync() => _databaseFixture.DisposeAsync();
    }
}
