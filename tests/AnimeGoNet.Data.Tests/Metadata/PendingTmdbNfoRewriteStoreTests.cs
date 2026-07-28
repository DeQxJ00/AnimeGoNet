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

        await store.FailAsync(first, "nfo_rewrite_failed", now, TimeSpan.FromSeconds(30));
        Assert.Null(await store.TryClaimNextAsync(now.AddSeconds(29), TimeSpan.FromMinutes(5)));
        var retry = Assert.IsType<PendingTmdbNfoRewriteClaim>(
            await store.TryClaimNextAsync(now.AddSeconds(31), TimeSpan.FromMinutes(5)));
        Assert.Equal(2, retry.AttemptCount);
        Assert.NotEqual(first.LeaseToken, retry.LeaseToken);

        await store.CompleteAsync(retry, now.AddSeconds(32));
        Assert.Null(await store.TryClaimNextAsync(now.AddHours(1), TimeSpan.FromMinutes(5)));
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
            INSERT INTO pending_tmdb_nfo_rewrite_jobs (
                id, bangumi_subject_id, tmdb_series_id, save_root_path,
                series_directory_name, canonical_series_name, state,
                created_at_utc, updated_at_utc)
            VALUES (
                'rewrite-job', 547888, 700, '/media',
                'Fallback Anime', 'Canonical Anime', 'pending', $now, $now);
            """;
        command.Parameters.AddWithValue("$now", "2026-07-28T09:00:00.0000000+00:00");
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }
}
