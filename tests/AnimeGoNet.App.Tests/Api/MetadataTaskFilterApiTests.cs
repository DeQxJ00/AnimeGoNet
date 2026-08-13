using System.Net;
using System.Text;
using System.Text.Json;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Api;

public sealed class MetadataTaskFilterApiTests
{
    [Fact]
    public async Task FiltersLatestFailureAndReturnsExplicitRetryClassification()
    {
        await using var app = await RunningApp.StartAsync();
        const string payload = """
            {
              "source": "mikan",
              "data": [{
                "torrent": "https://mikanani.me/private-passkey/metadata-filter.torrent",
                "info": {
                  "title": "Metadata filter",
                  "mikanid": 3951,
                  "bgmid": 547888
                }
              }]
            }
            """;
        using var ingest = await app.Client.PostAsync(
            "/api/v1/ingest",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        using var ingestJson = JsonDocument.Parse(await ingest.Content.ReadAsStreamAsync());
        var taskId = ingestJson.RootElement
            .GetProperty("items")[0]
            .GetProperty("ingest_id")
            .GetString()!;
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using (var connection = await database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE ingest_tasks
                SET status = 'download_preparing', updated_at_utc = $now
                WHERE id = $task_id;
                """;
            command.Parameters.AddWithValue("$task_id", taskId);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        var store = app.App.Services.GetRequiredService<MetadataResolutionStore>();
        var now = DateTimeOffset.UtcNow;
        var claim = Assert.IsType<MetadataTaskClaim>(
            await store.TryClaimNextDownloadedAsync(now, TimeSpan.FromMinutes(1)));
        await store.RecordAttemptAsync(
            claim,
            new MetadataAttempt(
                "series",
                "tmdb_title",
                4,
                "failed",
                "tmdb_network",
                true,
                claim.AttemptNumber,
                12,
                "safe network failure"),
            now.AddSeconds(1));
        await store.FailAsync(
            claim,
            new MetadataFailure(MetadataFailureKind.Network, "tmdb_network", false),
            false,
            "network_failure",
            now.AddSeconds(2));

        using var response = await app.Client.GetAsync(
            "/api/v1/metadata/tasks?page=1&page_size=10"
            + "&status=metadata_failed&failure_stage=series"
            + "&error_code=tmdb_network&retryability=retryable"
            + "&handling=explicit_retry&sort=failure&direction=asc");
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, json.RootElement.GetProperty("total_items").GetInt32());
        Assert.Equal("failure", json.RootElement.GetProperty("sort").GetString());
        Assert.Equal(taskId, item.GetProperty("task_id").GetString());
        Assert.Equal("series", item.GetProperty("failure_stage").GetString());
        Assert.Equal("tmdb_network", item.GetProperty("failure_code").GetString());
        Assert.True(item.GetProperty("failure_retryable").GetBoolean());
        Assert.Equal("explicit_retry", item.GetProperty("handling_category").GetString());
        Assert.DoesNotContain("private-passkey", body, StringComparison.Ordinal);
        Assert.DoesNotContain("metadata-filter.torrent", body, StringComparison.Ordinal);

        using var excluded = await app.Client.GetAsync(
            "/api/v1/metadata/tasks?handling=configuration");
        using var excludedJson = JsonDocument.Parse(await excluded.Content.ReadAsStreamAsync());
        Assert.Equal(0, excludedJson.RootElement.GetProperty("total_items").GetInt32());
    }

    [Fact]
    public async Task ClassifiesConfigurationFallbackAndSkippedWithoutInventingRetryability()
    {
        await using var app = await RunningApp.StartAsync();
        const string payload = """
            {
              "source": "mikan",
              "data": [{
                "torrent": "https://mikanani.me/private-passkey/metadata-category.torrent",
                "info": {
                  "title": "Metadata category",
                  "mikanid": 3951,
                  "bgmid": 547888
                }
              }]
            }
            """;
        using var ingest = await app.Client.PostAsync(
            "/api/v1/ingest",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        using var ingestJson = JsonDocument.Parse(await ingest.Content.ReadAsStreamAsync());
        var taskId = ingestJson.RootElement
            .GetProperty("items")[0]
            .GetProperty("ingest_id")
            .GetString()!;
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();

        await SetTaskStateAsync(database, taskId, "metadata_failed", "Configuration", "tmdb_api_key_missing");
        Assert.Equal(
            "configuration",
            await ReadOnlyHandlingCategoryAsync(app.Client, "configuration"));

        await SetTaskStateAsync(
            database,
            taskId,
            "metadata_resolved",
            "tmdb_completion_pending",
            "bangumi_fallback_pending");
        Assert.Equal(
            "fallback",
            await ReadOnlyHandlingCategoryAsync(app.Client, "fallback"));

        await SetTaskStateAsync(
            database,
            taskId,
            "download_skipped_duplicate",
            null,
            null);
        Assert.Equal(
            "skipped",
            await ReadOnlyHandlingCategoryAsync(app.Client, "skipped"));
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page_size=101")]
    [InlineData("sort=unknown")]
    [InlineData("direction=sideways")]
    [InlineData("handling=automatic")]
    [InlineData("file_state=other")]
    [InlineData("review_state=waiting")]
    [InlineData("failure_stage=bad%20stage")]
    public async Task RejectsInvalidFilters(string query)
    {
        await using var app = await RunningApp.StartAsync();
        using var response = await app.Client.GetAsync($"/api/v1/metadata/tasks?{query}");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "metadata_task_filter_invalid",
            json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task FiltersTasksContainingOtherFilesIndependentlyOfHandlingCategory()
    {
        await using var app = await RunningApp.StartAsync();
        const string payload = """
            {
              "source": "mikan",
              "data": [{
                "torrent": "https://mikanani.me/private-passkey/metadata-other-filter.torrent",
                "info": { "title": "Other file filter", "mikanid": 3951, "bgmid": 547888 }
              }]
            }
            """;
        using var ingest = await app.Client.PostAsync(
            "/api/v1/ingest",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        using var ingestJson = JsonDocument.Parse(await ingest.Content.ReadAsStreamAsync());
        var taskId = ingestJson.RootElement.GetProperty("items")[0]
            .GetProperty("ingest_id").GetString()!;
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using (var connection = await database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE ingest_tasks SET status = 'organized', updated_at_utc = $now
                WHERE id = $task_id;
                UPDATE task_files SET disposition = 'other', other_reason = 'episode_unresolved'
                WHERE task_id = $task_id;
                """;
            command.Parameters.AddWithValue("$task_id", taskId);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            Assert.Equal(2, await command.ExecuteNonQueryAsync());
        }

        using var response = await app.Client.GetAsync(
            "/api/v1/metadata/tasks?file_state=has_other");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(taskId, item.GetProperty("task_id").GetString());
        Assert.Equal(1, item.GetProperty("other_file_count").GetInt32());
        Assert.Equal("other", item.GetProperty("handling_category").GetString());
        var attention = json.RootElement.GetProperty("attention");
        Assert.Equal(1, attention.GetProperty("other_items").GetInt32());
        Assert.Equal(0, attention.GetProperty("failed_items").GetInt32());
        Assert.Equal(0, attention.GetProperty("review_pending_items").GetInt32());
    }

    [Fact]
    public async Task ReportsGlobalFailureAndReviewAttentionAndFiltersPendingReview()
    {
        await using var app = await RunningApp.StartAsync();
        const string payload = """
            {
              "source": "mikan",
              "data": [{
                "torrent": "https://mikanani.me/private-passkey/metadata-attention.torrent",
                "info": { "title": "Attention summary", "mikanid": 3951, "bgmid": 547888 }
              }]
            }
            """;
        using var ingest = await app.Client.PostAsync(
            "/api/v1/ingest",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        using var ingestJson = JsonDocument.Parse(await ingest.Content.ReadAsStreamAsync());
        var taskId = ingestJson.RootElement.GetProperty("items")[0]
            .GetProperty("ingest_id").GetString()!;
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using (var connection = await database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE ingest_tasks
                SET status = 'metadata_failed',
                    failure_kind = 'metadata_match_failed',
                    failure_reason = 'attention_test',
                    readaptation_review_state = 'pending',
                    updated_at_utc = $now
                WHERE id = $task_id;
                """;
            command.Parameters.AddWithValue("$task_id", taskId);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        using var response = await app.Client.GetAsync(
            "/api/v1/metadata/tasks?review_state=pending");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        var attention = json.RootElement.GetProperty("attention");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(taskId, item.GetProperty("task_id").GetString());
        Assert.Equal(0, attention.GetProperty("other_items").GetInt32());
        Assert.Equal(1, attention.GetProperty("failed_items").GetInt32());
        Assert.Equal(1, attention.GetProperty("review_pending_items").GetInt32());
    }

    private static async Task SetTaskStateAsync(
        AnimeGoSqliteDatabase database,
        string taskId,
        string status,
        string? failureKind,
        string? failureReason)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ingest_tasks
            SET status = $status, failure_kind = $failure_kind,
                failure_reason = $failure_reason, updated_at_utc = $now
            WHERE id = $task_id;
            """;
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$failure_kind", (object?)failureKind ?? DBNull.Value);
        command.Parameters.AddWithValue("$failure_reason", (object?)failureReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$task_id", taskId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<string> ReadOnlyHandlingCategoryAsync(
        HttpClient client,
        string handling)
    {
        using var response = await client.GetAsync(
            $"/api/v1/metadata/tasks?handling={handling}");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Null(item.GetProperty("failure_retryable").GetString());
        return item.GetProperty("handling_category").GetString()!;
    }
}
