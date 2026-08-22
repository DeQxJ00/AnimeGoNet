using System.Net;
using System.Text;
using System.Text.Json;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Api;

public sealed class MetadataAttemptApiTests
{
    [Fact]
    public async Task ListsCompleteSafeTimelineWithoutTorrentSecret()
    {
        await using var app = await RunningApp.StartAsync();
        const string payload = """
            {
              "source": "mikan",
              "data": [{
                "torrent": "https://mikanani.me/private-passkey/attempt-timeline.torrent",
                "info": { "title": "Attempt timeline", "mikanid": 3951, "bgmid": 547888 }
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
                "tmdb_series_not_found",
                false,
                claim.AttemptNumber,
                12),
            now.AddSeconds(1));
        await store.RecordAttemptAsync(
            claim,
            new MetadataAttempt(
                "season",
                "tmdb_fail_first_season",
                1,
                "matched",
                null,
                false,
                claim.AttemptNumber,
                3,
                "local S01 selected"),
            now.AddSeconds(2));
        await store.RecordAttemptAsync(
            claim,
            new MetadataAttempt(
                "episode",
                "ai_metadata",
                null,
                "error",
                "ai_episode_match_invalid",
                false,
                claim.AttemptNumber,
                60,
                AiUsage: new AnimeGoNet.Core.Metadata.AiMetadataProviderUsage(
                    "gpt-5.4-mini",
                    10,
                    0,
                    10,
                    1,
                    0),
                AiTriggerReason: "episode_unresolved:ai_response_invalid"),
            now.AddMilliseconds(2500));
        var matchedAttemptId = await store.RecordAttemptAsync(
            claim,
            new MetadataAttempt(
                "episode",
                "ai_metadata",
                null,
                "matched",
                null,
                false,
                claim.AttemptNumber,
                80,
                AiUsage: new AnimeGoNet.Core.Metadata.AiMetadataProviderUsage(
                    "gpt-5.4-mini",
                    100,
                    20,
                    120,
                    1,
                    0),
                AiTriggerReason: "episode_unresolved:ambiguous_episode_markers"),
            now.AddSeconds(3));
        await using (var connection = await database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO metadata_ai_validated_episodes (
                    attempt_id, tmdb_series_id, tmdb_season_number,
                    tmdb_episode_number, tmdb_episode_id, episode_name,
                    validated_at_utc)
                VALUES ($attempt_id, 42, 1, 7, 4207, 'Episode Seven', $now);
                """;
            command.Parameters.AddWithValue("$attempt_id", matchedAttemptId);
            command.Parameters.AddWithValue("$now", now.AddSeconds(4).ToString("O"));
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }
        var debugStore = app.App.Services.GetRequiredService<AiMetadataDebugTraceStore>();
        await debugStore.WriteAsync(
            new AiMetadataDebugChain(
                "trace-api",
                claim.RunId,
                claim.TaskId,
                now,
                now.AddSeconds(1),
                "prompt-v1",
                "responses",
                "gpt-5.4-mini",
                null,
                "PROMPT {{SOURCE_TITLE_JSON}}",
                "PROMPT Attempt timeline",
                [],
                "{\"matched\":true}",
                new AiMetadataMatchCandidate(true, 42, [], null),
                new AiMetadataProviderUsage("gpt-5.4-mini", 100, 20, 120, 1, 0),
                null),
            null,
            42,
            1);

        using var response = await app.Client.GetAsync(
            $"/api/v1/metadata/tasks/{taskId}/attempts");
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(taskId, json.RootElement.GetProperty("task_id").GetString());
        Assert.Equal(4, items.Length);
        Assert.Equal("episode", items[0].GetProperty("stage").GetString());
        Assert.Equal("gpt-5.4-mini", items[0].GetProperty("ai_model").GetString());
        Assert.Equal(100, items[0].GetProperty("ai_prompt_tokens").GetInt64());
        Assert.Equal(20, items[0].GetProperty("ai_completion_tokens").GetInt64());
        Assert.Equal(120, items[0].GetProperty("ai_total_tokens").GetInt64());
        Assert.Equal(1, items[0].GetProperty("ai_request_count").GetInt32());
        Assert.Equal(0, items[0].GetProperty("ai_tool_call_count").GetInt32());
        Assert.Equal("error", items[1].GetProperty("result").GetString());
        Assert.Equal("ai_episode_match_invalid", items[1].GetProperty("error_code").GetString());
        Assert.Equal("season", items[2].GetProperty("stage").GetString());
        Assert.Equal("local S01 selected", items[2].GetProperty("reason").GetString());
        Assert.Equal("series", items[3].GetProperty("stage").GetString());
        Assert.Equal("tmdb_series_not_found", items[3].GetProperty("reason").GetString());
        Assert.False(items[3].GetProperty("retryable").GetBoolean());
        Assert.Equal(12, items[3].GetProperty("duration_ms").GetInt64());
        Assert.DoesNotContain("private-passkey", body, StringComparison.Ordinal);
        Assert.DoesNotContain("attempt-timeline.torrent", body, StringComparison.Ordinal);

