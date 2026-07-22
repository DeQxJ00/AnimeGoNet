using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Core.Rules;
using AnimeGoNet.Data.Feeds;
using AnimeGoNet.Data.Sources;

namespace AnimeGoNet.Data.Tests.Feeds;

public sealed class MikanRssBatchStoreTests
{
    [Fact]
    public async Task SavesCompleteDecisionAuditWithoutRawTorrentUrlAndIsIdempotent()
    {
        await using var fixture = await BatchFixture.CreateAsync();
        var plan = Plan();
        var now = DateTimeOffset.Parse("2026-07-22T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        var first = await fixture.Store.SaveAsync("mikan", 3, true, plan, now);
        var second = await fixture.Store.SaveAsync("MIKAN", 3, true, plan, now.AddMinutes(1));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(2, first.Entries.Count);
        Assert.Equal(64, first.Fingerprint.Length);
        Assert.All(first.Entries, entry => Assert.Equal(64, entry.TorrentUrlFingerprint.Length));
        Assert.DoesNotContain(first.Entries, entry => entry.MikanUrl.Contains("credential-secret", StringComparison.Ordinal));
        Assert.Contains(first.Entries, entry => entry.Decision.EvaluatedPriorityGroups.Contains("codec"));
        await using var connection = await fixture.Database.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM mikan_rss_batches;";
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task OnlyWinnerCanBeClaimedAndExpiredLeaseCanRecover()
    {
        await using var fixture = await BatchFixture.CreateAsync();
        var now = DateTimeOffset.Parse("2026-07-22T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var stored = await fixture.Store.SaveAsync("mikan", 1, true, Plan(), now);
        var winner = stored.Entries.Single(entry => entry.Decision.Kind == MikanRssDecisionKind.Winner);
        var loser = stored.Entries.Single(entry => entry.Decision.Kind != MikanRssDecisionKind.Winner);

        Assert.Null(await fixture.Store.TryClaimWinnerAsync(
            stored.Id, loser.CandidateId, now, TimeSpan.FromMinutes(5)));
        var lease = Assert.IsType<MikanRssWinnerLease>(await fixture.Store.TryClaimWinnerAsync(
            stored.Id, winner.CandidateId, now, TimeSpan.FromMinutes(5)));
        Assert.Null(await fixture.Store.TryClaimWinnerAsync(
            stored.Id, winner.CandidateId, now.AddMinutes(1), TimeSpan.FromMinutes(5)));
        var recovered = Assert.IsType<MikanRssWinnerLease>(await fixture.Store.TryClaimWinnerAsync(
            stored.Id, winner.CandidateId, lease.LeaseExpiresAtUtc, TimeSpan.FromMinutes(5)));
        Assert.NotEqual(lease.LeaseToken, recovered.LeaseToken);

        var after = Assert.IsType<MikanRssBatchRecord>(await fixture.Store.GetAsync(stored.Id));
        Assert.Equal("claimed", after.Entries.Single(entry =>
            entry.CandidateId == winner.CandidateId).EffectState);
        Assert.Equal("blocked", after.Entries.Single(entry =>
            entry.CandidateId == loser.CandidateId).EffectState);

        await using var connection = await fixture.Database.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE mikan_rss_batch_entries SET effect_state = 'ready'
            WHERE batch_id = $batch AND candidate_id = $candidate;
            """;
        command.Parameters.AddWithValue("$batch", stored.Id);
        command.Parameters.AddWithValue("$candidate", loser.CandidateId);
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => command.ExecuteNonQueryAsync());
    }

    private static MikanRssBatchPlan Plan()
    {
        var feed = new RssFeedDocument(
        [
            Item("Show [03] x264", "a", "https://example.invalid/download/credential-secret-a"),
            Item("Show [03] x265", "b", "https://example.invalid/download/credential-secret-b"),
        ], 3951);
        var rules = new MikanRssRuleSet([], [],
        [
            new PriorityGroup("codec", "Codec",
            [
                new NamedMatchArray("x265", "x265", true, ["x265"]),
                new NamedMatchArray("x264", "x264", true, ["x264"]),
            ]),
        ]);
        return MikanRssBatchPlanner.Create(feed, rules);
    }

    private static RssFeedItem Item(string title, string id, string torrentUrl) => new(
        title, $"https://mikanani.me/Home/Episode/{id}", torrentUrl,
        "application/x-bittorrent", 42, "2026-07-22");

    private sealed class BatchFixture : IAsyncDisposable
    {
        private BatchFixture(SqliteDatabaseFixture database)
        {
            Database = database;
            Store = new MikanRssBatchStore(database.Database);
        }

        public SqliteDatabaseFixture Database { get; }
        public MikanRssBatchStore Store { get; }

        public static async Task<BatchFixture> CreateAsync()
        {
            var database = await SqliteDatabaseFixture.CreateAsync();
            await new SourceProfileStore(database.Database)
                .EnsureSeedsAsync(AnimeGoDefaults.CreateDocker().InitialSourceProfiles);
            return new BatchFixture(database);
        }

        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }
}
