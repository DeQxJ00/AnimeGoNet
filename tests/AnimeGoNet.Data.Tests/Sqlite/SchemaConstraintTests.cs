using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Tests.Sqlite;

public sealed class SchemaConstraintTests
{
    [Fact]
    public async Task TrustedOffsetRequiresThreeDistinctEpisodeEvidenceCount()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO mikan_trusted_offsets(
                mikanid, groupid, tmdb_series_id, tmdb_season_number,
                episode_offset, distinct_episode_count, state, updated_at_utc)
            VALUES (10, 20, 30, 1, 2, 2, 'trusted', '2026-07-19T00:00:00Z');
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