        using var detailResponse = await app.Client.GetAsync($"/api/v1/metadata/tasks/{taskId}");
        using var detail = JsonDocument.Parse(await detailResponse.Content.ReadAsStreamAsync());
        var ai = detail.RootElement.GetProperty("ai");
        Assert.Equal("gpt-5.4-mini", ai.GetProperty("model").GetString());
        Assert.Equal(120, ai.GetProperty("total_tokens").GetInt64());
        Assert.Equal(1, ai.GetProperty("request_count").GetInt32());

        using var aiLogResponse = await app.Client.GetAsync(
            "/api/v1/logs/ai-invocations?page=1&page_size=10"
            + "&search=Attempt%20timeline&stage=episode&result=matched&model=5.4-mini");
        var aiLogBody = await aiLogResponse.Content.ReadAsStringAsync();
        Assert.True(aiLogResponse.IsSuccessStatusCode, $"{aiLogResponse.StatusCode}: {aiLogBody}");
        using var aiLog = JsonDocument.Parse(aiLogBody);
        var aiLogItem = Assert.Single(aiLog.RootElement.GetProperty("items").EnumerateArray());
        var summary = aiLog.RootElement.GetProperty("summary");
        Assert.Equal(HttpStatusCode.OK, aiLogResponse.StatusCode);
        Assert.Equal(1, aiLog.RootElement.GetProperty("total_items").GetInt32());
        Assert.Equal(1, summary.GetProperty("matched_items").GetInt32());
        Assert.Equal(0, summary.GetProperty("failed_items").GetInt32());
        Assert.Equal(120, summary.GetProperty("total_tokens").GetInt64());
        Assert.Equal(1, summary.GetProperty("request_count").GetInt32());
        Assert.Equal(taskId, aiLogItem.GetProperty("task_id").GetString());
        Assert.Equal("Attempt timeline", aiLogItem.GetProperty("title").GetString());
        Assert.Equal(3951, aiLogItem.GetProperty("mikanid").GetInt32());
        Assert.Equal(547888, aiLogItem.GetProperty("bgmid").GetInt32());
        Assert.Equal("gpt-5.4-mini", aiLogItem.GetProperty("model").GetString());
        Assert.Equal(
            "episode_unresolved:ambiguous_episode_markers",
            aiLogItem.GetProperty("ai_trigger_reason").GetString());
        var validatedEpisode = Assert.Single(
            aiLogItem.GetProperty("validated_episodes").EnumerateArray());
        Assert.Equal(42, validatedEpisode.GetProperty("tmdb_series_id").GetInt32());
        Assert.Equal(1, validatedEpisode.GetProperty("season_number").GetInt32());
        Assert.Equal(7, validatedEpisode.GetProperty("episode_number").GetInt32());
        Assert.Equal("Episode Seven", validatedEpisode.GetProperty("episode_name").GetString());
        Assert.True(aiLogItem.GetProperty("debug_available").GetBoolean());
        Assert.DoesNotContain("private-passkey", aiLogBody, StringComparison.Ordinal);
        Assert.DoesNotContain("attempt-timeline.torrent", aiLogBody, StringComparison.Ordinal);

        using var allAiLogResponse = await app.Client.GetAsync(
            "/api/v1/logs/ai-invocations?page=1&page_size=10");
        using var allAiLog = JsonDocument.Parse(await allAiLogResponse.Content.ReadAsStreamAsync());
        var allSummary = allAiLog.RootElement.GetProperty("summary");
        Assert.Equal(2, allAiLog.RootElement.GetProperty("total_items").GetInt32());
        Assert.Equal(1, allSummary.GetProperty("matched_items").GetInt32());
        Assert.Equal(1, allSummary.GetProperty("failed_items").GetInt32());
        Assert.Equal(1, allSummary.GetProperty("output_format_failed_items").GetInt32());
        Assert.Equal(130, allSummary.GetProperty("total_tokens").GetInt64());

        using var errorResponse = await app.Client.GetAsync(
            "/api/v1/logs/ai-invocations?result=error");
        using var errorLog = JsonDocument.Parse(await errorResponse.Content.ReadAsStreamAsync());
        Assert.Equal(1, errorLog.RootElement.GetProperty("total_items").GetInt32());
        Assert.Equal(
            "ai_episode_match_invalid",
            errorLog.RootElement.GetProperty("items")[0].GetProperty("error_code").GetString());
        Assert.Equal(
            "output_format",
            errorLog.RootElement.GetProperty("items")[0].GetProperty("error_category").GetString());

