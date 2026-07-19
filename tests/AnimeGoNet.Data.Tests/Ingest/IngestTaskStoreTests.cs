using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Ingest;
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
        var normalized = Assert.IsType<NormalizedIngestItem>(IngestCommandNormalizer.Normalize(
            "mikan",
            new IngestItemCommand(
                "https://tracker.invalid/personal-passkey/file.torrent",
                new IngestItemInfo("Episode 1", null, "item-1", "3951", null, null, 3951, 547888, null, null))).Item);

        var task = await new IngestTaskStore(fixture.Database).AddAsync(normalized, profile);

        Assert.Equal("bt", task.DownloaderId);
        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT torrent_url_fingerprint, route_snapshot_json FROM ingest_tasks WHERE id = $id;";
        command.Parameters.AddWithValue("$id", task.Id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(64, reader.GetString(0).Length);
        Assert.DoesNotContain("personal-passkey", reader.GetString(1), StringComparison.Ordinal);
        Assert.Contains("\"file_strategy\":\"move\"", reader.GetString(1), StringComparison.Ordinal);
    }
}
