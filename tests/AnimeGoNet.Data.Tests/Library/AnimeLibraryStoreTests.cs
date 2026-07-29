using AnimeGoNet.Data.Library;
using AnimeGoNet.Data.Sqlite;

namespace AnimeGoNet.Data.Tests.Library;

public sealed class AnimeLibraryStoreTests
{
    [Fact]
    public async Task DefaultListAggregatesCanonicalProgressAndExcludesFallbackSeries()
    {
        await using var fixture = await LibraryFixture.CreateAsync();

        var page = await fixture.Store.ListSeasonsAsync(new AnimeSeasonListQuery());

        Assert.Equal(3, page.TotalItems);
        Assert.Equal([(100, 1), (200, 1), (100, 2)],
            page.Items.Select(item => (item.TmdbSeriesId, item.TmdbSeasonNumber)).ToArray());
        var alpha = page.Items[0];
        Assert.Equal("Alpha", alpha.DisplayName);
        Assert.Equal("alpha", alpha.SortName);
        Assert.Equal(3, alpha.EpisodeTotal);
        Assert.Equal(3, alpha.EpisodeSnapshotCount);
        Assert.Equal(2, alpha.EpisodeDownloaded);
        Assert.Equal("tmdb_title", alpha.SeriesResolutionSource);
        Assert.Equal("tmdb_air_date", alpha.SeasonResolutionSource);
        Assert.Equal("verified", alpha.ValidationStatus);
        Assert.Equal("run-alpha-2", alpha.LastResolutionRunId);
        Assert.Contains("completion_without_snapshot", alpha.Warnings);
        Assert.Contains("completion_media_path_unknown", alpha.Warnings);
        Assert.DoesNotContain("episode_snapshot_incomplete", alpha.Warnings);
        var beta = page.Items[1];
        Assert.Equal(0, beta.EpisodeSnapshotCount);
        Assert.Contains("episode_snapshot_incomplete", beta.Warnings);
    }

    [Fact]
    public async Task AirDateSortKeepsMissingDatesLastInBothDirections()
    {
        await using var fixture = await LibraryFixture.CreateAsync();

        var ascending = await fixture.Store.ListSeasonsAsync(new AnimeSeasonListQuery(
            Sort: AnimeLibrarySort.AirDate,
            Direction: AnimeLibrarySortDirection.Ascending));
        var descending = await fixture.Store.ListSeasonsAsync(new AnimeSeasonListQuery(
            Sort: AnimeLibrarySort.AirDate,
            Direction: AnimeLibrarySortDirection.Descending));

        Assert.Equal([(100, 1), (100, 2), (200, 1)],
            ascending.Items.Select(item => (item.TmdbSeriesId, item.TmdbSeasonNumber)).ToArray());
        Assert.Equal([(100, 2), (100, 1), (200, 1)],
            descending.Items.Select(item => (item.TmdbSeriesId, item.TmdbSeasonNumber)).ToArray());
        Assert.Null(ascending.Items[^1].AirDate);
        Assert.Null(descending.Items[^1].AirDate);
    }

    [Fact]
    public async Task NameSortPaginatesBeforeStableTieBreakersWithoutDuplicates()
    {
        await using var fixture = await LibraryFixture.CreateAsync();
        var items = new List<(int SeriesId, int SeasonNumber)>();
        for (var pageNumber = 1; pageNumber <= 3; pageNumber++)
        {
            var page = await fixture.Store.ListSeasonsAsync(new AnimeSeasonListQuery(
                pageNumber,
                PageSize: 1,
                Sort: AnimeLibrarySort.Name,
                Direction: AnimeLibrarySortDirection.Ascending));
            Assert.Equal(3, page.TotalItems);
            var item = Assert.Single(page.Items);
            items.Add((item.TmdbSeriesId, item.TmdbSeasonNumber));
        }

        Assert.Equal([(100, 1), (100, 2), (200, 1)], items);
        Assert.Equal(3, items.Distinct().Count());
    }

