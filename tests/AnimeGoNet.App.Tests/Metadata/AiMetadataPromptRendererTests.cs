using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.Tests.Metadata;

public sealed class AiMetadataPromptRendererTests
{
    [Fact]
    public void LoadsSingleAuthoritativePromptAndRendersExactRequestContract()
    {
        var input = new AiMetadataMatchInput(
            "任务 \"标题\"",
            [new AiMetadataFileInput("Season 1/01.mkv", 123)],
            BangumiSubjectId: 100,
            AniDbAnimeId: 200,
            ImdbTitleId: "tt1234567",
            TorrentFileCount: 1,
            PublishedAt: DateTimeOffset.Parse(
                "2026-07-26T20:30:00+08:00",
                System.Globalization.CultureInfo.InvariantCulture),
            BangumiEpisodeCandidate: 3,
            UseBangumiPubDateFirst: true);

        var rendered = AiMetadataPromptRenderer.LoadAndRender(input);

        Assert.Contains("\"title\": \"任务 \\\"标题\\\"\"", rendered, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"Season 1/01.mkv\"", rendered, StringComparison.Ordinal);
        Assert.Contains("\"size_bytes\":123", rendered, StringComparison.Ordinal);
        Assert.Contains("\"bgmid\": 100", rendered, StringComparison.Ordinal);
        Assert.Contains("\"anidbid\": 200", rendered, StringComparison.Ordinal);
        Assert.Contains("\"imdbid\": \"tt1234567\"", rendered, StringComparison.Ordinal);
        Assert.Contains("lookup_imdb_tmdb_tv", rendered, StringComparison.Ordinal);
        Assert.Contains("不得把 imdbid 或自定义 URL 作为工具参数", rendered, StringComparison.Ordinal);
        Assert.Contains("\"torrent_file_count\": 1", rendered, StringComparison.Ordinal);
        Assert.Contains("\"use_bangumi_pubdate_first\": true", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", rendered, StringComparison.Ordinal);
        Assert.Contains(
            "9. 优先使用单集首播日期判断对应关系，允许正负 1 天的时区误差。",
            rendered,
            StringComparison.Ordinal);
        Assert.Contains("published_at", rendered, StringComparison.Ordinal);
        Assert.Equal("tmdb-ai-match-v11", AiMetadataPromptRenderer.PromptVersion);
    }

    [Fact]
    public void RejectsMoreThanOnePromptTextBlock()
    {
        var exception = Assert.Throws<AiMetadataMatcherException>(() =>
            AiMetadataPromptRenderer.ExtractSingleTextCodeBlock(
                "```text\none\n```\n```text\ntwo\n```"));

        Assert.Equal("ai_prompt_text_block_invalid", exception.SafeCode);
    }

    [Fact]
    public void DisabledPubdateGateKeepsMikanPublicationAsNonBindingAiContext()
    {
        var input = new AiMetadataMatchInput(
            "Task",
            [new AiMetadataFileInput("01.mkv", 1)],
            BangumiSubjectId: 100,
            AniDbAnimeId: null,
            ImdbTitleId: null,
            TorrentFileCount: 1,
            PublishedAt: DateTimeOffset.Parse(
                "2026-07-26T20:30:00+08:00",
                System.Globalization.CultureInfo.InvariantCulture),
            BangumiEpisodeCandidate: 3,
            UseBangumiPubDateFirst: false);

        var rendered = AiMetadataPromptRenderer.LoadAndRender(input);

        Assert.Contains("\"published_at\": \"2026-07-26T20:30:00.0000000+08:00\"", rendered, StringComparison.Ordinal);
        Assert.Contains("\"bgm_episode_candidate\": null", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void RequestLocalTemplateOverrideIsRenderedWithoutChangingDefaultTemplate()
    {
        var input = new AiMetadataMatchInput(
            "Custom title",
            [new AiMetadataFileInput("01.mkv", 1)],
            null, null, null, 1, null, null, false)
        {
            PromptTemplateOverride = "TEST {{SOURCE_TITLE_JSON}} {{FILES_JSON}}",
        };

        var rendered = AiMetadataPromptRenderer.LoadAndRender(input);

        Assert.StartsWith("TEST \"Custom title\"", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("TEST", AiMetadataPromptRenderer.LoadTemplate(), StringComparison.Ordinal);
    }

    [Fact]
    public void RequestLocalTemplateOverrideHasBoundedSize()
    {
        var input = new AiMetadataMatchInput(
            "Title",
            [new AiMetadataFileInput("01.mkv", 1)],
            null, null, null, 1, null, null, false)
        {
            PromptTemplateOverride = new string('x', AiMetadataPromptRenderer.MaximumTemplateLength + 1),
        };

        var exception = Assert.Throws<AiMetadataMatcherException>(() =>
            AiMetadataPromptRenderer.LoadAndRender(input));

        Assert.Equal("ai_prompt_template_too_large", exception.SafeCode);
    }
}
