using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Metadata;

namespace AnimeGoNet.Data.Tests.Metadata;

public sealed class PendingTmdbRecoveryStoreTests
{
    private static readonly DateTimeOffset RecoveredAt =
        new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MultipleFallbacksConvergeToOneCanonicalCompletionWithoutDeletingFiles()
    {
        await using var fixture = await RecoveryFixture.CreateAsync(2);
        var result = await fixture.Store.RecoverAsync(
            Request(
                Mapping("fallback-1", episodeId: 9007, episodeNumber: 7),
                Mapping("fallback-2", episodeId: 9007, episodeNumber: 7)),
            RecoveredAt);

        Assert.False(result.HasPendingFallbackRecords);
        Assert.Equal("duplicate_after_resolution", Assert.Single(
            result.Items,
            item => item.FallbackCompletionId == "fallback-1").State);
        Assert.Equal("resolved", Assert.Single(
            result.Items,
            item => item.FallbackCompletionId == "fallback-2").State);

        await using var connection = await fixture.Database.OpenConnectionAsync();
        Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM completion_records;"));
        Assert.Equal(2, await ScalarAsync(connection, "SELECT COUNT(*) FROM completion_aliases;"));
        Assert.Equal(2, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM fallback_completion_records WHERE resolved_completion_id IS NOT NULL;"));
        Assert.Equal(0, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM anime_series WHERE tmdb_series_id = 0;"));
        Assert.Equal(1, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM anime_series WHERE tmdb_series_id = 700 AND bangumi_subject_id = 547888;"));
        Assert.Equal(1, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM task_files WHERE disposition = 'episode' AND other_reason = 'tmdb_recovered';"));
        Assert.Equal(12, await ScalarAsync(connection, "SELECT COUNT(*) FROM tmdb_episodes;"));
        Assert.Equal(1, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM task_files WHERE disposition = 'duplicate' AND other_reason = 'duplicate_after_resolution';"));
        Assert.Equal(2, await ScalarAsync(connection, "SELECT COUNT(*) FROM download_jobs;"));
        Assert.Equal(1, await ScalarAsync(
            connection,
            """
            SELECT COUNT(*) FROM pending_tmdb_nfo_rewrite_jobs
            WHERE state = 'pending'
              AND tmdb_series_id = 700
              AND series_directory_name = 'Fallback Anime';
            """));

        await using (var library = connection.CreateCommand())
        {
            library.CommandText = """
                SELECT series.first_air_date, series.poster_path,
                       season.air_date, season.episode_count, season.poster_path
                FROM anime_series AS series
                JOIN anime_seasons AS season ON season.series_id = series.id
                WHERE series.tmdb_series_id = 700 AND season.season_number = 1;
                """;
            await using var reader = await library.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("2026-01-01", reader.GetString(0));
            Assert.Equal("/canonical-series.jpg", reader.GetString(1));
            Assert.Equal("2026-01-01", reader.GetString(2));
            Assert.Equal(12, reader.GetInt32(3));
            Assert.Equal("/canonical-season.jpg", reader.GetString(4));
        }

        await using (var query = connection.CreateCommand())
        {
            query.CommandText = "SELECT media_path, completed_at_utc FROM completion_records;";
            await using var reader = await query.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("/media/fallback-2.mkv", reader.GetString(0));
            Assert.StartsWith("2026-07-27T10:00:00", reader.GetString(1), StringComparison.Ordinal);
        }

        await using (var delete = connection.CreateCommand())
        {
            delete.CommandText = "DELETE FROM completion_records;";
            Assert.Equal(1, await delete.ExecuteNonQueryAsync());
        }

        Assert.Equal(0, await ScalarAsync(connection, "SELECT COUNT(*) FROM completion_aliases;"));
        Assert.Equal(0, await ScalarAsync(connection, "SELECT COUNT(*) FROM fallback_completion_records;"));
    }

    [Fact]
    public async Task ExistingCanonicalCompletionWinsAndFallbackBecomesDuplicateAfterResolution()
    {
        await using var fixture = await RecoveryFixture.CreateAsync(1);
        await fixture.SeedCanonicalCompletionAsync();

        var result = await fixture.Store.RecoverAsync(
            Request(Mapping("fallback-1", episodeId: 9007, episodeNumber: 7)),
            RecoveredAt);

        var item = Assert.Single(result.Items);
        Assert.Equal("duplicate_after_resolution", item.State);
        Assert.Equal("existing-completion", item.CompletionId);

        await using var connection = await fixture.Database.OpenConnectionAsync();
        Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM completion_records;"));
        Assert.Equal(1, await ScalarAsync(
            connection,
            """
            SELECT COUNT(*) FROM fallback_completion_records
            WHERE resolution_state = 'duplicate_after_resolution'
              AND resolution_source = 'manual'
              AND resolved_completion_id = 'existing-completion';
            """));
        Assert.Equal(1, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM completion_records WHERE media_path = '/media/existing.mkv';"));
    }

    [Fact]
    public async Task PartialRecoveryKeepsPendingSeriesUntilLastFallbackIsMapped()
    {
        await using var fixture = await RecoveryFixture.CreateAsync(2);
        var first = await fixture.Store.RecoverAsync(
            Request(Mapping("fallback-1", episodeId: 9007, episodeNumber: 7)),
            RecoveredAt);

        Assert.True(first.HasPendingFallbackRecords);
        var pending = await new PendingTmdbStore(fixture.Database).ListAsync();
        var summary = Assert.Single(pending);
        Assert.Equal(547888, summary.BangumiSubjectId);
        Assert.Equal(1, summary.CompletionRecordCount);

        var second = await fixture.Store.RecoverAsync(
            Request(Mapping("fallback-2", episodeId: 9008, episodeNumber: 8)),
            RecoveredAt.AddMinutes(1));

        Assert.False(second.HasPendingFallbackRecords);
        Assert.Empty(await new PendingTmdbStore(fixture.Database).ListAsync());
    }

    [Fact]
    public async Task MissingFallbackRollsBackWholeRecovery()
    {
        await using var fixture = await RecoveryFixture.CreateAsync(1);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Store.RecoverAsync(
            Request(
                Mapping("fallback-1", episodeId: 9007, episodeNumber: 7),
                Mapping("missing", episodeId: 9008, episodeNumber: 8)),
            RecoveredAt));

        await using var connection = await fixture.Database.OpenConnectionAsync();
        Assert.Equal(0, await ScalarAsync(connection, "SELECT COUNT(*) FROM completion_records;"));
        Assert.Equal(0, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM anime_series WHERE tmdb_series_id > 0;"));
        Assert.Equal(1, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM fallback_completion_records WHERE resolution_state = 'pending';"));
    }

    [Fact]
    public async Task ConflictingTmdbEpisodeIdRollsBackRecovery()
    {
        await using var fixture = await RecoveryFixture.CreateAsync(1);
        await fixture.SeedConflictingEpisodeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.RecoverAsync(
            Request(Mapping("fallback-1", episodeId: 9007, episodeNumber: 7)),
            RecoveredAt));

        await using var connection = await fixture.Database.OpenConnectionAsync();
        Assert.Equal(0, await ScalarAsync(connection, "SELECT COUNT(*) FROM completion_records;"));
        Assert.Equal(1, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM fallback_completion_records WHERE resolution_state = 'pending';"));
    }

    [Fact]
    public async Task ActiveCanonicalClaimBlocksRecoveryWithoutStartingOrChangingDownloads()
    {
        await using var fixture = await RecoveryFixture.CreateAsync(1);
        await fixture.SeedActiveCanonicalClaimAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.RecoverAsync(
            Request(Mapping("fallback-1", episodeId: 9007, episodeNumber: 7)),
            RecoveredAt));

        Assert.Contains("currently claimed", exception.Message, StringComparison.Ordinal);
        await using var connection = await fixture.Database.OpenConnectionAsync();
        Assert.Equal(0, await ScalarAsync(connection, "SELECT COUNT(*) FROM completion_records;"));
        Assert.Equal(1, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM episode_claims WHERE state = 'active';"));
        Assert.Equal(1, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM fallback_completion_records WHERE resolution_state = 'pending';"));
    }

    private static PendingTmdbRecoveryRequest Request(
        params PendingTmdbRecoveryMapping[] mappings) =>
        new(
            547888,
            new TmdbSeries(
                700,
                "Canonical Anime",
                "Canonical Anime",
                new DateOnly(2026, 1, 1),
                "/canonical-series.jpg"),
            mappings,
            "manual");

    private static PendingTmdbRecoveryMapping Mapping(
        string fallbackId,
        int episodeId,
        int episodeNumber) =>
        new(
            fallbackId,
            new TmdbSeason(
                800,
                700,
                1,
                "Season 1",
                new DateOnly(2026, 1, 1),
                12,
                "/canonical-season.jpg",
                Enumerable.Range(1, 12)
                    .Select(number => new TmdbEpisode(
                        9000 + number,
                        700,
                        1,
                        number,
                        $"Episode {number}",
                        new DateOnly(2026, 1, 1).AddDays(number - 1)))
                    .ToArray()),
            new TmdbEpisode(
                episodeId,
                700,
                1,
                episodeNumber,
                $"Episode {episodeNumber}",
                new DateOnly(2026, 1, episodeNumber)));

    private static async Task<int> ScalarAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed class RecoveryFixture : IAsyncDisposable
    {
        private readonly SqliteDatabaseFixture fixture;

        private RecoveryFixture(SqliteDatabaseFixture fixture)
        {
            this.fixture = fixture;
            Store = new PendingTmdbRecoveryStore(fixture.Database);
        }

        public AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase Database => fixture.Database;

        public PendingTmdbRecoveryStore Store { get; }

        public static async Task<RecoveryFixture> CreateAsync(int fallbackCount)
        {
            var fixture = await SqliteDatabaseFixture.CreateAsync();
            var result = new RecoveryFixture(fixture);
            await result.SeedAsync(fallbackCount);
            return result;
        }

        public async Task SeedCanonicalCompletionAsync()
        {
            await using var connection = await Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO completion_records (
                    id, tmdb_series_id, tmdb_season_number, tmdb_episode_number,
                    source_id, source_item_id, media_path, completed_at_utc)
                VALUES (
                    'existing-completion', 700, 1, 7,
                    'u2', 'existing', '/media/existing.mkv', '2026-07-26T10:00:00.0000000+00:00');
                """;
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        public async Task SeedConflictingEpisodeAsync()
        {
            await using var connection = await Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO anime_series (
                    id, tmdb_series_id, bangumi_subject_id, canonical_name,
                    original_name, needs_tmdb_completion, created_at_utc, updated_at_utc)
                VALUES (
                    'canonical-series', 700, 547888, 'Canonical Anime',
                    'Canonical Anime', 0, $now, $now);

                INSERT INTO anime_seasons (
                    id, series_id, season_number, canonical_name,
                    created_at_utc, updated_at_utc)
                VALUES ('canonical-season', 'canonical-series', 1, 'Season 1', $now, $now);

                INSERT INTO tmdb_episodes (
                    tmdb_episode_id, series_id, season_number, episode_number,
                    name, fetched_at_utc)
                VALUES (9007, 'canonical-series', 1, 8, 'Different Episode', $now);
                """;
            command.Parameters.AddWithValue("$now", "2026-07-28T10:00:00.0000000+00:00");
            Assert.Equal(3, await command.ExecuteNonQueryAsync());
        }

        public async Task SeedActiveCanonicalClaimAsync()
        {
            await using var connection = await Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO ingest_tasks (
                    id, source_profile_id, source_profile_revision, source_id,
                    source_item_id, title, torrent_url_fingerprint, downloader_id,
                    route_snapshot_json, status, created_at_utc, updated_at_utc)
                VALUES (
                    'canonical-task', 'mikan', 1, 'mikan',
                    'canonical-item', 'Canonical contender', 'canonical-fingerprint', 'bt',
                    '{}', 'download_queued', $now, $now);

                INSERT INTO task_files (
                    id, task_id, relative_path, size_bytes, source_episode,
                    tmdb_series_id, tmdb_season_number, tmdb_episode_number,
                    disposition)
                VALUES (
                    'canonical-file', 'canonical-task', 'canonical.mkv', 100, '7',
                    700, 1, 7, 'episode');

                INSERT INTO episode_claims (
                    id, tmdb_series_id, tmdb_season_number, tmdb_episode_number,
                    task_file_id, state, claimed_at_utc)
                VALUES ('canonical-claim', 700, 1, 7, 'canonical-file', 'active', $now);
                """;
            command.Parameters.AddWithValue("$now", "2026-07-28T10:00:00.0000000+00:00");
            Assert.Equal(3, await command.ExecuteNonQueryAsync());
        }

        private async Task SeedAsync(int fallbackCount)
        {
            await using var connection = await Database.OpenConnectionAsync();
            await using (var seed = connection.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO source_profiles (
                        id, display_name, adapter, downloader_id, file_strategy,
                        rss_filter_enabled, rss_priority_enabled, revision, enabled,
                        created_at_utc, updated_at_utc)
                    VALUES (
                        'mikan', 'Mikan', 'mikan', 'bt', 'move',
                        1, 1, 1, 1, $now, $now);

                    INSERT INTO anime_series (
                        id, tmdb_series_id, bangumi_subject_id, canonical_name,
                        original_name, needs_tmdb_completion, created_at_utc, updated_at_utc)
                    VALUES (
                        'fallback-series', 0, 547888, 'Fallback Anime',
                        'Fallback Anime', 1, $now, $now);

                    INSERT INTO anime_seasons (
                        id, series_id, season_number, canonical_name,
                        created_at_utc, updated_at_utc)
                    VALUES ('fallback-season', 'fallback-series', 1, 'Season 1', $now, $now);
                    """;
                seed.Parameters.AddWithValue("$now", "2026-07-28T10:00:00.0000000+00:00");
                await seed.ExecuteNonQueryAsync();
            }

            for (var index = 1; index <= fallbackCount; index++)
            {
                await using var seed = connection.CreateCommand();
                seed.CommandText = """
                    INSERT INTO ingest_tasks (
                        id, source_profile_id, source_profile_revision, source_id,
                        source_item_id, source_work_id, mikanid, bangumi_subject_id,
                        title, torrent_url_fingerprint, downloader_id, route_snapshot_json,
                        status, failure_kind, failure_reason, created_at_utc, updated_at_utc)
                    VALUES (
                        $task_id, 'mikan', 1, 'mikan',
                        $source_item, '3951', 3951, 547888,
                        $title, $fingerprint, 'bt', '{}',
                        'organized', 'tmdb_completion_pending', 'tmdb_no_match', $now, $now);

                    INSERT INTO task_files (
                        id, task_id, relative_path, size_bytes, source_episode,
                        tmdb_season_number, disposition, other_reason)
                    VALUES (
                        $file_id, $task_id, $relative_path, 100, $episode,
                        1, 'other', 'tmdb_fallback_pending_completion');

                    INSERT INTO fallback_claims (
                        id, scope_kind, scope_key, task_file_id, state, claimed_at_utc)
                    VALUES (
                        $claim_id, 'mikan_episode', $scope_key, $file_id, 'completed', $now);

                    INSERT INTO fallback_completion_records (
                        id, anime_series_id, bangumi_subject_id, scope_kind, scope_key,
                        source_id, source_episode, media_path, completed_at_utc)
                    VALUES (
                        $fallback_id, 'fallback-series', 547888, 'mikan_episode', $scope_key,
                        'mikan', $episode, $media_path, $completed_at);

                    INSERT INTO download_jobs (
                        id, task_id, downloader_id, state, progress,
                        downloaded_bytes, total_bytes, speed_bytes_per_second,
                        download_root_path, save_root_path, created_at_utc, updated_at_utc)
                    VALUES (
                        $job_id, $task_id, 'bt', 'complete', 1,
                        100, 100, 0, '/download', '/media', $now, $now);
                    """;
                seed.Parameters.AddWithValue("$task_id", $"task-{index}");
                seed.Parameters.AddWithValue("$source_item", $"item-{index}");
                seed.Parameters.AddWithValue("$title", $"Fallback {index}");
                seed.Parameters.AddWithValue("$fingerprint", $"fingerprint-{index}");
                seed.Parameters.AddWithValue("$now", "2026-07-28T10:00:00.0000000+00:00");
                seed.Parameters.AddWithValue("$file_id", $"file-{index}");
                seed.Parameters.AddWithValue("$relative_path", $"fallback-{index}.mkv");
                seed.Parameters.AddWithValue("$episode", (6 + index).ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
                seed.Parameters.AddWithValue("$claim_id", $"claim-{index}");
                seed.Parameters.AddWithValue("$scope_key", $"mikan:3951:{6 + index}");
                seed.Parameters.AddWithValue("$fallback_id", $"fallback-{index}");
                seed.Parameters.AddWithValue("$media_path", $"/media/fallback-{index}.mkv");
                seed.Parameters.AddWithValue("$job_id", $"job-{index}");
                seed.Parameters.AddWithValue(
                    "$completed_at",
                    index == 2
                        ? "2026-07-27T10:00:00.0000000+00:00"
                        : "2026-07-28T10:00:00.0000000+00:00");
                await seed.ExecuteNonQueryAsync();
            }
        }

        public ValueTask DisposeAsync() => fixture.DisposeAsync();
    }
}