    [Fact]
    public async Task SeasonDetailUsesOfficialEpisodeSnapshotAndReflectsCompletionDeletion()
    {
        await using var fixture = await LibraryFixture.CreateAsync();

        var detail = await fixture.Store.GetSeasonAsync(100, 1);

        Assert.NotNull(detail);
        Assert.Equal(3, detail.Season.EpisodeTotal);
        Assert.Equal(3, detail.Season.EpisodeSnapshotCount);
        Assert.Equal(2, detail.Season.EpisodeDownloaded);
        Assert.Equal([1, 2, 3], detail.Episodes.Select(episode => episode.EpisodeNumber).ToArray());
        Assert.Equal([1001, 1002, 1003], detail.Episodes.Select(episode => episode.TmdbEpisodeId).ToArray());
        Assert.Equal([true, false, true], detail.Episodes.Select(episode => episode.Downloaded).ToArray());
        Assert.Equal(24, detail.Episodes[0].RuntimeMinutes);
        Assert.Equal("test", detail.Episodes[0].DownloadSourceId);
        Assert.False(detail.Episodes[0].MediaPathKnown);
        Assert.Null(detail.Episodes[1].DownloadSourceId);
        Assert.Null(detail.Episodes[1].DownloadedAtUtc);
        Assert.True(detail.Episodes[2].MediaPathKnown);
        Assert.Contains("completion_without_snapshot", detail.Season.Warnings);

        await using (var connection = await fixture.Database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM completion_records WHERE id = 'completion-3';";
            await command.ExecuteNonQueryAsync();
        }

        var afterDeletion = await fixture.Store.GetSeasonAsync(100, 1);
        Assert.NotNull(afterDeletion);
        Assert.Equal(1, afterDeletion.Season.EpisodeDownloaded);
        Assert.False(afterDeletion.Episodes[2].Downloaded);
        Assert.Null(afterDeletion.Episodes[2].DownloadedAtUtc);
    }

    [Fact]
    public async Task SeasonDetailIncludesManualOffsetsRelatedTasksAndCompleteAttemptTimeline()
    {
        await using var fixture = await LibraryFixture.CreateAsync();

        var detail = await fixture.Store.GetSeasonAsync(100, 1);

        Assert.NotNull(detail);
        Assert.Equal([7788, 7789], detail.Audit.ManualOffsets
            .Select(value => value.MikanId)
            .ToArray());
        Assert.Equal([2, -1], detail.Audit.ManualOffsets
            .Select(value => value.EpisodeOffset)
            .ToArray());
        Assert.True(detail.Audit.ManualOffsets[0].Enabled);
        Assert.False(detail.Audit.ManualOffsets[1].Enabled);
        Assert.Equal(2, detail.Audit.RelatedTaskTotal);
        Assert.False(detail.Audit.RelatedTasksTruncated);
        Assert.Equal(["task-alpha-2", "task-alpha-1"], detail.Audit.RelatedTasks
            .Select(value => value.TaskId)
            .ToArray());
        Assert.Equal(6, detail.Audit.ResolutionAttemptTotal);
        Assert.False(detail.Audit.ResolutionAttemptsTruncated);
        Assert.Equal("manual_mikan_offset", detail.Audit.ResolutionAttempts[0].Strategy);
        Assert.Contains(
            detail.Audit.ResolutionAttempts,
            value => value.TaskId == "task-alpha-1"
                && value.Strategy == "old_failed_search"
                && value.Result == "failed");
    }

    [Fact]
    public async Task SeasonDetailRejectsInvalidKeysAndExcludesFallbackOrMissingSeasons()
    {
        await using var fixture = await LibraryFixture.CreateAsync();

        Assert.Null(await fixture.Store.GetSeasonAsync(999, 1));
        Assert.Null(await fixture.Store.GetSeasonAsync(100, 9));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => fixture.Store.GetSeasonAsync(0, 1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => fixture.Store.GetSeasonAsync(100, 0));
    }

    [Fact]
    public async Task PosterProjectionUsesSeasonThenSeriesThenPlaceholder()
    {
        await using var fixture = await LibraryFixture.CreateAsync();

        Assert.Equal(
            new AnimePosterProjection("/alpha-s1.jpg", "season"),
            await fixture.Store.GetPosterAsync(100, 1));
        Assert.Equal(
            new AnimePosterProjection("/alpha.jpg", "series"),
            await fixture.Store.GetPosterAsync(100, 2));
        Assert.Equal(
            new AnimePosterProjection(null, "placeholder"),
            await fixture.Store.GetPosterAsync(200, 1));
        Assert.Null(await fixture.Store.GetPosterAsync(999, 1));
    }

