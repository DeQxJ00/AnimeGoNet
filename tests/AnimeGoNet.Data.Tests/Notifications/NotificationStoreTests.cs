using AnimeGoNet.Data.Notifications;
using System.Globalization;

namespace AnimeGoNet.Data.Tests.Notifications;

public sealed class NotificationStoreTests
{
    [Fact]
    public async Task ChannelRoundTripsSecretsOptionsAndEvents()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new NotificationStore(fixture.Database);
        var saved = await store.SaveChannelAsync(null, new NotificationChannelWrite(
            "My Bark", "bark", true, "https://api.day.app", "device-key", null,
            "{\"group\":\"AnimeGoNet\"}", ["metadata_failed", "organization_completed"]),
            DateTimeOffset.UtcNow);

        var loaded = Assert.Single(await store.ListChannelsAsync());
        Assert.Equal(saved.Id, loaded.Id);
        Assert.Equal("device-key", loaded.Secret);
        Assert.Contains("AnimeGoNet", loaded.OptionsJson, StringComparison.Ordinal);
        Assert.Equal(["metadata_failed", "organization_completed"], loaded.Events);
        Assert.True(await store.DeleteChannelAsync(saved.Id));
        Assert.Empty(await store.ListChannelsAsync());
    }

    [Fact]
    public async Task TaskStateTransitionCreatesDurableNotificationEvent()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        await using (var connection = await fixture.Database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO source_profiles (
                    id, display_name, adapter, downloader_id, file_strategy,
                    rss_filter_enabled, rss_priority_enabled, revision, enabled,
                    created_at_utc, updated_at_utc)
                VALUES ('notify-source', 'Notify', 'mikan', 'bt', 'move',
                        1, 1, 1, 1, $now, $now);
                INSERT INTO ingest_tasks (
                    id, source_profile_id, source_profile_revision, source_id,
                    title, torrent_url_fingerprint, downloader_id, route_snapshot_json,
                    status, created_at_utc, updated_at_utc)
                VALUES ('notify-task', 'notify-source', 1, 'mikan', 'Notify Anime',
                        $fingerprint, 'bt', '{}', 'metadata_resolving', $now, $now);
                UPDATE ingest_tasks
                SET status = 'metadata_failed', failure_kind = 'network',
                    failure_reason = 'tmdb_timeout', updated_at_utc = $later
                WHERE id = 'notify-task';
                """;
            command.Parameters.AddWithValue("$now", "2026-08-20T00:00:00Z");
            command.Parameters.AddWithValue("$later", "2026-08-20T00:01:00Z");
            command.Parameters.AddWithValue("$fingerprint", new string('a', 64));
            await command.ExecuteNonQueryAsync();
        }

        var store = new NotificationStore(fixture.Database);
        var value = Assert.IsType<NotificationEvent>(await store.ClaimNextEventAsync(
            DateTimeOffset.Parse("2026-08-20T00:02:00Z", CultureInfo.InvariantCulture), TimeSpan.FromMinutes(1)));
        Assert.Equal("metadata_failed", value.EventType);
        Assert.Equal("notify-task", value.TaskId);
        Assert.Contains("tmdb_timeout", value.Body, StringComparison.Ordinal);
        await store.CompleteEventAsync(value.Id, DateTimeOffset.Parse("2026-08-20T00:03:00Z", CultureInfo.InvariantCulture));
        Assert.Null(await store.ClaimNextEventAsync(
            DateTimeOffset.Parse("2026-08-20T00:04:00Z", CultureInfo.InvariantCulture), TimeSpan.FromMinutes(1)));
    }
}
