using System.Text.Json;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.Tests.Metadata;

public sealed class AiMetadataDebugTraceStoreTests
{
    [Fact]
    public async Task PersistsReadsAndDeletesCompleteDebugDocumentByHashedRunId()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "animegonet-ai-debug-tests",
            Guid.NewGuid().ToString("N"));
        var layout = DirectoryLayout.From(AnimeGoDefaults.CreateNative(root).Paths);
        layout.CreateDataDirectories();
        try
        {
            using var store = new AiMetadataDebugTraceStore(layout);
            var chain = new AiMetadataDebugChain(
                "trace-1",
                "run-sensitive-1",
                "task-1",
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddSeconds(1),
                "prompt-v1",
                "responses",
                "test-model",
                new AiMetadataDebugPreAiContext(
                    "episode",
                    new AiMetadataDebugTaskInput(
                        "Task title",
                        3951,
                        7,
                        123,
                        null,
                        null,
                        "mikan",
                        "profile",
                        "source",
                        1,
                        [new AiMetadataDebugTaskFileInput(
                            "01.mkv", 100, "1", "1", null, null, null)]),
                    42,
                    1,
                    ["Task title"],
                    DateTimeOffset.UnixEpoch,
                    1,
                    true,
                    "matched",
                    null,
                    []),
                "PROMPT {{SOURCE_TITLE_JSON}}",
                "PROMPT \"Task title\"",
                [new AiMetadataDebugExchange(
                    1,
                    "ai",
                    "request_attempt_1",
                    "https://ai.test.invalid/v1/responses",
                    "{\"input\":\"request\"}",
                    200,
                    "{\"output\":\"response\"}",
                    25,
                    null)],
                "{\"matched\":true}",
                new AiMetadataMatchCandidate(true, 42, [], null),
                new AiMetadataProviderUsage("test-model", 10, 5, 15, 1, 0),
                null);

            await store.WriteAsync(chain, null, 42, 1);

            Assert.True(store.Exists("run-sensitive-1"));
            var files = Directory.GetFiles(layout.AiDebugPath, "*.json");
            var file = Assert.Single(files);
            Assert.DoesNotContain("run-sensitive-1", Path.GetFileName(file), StringComparison.Ordinal);
            var json = Assert.IsType<string>(await store.ReadAsync("run-sensitive-1"));
            using var document = JsonDocument.Parse(json);
            Assert.Equal("PROMPT {{SOURCE_TITLE_JSON}}", document.RootElement
                .GetProperty("chain").GetProperty("prompt_template").GetString());
            Assert.Equal(42, document.RootElement
                .GetProperty("validation").GetProperty("expected_tmdb_series_id").GetInt32());
            Assert.Equal("request_attempt_1", document.RootElement
                .GetProperty("chain").GetProperty("exchanges")[0].GetProperty("operation").GetString());

            Assert.True(store.Delete("run-sensitive-1"));
            Assert.False(store.Exists("run-sensitive-1"));
            Assert.Null(await store.ReadAsync("run-sensitive-1"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