    [Fact]
    public async Task InvalidPagingIsRejectedBeforeOpeningAQuery()
    {
        await using var fixture = await LibraryFixture.CreateAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            fixture.Store.ListSeasonsAsync(new AnimeSeasonListQuery(Page: 0)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            fixture.Store.ListSeasonsAsync(new AnimeSeasonListQuery(PageSize: 101)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            fixture.Store.ListSeasonsAsync(new AnimeSeasonListQuery(
                Sort: (AnimeLibrarySort)999)));
    }

    private sealed class LibraryFixture : IAsyncDisposable
    {
        private readonly SqliteDatabaseFixture _databaseFixture;

        private LibraryFixture(SqliteDatabaseFixture databaseFixture)
        {
            _databaseFixture = databaseFixture;
            Store = new AnimeLibraryStore(databaseFixture.Database);
        }

        public AnimeLibraryStore Store { get; }

        public AnimeGoSqliteDatabase Database => _databaseFixture.Database;

        public static async Task<LibraryFixture> CreateAsync()
        {
            var fixture = new LibraryFixture(await SqliteDatabaseFixture.CreateAsync());
            await fixture.SeedAsync();
            return fixture;
        }

        public ValueTask DisposeAsync() => _databaseFixture.DisposeAsync();

        private async Task SeedAsync()
        {
            await using var connection = await _databaseFixture.Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO source_profiles (
                    id, display_name, adapter, downloader_id, file_strategy,
                    rss_filter_enabled, rss_priority_enabled, revision, enabled,
                    created_at_utc, updated_at_utc)
                VALUES (
                    'test', 'Test', 'test', 'bt', 'move',
                    0, 0, 1, 1, $base_time, $base_time);

                INSERT INTO anime_series (
                    id, tmdb_series_id, bangumi_subject_id,
                    canonical_name, original_name, poster_path,
                    needs_tmdb_completion, created_at_utc, updated_at_utc, first_air_date)
                VALUES
                    ('alpha', 100, NULL, 'Alpha', 'Alpha JP', '/alpha.jpg', 0,
                     '2026-01-01T00:00:00.0000000+00:00',
                     '2026-01-05T00:00:00.0000000+00:00', '2024-01-01'),
                    ('beta', 200, NULL, 'Beta', 'Beta JP', NULL, 0,
                     '2026-01-03T00:00:00.0000000+00:00',
                     '2026-01-06T00:00:00.0000000+00:00', NULL),
                    ('fallback', 0, 547888, 'Fallback', 'Fallback', NULL, 1,
                     '2026-01-04T00:00:00.0000000+00:00',
                     '2026-01-09T00:00:00.0000000+00:00', NULL);

                INSERT INTO anime_seasons (
                    id, series_id, season_number, canonical_name, poster_path,
                    created_at_utc, updated_at_utc, air_date, episode_count)
                VALUES
                    ('alpha-1', 'alpha', 1, 'Alpha One', '/alpha-s1.jpg',
                     '2026-01-01T00:00:00.0000000+00:00',
                     '2026-01-05T00:00:00.0000000+00:00', '2024-01-01', 3),
                    ('alpha-2', 'alpha', 2, 'Alpha Two', NULL,
                     '2026-01-02T00:00:00.0000000+00:00',
                     '2026-01-04T00:00:00.0000000+00:00', '2025-01-01', 2),
                    ('beta-1', 'beta', 1, 'Beta One', NULL,
                     '2026-01-03T00:00:00.0000000+00:00',
                     '2026-01-06T00:00:00.0000000+00:00', NULL, 1),
                    ('fallback-1', 'fallback', 1, 'Fallback One', NULL,
                     '2026-01-04T00:00:00.0000000+00:00',
                     '2026-01-09T00:00:00.0000000+00:00', NULL, 0);

                INSERT INTO tmdb_episodes (
                    tmdb_episode_id, series_id, season_number, episode_number,
                    name, air_date, runtime_minutes, fetched_at_utc)
                VALUES
                    (1001, 'alpha', 1, 1, 'Alpha 1', '2024-01-01', 24, $base_time),
                    (1002, 'alpha', 1, 2, 'Alpha 2', '2024-01-08', 25, $base_time),
                    (1003, 'alpha', 1, 3, 'Alpha 3', '2024-01-15', 26, $base_time),
                    (2001, 'alpha', 2, 1, 'Alpha S2 1', '2025-01-01', 24, $base_time),
                    (2002, 'alpha', 2, 2, 'Alpha S2 2', '2025-01-08', 24, $base_time);

                INSERT INTO completion_records (
                    id, tmdb_series_id, tmdb_season_number, tmdb_episode_number,
                    source_id, media_path, completed_at_utc)
                VALUES
                    ('completion-1', 100, 1, 1, 'test', NULL,
                     '2026-01-06T00:00:00.0000000+00:00'),
                    ('completion-3', 100, 1, 3, 'test', '/media/alpha-3.mkv',
                     '2026-01-07T00:00:00.0000000+00:00'),
                    ('completion-99', 100, 1, 99, 'test', '/media/legacy.mkv',
                     '2026-01-07T00:00:00.0000000+00:00');

                INSERT INTO ingest_tasks (
                    id, source_profile_id, source_profile_revision, source_id,
                    mikanid, bangumi_subject_id, title,
                    torrent_url_fingerprint, downloader_id, route_snapshot_json,
                    status, created_at_utc, updated_at_utc)
                VALUES
                (
                    'task-alpha-1', 'test', 1, 'test', 7788, 42,
                    'Alpha',
                    'fingerprint-alpha', 'bt', '{}', 'metadata_resolved',
                    $base_time, '2026-01-07T00:00:00.0000000+00:00'),
                (
                    'task-alpha-2', 'test', 1, 'test', 7789, 43,
                    'Alpha Again',
                    'fingerprint-alpha-2', 'bt', '{}', 'metadata_resolved',
                    $base_time, '2026-01-09T00:00:00.0000000+00:00');

                INSERT INTO task_files (
                    id, task_id, relative_path, size_bytes, tmdb_series_id,
                    tmdb_season_number, disposition)
                VALUES (
                    'file-alpha-1', 'task-alpha-1', 'alpha.mkv', 100,
                    100, 1, 'episode');

                INSERT INTO metadata_resolution_runs (
                    id, task_id, status, tmdb_access_confirmed, fallback_eligible,
                    started_at_utc, completed_at_utc, attempt_number,
                    tmdb_series_id, tmdb_season_number)
                VALUES
                (
                    'run-alpha-1', 'task-alpha-1', 'season_resolved', 1, 0,
                    $base_time, '2026-01-08T00:00:00.0000000+00:00', 2, 100, 1),
                (
                    'run-alpha-0', 'task-alpha-1', 'failed', 1, 0,
                    '2025-12-31T00:00:00.0000000+00:00',
                    '2025-12-31T00:00:01.0000000+00:00', 1, NULL, NULL),
                (
                    'run-alpha-2', 'task-alpha-2', 'episode_resolved', 1, 0,
                    '2026-01-09T00:00:00.0000000+00:00',
                    '2026-01-09T00:00:01.0000000+00:00', 1, 100, 1);

                INSERT INTO metadata_resolution_attempts (
                    id, run_id, stage, strategy, priority, result,
                    error_code, reason, retryable,
                    attempt_number, duration_ms, created_at_utc)
                VALUES
                    ('attempt-series', 'run-alpha-1', 'series', 'tmdb_title',
                     NULL, 'matched', NULL, NULL, 0, 1, 10, $base_time),
                    ('attempt-season', 'run-alpha-1', 'season', 'tmdb_air_date',
                     3, 'matched', NULL, NULL, 0, 1, 20, $base_time),
                    ('attempt-old', 'run-alpha-0', 'series', 'old_failed_search',
                     NULL, 'failed', 'tmdb_no_match', 'No verified Series.', 0,
                     1, 30, '2025-12-31T00:00:01.0000000+00:00'),
                    ('attempt-alpha-2-series', 'run-alpha-2', 'series', 'tmdb_title',
                     NULL, 'matched', NULL, NULL, 0,
                     1, 10, '2026-01-09T00:00:00.0000000+00:00'),
                    ('attempt-alpha-2-season', 'run-alpha-2', 'season', 'tmdb_air_date',
                     3, 'matched', NULL, NULL, 0,
                     1, 10, '2026-01-09T00:00:00.0000000+00:00'),
                    ('attempt-offset', 'run-alpha-2', 'episode', 'manual_mikan_offset',
                     100, 'matched', NULL, 'Applied +2.', 0,
                     1, 5, '2026-01-09T00:00:01.0000000+00:00');

                INSERT INTO mikan_work_rules (
                    mikanid, bangumi_subject_id, tmdb_series_id,
                    tmdb_season_number, episode_offset, enabled, revision,
                    created_at_utc, updated_at_utc)
                VALUES
                    (7788, 42, 100, 1, 2, 1, 3, $base_time,
                     '2026-01-09T00:00:00.0000000+00:00'),
                    (7789, 43, NULL, NULL, -1, 0, 2, $base_time,
                     '2026-01-08T00:00:00.0000000+00:00'),
                    (7790, 44, 200, 1, 9, 1, 1, $base_time,
                     '2026-01-07T00:00:00.0000000+00:00');
                """;
            command.Parameters.AddWithValue("$base_time", "2026-01-01T00:00:00.0000000+00:00");
            await command.ExecuteNonQueryAsync();
        }
    }
}
