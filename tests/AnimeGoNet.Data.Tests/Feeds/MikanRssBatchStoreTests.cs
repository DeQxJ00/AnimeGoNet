using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Rules;
using AnimeGoNet.Core.Torrents;
using AnimeGoNet.Data.Feeds;
using AnimeGoNet.Data.Ingest;
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

    [Fact]
    public async Task LeaseTokenControlsReleaseAndCompletionRequiresRealIngestTask()
    {
        await using var fixture = await BatchFixture.CreateAsync();
        var now = DateTimeOffset.Parse("2026-07-22T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var stored = await fixture.Store.SaveAsync("mikan", 1, true, Plan(), now);
        var winner = stored.Entries.Single(entry => entry.Decision.Kind == MikanRssDecisionKind.Winner);
        var lease = Assert.IsType<MikanRssWinnerLease>(await fixture.Store.TryClaimWinnerAsync(
            stored.Id, winner.CandidateId, now, TimeSpan.FromMinutes(5)));
        var wrong = lease with { LeaseToken = Guid.NewGuid().ToString("N") };

        Assert.False(await fixture.Store.ReleaseWinnerAsync(wrong));
        Assert.True(await fixture.Store.ReleaseWinnerAsync(lease));
        var second = Assert.IsType<MikanRssWinnerLease>(await fixture.Store.TryClaimWinnerAsync(
            stored.Id, winner.CandidateId, now.AddMinutes(1), TimeSpan.FromMinutes(5)));
        var profile = Assert.IsType<AnimeGoNet.Data.Sources.SourceProfileRecord>(
            await new SourceProfileStore(fixture.Database.Database).GetEnabledAsync("mikan"));
        var task = await new IngestTaskStore(fixture.Database.Database).AddAsync(
            new NormalizedIngestItem(
                "mikan", new Uri("https://example.invalid/a.torrent"), new string('a', 64),
                "Show [03]", winner.CandidateId, "3951", 3951, null, null, null),
            profile);

        Assert.False(await fixture.Store.CompleteWinnerAsync(wrong, task.Id));
        Assert.True(await fixture.Store.CompleteWinnerAsync(second, task.Id));
        Assert.Null(await fixture.Store.TryClaimWinnerAsync(
            stored.Id, winner.CandidateId, now.AddMinutes(2), TimeSpan.FromMinutes(5)));
        var after = Assert.IsType<MikanRssBatchRecord>(await fixture.Store.GetAsync(stored.Id));
        Assert.Equal("ingested", after.Entries.Single(entry => entry.CandidateId == winner.CandidateId).EffectState);
    }

    [Fact]
    public async Task LostWinnerLeaseRollsBackTaskFilesAndStagedTorrentTogether()
    {
        await using var fixture = await BatchFixture.CreateAsync();
        var now = DateTimeOffset.Parse("2026-07-22T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var stored = await fixture.Store.SaveAsync("mikan", 1, true, Plan(), now);
        var winner = stored.Entries.Single(entry => entry.Decision.Kind == MikanRssDecisionKind.Winner);
        var lease = Assert.IsType<MikanRssWinnerLease>(await fixture.Store.TryClaimWinnerAsync(
            stored.Id, winner.CandidateId, now, TimeSpan.FromMinutes(5)));
        var wrong = lease with { LeaseToken = Guid.NewGuid().ToString("N") };
        var profile = Assert.IsType<AnimeGoNet.Data.Sources.SourceProfileRecord>(
            await new SourceProfileStore(fixture.Database.Database).GetEnabledAsync("mikan"));
        var metadata = TorrentMetainfoParser.Parse(System.Text.Encoding.UTF8.GetBytes(
            "d8:announce20:https://secret/token4:infod6:lengthi5e4:name11:episode.mkv12:piece lengthi16384e6:pieces20:aaaaaaaaaaaaaaaaaaaaee"));
        var item = new NormalizedIngestItem(
            "mikan", new Uri("https://example.invalid/a.torrent"), new string('a', 64),
            "Show [03]", winner.CandidateId, "3951", 3951, null, null, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new IngestTaskStore(fixture.Database.Database).AddStagedForRssWinnerAsync(
                item, profile, metadata, "lost-lease.torrent", now.AddHours(1), wrong));

        await using var connection = await fixture.Database.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM ingest_tasks),
                (SELECT COUNT(*) FROM task_files),
                (SELECT COUNT(*) FROM staged_torrents);
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetInt32(0));
        Assert.Equal(0, reader.GetInt32(1));
        Assert.Equal(0, reader.GetInt32(2));
    }

    [Fact]
    public async Task PersistsLegacyFilterRevisionDecisionScopeAndIdentity()
    {
        await using var fixture = await BatchFixture.CreateAsync();
        var feed = new RssFeedDocument(
        [
            Item("Show [03] x265", "a", "https://example.invalid/download/a"),
            Item("Show [03] x264", "b", "https://example.invalid/download/b"),
        ], 3951);
        var audits = new[]
        {
            new MikanLegacyFilterAudit(
                MikanLegacyFilterState.Rejected, "RejectedByLegacyMikanTool",
                "Filiter1", "key_3951_370", 3951, 370),
            new MikanLegacyFilterAudit(
                MikanLegacyFilterState.SkippedByConfiguration, "SkippedByConfiguration"),
        };
        var plan = MikanRssBatchPlanner.Create(
            feed, new MikanRssRuleSet([], [], []),
            legacyFilterAudits: audits, legacyFilterRevision: 9, legacyFilterEnabled: true);

        var stored = await fixture.Store.SaveAsync(
            "mikan", 1, true, plan,
            DateTimeOffset.Parse("2026-07-22T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(9, stored.LegacyFilterRevision);
        Assert.True(stored.LegacyFilterEnabled);
        var rejected = stored.Entries[0];
        Assert.Equal(MikanRssDecisionKind.RejectedByLegacyFilter, rejected.Decision.Kind);
        Assert.Equal(MikanLegacyFilterState.Rejected, rejected.LegacyFilterAudit.State);
        Assert.Equal("Filiter1", rejected.LegacyFilterAudit.MatchedScope);
        Assert.Equal("key_3951_370", rejected.LegacyFilterAudit.MatchedKey);
        Assert.Equal(3951, rejected.LegacyFilterAudit.IdentityMikanId);
        Assert.Equal(370, rejected.LegacyFilterAudit.IdentityGroupId);
        Assert.Equal("blocked", rejected.EffectState);
        Assert.Equal(MikanRssDecisionKind.Winner, stored.Entries[1].Decision.Kind);
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
