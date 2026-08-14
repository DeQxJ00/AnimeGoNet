using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Tests.Sqlite;

public sealed class SchemaConstraintTests
{
    [Theory]
    [InlineData("completed", 0, 1)]
    [InlineData("rename_planning", 2, 1)]
    [InlineData("not_started", 0, 1)]
    public async Task MediaOrganizationProgressRejectsContradictoryState(
        string phase,
        int completed,
        int total)
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE download_jobs
            SET organization_phase = $phase,
                organization_completed_units = $completed,
                organization_total_units = $total
            WHERE id = 'missing';

            INSERT INTO source_profiles (
                id, display_name, adapter, downloader_id, file_strategy,
                rss_filter_enabled, rss_priority_enabled, revision, enabled,
                created_at_utc, updated_at_utc)
            VALUES ('constraint-source', 'Constraint', 'mikan', 'bt', 'move',
                    1, 1, 1, 1, $now, $now);
            INSERT INTO ingest_tasks (
                id, source_profile_id, source_profile_revision, source_id,
                title, torrent_url_fingerprint, downloader_id, route_snapshot_json,
                status, created_at_utc, updated_at_utc)
            VALUES ('constraint-task', 'constraint-source', 1, 'mikan', 'Constraint',
                    $fingerprint, 'bt', '{}', 'downloaded', $now, $now);
            INSERT INTO download_jobs (
                id, task_id, downloader_id, state, progress, downloaded_bytes,
                total_bytes, speed_bytes_per_second, created_at_utc, updated_at_utc,
                organization_state, organization_phase,
                organization_completed_units, organization_total_units)
            VALUES ('constraint-job', 'constraint-task', 'bt', 'complete', 1, 5,
                    5, 0, $now, $now, 'pending', $phase, $completed, $total);
            """;
        command.Parameters.AddWithValue("$phase", phase);
        command.Parameters.AddWithValue("$completed", completed);
        command.Parameters.AddWithValue("$total", total);
        command.Parameters.AddWithValue("$now", "2026-08-08T00:00:00.0000000+00:00");
        command.Parameters.AddWithValue("$fingerprint", new string('c', 64));

        await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task TrustedOffsetRequiresAtLeastOneDistinctEpisodeEvidence()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO mikan_trusted_offsets(
                mikanid, groupid, tmdb_series_id, tmdb_season_number,
                episode_offset, distinct_episode_count, state, updated_at_utc)
            VALUES (10, 20, 30, 1, 2, 0, 'trusted', '2026-07-19T00:00:00Z');
            """;

        await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task SameMikanGroupAndSourceEpisodeCannotCountTwice()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO mikan_offset_evidence(
                id, mikanid, groupid, source_episode, tmdb_series_id,
                tmdb_season_number, episode_offset, observed_at_utc)
            VALUES
                ('a', 10, 20, '3', 30, 1, 2, '2026-07-19T00:00:00Z'),
                ('b', 10, 20, '3', 30, 1, 2, '2026-07-19T00:01:00Z');
            """;

        await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task TmdbZeroSeriesRequiresBangumiIdAndPendingCompletionState()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO anime_series(
                id, tmdb_series_id, bangumi_subject_id, needs_tmdb_completion,
                created_at_utc, updated_at_utc)
            VALUES ('invalid-fallback', 0, NULL, 1, '2026-07-19T00:00:00Z', '2026-07-19T00:00:00Z');
            """;

        await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
    }
}
