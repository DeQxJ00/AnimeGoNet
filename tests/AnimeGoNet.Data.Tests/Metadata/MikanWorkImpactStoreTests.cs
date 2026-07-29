using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Mikan;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Sources;

namespace AnimeGoNet.Data.Tests.Metadata;

public sealed class MikanWorkImpactStoreTests
{
    [Fact]
    public async Task ImpactCountsAllTasksAndRematchOnlyRetriesFailedUnleasedTasks()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var profiles = new SourceProfileStore(fixture.Database);
        await profiles.EnsureSeedsAsync(AnimeGoDefaults.CreateDocker().InitialSourceProfiles);
        var profile = Assert.IsType<SourceProfileRecord>(await profiles.GetEnabledAsync("mikan"));
        var taskStore = new IngestTaskStore(fixture.Database);
        var taskIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (status, marker) in new[]
                 {
                     ("received", 'a'),
                     ("metadata_failed", 'b'),
                     ("metadata_resolving", 'c'),
                     ("metadata_resolved", 'd'),
                     ("organized", 'e'),
                 })
        {
            var task = await taskStore.AddAsync(
                new NormalizedIngestItem(
                    "mikan",
                    new Uri($"https://mikanani.me/{marker}.torrent"),
                    new string(marker, 64),
                    $"Task {status}",
                    $"item-{marker}",
                    "3951",
                    3951,
                    547888,
                    null,
                    null),
                profile);
            taskIds[status] = task.Id;
            await SetStatusAsync(fixture, task.Id, status);
        }

        var store = new MetadataResolutionStore(fixture.Database);
        var impact = await store.GetMikanWorkImpactAsync(3951, limit: 3);

        Assert.Equal(5, impact.TotalTaskCount);
        Assert.Equal(1, impact.FutureTaskCount);
        Assert.Equal(1, impact.RetryableFailedTaskCount);
        Assert.Equal(1, impact.ActiveTaskCount);
        Assert.Equal(1, impact.ResolvedProtectedTaskCount);
        Assert.Equal(1, impact.CompletedProtectedTaskCount);
        Assert.True(impact.IsTruncated);
        Assert.Equal(3, impact.Tasks.Count);

        var retried = await store.RematchFailedMikanTasksAsync(
            3951,
            expectedRuleRevision: 0,
            DateTimeOffset.UtcNow);

        Assert.Equal(1, retried);
        Assert.Equal("downloaded", await ReadStatusAsync(fixture, taskIds["metadata_failed"]));
        Assert.Equal("metadata_resolved", await ReadStatusAsync(fixture, taskIds["metadata_resolved"]));
        Assert.Equal("organized", await ReadStatusAsync(fixture, taskIds["organized"]));
    }

    [Fact]
    public async Task RematchRequiresCurrentRuleRevision()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var rule = await new MikanWorkMetadataRuleStore(fixture.Database).SaveAsync(
            new MikanWorkMetadataRuleUpdate(3951, 547888, null, null, null, true),
            expectedRevision: 0,
            DateTimeOffset.UtcNow);
        var store = new MetadataResolutionStore(fixture.Database);

        var exception = await Assert.ThrowsAsync<MikanWorkRuleRematchRevisionException>(() =>
            store.RematchFailedMikanTasksAsync(
                3951,
                expectedRuleRevision: 0,
                DateTimeOffset.UtcNow));

        Assert.Contains("revision changed", exception.Message, StringComparison.Ordinal);
        Assert.Equal(
            0,
            await store.RematchFailedMikanTasksAsync(
                3951,
                rule.Revision,
                DateTimeOffset.UtcNow));
    }

    private static async Task SetStatusAsync(
        SqliteDatabaseFixture fixture,
        string taskId,
        string status)
    {
        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ingest_tasks
            SET status = $status,
                failure_kind = CASE WHEN $status = 'metadata_failed' THEN 'SemanticNoMatch' ELSE NULL END,
                failure_reason = CASE WHEN $status = 'metadata_failed' THEN 'fixture_failure' ELSE NULL END
            WHERE id = $task_id;
            """;
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$task_id", taskId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<string> ReadStatusAsync(
        SqliteDatabaseFixture fixture,
        string taskId)
    {
        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT status FROM ingest_tasks WHERE id = $task_id;";
        command.Parameters.AddWithValue("$task_id", taskId);
        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }
}
