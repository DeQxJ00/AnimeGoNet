using System.Net;
using System.Text;
using System.Text.Json;
using AnimeGoNet.App.Configuration;
using AnimeGoNet.Data.Sources;
using AnimeGoNet.Data.Mikan;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Api;

public sealed class ConfigurationArchiveApiTests
{
    [Fact]
    public async Task ExportPreviewImportAndAutomaticBackupFormAReviewableFlow()
    {
        await using var app = await RunningApp.StartAsync();
        var downloaderStore = app.App.Services.GetRequiredService<DownloaderOverrideStore>();
        var downloaderSnapshot = await downloaderStore.LoadAsync();
        await downloaderStore.UpsertAsync(
            "archive-qbt",
            new DownloaderOverrideEntry(
                "http://127.0.0.1:8080/",
                "admin",
                "archive-password",
                Path.Combine(app.RootPath, "download", "incomplete", "archive-qbt"),
                true,
                0,
                DateTimeOffset.UtcNow),
            downloaderSnapshot.Revision);

        var sourceStore = app.App.Services.GetRequiredService<SourceProfileStore>();
        var source = (await sourceStore.GetAsync("mikan"))!;
        await sourceStore.UpdateAsync(
            source.Id,
            new SourceProfileDefinition(
                source.DisplayName,
                source.Adapter,
                "archive-qbt",
                source.FileStrategy,
                source.AllowedTorrentHosts,
                source.Category,
                source.Tags,
                source.SeedingTimeMinutes,
                source.RssFilterEnabled,
                source.RssPriorityEnabled,
                source.Enabled,
                "archive-cookie",
                source.DynamicTagTemplate,
                "https://mikanime.tv/RSS/test",
                source.RssScheduleEnabled,
                source.RssScheduleCron,
                source.DuplicateNotificationEnabled,
                MediaType: "movie"),
            source.Revision,
            DateTimeOffset.UtcNow);
        var workRules = app.App.Services.GetRequiredService<MikanWorkMetadataRuleStore>();
        await workRules.SaveAsync(
            new MikanWorkMetadataRuleUpdate(3951, 547888, 11111, 2, 12),
            0,
            DateTimeOffset.UtcNow);

        using var export = await app.Client.GetAsync("/api/v1/configuration-archive/export");
        export.EnsureSuccessStatusCode();
        var archive = await export.Content.ReadAsByteArrayAsync();
        var archiveText = Encoding.UTF8.GetString(archive);
        Assert.Contains("archive-password", archiveText, StringComparison.Ordinal);
        Assert.Contains("archive-cookie", archiveText, StringComparison.Ordinal);
        Assert.Contains("\"media_type\": \"movie\"", archiveText, StringComparison.Ordinal);
        Assert.DoesNotContain("animegonet.db", archiveText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ingest_tasks", archiveText, StringComparison.OrdinalIgnoreCase);

        using var preview = await app.Client.PostAsync(
            "/api/v1/configuration-archive/import/preview",
            Json(archive));
        var previewBody = await preview.Content.ReadAsByteArrayAsync();
        Assert.True(preview.IsSuccessStatusCode, Encoding.UTF8.GetString(previewBody));
        using var previewJson = JsonDocument.Parse(previewBody);
        var sha256 = previewJson.RootElement.GetProperty("sha256").GetString()!;
        Assert.Equal(1, previewJson.RootElement.GetProperty("counts").GetProperty("downloaders").GetInt32());
        Assert.True(previewJson.RootElement.GetProperty("counts").GetProperty("sources").GetInt32() >= 1);
        Assert.Equal(1, previewJson.RootElement.GetProperty("counts").GetProperty("mikan_work_rules").GetInt32());
        Assert.NotEmpty(previewJson.RootElement.GetProperty("warnings").EnumerateArray());

        using var wrongDigest = await app.Client.PostAsync(
            "/api/v1/configuration-archive/import?expected_sha256=00",
            Json(archive));
        Assert.Equal(HttpStatusCode.BadRequest, wrongDigest.StatusCode);
        var backupDirectory = Path.Combine(
            app.RootPath, "data", "backups", "configuration-archives");
        Assert.False(Directory.Exists(backupDirectory)
            && Directory.EnumerateFiles(backupDirectory, "pre-import-*.json").Any());

        using var imported = await app.Client.PostAsync(
            $"/api/v1/configuration-archive/import?expected_sha256={sha256}",
            Json(archive));
        imported.EnsureSuccessStatusCode();
        using var importedJson = JsonDocument.Parse(await imported.Content.ReadAsByteArrayAsync());
        Assert.True(importedJson.RootElement.GetProperty("restart_required").GetBoolean());
        var safetyBackup = importedJson.RootElement.GetProperty("backup_id").GetString()!;
        Assert.StartsWith("pre-import-", safetyBackup, StringComparison.Ordinal);
        Assert.Equal("movie", (await sourceStore.GetAsync("mikan"))?.MediaType);

        using var backups = await app.Client.GetAsync("/api/v1/configuration-archive/backups");
        backups.EnsureSuccessStatusCode();
        using var backupsJson = JsonDocument.Parse(await backups.Content.ReadAsByteArrayAsync());
        Assert.Contains(
            backupsJson.RootElement.EnumerateArray(),
            item => item.GetProperty("id").GetString() == safetyBackup);
    }

    [Fact]
    public async Task ManualBackupCanBeDownloadedRestoredAndDeleted()
    {
        await using var app = await RunningApp.StartAsync();

        using var created = await app.Client.PostAsync(
            "/api/v1/configuration-archive/backups",
            content: null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsByteArrayAsync());
        var id = createdJson.RootElement.GetProperty("id").GetString()!;

        using var download = await app.Client.GetAsync(
            $"/api/v1/configuration-archive/backups/{id}/download");
        download.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("AnimeGoNet", document.RootElement.GetProperty("product").GetString());

        using var restored = await app.Client.PostAsync(
            $"/api/v1/configuration-archive/backups/{id}/restore",
            content: null);
        restored.EnsureSuccessStatusCode();
        using var restoredJson = JsonDocument.Parse(await restored.Content.ReadAsByteArrayAsync());
        Assert.StartsWith(
            "pre-restore-",
            restoredJson.RootElement.GetProperty("backup_id").GetString(),
            StringComparison.Ordinal);

        using var deleted = await app.Client.DeleteAsync(
            $"/api/v1/configuration-archive/backups/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var missing = await app.Client.GetAsync(
            $"/api/v1/configuration-archive/backups/{id}/download");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task DailyAutomationDefaultsToTenAndRotatesOnlyAutomaticBackups()
    {
        await using var app = await RunningApp.StartAsync();

        using var initial = await app.Client.GetAsync(
            "/api/v1/configuration-archive/automation");
        initial.EnsureSuccessStatusCode();
        using (var initialJson = JsonDocument.Parse(await initial.Content.ReadAsByteArrayAsync()))
        {
            Assert.False(initialJson.RootElement.GetProperty("enabled").GetBoolean());
            Assert.Equal(10, initialJson.RootElement.GetProperty("retention_count").GetInt32());
        }

        using var invalid = await app.Client.PutAsync(
            "/api/v1/configuration-archive/automation",
            new StringContent("{\"enabled\":true,\"retention_count\":0}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var saved = await app.Client.PutAsync(
            "/api/v1/configuration-archive/automation",
            new StringContent("{\"enabled\":true,\"retention_count\":2}", Encoding.UTF8, "application/json"));
        saved.EnsureSuccessStatusCode();

        using var manualResponse = await app.Client.PostAsync(
            "/api/v1/configuration-archive/backups",
            content: null);
        manualResponse.EnsureSuccessStatusCode();
        using var manualJson = JsonDocument.Parse(await manualResponse.Content.ReadAsByteArrayAsync());
        var manualId = manualJson.RootElement.GetProperty("id").GetString()!;

        var service = app.App.Services.GetRequiredService<ConfigurationArchiveService>();
        var first = await service.CreateAutomaticBackupIfDueAsync(
            new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero),
            TimeZoneInfo.Utc,
            2);
        var duplicate = await service.CreateAutomaticBackupIfDueAsync(
            new DateTimeOffset(2030, 1, 1, 23, 0, 0, TimeSpan.Zero),
            TimeZoneInfo.Utc,
            2);
        var second = await service.CreateAutomaticBackupIfDueAsync(
            new DateTimeOffset(2030, 1, 2, 12, 0, 0, TimeSpan.Zero),
            TimeZoneInfo.Utc,
            2);
        var third = await service.CreateAutomaticBackupIfDueAsync(
            new DateTimeOffset(2030, 1, 3, 12, 0, 0, TimeSpan.Zero),
            TimeZoneInfo.Utc,
            2);

        Assert.NotNull(first);
        Assert.Null(duplicate);
        Assert.NotNull(second);
        Assert.NotNull(third);
        var backups = await service.ListBackupsAsync();
        Assert.Contains(backups, item => item.Id == manualId && item.Kind == "manual");
        var automatic = backups.Where(item => item.Kind == "automatic").ToArray();
        Assert.Equal(2, automatic.Length);
        Assert.DoesNotContain(automatic, item => item.Id == first.Id);
        Assert.Contains(automatic, item => item.Id == second!.Id);
        Assert.Contains(automatic, item => item.Id == third!.Id);
    }

    [Fact]
    public async Task PreviewRejectsUnknownOrOversizedArchivesWithoutChangingState()
    {
        await using var app = await RunningApp.StartAsync();
        const string unknown = """
            {"format_version":99,"product":"AnimeGoNet","contains_secrets":true}
            """;
        using var invalid = await app.Client.PostAsync(
            "/api/v1/configuration-archive/import/preview",
            new StringContent(unknown, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var oversized = new byte[ConfigurationArchiveService.MaximumArchiveBytes + 1];
        using var tooLarge = await app.Client.PostAsync(
            "/api/v1/configuration-archive/import/preview",
            new ByteArrayContent(oversized));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
    }

    private static ByteArrayContent Json(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("application/json");
        return content;
    }
}
