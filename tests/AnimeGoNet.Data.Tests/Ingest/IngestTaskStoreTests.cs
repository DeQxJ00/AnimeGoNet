using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Torrents;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Sources;

namespace AnimeGoNet.Data.Tests.Ingest;

public sealed class IngestTaskStoreTests
{
    [Fact]
    public async Task SeedsProfilesWithoutOverwritingAndStoresOnlyTorrentFingerprint()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var profileStore = new SourceProfileStore(fixture.Database);
        var options = AnimeGoDefaults.CreateDocker();
        await profileStore.EnsureSeedsAsync(options.InitialSourceProfiles);
        await profileStore.EnsureSeedsAsync(options.InitialSourceProfiles);
        var profile = Assert.IsType<SourceProfileRecord>(await profileStore.GetEnabledAsync("mikan"));
        Assert.Equal(["mikanani.me"], profile.AllowedTorrentHosts);
        var normalized = Assert.IsType<NormalizedIngestItem>(IngestCommandNormalizer.Normalize(
            "mikan",
            new IngestItemCommand(
                "https://tracker.invalid/personal-passkey/file.torrent",
                new IngestItemInfo("Episode 1", null, "item-1", "3951", null, null, 3951, 547888, null, null))).Item);

        var task = await new IngestTaskStore(fixture.Database).AddAsync(normalized, profile);

        Assert.Equal("bt", task.DownloaderId);
        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT torrent_url_fingerprint, route_snapshot_json, media_type FROM ingest_tasks WHERE id = $id;";
        command.Parameters.AddWithValue("$id", task.Id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(64, reader.GetString(0).Length);
        Assert.DoesNotContain("personal-passkey", reader.GetString(1), StringComparison.Ordinal);
        Assert.Contains("\"file_strategy\":\"move\"", reader.GetString(1), StringComparison.Ordinal);
        Assert.Contains("\"category\":\"animegonet\"", reader.GetString(1), StringComparison.Ordinal);
        Assert.Contains("\"tags\":[]", reader.GetString(1), StringComparison.Ordinal);
        using (var route = System.Text.Json.JsonDocument.Parse(reader.GetString(1)))
        {
            Assert.Equal(
                "{year}年{quarter}月新番",
                route.RootElement.GetProperty("dynamic_tag_template").GetString());
            Assert.True(
                route.RootElement.GetProperty("duplicate_notification_enabled").GetBoolean());
        }
        Assert.Contains("\"seeding_time_minutes\":0", reader.GetString(1), StringComparison.Ordinal);
        Assert.Contains("\"allowed_torrent_hosts\":[\"mikanani.me\"]", reader.GetString(1), StringComparison.Ordinal);
        Assert.Equal("tv", reader.GetString(2));
    }

    [Fact]
    public async Task PersistsMovieMediaTypeOnIngestTask()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var profiles = new SourceProfileStore(fixture.Database);
        await profiles.EnsureSeedsAsync(AnimeGoDefaults.CreateDocker().InitialSourceProfiles);
        var profile = Assert.IsType<SourceProfileRecord>(await profiles.GetEnabledAsync("mikan"));
        var normalized = CreateNormalized() with { MediaType = "movie" };

