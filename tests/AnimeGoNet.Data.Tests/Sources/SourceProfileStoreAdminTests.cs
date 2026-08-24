using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Media;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Sources;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Tests.Sources;

public sealed class SourceProfileStoreAdminTests
{
    [Fact]
    public async Task MikanMovieMediaTypeIsPersistedAndNonMikanMovieIsRejected()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new SourceProfileStore(fixture.Database);
        var created = await store.CreateAsync(
            "mikan-movie",
            Definition("Mikan movie", "mikan", "bt", "move", ["mikanani.me"]) with
            {
                MediaType = MediaTypes.Movie,
            },
            At(10));

        Assert.Equal(MediaTypes.Movie, created.MediaType);
        Assert.Equal(
            MediaTypes.Movie,
            (await store.GetEnabledAsync("mikan-movie"))?.MediaType);
        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateAsync(
            "u2-movie",
            Definition("U2 movie", "u2", "pt", "link", ["u2.invalid"]) with
            {
                MediaType = MediaTypes.Movie,
            },
            At(11)));
    }

    [Fact]
    public async Task CreateUpdateAndListPreserveRevisionAndImmutableTaskRoute()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new SourceProfileStore(fixture.Database);
        await store.EnsureSeedsAsync(AnimeGoDefaults.CreateDocker().InitialSourceProfiles);
        var created = await store.CreateAsync(
            "u2",
            Definition("U2", "u2", "pt", "link", ["u2.invalid"]) with
            {
                DynamicTagTemplate = "{year}年{quarter}月新番,EP{ep}",
            },
            At(10));
        var enabled = Assert.IsType<SourceProfileRecord>(await store.GetEnabledAsync("u2"));
        var task = await new IngestTaskStore(fixture.Database).AddAsync(
            new NormalizedIngestItem(
                "u2", new Uri("https://u2.invalid/download/a.torrent"), new string('a', 64),
                "U2 episode", "u2-a", "u2-work", null, null, null, null),
            enabled);

        var updated = await store.UpdateAsync(
            "u2",
            Definition("U2 route", "u2", "bt", "move", ["u2.invalid", "*.u2.invalid"]),
            expectedRevision: 1,
            At(11));
        var listed = Assert.Single(await store.ListAsync(), item => item.Id == "u2");

        Assert.Equal(1, created.Revision);
        Assert.Equal("{year}年{quarter}月新番,EP{ep}", created.DynamicTagTemplate);
        Assert.Equal(2, updated.Revision);
        Assert.Equal("bt", updated.DownloaderId);
        Assert.Equal("move", updated.FileStrategy);
        Assert.Equal("animegonet", updated.Category);
        Assert.Equal(["source-test"], updated.Tags);
        Assert.Equal(0, updated.SeedingTimeMinutes);
        Assert.Null(updated.DynamicTagTemplate);
        Assert.Equal(1, listed.IngestTaskCount);
        Assert.Equal("pt", task.DownloaderId);
        Assert.Equal(1, task.SourceProfileRevision);
        await Assert.ThrowsAsync<SourceProfileRevisionException>(() => store.UpdateAsync(
            "u2", Definition("stale", "u2", "bt", "move", ["u2.invalid"]), 1, At(12)));
        await Assert.ThrowsAsync<SourceProfileConflictException>(() => store.DeleteAsync("u2", 2));
    }

    [Fact]
    public async Task UnreferencedProfileCanBeDeletedButDefaultMikanIsProtected()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new SourceProfileStore(fixture.Database);
        await store.EnsureSeedsAsync(AnimeGoDefaults.CreateDocker().InitialSourceProfiles);
        _ = await store.CreateAsync(
            "ttg", Definition("TTG", "ttg", "pt", "link", ["ttg.invalid"]), At(10));

        await store.DeleteAsync("ttg", 1);

        Assert.Null(await store.GetAsync("ttg"));
        await Assert.ThrowsAsync<SourceProfileConflictException>(() => store.DeleteAsync("mikan", 1));
    }

    [Fact]
    public async Task DuplicateCreateAndMissingUpdateHaveStableExceptions()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new SourceProfileStore(fixture.Database);
        await store.EnsureSeedsAsync(AnimeGoDefaults.CreateDocker().InitialSourceProfiles);
        var definition = Definition("U2", "u2", "pt", "link", ["u2.invalid"]);
        _ = await store.CreateAsync("u2", definition, At(10));

        await Assert.ThrowsAsync<SourceProfileDuplicateException>(() => store.CreateAsync("u2", definition, At(11)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.UpdateAsync("missing", definition, 1, At(11)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.DeleteAsync("missing", 1));
    }

    [Fact]
    public async Task StoreEnforcesDownloadPolicyWithoutApiLayer()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new SourceProfileStore(fixture.Database);

        var invalid = Definition("U2", "u2", "pt", "move", ["u2.invalid"]) with
        {
            SeedingTimeMinutes = 1,
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.CreateAsync("u2", invalid, At(10)));

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.CreateAsync(
                "u2-tag",
                Definition("U2", "u2", "pt", "link", ["u2.invalid"]) with
                {
                    DynamicTagTemplate = "{unsupported}",
                },
                At(10)));
    }

    [Fact]
    public async Task MikanCookieIsNormalizedVersionedAndNeverFormatted()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new SourceProfileStore(fixture.Database);
        var seed = AnimeGoDefaults.CreateDocker().InitialSourceProfiles[0]
            with
        {
            MikanIdentityCookie =
                    ".AspNetCore.Identity.Application=first-secret",
        };

        await store.EnsureSeedsAsync([seed]);
        await store.EnsureSeedsAsync([seed]);
        var first = Assert.IsType<SourceProfileAdminRecord>(
            await store.GetAsync("mikan"));

        Assert.Equal(1, first.Revision);
        Assert.Equal("first-secret", first.MikanIdentityCookie);
        Assert.DoesNotContain(
            "first-secret",
            first.ToString(),
            StringComparison.Ordinal);

        await store.EnsureSeedsAsync(
        [
            seed with { MikanIdentityCookie = "second-secret" },
        ]);
        var second = Assert.IsType<SourceProfileRecord>(
            await store.GetEnabledAsync("mikan"));

        Assert.Equal(2, second.Revision);
        Assert.Equal("second-secret", second.MikanIdentityCookie);
        Assert.DoesNotContain(
            "second-secret",
            second.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SeedRssScheduleIsPersistedOnceAndUserClearSurvivesRestart()
    {
        const string feedUrl =
            "https://mikanani.me/RSS/MyBangumi?token=seed-private-value";
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new SourceProfileStore(fixture.Database);
        var seed = AnimeGoDefaults.CreateDocker().InitialSourceProfiles[0] with
        {
            RssFeedUrl = feedUrl,
            RssScheduleEnabled = true,
            RssScheduleCron = "0 3/20 * * * ?",
        };

        await store.EnsureSeedsAsync([seed]);
        var inserted = Assert.IsType<SourceProfileAdminRecord>(
            await store.GetAsync("mikan"));
        Assert.Equal(feedUrl, inserted.RssFeedUrl);
        Assert.True(inserted.RssScheduleEnabled);
        Assert.Equal("0 3/20 * * * ?", inserted.RssScheduleCron);

        _ = await store.UpdateAsync(
            "mikan",
            Definition("Mikan", "mikan", "bt", "move", ["mikanani.me"]),
            inserted.Revision,
            At(10));
        await store.EnsureSeedsAsync([seed]);

        var afterRestart = Assert.IsType<SourceProfileAdminRecord>(
            await store.GetAsync("mikan"));
        Assert.Null(afterRestart.RssFeedUrl);
        Assert.False(afterRestart.RssScheduleEnabled);
        Assert.DoesNotContain(
            "seed-private-value",
            afterRestart.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeploymentOverrideRemainsAuthoritativeAndIsIdempotent()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new SourceProfileStore(fixture.Database);
        var seed = AnimeGoDefaults.CreateDocker().InitialSourceProfiles[0];
        await store.EnsureSeedsAsync([seed]);
        var initial = Assert.IsType<SourceProfileAdminRecord>(await store.GetAsync("mikan"));
        var userValue = await store.UpdateAsync(
            "mikan",
            Definition("Mikan", "mikan", "bt", "move", ["mikanani.me"]) with
            {
                Category = "user-category",
                DynamicTagTemplate = "{year}-user",
                MikanIdentityCookie = "user-cookie",
            },
            initial.Revision,
            At(10));

        var deploymentOverride = new SourceProfileDeploymentOverride(
            "mikan",
            "mikan",
            OverrideCategory: true,
            Category: "environment-category",
            OverrideDynamicTagTemplate: true,
            DynamicTagTemplate: "{year}-environment",
            OverrideMikanIdentityCookie: true,
            MikanIdentityCookie: "environment-cookie");
        Assert.True(await store.ApplyDeploymentOverrideAsync(
            deploymentOverride,
            At(11)));
        var applied = Assert.IsType<SourceProfileAdminRecord>(await store.GetAsync("mikan"));

        Assert.Equal(userValue.Revision + 1, applied.Revision);
        Assert.Equal("environment-category", applied.Category);
        Assert.Equal("{year}-environment", applied.DynamicTagTemplate);
        Assert.Equal("environment-cookie", applied.MikanIdentityCookie);
        Assert.DoesNotContain("environment-cookie", deploymentOverride.ToString(), StringComparison.Ordinal);
        Assert.False(await store.ApplyDeploymentOverrideAsync(
            deploymentOverride,
            At(12)));
        Assert.Equal(
            applied.Revision,
            (await store.GetAsync("mikan"))!.Revision);

        Assert.True(await store.ApplyDeploymentOverrideAsync(
            deploymentOverride with
            {
                OverrideCategory = false,
                DynamicTagTemplate = null,
                MikanIdentityCookie = null,
            },
            At(13)));
        var cleared = Assert.IsType<SourceProfileAdminRecord>(await store.GetAsync("mikan"));
        Assert.Equal("environment-category", cleared.Category);
        Assert.Null(cleared.DynamicTagTemplate);
        Assert.Null(cleared.MikanIdentityCookie);
    }

    [Fact]
    public async Task NonMikanProfileCannotPersistMikanCookie()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new SourceProfileStore(fixture.Database);
        var definition = Definition(
            "U2",
            "u2",
            "pt",
            "link",
            ["u2.invalid"]) with
        {
            MikanIdentityCookie = "must-not-persist",
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.CreateAsync("u2", definition, At(10)));

        Assert.Null(await store.GetAsync("u2"));
    }

    [Fact]
    public async Task RssScheduleIsVersionedAuditedAndRecoversInterruptedRuns()
    {
        const string secretUrl =
            "https://mikanani.me/RSS/MyBangumi?token=store-private-value";
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new SourceProfileStore(fixture.Database);
        var created = await store.CreateAsync(
            "mikan-scheduled",
            Definition(
                "Mikan scheduled",
                "mikan",
                "bt",
                "move",
                ["mikanani.me"]) with
            {
                RssFeedUrl = secretUrl,
                RssScheduleEnabled = true,
                RssScheduleCron = "0 5/15 * * * ?",
            },
            At(10));

        Assert.Equal(secretUrl, created.RssFeedUrl);
        Assert.DoesNotContain("store-private-value", created.ToString(), StringComparison.Ordinal);
        Assert.Equal(
            "mikan-scheduled",
            Assert.Single(await store.ListScheduledAsync()).Id);
        Assert.NotNull(await store.GetScheduledExecutionAsync("mikan-scheduled", 1));
        Assert.True(await store.TryStartScheduledRunAsync("mikan-scheduled", 1, At(11)));
        Assert.False(await store.TryStartScheduledRunAsync("mikan-scheduled", 1, At(11)));
        Assert.True(await store.FailScheduledRunAsync(
            "mikan-scheduled", 1, "rss_request_failed", At(12)));

        var failed = Assert.IsType<SourceProfileAdminRecord>(
            await store.GetAsync("mikan-scheduled"));
        Assert.Equal("failed", failed.RssLastRunState);
        Assert.Equal("rss_request_failed", failed.RssLastFailureCode);
        Assert.Equal(At(12), failed.RssLastCompletedAtUtc);

        var updated = await store.UpdateAsync(
            "mikan-scheduled",
            Definition(
                "Mikan scheduled",
                "mikan",
                "bt",
                "move",
                ["mikanani.me"]) with
            {
                RssFeedUrl = secretUrl,
                RssScheduleEnabled = true,
                RssScheduleCron = "0 10/15 * * * ?",
            },
            1,
            At(13));
        Assert.Equal("never", updated.RssLastRunState);
        Assert.Null(updated.RssLastFailureCode);
        Assert.Null(await store.GetScheduledExecutionAsync("mikan-scheduled", 1));
        Assert.NotNull(await store.GetScheduledExecutionAsync("mikan-scheduled", 2));

        Assert.True(await store.TryStartScheduledRunAsync("mikan-scheduled", 2, At(14)));
        Assert.Equal(1, await store.RecoverInterruptedScheduledRunsAsync(At(15)));
        var recovered = Assert.IsType<SourceProfileAdminRecord>(
            await store.GetAsync("mikan-scheduled"));
        Assert.Equal("failed", recovered.RssLastRunState);
        Assert.Equal("rss_schedule_interrupted", recovered.RssLastFailureCode);
        Assert.Equal(At(15), recovered.RssLastCompletedAtUtc);
    }

    [Fact]
    public async Task RssScheduleRejectsNonMikanUrlAndMissingUrl()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new SourceProfileStore(fixture.Database);

        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateAsync(
            "u2-rss",
            Definition("U2 RSS", "u2", "pt", "link", ["u2.invalid"]) with
            {
                RssFeedUrl = "https://u2.invalid/rss",
            },
            At(10)));
        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateAsync(
            "mikan-rss",
            Definition("Mikan RSS", "mikan", "bt", "move", ["mikanani.me"]) with
            {
                RssScheduleEnabled = true,
            },
            At(10)));
    }

    [Fact]
    public async Task ManualRssRunIsAuditedWhileAutomaticScheduleIsDisabled()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new SourceProfileStore(fixture.Database);
        var created = await store.CreateAsync(
            "mikan-manual-rss",
            Definition("Mikan manual RSS", "mikan", "bt", "move", ["mikanani.me"]) with
            {
                RssFeedUrl = "https://mikanani.me/RSS/MyBangumi?token=manual-private",
                RssScheduleEnabled = false,
            },
            At(10));

        Assert.Empty(await store.ListScheduledAsync());
        Assert.True(await store.TryStartManualRssRunAsync(created.Id, created.Revision, At(11)));
        Assert.False(await store.TryStartManualRssRunAsync(created.Id, created.Revision, At(11)));
        Assert.True(await store.FailScheduledRunAsync(
            created.Id,
            created.Revision,
            "rss_manual_test_failure",
            At(12)));

        var completed = Assert.IsType<SourceProfileAdminRecord>(await store.GetAsync(created.Id));
        Assert.False(completed.RssScheduleEnabled);
        Assert.Equal("failed", completed.RssLastRunState);
        Assert.Equal("rss_manual_test_failure", completed.RssLastFailureCode);
        Assert.Null(completed.RssLastBatchId);
        Assert.Equal(At(12), completed.RssLastCompletedAtUtc);
    }

    [Fact]
    public async Task V33UpgradeAppliesConfiguredTemplateOnceAndExplicitClearSurvivesRestartSeed()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "animegonet-dynamic-tag-upgrade",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var databasePath = Path.Combine(root, "animegonet.db");
            await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                await using (var migrationTable = connection.CreateCommand())
                {
                    migrationTable.CommandText = """
                        CREATE TABLE schema_migrations (
                            version INTEGER NOT NULL PRIMARY KEY,
                            name TEXT NOT NULL UNIQUE,
                            applied_at_utc TEXT NOT NULL
                        ) STRICT;
                        """;
                    await migrationTable.ExecuteNonQueryAsync();
                }

                foreach (var migration in DatabaseSchema.Migrations.Where(item => item.Version <= 33))
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = migration.Sql + """

                        INSERT INTO schema_migrations(version, name, applied_at_utc)
                        VALUES ($version, $name, $now);
                        """;
                    command.Parameters.AddWithValue("$version", migration.Version);
                    command.Parameters.AddWithValue("$name", migration.Name);
                    command.Parameters.AddWithValue("$now", At(9).ToString("O"));
                    await command.ExecuteNonQueryAsync();
                }

                await using var seed = connection.CreateCommand();
                seed.CommandText = """
                    INSERT INTO source_profiles (
                        id, display_name, adapter, downloader_id, file_strategy,
                        rss_filter_enabled, rss_priority_enabled, revision, enabled,
                        created_at_utc, updated_at_utc)
                    VALUES (
                        'mikan', 'Mikan', 'mikan', 'bt', 'move',
                        1, 1, 7, 1, $now, $now);
                    """;
                seed.Parameters.AddWithValue("$now", At(10).ToString("O"));
                Assert.Equal(1, await seed.ExecuteNonQueryAsync());
            }

            var database = new AnimeGoSqliteDatabase(databasePath);
            await database.InitializeAsync();
            var store = new SourceProfileStore(database);
            var configuredSeed = AnimeGoDefaults.CreateDocker().InitialSourceProfiles[0] with
            {
                DynamicTagTemplate = "{year}-configured",
            };

            await store.EnsureSeedsAsync([configuredSeed]);
            var upgraded = Assert.IsType<SourceProfileAdminRecord>(await store.GetAsync("mikan"));
            Assert.Equal("{year}-configured", upgraded.DynamicTagTemplate);
            Assert.Equal(8, upgraded.Revision);

            var cleared = await store.UpdateAsync(
                "mikan",
                new SourceProfileDefinition(
                    upgraded.DisplayName,
                    upgraded.Adapter,
                    upgraded.DownloaderId,
                    upgraded.FileStrategy,
                    upgraded.AllowedTorrentHosts,
                    upgraded.Category,
                    upgraded.Tags,
                    upgraded.SeedingTimeMinutes,
                    upgraded.RssFilterEnabled,
                    upgraded.RssPriorityEnabled,
                    upgraded.Enabled,
                    upgraded.MikanIdentityCookie,
                    DynamicTagTemplate: null),
                upgraded.Revision,
                At(12));
            Assert.Null(cleared.DynamicTagTemplate);

            await store.EnsureSeedsAsync([configuredSeed]);
            var restarted = Assert.IsType<SourceProfileAdminRecord>(await store.GetAsync("mikan"));
            Assert.Null(restarted.DynamicTagTemplate);
            Assert.Equal(cleared.Revision, restarted.Revision);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static SourceProfileDefinition Definition(
        string name,
        string adapter,
        string downloader,
        string strategy,
        IReadOnlyList<string> hosts) =>
        new(
            name,
            adapter,
            downloader,
            strategy,
            hosts,
            "animegonet",
            ["source-test"],
            strategy == "move" ? 0 : -1,
            false,
            false,
            true);

    private static DateTimeOffset At(int hour) =>
        DateTimeOffset.Parse(
            $"2026-07-26T{hour:00}:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture);
}