        using var outputFormatResponse = await app.Client.GetAsync(
            "/api/v1/logs/ai-invocations?error_category=output_format");
        using var outputFormatLog = JsonDocument.Parse(
            await outputFormatResponse.Content.ReadAsStreamAsync());
        Assert.Equal(1, outputFormatLog.RootElement.GetProperty("total_items").GetInt32());
        Assert.Equal(
            "ai_episode_match_invalid",
            outputFormatLog.RootElement.GetProperty("items")[0].GetProperty("error_code").GetString());

        using var otherErrorResponse = await app.Client.GetAsync(
            "/api/v1/logs/ai-invocations?error_category=other");
        using var otherErrorLog = JsonDocument.Parse(
            await otherErrorResponse.Content.ReadAsStreamAsync());
        Assert.Equal(0, otherErrorLog.RootElement.GetProperty("total_items").GetInt32());

        using var noMatchResponse = await app.Client.GetAsync(
            "/api/v1/logs/ai-invocations?result=failed");
        using var noMatch = JsonDocument.Parse(await noMatchResponse.Content.ReadAsStreamAsync());
        Assert.Equal(0, noMatch.RootElement.GetProperty("total_items").GetInt32());
        Assert.Empty(noMatch.RootElement.GetProperty("items").EnumerateArray());

        using var debugResponse = await app.Client.GetAsync(
            $"/api/v1/logs/ai-invocations/{claim.RunId}/debug");
        var debugBody = await debugResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, debugResponse.StatusCode);
        Assert.Contains("PROMPT Attempt timeline", debugBody, StringComparison.Ordinal);
        Assert.DoesNotContain("private-passkey", debugBody, StringComparison.Ordinal);

        using var deleteDebug = await app.Client.DeleteAsync(
            $"/api/v1/logs/ai-invocations/{claim.RunId}/debug");
        using var missingDebug = await app.Client.GetAsync(
            $"/api/v1/logs/ai-invocations/{claim.RunId}/debug");
        Assert.Equal(HttpStatusCode.NoContent, deleteDebug.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingDebug.StatusCode);
    }

    [Fact]
    public async Task MissingTaskAndInvalidLimitUseStableErrors()
    {
        await using var app = await RunningApp.StartAsync();

        using var missing = await app.Client.GetAsync(
            "/api/v1/metadata/tasks/missing/attempts");
        using var invalid = await app.Client.GetAsync(
            "/api/v1/metadata/tasks/missing/attempts?limit=501");
        using var missingJson = JsonDocument.Parse(await missing.Content.ReadAsStreamAsync());
        using var invalidJson = JsonDocument.Parse(await invalid.Content.ReadAsStreamAsync());
        using var invalidStage = await app.Client.GetAsync(
            "/api/v1/logs/ai-invocations?stage=prompt");
        using var invalidRange = await app.Client.GetAsync(
            "/api/v1/logs/ai-invocations?from_utc=2026-08-13T10%3A00%3A00Z"
            + "&to_utc=2026-08-13T09%3A00%3A00Z");
        using var tooLong = await app.Client.GetAsync(
            $"/api/v1/logs/ai-invocations?search={new string('a', 201)}");
        using var invalidErrorCategory = await app.Client.GetAsync(
            "/api/v1/logs/ai-invocations?error_category=network");
        using var invalidStageJson = JsonDocument.Parse(await invalidStage.Content.ReadAsStreamAsync());
        using var invalidRangeJson = JsonDocument.Parse(await invalidRange.Content.ReadAsStreamAsync());
        using var tooLongJson = JsonDocument.Parse(await tooLong.Content.ReadAsStreamAsync());
        using var invalidErrorCategoryJson = JsonDocument.Parse(
            await invalidErrorCategory.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(
            "metadata_task_not_found",
            missingJson.RootElement.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(
            "metadata_attempt_limit_invalid",
            invalidJson.RootElement.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, invalidStage.StatusCode);
        Assert.Equal(
            "ai_log_stage_invalid",
            invalidStageJson.RootElement.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, invalidRange.StatusCode);
        Assert.Equal(
            "ai_log_time_range_invalid",
            invalidRangeJson.RootElement.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(
            "ai_log_filter_too_long",
            tooLongJson.RootElement.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, invalidErrorCategory.StatusCode);
        Assert.Equal(
            "ai_log_error_category_invalid",
            invalidErrorCategoryJson.RootElement.GetProperty("code").GetString());
    }
}