        var task = await new IngestTaskStore(fixture.Database).AddAsync(normalized, profile);

        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT media_type FROM ingest_tasks WHERE id = $id;";
        command.Parameters.AddWithValue("$id", task.Id);
        Assert.Equal("movie", await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task StagedIngestAtomicallyStoresSafeTorrentMetadataAndFiles()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var profileStore = new SourceProfileStore(fixture.Database);
        await profileStore.EnsureSeedsAsync(AnimeGoDefaults.CreateDocker().InitialSourceProfiles);
        var profile = Assert.IsType<SourceProfileRecord>(await profileStore.GetEnabledAsync("mikan"));
        var normalized = CreateNormalized();
        var metadata = new TorrentMetadata(
            "Show",
            new string('a', 40),
            7,
            [
                new TorrentFile("Show/episode.mkv", 5, false),
                new TorrentFile("Show/_____padding_file0", 2, true),
            ]);

        var task = await new IngestTaskStore(fixture.Database).AddStagedAsync(
            normalized,
            profile,
            metadata,
            "safe-random-name.torrent",
            DateTimeOffset.UtcNow.AddMinutes(15));

        Assert.Equal("staged", task.Status);
        Assert.Equal(2, task.FileCount);
        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ingest_tasks.status, staged_torrents.staging_file_name,
                   staged_torrents.info_hash, COUNT(task_files.id),
                   SUM(CASE WHEN task_files.disposition = 'ignored' THEN 1 ELSE 0 END)
            FROM ingest_tasks
            JOIN staged_torrents ON staged_torrents.task_id = ingest_tasks.id
            JOIN task_files ON task_files.task_id = ingest_tasks.id
            WHERE ingest_tasks.id = $id
            GROUP BY ingest_tasks.id;
            """;
        command.Parameters.AddWithValue("$id", task.Id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("staged", reader.GetString(0));
        Assert.Equal("safe-random-name.torrent", reader.GetString(1));
        Assert.Equal(new string('a', 40), reader.GetString(2));
        Assert.Equal(2, reader.GetInt32(3));
        Assert.Equal(1, reader.GetInt32(4));
        var persisted = string.Join('|', Enumerable.Range(0, reader.FieldCount).Select(reader.GetValue));
        Assert.DoesNotContain("personal-passkey", persisted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StagedIngestPersistsOnlyNormalIntegerEpisodeAsTmdbCandidate()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var profileStore = new SourceProfileStore(fixture.Database);
        await profileStore.EnsureSeedsAsync(AnimeGoDefaults.CreateDocker().InitialSourceProfiles);
        var profile = Assert.IsType<SourceProfileRecord>(await profileStore.GetEnabledAsync("mikan"));
        var metadata = new TorrentMetadata(
            "Show",
            new string('f', 40),
            15,
            [
                new TorrentFile("Show [04].mkv", 5, false),
                new TorrentFile("Show [48.5].mkv", 5, false),
                new TorrentFile("Show [SP01].mkv", 5, false),
            ]);

        var task = await new IngestTaskStore(fixture.Database).AddStagedAsync(
            CreateNormalized(),
            profile,
            metadata,
            "episode-candidates.torrent",
            DateTimeOffset.UtcNow.AddMinutes(15));

        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT relative_path, source_episode, file_episode_candidate
            FROM task_files
            WHERE task_id = $task_id
            ORDER BY relative_path;
            """;
        command.Parameters.AddWithValue("$task_id", task.Id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("4", reader.GetString(1));
        Assert.Equal("4", reader.GetString(2));
        Assert.True(await reader.ReadAsync());
        Assert.Equal("48.5", reader.GetString(1));
        Assert.True(reader.IsDBNull(2));
        Assert.True(await reader.ReadAsync());
        Assert.Equal("sp01", reader.GetString(1));
        Assert.True(reader.IsDBNull(2));
    }

    [Fact]
    public async Task FileEpisodeCandidateUsesUpstreamParserSafetyPolicyOnlyForMikanAdapter()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var profileStore = new SourceProfileStore(fixture.Database);
        await profileStore.EnsureSeedsAsync(AnimeGoDefaults.CreateDocker().InitialSourceProfiles);
        await profileStore.CreateAsync(
            "u2",
            new SourceProfileDefinition(
                "U2",
                "u2",
                "bt",
                "move",
                ["tracker.invalid"],
                "animegonet-test",
                [],
                0,
                false,
                false,
                true),
            DateTimeOffset.UtcNow);
        var mikan = Assert.IsType<SourceProfileRecord>(await profileStore.GetEnabledAsync("mikan"));
        var u2 = Assert.IsType<SourceProfileRecord>(await profileStore.GetEnabledAsync("u2"));
        var store = new IngestTaskStore(fixture.Database);
        var mikanTask = await store.AddStagedAsync(
            CreateNormalized(),
            mikan,
            new TorrentMetadata(
                "Mikan",
                new string('d', 40),
                20,
                [
                    new TorrentFile("Show [04].mkv", 5, false),
                    new TorrentFile("Show [2024].mkv", 5, false),
                    new TorrentFile("Show [01][02].mkv", 5, false),
                    new TorrentFile("Show -  7.mkv", 5, false),
                    new TorrentFile(
                        "[Dynamis One] Kokoore - 07 (CR 1920x1080 AVC AAC MKV) [13335833].mkv",
                        5,
                        false),
                ]),
            "mikan-policy.torrent",
            DateTimeOffset.UtcNow.AddMinutes(15));
        var u2Task = await store.AddStagedAsync(
            CreateNormalized("u2"),
            u2,
            new TorrentMetadata(
                "U2",
                new string('e', 40),
                5,
                [new TorrentFile("Show [04].mkv", 5, false)]),
            "u2-policy.torrent",
            DateTimeOffset.UtcNow.AddMinutes(15));

        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT task_id, relative_path, source_episode, file_episode_candidate
            FROM task_files
            WHERE task_id IN ($mikan_task_id, $u2_task_id)
            ORDER BY task_id, relative_path;
            """;
        command.Parameters.AddWithValue("$mikan_task_id", mikanTask.Id);
        command.Parameters.AddWithValue("$u2_task_id", u2Task.Id);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(string TaskId, string Path, string? SourceEpisode, string? Candidate)>();
        while (await reader.ReadAsync())
        {
            rows.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        Assert.Contains(rows, row =>
            row.TaskId == mikanTask.Id
            && row.Path == "Show [04].mkv"
            && row.SourceEpisode == "4"
            && row.Candidate == "4");
        Assert.Contains(rows, row =>
            row.TaskId == mikanTask.Id
            && row.Path == "Show -  7.mkv"
            && row.SourceEpisode == "7"
            && row.Candidate == "7");
        Assert.Contains(rows, row =>
            row.TaskId == mikanTask.Id
            && row.Path == "[Dynamis One] Kokoore - 07 (CR 1920x1080 AVC AAC MKV) [13335833].mkv"
            && row.SourceEpisode == "7"
            && row.Candidate == "7");
        Assert.Contains(rows, row =>
            row.TaskId == mikanTask.Id
            && row.Path == "Show [2024].mkv"
            && row.Candidate is null);
        Assert.Contains(rows, row =>
            row.TaskId == mikanTask.Id
            && row.Path == "Show [01][02].mkv"
            && row.Candidate is null);
        Assert.Contains(rows, row =>
            row.TaskId == u2Task.Id
            && row.Path == "Show [04].mkv"
            && row.SourceEpisode == "4"
            && row.Candidate is null);
    }

    [Fact]
    public async Task ExpiredStagingBecomesFailedAndReturnsOnlySafeFileNameForCleanup()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var profileStore = new SourceProfileStore(fixture.Database);
        await profileStore.EnsureSeedsAsync(AnimeGoDefaults.CreateDocker().InitialSourceProfiles);
        var profile = Assert.IsType<SourceProfileRecord>(await profileStore.GetEnabledAsync("mikan"));
        var store = new IngestTaskStore(fixture.Database);
        var now = DateTimeOffset.UtcNow;
        var task = await store.AddStagedAsync(
            CreateNormalized(),
            profile,
            new TorrentMetadata("episode.mkv", new string('b', 40), 5, [new TorrentFile("episode.mkv", 5, false)]),
            "expired.torrent",
            now.AddSeconds(-1));

        var expired = Assert.Single(await store.ExpireStagedAsync(now));

        Assert.Equal(task.Id, expired.TaskId);
        Assert.Equal("expired.torrent", expired.StagingFileName);
        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT status, failure_kind,
                   (SELECT COUNT(*) FROM staged_torrents WHERE task_id = $id)
            FROM ingest_tasks WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", task.Id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("failed", reader.GetString(0));
        Assert.Equal("staging_expired", reader.GetString(1));
        Assert.Equal(0, reader.GetInt32(2));
    }

    [Fact]
    public async Task ConcurrentDispatchClaimsReturnEachStagedTaskAtMostOnce()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var profileStore = new SourceProfileStore(fixture.Database);
        await profileStore.EnsureSeedsAsync(AnimeGoDefaults.CreateDocker().InitialSourceProfiles);
        var profile = Assert.IsType<SourceProfileRecord>(await profileStore.GetEnabledAsync("mikan"));
        var store = new IngestTaskStore(fixture.Database);
        var task = await store.AddStagedAsync(
            CreateNormalized(),
            profile,
            new TorrentMetadata("episode.mkv", new string('c', 40), 5, [new TorrentFile("episode.mkv", 5, false)]),
            "claim-once.torrent",
            DateTimeOffset.UtcNow.AddMinutes(15));
        var now = DateTimeOffset.UtcNow;

        var claims = await Task.WhenAll(
            store.TryClaimNextStagedAsync(now, TimeSpan.FromMinutes(1)),
            store.TryClaimNextStagedAsync(now, TimeSpan.FromMinutes(1)));

        var claim = Assert.Single(claims, item => item is not null);
        Assert.Equal(task.Id, claim!.TaskId);
        Assert.Equal(1, claim.AttemptCount);
        Assert.Equal("animegonet", claim.Category);
        Assert.Empty(claim.Tags);
        Assert.Equal(0, claim.SeedingTimeMinutes);
        Assert.Equal("{year}年{quarter}月新番", claim.DynamicTagTemplate);
    }

    private static NormalizedIngestItem CreateNormalized(string source = "mikan") =>
        Assert.IsType<NormalizedIngestItem>(IngestCommandNormalizer.Normalize(
            source,
            new IngestItemCommand(
                "https://tracker.invalid/personal-passkey/file.torrent",
                source == "mikan"
                    ? new IngestItemInfo(
                        "Episode 1",
                        null,
                        "item-1",
                        "3951",
                        null,
                        null,
                        3951,
                        547888,
                        null,
                        null)
                    : new IngestItemInfo(
                        "Episode 1",
                        null,
                        "item-u2",
                        "work-u2",
                        null,
                        null,
                        null,
                        null,
                        1234,
                        null))).Item);
}
