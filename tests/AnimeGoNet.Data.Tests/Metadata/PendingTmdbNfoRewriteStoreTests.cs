using AnimeGoNet.Data.Metadata;

namespace AnimeGoNet.Data.Tests.Metadata;

public sealed class PendingTmdbNfoRewriteStoreTests
{
    [Fact]
    public async Task ClaimFailureDelayAndRetryArePersistent()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        await SeedAsync(fixture);
        var store = new PendingTmdbNfoRewriteStore(fixture.Database);
        var now = new DateTimeOffset(2026, 7, 28, 10, 0, 0, TimeSpan.Zero);

        var first = Assert.IsType<PendingTmdbNfoRewriteClaim>(
            await store.TryClaimNextAsync(now, TimeSpan.FromMinutes(5)));
        Assert.Equal(1, first.AttemptCount);
        Assert.Null(await store.TryClaimNextAsync(now, TimeSpan.FromMinutes(5)));
        var writing = Assert.Single(await store.ListForTaskAsync("nfo-task"));
        Assert.Equal("rewrite-job", writing.JobId);
        Assert.Equal(547888, writing.BangumiSubjectId);
        Assert.Equal(700, writing.TmdbSeriesId);
        Assert.Equal("writing", writing.State);
        Assert.Equal(1, writing.AttemptCount);
        Assert.Null(writing.FailureCode);
        Assert.Null(writing.NextAttemptAtUtc);

        await store.FailAsync(first, "nfo_rewrite_failed", now, TimeSpan.FromSeconds(30));
        var failed = Assert.Single(await store.ListForTaskAsync("nfo-task"));
        Assert.Equal("failed", failed.State);
        Assert.Equal("nfo_rewrite_failed", failed.FailureCode);
        Assert.Equal(now.AddSeconds(30), failed.NextAttemptAtUtc);
        Assert.Null(await store.TryClaimNextAsync(now.AddSeconds(29), TimeSpan.FromMinutes(5)));
        var retry = Assert.IsType<PendingTmdbNfoRewriteClaim>(
            await store.TryClaimNextAsync(now.AddSeconds(31), TimeSpan.FromMinutes(5)));
        Assert.Equal(2, retry.AttemptCount);
        Assert.NotEqual(first.LeaseToken, retry.LeaseToken);

        await store.CompleteAsync(retry, now.AddSeconds(32));
        Assert.Null(await store.TryClaimNextAsync(now.AddHours(1), TimeSpan.FromMinutes(5)));
        var completed = Assert.Single(await store.ListForTaskAsync("nfo-task"));
        Assert.Equal("completed", completed.State);
        Assert.Equal(2, completed.AttemptCount);
        Assert.Equal(now.AddSeconds(32), completed.CompletedAtUtc);
        Assert.Empty(await store.ListForTaskAsync("missing-task"));
    }

    [Fact]
    public async Task ExpiredLeaseIsRecoveredWithNewToken()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        await SeedAsync(fixture);
        var store = new PendingTmdbNfoRewriteStore(fixture.Database);
        var now = new DateTimeOffset(2026, 7, 28, 10, 0, 0, TimeSpan.Zero);
        var first = Assert.IsType<PendingTmdbNfoRewriteClaim>(
            await store.TryClaimNextAsync(now, TimeSpan.FromMinutes(5)));

        var recovered = Assert.IsType<PendingTmdbNfoRewriteClaim>(
            await store.TryClaimNextAsync(now.AddMinutes(6), TimeSpan.FromMinutes(5)));

        Assert.Equal(2, recovered.AttemptCount);
        Assert.NotEqual(first.LeaseToken, recovered.LeaseToken);
    }

    private static async Task SeedAsync(SqliteDatabaseFixture fixture)
    {
        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO source_profiles (
                id, display_name, adapter, downloader_id, file_strategy,
                rss_filter_enabled, rss_priority_enabled,
                revision, enabled, created_at_utc, updated_at_utc)
            VALUES (
                'mikan', 'Mikan', 'mikan', 'bt', 'move',
                1, 1,
                1, 1, $now, $now);

            INSERT INTO ingest_tasks (
                id, source_profile_id, source_profile_revision, source_id,
                bangumi_subject_id, title, torrent_url_fingerprint,
                downloader_id, route_snapshot_json, status,
                created_at_utc, updated_at_utc)
            VALUES (
                'nfo-task', 'mikan', 1, 'mikan',
                547888, 'Fallback Anime', $fingerprint,
                'bt', '{}', 'metadata_resolved', $now, $now);

            INSERT INTO task_files (
                id, task_id, relative_path, size_bytes,
                tmdb_series_id, tmdb_season_number, tmdb_episode_number,
                disposition)
            VALUES (
                'nfo-file', 'nfo-task', 'episode.mkv', 100,
                700, 2, 1, 'episode');

            INSERT INTO download_jobs (
                id, task_id, downloader_id, state, progress,
                downloaded_bytes, total_bytes, speed_bytes_per_second,
                download_root_path, save_root_path,
                created_at_utc, updated_at_utc)
            VALUES (
                'nfo-download', 'nfo-task', 'bt', 'waiting', 0,
                0, 100, 0, '/download', '/media', $now, $now);

            INSERT INTO pending_tmdb_nfo_rewrite_jobs (
                id, bangumi_subject_id, tmdb_series_id, save_root_path,
                series_directory_name, canonical_series_name, state,
                created_at_utc, updated_at_utc)
            VALUES (
                'rewrite-job', 547888, 700, '/media',
                'Fallback Anime', 'Canonical Anime', 'pending', $now, $now);
            """;
        command.Parameters.AddWithValue("$now", "2026-07-28T09:00:00.0000000+00:00");
        command.Parameters.AddWithValue("$fingerprint", new string('a', 64));
        Assert.Equal(5, await command.ExecuteNonQueryAsync());
    }
}
