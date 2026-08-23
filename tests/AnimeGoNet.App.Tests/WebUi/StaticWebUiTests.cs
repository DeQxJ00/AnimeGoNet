using System.Net;

namespace AnimeGoNet.App.Tests.WebUi;

public sealed class StaticWebUiTests
{
    [Theory]
    [InlineData("/", "text/html", "AnimeGoNet")]
    [InlineData("/", "text/html", "/styles.css?v=20260823-root-font-size")]
    [InlineData("/styles.css", "text/css", ".hero")]
    [InlineData("/styles.css", "text/css", ".metadata-card")]
    [InlineData("/styles.css", "text/css", ".metadata-filters")]
    [InlineData("/styles.css", "text/css", ".metadata-attention-summary")]
    [InlineData("/styles.css", "text/css", ".metadata-attempt")]
    [InlineData("/styles.css", "text/css", ".metadata-resolution-reference")]
    [InlineData("/styles.css", "text/css", ".library-card")]
    [InlineData("/styles.css", "text/css", ".library-episode.downloaded")]
    [InlineData("/styles.css", "text/css", ".library-admin-form")]
    [InlineData("/styles.css", "text/css", ".library-search-form")]
    [InlineData("/styles.css", "text/css", ".external-import-result")]
    [InlineData("/styles.css", "text/css", "--control-font-size")]
    [InlineData("/styles.css", "text/css", "input[type=\"file\"]::file-selector-button")]
    [InlineData("/styles.css", "text/css", ".download-filter-actions button")]
    [InlineData("/styles.css", "text/css", ".library-audit-group")]
    [InlineData("/styles.css", "text/css", ".download-timeline")]
    [InlineData("/styles.css", "text/css", ".download-summary-card")]
    [InlineData("/styles.css", "text/css", ".download-stage-progress")]
    [InlineData("/styles.css", "text/css", ".data-update-columns")]
    [InlineData("/styles.css", "text/css", ".data-update-offline")]
    [InlineData("/styles.css", "text/css", ".configuration-data-update-grid select { width: 100%")]
    [InlineData("/styles.css", "text/css", "prefers-reduced-motion")]
    [InlineData("/styles.css", "text/css", ".skip-link:focus")]
    [InlineData("/app.js", "text/javascript", "/api/v1/downloads")]
    [InlineData("/app.js", "text/javascript", "/api/v1/cache/buckets")]
    [InlineData("/app.js", "text/javascript", "查看完整内容")]
    [InlineData("/app.js", "text/javascript", "/api/v1/ai-test/run")]
    [InlineData("/app.js", "text/javascript", "/api/v1/ai-test/mikan-import")]
    [InlineData("/app.js", "text/javascript", "/api/v1/ai-test/prompt")]
    [InlineData("/app.js", "text/javascript", "只删除这一条 bolt 缓存")]
    [InlineData("/api-client.js", "text/javascript", "invalid_api_path")]
    [InlineData("/webui-auth.js", "text/javascript", "createAuthenticatedFetch")]
    [InlineData("/ui-state.js", "text/javascript", "renderRegionMessage")]
    [InlineData("/app.js", "text/javascript", "查看文件与时间线")]
    [InlineData("/app.js", "text/javascript", "字幕关联与移动")]
    [InlineData("/app.js", "text/javascript", "expected_revision")]
    [InlineData("/app.js", "text/javascript", "downloader_id")]
    [InlineData("/app.js", "text/javascript", "connected_download_speed_bytes_per_second")]
    [InlineData("/app.js", "text/javascript", "seeding_target_minutes")]
    [InlineData("/app.js", "text/javascript", "做种：")]
    [InlineData("/styles.css", "text/css", ".download-seeding")]
    [InlineData("/styles.css", "text/css", ".download-dynamic-tags")]
    [InlineData("/app.js", "text/javascript", "动态 Tags：")]
    [InlineData("/app.js", "text/javascript", "/api/v1/library/seasons")]
    [InlineData("/app.js", "text/javascript", "/api/v1/library/external-media/import")]
    [InlineData("/app.js", "text/javascript", "external_media_episode_ambiguous")]
    [InlineData("/app.js", "text/javascript", "animegonet.library.v1")]
    [InlineData("/app.js", "text/javascript", "query.set(\"search\"")]
    [InlineData("/app.js", "text/javascript", "edit.className = \"secondary-button\"")]
    [InlineData("/app.js", "text/javascript", "TMDB EP snapshot")]
    [InlineData("/app.js", "text/javascript", "expected_revision: detail.resource_revision")]
    [InlineData("/app.js", "text/javascript", "有业务引用时请使用四类删除流程")]
    [InlineData("/app.js", "text/javascript", "季度级逐次验证时间线")]
    [InlineData("/app.js", "text/javascript", "manual_offsets")]
    [InlineData("/app.js", "text/javascript", "not_downloaded")]
    [InlineData("/app.js", "text/javascript", "/api/v1/metadata/tasks")]
    [InlineData("/app.js", "text/javascript", "查看来源 / TMDB 对照")]
    [InlineData("/app.js", "text/javascript", "TMDB 恢复后的 NFO 重写")]
    [InlineData("/app.js", "text/javascript", "RSS 入口与文件候选审计")]
    [InlineData("/app.js", "text/javascript", "可信依据：TMDB 已验证")]
    [InlineData("/app.js", "text/javascript", "多个文件使用不同来源或证据")]
    [InlineData("/app.js", "text/javascript", "episode_attempt_id")]
    [InlineData("/app.js", "text/javascript", "可安全重试（需显式）")]
    [InlineData("/app.js", "text/javascript", "handling_category")]
    [InlineData("/app.js", "text/javascript", "/attempts")]
    [InlineData("/app.js", "text/javascript", "查看策略时间线")]
    [InlineData("/app.js", "text/javascript", "重新适配 Other")]
    [InlineData("/app.js", "text/javascript", "/other-readaptation/preview")]
    [InlineData("/app.js", "text/javascript", "file_state")]
    [InlineData("/app.js", "text/javascript", "review_state")]
    [InlineData("/", "text/html", "metadata-file-state-filter")]
    [InlineData("/", "text/html", "metadata-attention-other-count")]
    [InlineData("/", "text/html", "metadata-attention-failed-count")]
    [InlineData("/", "text/html", "metadata-attention-review-count")]
    [InlineData("/", "text/html", "metadata-review-filter")]
    [InlineData("/app.js", "text/javascript", "可自动重试")]
    [InlineData("/app.js", "text/javascript", "Bangumi 完全兜底：")]
    [InlineData("/app.js", "text/javascript", "TMDB 权威访问未确认")]
    [InlineData("/app.js", "text/javascript", "bangumi_fallback_denial_reason")]
    [InlineData("/app.js", "text/javascript", "/api/v1/metadata/pending-tmdb")]
    [InlineData("/app.js", "text/javascript", "验证并恢复")]
    [InlineData("/app.js", "text/javascript", "DuplicateAfterResolution")]
    [InlineData("/app.js", "text/javascript", "可能跨来源重复")]
    [InlineData("/app.js", "text/javascript", "download_preparing")]
    [InlineData("/app.js", "text/javascript", "download_skipped_duplicate")]
    [InlineData("/app.js", "text/javascript", "重复命中通知")]
    [InlineData("/app.js", "text/javascript", "organizing_cleanup")]
    [InlineData("/app.js", "text/javascript", "organized")]
    [InlineData("/app.js", "text/javascript", "/api/v1/rss/ingest")]
    [InlineData("/app.js", "text/javascript", "聚合 RSS")]
    [InlineData("/app.js", "text/javascript", "同一 mikanid 与来源 EP 已完成，已跳过")]
    [InlineData("/app.js", "text/javascript", "/api/v1/ingest")]
    [InlineData("/app.js", "text/javascript", "/api/v1/ingest/mikan/resolve")]
    [InlineData("/app.js", "text/javascript", "/impact?limit=100")]
    [InlineData("/app.js", "text/javascript", "/rematch")]
    [InlineData("/app.js", "text/javascript", "/api/v1/data-update")]
    [InlineData("/app.js", "text/javascript", "/api/v1/data-update/archive-usage")]
    [InlineData("/app.js", "text/javascript", "/rollback")]
    [InlineData("/app.js", "text/javascript", "导入此版本")]
    [InlineData("/app.js", "text/javascript", "/api/v1/data-update/offline/import")]
    [InlineData("/app.js", "text/javascript", "/api/v1/plugins/")]
    [InlineData("/app.js", "text/javascript", "清除故障状态")]
    [InlineData("/app.js", "text/javascript", "configured_write_only_paths")]
    [InlineData("/app.js", "text/javascript", "input.type = \"text\"")]
    [InlineData("/app.js", "text/javascript", "/configuration")]
    [InlineData("/app.js", "text/javascript", "externalSourceAdapters")]
    [InlineData("/app.js", "text/javascript", "插件包不可用")]
    [InlineData("/app.js", "text/javascript", "当前不可用")]
    [InlineData("/", "text/html", "external-plugin-list")]
    [InlineData("/", "text/html", "href=\"#main-content\"")]
    [InlineData("/", "text/html", "data-ui-state=\"loading\"")]
    [InlineData("/", "text/html", "writeOnly")]
    [InlineData("/", "text/html", "cache-browser")]
    [InlineData("/", "text/html", "cache-entry-dialog")]
    [InlineData("/", "text/html", "ai-metadata-test")]
    [InlineData("/", "text/html", "ai-test-prompt-template")]
    [InlineData("/", "text/html", "configuration-ai-prompt-template")]
    [InlineData("/", "text/html", "configuration-ai-prompt-reset")]
    [InlineData("/", "text/html", "configuration-mikan-episode-cache-hours")]
    [InlineData("/", "text/html", "configuration-mikan-bangumi-cache-hours")]
    [InlineData("/", "text/html", "web-api-compatibility-access-key")]
    [InlineData("/", "text/html", "inner_plugin_mikan")]
    [InlineData("/", "text/html", "inner_plugin_mikan.access_key")]
    [InlineData("/app.js", "text/javascript", "/api/config?key=all&backup=true")]
    [InlineData("/app.js", "text/javascript", "PluginName 保持 inner_plugin_mikan")]
    [InlineData("/", "text/html", "webui-authentication-access-key")]
    [InlineData("/", "text/html", "webui-access-key-dialog")]
    [InlineData("/", "text/html", "输入 WebUI AccessKey")]
    [InlineData("/styles.css", "text/css", ".webui-access-key-panel")]
    [InlineData("/app.js", "text/javascript", "WebUI-Access-Key")]
    [InlineData("/app.js", "text/javascript", "webui_access_key")]
    [InlineData("/app.js", "text/javascript", "delete web.access_key")]
    [InlineData("/app.js", "text/javascript", "mikan_episode_identity_cache_hours")]
    [InlineData("/app.js", "text/javascript", "mikan_bangumi_identity_cache_hours")]
    [InlineData("/", "text/html", "/app.js?v=20260823-library-title-clamp")]
    [InlineData("/", "text/html", "ai-test-mikan-import")]
    [InlineData("/", "text/html", "ai-test-enable-tmdb-mcp")]
    [InlineData("/app.js", "text/javascript", "enable_bgm_mcp")]
    [InlineData("/app.js", "text/javascript", "/api/v1/ai-test/run-stream")]
    [InlineData("/app.js", "text/javascript", "/api/v1/ai-test/stop")]
    [InlineData("/app.js", "text/javascript", "/api/v1/ai-test/torrent-import")]
    [InlineData("/", "text/html", "common-input-region")]
    [InlineData("/", "text/html", "发送给 AI API 的请求")]
    [InlineData("/", "text/html", "ai-test-http-proxy")]
    [InlineData("/", "text/html", "API Key（从主配置回填，本次测试可修改）")]
    [InlineData("/", "text/html", "Torrent URL（直接显示）")]
    [InlineData("/", "text/html", "RSS URL（直接显示）")]
    [InlineData("/app.js", "text/javascript", "ai_http_timeout_seconds")]
    [InlineData("/app.js", "text/javascript", "TMDB Read Access Token")]
    [InlineData("/app.js", "text/javascript", "config.editable.ai_api_key")]
    [InlineData("/app.js", "text/javascript", "ai_prompt_template")]
    [InlineData("/app.js", "text/javascript", "后台 Worker 与测试工具共用")]
    [InlineData("/", "text/html", "ai-test-api-mode")]
    [InlineData("/", "text/html", "chat-completions")]
    [InlineData("/styles.css", "text/css", ".ai-test-trace-row")]
    [InlineData("/styles.css", "text/css", ".external-plugin-card")]
    [InlineData("/styles.css", "text/css", ".external-plugin-form")]
    [InlineData("/styles.css", "text/css", ".cache-browser-layout")]
    [InlineData("/", "text/html", "metadata-tasks")]
    [InlineData("/", "text/html", "metadata-handling-filter")]
    [InlineData("/", "text/html", "anime-library")]
    [InlineData("/", "text/html", "manual-download-form")]
    [InlineData("/", "text/html", "manual-download-mikan-resolve")]
    [InlineData("/", "text/html", "manual-rss-form")]
    [InlineData("/", "text/html", "manual-rss-run-saved")]
    [InlineData("/", "text/html", "manual-rss-manage-source")]
    [InlineData("/", "text/html", "source-duplicate-notification-enabled")]
    [InlineData("/", "text/html", "mikan-work-rule-form")]
    [InlineData("/", "text/html", "mikan-work-rule-rematch")]
    [InlineData("/", "text/html", "library-episode-filter")]
    [InlineData("/", "text/html", "library-search-form")]
    [InlineData("/", "text/html", "library-search-clear")]
    [InlineData("/", "text/html", "library-create-form")]
    [InlineData("/", "text/html", "library-detail-refresh")]
    [InlineData("/", "text/html", "library-detail-tmdb-link")]
    [InlineData("/", "text/html", "library-external-import")]
    [InlineData("/", "text/html", "library-detail-external-import")]
    [InlineData("/", "text/html", "library-detail-delete")]
    [InlineData("/", "text/html", "library-detail-delete-content")]
    [InlineData("/", "text/html", "library-detail-mikan-completion")]
    [InlineData("/", "text/html", "mikan-season-completion-dialog")]
    [InlineData("/app.js", "text/javascript", "previewMikanSeasonCompletion")]
    [InlineData("/app.js", "text/javascript", "library-related-task-delete-group")]
    [InlineData("/app.js", "text/javascript", "dataset.libraryDeleteTask")]
    [InlineData("/", "text/html", "library-audit")]
    [InlineData("/", "text/html", "pending-tmdb-list")]
    [InlineData("/", "text/html", "download-filter-reset")]
    [InlineData("/", "text/html", "download-business-status")]
    [InlineData("/", "text/html", "data-update-check")]
    [InlineData("/", "text/html", "data-update-downloads")]
    [InlineData("/", "text/html", "data-update-usage-list")]
    [InlineData("/", "text/html", "data-update-offline-package")]
    [InlineData("/styles.css", "text/css", ".pending-tmdb-card")]
    [InlineData("/styles.css", "text/css", ".pending-recovery-form")]
    [InlineData("/styles.css", "text/css", ".manual-submit-card")]
    [InlineData("/styles.css", "text/css", ".mikan-work-impact-task")]
    [InlineData("/styles.css", "text/css", ".metadata-fallback-decision")]
    [InlineData("/styles.css", "text/css", ".metadata-nfo-rewrite")]
    [InlineData("/styles.css", "text/css", ".metadata-rss-evidence-row")]
    public async Task ServesStaticAssets(string path, string mediaType, string marker)
    {
        await using var app = await RunningApp.StartAsync();

        using var response = await app.Client.GetAsync(path);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(marker, content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledRuntimeCapabilityIsNotPresentedAsUnimplemented()
    {
        await using var app = await RunningApp.StartAsync();

        var script = await app.Client.GetStringAsync("/app.js");

        Assert.Contains("enabled ? \"已启用\" : \"当前不可用\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("enabled ? \"已启用\" : \"待实现\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MetadataBackgroundRefreshKeepsStableCardsAndScrollAnchor()
    {
        await using var app = await RunningApp.StartAsync();

        var script = await app.Client.GetStringAsync("/app.js");

        Assert.Contains("renderSignature !== metadataRenderSignature", script, StringComparison.Ordinal);
        Assert.Contains("expandedMetadataTaskIds.size > 0", script, StringComparison.Ordinal);
        Assert.Contains("card.dataset.taskId = item.task_id", script, StringComparison.Ordinal);
        Assert.Contains("window.scrollBy", script, StringComparison.Ordinal);
        Assert.Contains("background && hadReadyContent", script, StringComparison.Ordinal);
        Assert.Contains("loadMetadataTasks(true)", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadBackgroundRefreshPreservesInteractionAndVisibleCard()
    {
        await using var app = await RunningApp.StartAsync();

        var script = await app.Client.GetStringAsync("/app.js");

        Assert.Contains("renderSignature !== downloadRenderSignature", script, StringComparison.Ordinal);
        Assert.Contains("expandedDownloadJobIds.size > 0", script, StringComparison.Ordinal);
        Assert.Contains("card.dataset.jobId = item.job_id", script, StringComparison.Ordinal);
        Assert.Contains("hasFocusedEditorWithin(\"#download-tasks-workspace\")", script, StringComparison.Ordinal);
        Assert.Contains("isSubviewVisible(\"tasks\", \"downloads\")", script, StringComparison.Ordinal);
        Assert.Contains("loadDownloads(true)", script, StringComparison.Ordinal);
    }
}
