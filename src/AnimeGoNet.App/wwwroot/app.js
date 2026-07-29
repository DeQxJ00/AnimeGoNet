"use strict";
function element(selector) {
    const found = document.querySelector(selector);
    if (!found)
        throw new Error(`Required WebUI element is missing: ${selector}`);
    return found;
}
function errorMessage(error, fallback) {
    return error instanceof Error ? error.message : fallback;
}
async function responseError(response) {
    const body = await response.json().catch(() => null);
    return body?.message ?? `HTTP ${response.status}`;
}
const accessKey = new URLSearchParams(window.location.search).get("access_key");
const headers = new Headers();
if (accessKey)
    headers.set("Access-Key", accessKey);
const deleteDialog = element("#delete-dialog");
const deleteConfirm = element("#delete-confirm");
const downloaderConfigDialog = element("#downloader-config-dialog");
const configurationDialog = element("#configuration-dialog");
let activeDeletePreview = null;
let currentConfiguration = null;
let activeRssRules = null;
let sourceProfiles = [];
let activeSourceId = null;
let downloaderInstances = [];
let downloaderConfigurationRevision = 0;
let activeDownloaderId = null;
let ruleIdSequence = 0;
const libraryStorageKey = "animegonet.library.v1";
let libraryState = readLibraryState();
let activeLibraryDetail = null;
let libraryListRequestSequence = 0;
let libraryDetailRequestSequence = 0;
let activeMikanWorkRule = null;
let loadedMikanWorkId = null;
let activeMikanWorkImpact = null;
let activeConfigurationLockedFields = new Set();
const statusLabels = {
    received: "已接收",
    staged: "种子已暂存",
    dispatching: "正在提交下载器",
    download_preparing: "等待下载前匹配",
    download_queued: "已允许下载",
    download_skipped_duplicate: "重复集已跳过",
    organizing_cleanup: "整理完成，正在清理下载器任务",
    organized: "已整理入库",
    downloading: "下载中",
    downloaded: "等待元数据匹配",
    metadata_resolving: "正在匹配 Series / Season",
    metadata_season_resolved: "季度已确认",
    metadata_episode_resolving: "正在验证 Episode",
    metadata_resolved: "元数据已确认",
    metadata_failed: "元数据失败",
};
const deleteGroups = [
    { flag: "delete_business_record", label: "业务完成记录", collection: "business_records", help: "删除后该 TMDB 单集可重新导入" },
    { flag: "delete_downloader_task", label: "qBittorrent 任务", collection: "downloader_tasks", help: "只删除任务，永不让 qB 删除文件" },
    { flag: "delete_source_files", label: "下载源文件", collection: "source_files", help: "精确删除捕获下载根目录内的文件" },
    { flag: "delete_media_files", label: "媒体库文件", collection: "media_files", help: "精确删除捕获媒体库根目录内的文件" },
];
async function loadStatus() {
    const health = element("#health");
    try {
        const response = await fetch("/api/v1/status", { headers });
        if (!response.ok)
            throw new Error(`HTTP ${response.status}`);
        const status = await response.json();
        element("#schema").textContent = `v${status.database_schema_version}`;
        element("#runtime").textContent = status.native_aot
            ? `NativeAOT · ${status.runtime_identifier}`
            : `JIT · ${status.runtime_identifier}`;
        element("#data-path").textContent = status.paths.data_path;
        const modules = Object.entries(status.capabilities).map(([name, enabled]) => {
            const item = document.createElement("article");
            item.className = `module ${enabled ? "enabled" : ""}`;
            const title = document.createElement("strong");
            title.textContent = name.replaceAll("_", " ");
            const state = document.createElement("span");
            state.textContent = enabled ? "已启用" : "待实现";
            item.append(title, state);
            return item;
        });
        element("#modules").replaceChildren(...modules);
        health.textContent = "运行中";
        health.className = "badge ready";
    }
    catch (error) {
        health.textContent = errorMessage(error, "连接失败");
        health.className = "badge error";
    }
}
function readLibraryState() {
    const defaults = {
        sort: "last_updated",
        direction: "desc",
        page: 1,
        page_size: 24,
        episode_filter: "all",
        active_series_id: null,
        active_season_number: null,
    };
    try {
        const raw = window.localStorage.getItem(libraryStorageKey);
        if (!raw)
            return defaults;
        const stored = JSON.parse(raw);
        const sorts = ["last_updated", "name", "air_date", "added_at"];
        const directions = ["asc", "desc"];
        const filters = ["all", "downloaded", "not_downloaded"];
        const pageSizes = [12, 24, 48];
        return {
            sort: sorts.includes(stored.sort)
                ? stored.sort : defaults.sort,
            direction: directions.includes(stored.direction)
                ? stored.direction : defaults.direction,
            page: Number.isInteger(stored.page) && (stored.page ?? 0) > 0
                ? stored.page : defaults.page,
            page_size: pageSizes.includes(stored.page_size)
                ? stored.page_size : defaults.page_size,
            episode_filter: filters.includes(stored.episode_filter)
                ? stored.episode_filter : defaults.episode_filter,
            active_series_id: Number.isInteger(stored.active_series_id)
                && (stored.active_series_id ?? 0) > 0
                ? stored.active_series_id : null,
            active_season_number: Number.isInteger(stored.active_season_number)
                && (stored.active_season_number ?? 0) > 0
                ? stored.active_season_number : null,
        };
    }
    catch {
        return defaults;
    }
}
function saveLibraryState() {
    try {
        window.localStorage.setItem(libraryStorageKey, JSON.stringify(libraryState));
    }
    catch {
        // Browser storage is an optional UI preference; business state remains server-side.
    }
}
function authorizedAssetUrl(path) {
    if (!accessKey)
        return path;
    const url = new URL(path, window.location.origin);
    url.searchParams.set("access_key", accessKey);
    return `${url.pathname}${url.search}`;
}
function libraryDate(value, includeTime = false) {
    if (!value)
        return "未提供";
    if (!includeTime)
        return value;
    const date = new Date(value);
    return Number.isNaN(date.valueOf()) ? value : date.toLocaleString();
}
function libraryStrategy(value) {
    const labels = {
        manual_override: "人工覆盖",
        tmdb_title: "TMDB 标题搜索",
        tmdb_air_date: "TMDB 开播日期验证",
        bangumi_backtrace: "P3 Bangumi 回溯验证",
        ai_metadata: "AI 统一匹配 + TMDB 验证",
        title_season: "P2 本地任务 title 季度（未验证）",
        first_season: "P1 本地 S01（未验证）",
        pending_tmdb_manual: "待补全 TMDB 人工恢复",
        pending_tmdb_automatic: "待补全 TMDB 自动恢复",
    };
    return value ? labels[value] ?? value : "未记录";
}
function libraryWarning(value) {
    const labels = {
        episode_snapshot_incomplete: "TMDB EP snapshot 不完整",
        completion_without_snapshot: "存在 snapshot 外完成记录",
        completion_media_path_unknown: "完成记录缺少媒体路径",
        season_not_tmdb_verified: "本地季度尚未通过 TMDB Season 验证",
    };
    return labels[value] ?? value;
}
function libraryValidation(value) {
    const labels = {
        verified: "TMDB 已验证",
        local_unverified: "本地季度 · 未验证",
        projection_only: "仅有 TMDB 投影",
    };
    return labels[value] ?? value;
}
function librarySortLabel(value) {
    const labels = {
        last_updated: "最后更新时间",
        name: "TMDB 名称",
        air_date: "季度开播日期",
        added_at: "本地加入日期",
    };
    return labels[value];
}
function libraryPoster(url, title, className) {
    const image = document.createElement("img");
    image.className = className;
    image.src = authorizedAssetUrl(url);
    image.alt = `${title} 封面`;
    image.loading = "lazy";
    image.width = 500;
    image.height = 750;
    image.addEventListener("error", () => {
        image.classList.add("failed");
        image.alt = `${title} 封面加载失败`;
    }, { once: true });
    return image;
}
function libraryProgress(downloaded, total) {
    const progress = document.createElement("progress");
    progress.max = Math.max(total, 1);
    progress.value = Math.min(downloaded, progress.max);
    progress.setAttribute("aria-label", `TMDB EP 完成进度 ${downloaded} / ${total}`);
    const label = document.createElement("span");
    label.textContent = total > 0 ? `${downloaded} / ${total} EP` : "尚无完整 TMDB EP snapshot";
    return { progress, label };
}
function renderLibraryWarnings(values) {
    const warnings = document.createElement("div");
    warnings.className = "library-warnings";
    warnings.replaceChildren(...values.map((value) => {
        const warning = document.createElement("span");
        warning.textContent = libraryWarning(value);
        return warning;
    }));
    return warnings;
}
function renderLibraryPage(page) {
    const list = element("#library-list");
    list.setAttribute("aria-busy", "false");
    const pageCount = Math.max(1, Math.ceil(page.total_items / page.page_size));
    element("#library-status").textContent =
        `${page.total_items} 个季度 · ${librarySortLabel(page.sort)} · `
            + (page.direction === "asc" ? "升序" : "降序");
    element("#library-page-label").textContent =
        `第 ${page.page} / ${pageCount} 页`;
    element("#library-previous").disabled = page.page <= 1;
    element("#library-next").disabled = page.page >= pageCount;
    if (page.items.length === 0) {
        const empty = document.createElement("p");
        empty.className = "muted empty";
        empty.textContent = "作品库暂时为空。只有已确认 TMDB Series 与普通 Season 的作品会显示在这里；tmdbid=0 条目请到“待补全 TMDB”处理。";
        list.replaceChildren(empty);
        return;
    }
    list.replaceChildren(...page.items.map((item) => {
        const card = document.createElement("button");
        card.type = "button";
        card.className = "library-card";
        if (libraryState.active_series_id === item.tmdb_series_id
            && libraryState.active_season_number === item.tmdb_season_number) {
            card.classList.add("active");
        }
        card.setAttribute("aria-label", `查看 ${item.display_name} ${item.season_name} 的 TMDB EP 详情`);
        const image = libraryPoster(item.poster_url, `${item.display_name} ${item.season_name}`, "library-poster");
        const content = document.createElement("span");
        content.className = "library-card-content";
        const heading = document.createElement("span");
        heading.className = "library-card-heading";
        const title = document.createElement("strong");
        title.textContent = item.display_name;
        const season = document.createElement("span");
        season.textContent = `${item.season_name} · S${String(item.tmdb_season_number).padStart(2, "0")}`;
        heading.append(title, season);
        const identity = document.createElement("span");
        identity.className = "library-card-identity";
        identity.textContent =
            `TMDB ${item.tmdb_series_id} · 开播 ${libraryDate(item.air_date)} · ${libraryValidation(item.validation_status)}`;
        const progressRow = document.createElement("span");
        progressRow.className = "library-progress";
        const progress = libraryProgress(item.episode_downloaded, item.episode_total);
        progressRow.append(progress.progress, progress.label);
        content.append(heading, identity, progressRow);
        if (item.warnings.length > 0)
            content.append(renderLibraryWarnings(item.warnings));
        card.append(image, content);
        card.addEventListener("click", () => {
            libraryState.active_series_id = item.tmdb_series_id;
            libraryState.active_season_number = item.tmdb_season_number;
            saveLibraryState();
            renderLibraryPage(page);
            void loadLibraryDetail(item.tmdb_series_id, item.tmdb_season_number, true);
        });
        return card;
    }));
}
function renderLibraryEpisodes(detail) {
    const container = element("#library-episodes");
    const filtered = detail.episodes.filter((episode) => libraryState.episode_filter === "all"
        || episode.status === libraryState.episode_filter);
    element("#library-episode-status").textContent =
        `显示 ${filtered.length} / ${detail.episodes.length} 个 TMDB Episode`;
    if (filtered.length === 0) {
        const empty = document.createElement("p");
        empty.className = "muted empty";
        empty.textContent = libraryState.episode_filter === "all"
            ? "当前季度没有可展示的 TMDB Episode snapshot。"
            : "当前筛选条件下没有 Episode。";
        container.replaceChildren(empty);
        return;
    }
    container.replaceChildren(...filtered.map((episode) => {
        const card = document.createElement("details");
        card.className = `library-episode ${episode.status}`;
        const summary = document.createElement("summary");
        summary.setAttribute("role", "button");
        summary.setAttribute("aria-expanded", "false");
        const number = document.createElement("strong");
        number.textContent = `EP ${String(episode.episode_number).padStart(2, "0")}`;
        const status = document.createElement("span");
        status.textContent = episode.status === "downloaded" ? "✓ 已下载" : "○ 未下载";
        summary.append(number, status);
        const name = document.createElement("p");
        name.className = "library-episode-name";
        name.textContent = episode.name || "TMDB 未提供 Episode 名称";
        const metadata = document.createElement("p");
        metadata.className = "library-episode-meta";
        metadata.textContent =
            `TMDB Episode ${episode.tmdb_episode_id} · 开播 ${libraryDate(episode.air_date)} · `
                + `${episode.runtime_minutes === null ? "时长未提供" : `${episode.runtime_minutes} 分钟`} · `
                + `snapshot ${libraryDate(episode.fetched_at_utc, true)}`;
        const completion = document.createElement("p");
        completion.className = "library-episode-completion";
        completion.textContent = episode.status === "downloaded"
            ? `完成于 ${libraryDate(episode.downloaded_at_utc, true)} · 来源 ${episode.source_id ?? "未记录"}`
                + (episode.media_path_known ? "" : " · 媒体路径未记录")
            : "没有规范完成记录；等待、下载中、整理失败或删除完成记录后都保持未下载。";
        card.addEventListener("toggle", () => {
            summary.setAttribute("aria-expanded", String(card.open));
        });
        summary.addEventListener("keydown", (event) => {
            if (event.key !== "Enter" && event.key !== " ")
                return;
            event.preventDefault();
            card.open = !card.open;
        });
        card.append(summary, name, metadata, completion);
        return card;
    }));
}
function renderLibraryDetail(detail, focus) {
    activeLibraryDetail = detail;
    const panel = element("#library-detail");
    panel.hidden = false;
    element("#library-detail-title").textContent =
        `${detail.display_name} · ${detail.season_name}`;
    const summary = element("#library-detail-summary");
    const layout = document.createElement("div");
    layout.className = "library-detail-layout";
    const image = libraryPoster(detail.poster_url, `${detail.display_name} ${detail.season_name}`, "library-detail-poster");
    const content = document.createElement("div");
    const progressRow = document.createElement("div");
    progressRow.className = "library-detail-progress";
    const progress = libraryProgress(detail.episode_downloaded, detail.episode_total);
    progressRow.append(progress.progress, progress.label);
    const facts = document.createElement("dl");
    facts.className = "library-detail-facts";
    const values = [
        ["TMDB 身份", `Series ${detail.tmdb_series_id} · Season ${detail.tmdb_season_number}`],
        ["季度开播", libraryDate(detail.air_date)],
        ["本地加入", libraryDate(detail.added_at_utc, true)],
        ["最后更新", libraryDate(detail.last_updated_at_utc, true)],
        ["Series 取得", libraryStrategy(detail.series_resolution_source)],
        ["Season 取得", libraryStrategy(detail.season_resolution_source)],
        ["验证状态", libraryValidation(detail.validation_status)],
        ["最近解析 Run", detail.last_resolution_run_id ?? "未记录"],
        ["EP snapshot", `${detail.episode_snapshot_count} / TMDB 声明 ${detail.episode_total}`],
    ];
    facts.replaceChildren(...values.map(([label, value]) => {
        const row = document.createElement("div");
        const term = document.createElement("dt");
        term.textContent = label;
        const description = document.createElement("dd");
        description.textContent = value;
        row.append(term, description);
        return row;
    }));
    content.append(progressRow, facts);
    if (detail.warnings.length > 0)
        content.append(renderLibraryWarnings(detail.warnings));
    layout.append(image, content);
    summary.replaceChildren(layout);
    renderLibraryEpisodes(detail);
    if (focus) {
        panel.scrollIntoView({ behavior: "smooth", block: "start" });
        element("#library-detail-close").focus({ preventScroll: true });
    }
}
async function loadLibraryDetail(tmdbSeriesId, seasonNumber, focus = false) {
    const sequence = ++libraryDetailRequestSequence;
    const panel = element("#library-detail");
    panel.hidden = false;
    element("#library-detail-title").textContent = "正在读取季度详情…";
    element("#library-detail-summary").replaceChildren();
    element("#library-episodes").replaceChildren();
    element("#library-episode-status").textContent = "";
    try {
        const response = await fetch(`/api/v1/library/seasons/${tmdbSeriesId}/${seasonNumber}`, { headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        const detail = await response.json();
        if (sequence !== libraryDetailRequestSequence)
            return;
        renderLibraryDetail(detail, focus);
    }
    catch (error) {
        if (sequence !== libraryDetailRequestSequence)
            return;
        activeLibraryDetail = null;
        const message = document.createElement("p");
        message.className = "muted empty";
        message.textContent = `季度详情读取失败：${errorMessage(error, "未知错误")}`;
        element("#library-detail-summary").replaceChildren(message);
    }
}
async function loadLibrary() {
    const sequence = ++libraryListRequestSequence;
    const list = element("#library-list");
    list.setAttribute("aria-busy", "true");
    element("#library-status").textContent = "正在读取作品库…";
    const query = new URLSearchParams({
        page: String(libraryState.page),
        page_size: String(libraryState.page_size),
        sort: libraryState.sort,
        direction: libraryState.direction,
    });
    try {
        const response = await fetch(`/api/v1/library/seasons?${query}`, { headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        const page = await response.json();
        if (sequence !== libraryListRequestSequence)
            return;
        if (page.items.length === 0 && page.total_items > 0 && libraryState.page > 1) {
            libraryState.page = Math.max(1, Math.ceil(page.total_items / page.page_size));
            saveLibraryState();
            await loadLibrary();
            return;
        }
        libraryState.page = page.page;
        libraryState.page_size = page.page_size;
        libraryState.sort = page.sort;
        libraryState.direction = page.direction;
        saveLibraryState();
        renderLibraryPage(page);
        if (libraryState.active_series_id !== null
            && libraryState.active_season_number !== null
            && page.items.some((item) => item.tmdb_series_id === libraryState.active_series_id
                && item.tmdb_season_number === libraryState.active_season_number)) {
            void loadLibraryDetail(libraryState.active_series_id, libraryState.active_season_number);
        }
        else if (libraryState.active_series_id !== null) {
            closeLibraryDetail();
        }
    }
    catch (error) {
        if (sequence !== libraryListRequestSequence)
            return;
        list.setAttribute("aria-busy", "false");
        const failure = document.createElement("p");
        failure.className = "muted empty";
        failure.textContent = `作品库读取失败：${errorMessage(error, "未知错误")}`;
        list.replaceChildren(failure);
        element("#library-status").textContent = "读取失败";
    }
}
function closeLibraryDetail() {
    libraryDetailRequestSequence++;
    activeLibraryDetail = null;
    libraryState.active_series_id = null;
    libraryState.active_season_number = null;
    saveLibraryState();
    element("#library-detail").hidden = true;
    document.querySelectorAll(".library-card.active")
        .forEach((card) => card.classList.remove("active"));
}
function changeLibraryOrdering() {
    libraryState.sort = element("#library-sort")
        .value;
    libraryState.direction = element("#library-direction")
        .value;
    libraryState.page_size = Number(element("#library-page-size").value);
    libraryState.page = 1;
    closeLibraryDetail();
    saveLibraryState();
    void loadLibrary();
}
function configurationCard(title, fields) {
    const card = document.createElement("article");
    card.className = "configuration-card";
    const heading = document.createElement("h3");
    heading.textContent = title;
    const list = document.createElement("dl");
    list.replaceChildren(...fields.map(([label, value]) => {
        const row = document.createElement("div");
        const term = document.createElement("dt");
        term.textContent = label;
        const detail = document.createElement("dd");
        detail.textContent = value;
        row.append(term, detail);
        return row;
    }));
    card.append(heading, list);
    return card;
}
function enabledLabel(value) {
    return value ? "已启用" : "已关闭";
}
function seasonFailurePriority(metadata) {
    const panel = document.createElement("section");
    panel.className = "failure-priority";
    panel.setAttribute("aria-label", "TMDB 季度失败优先级");
    const caption = document.createElement("p");
    caption.className = "failure-priority-caption";
    caption.textContent = "由高到低执行；任一策略成功立即停止。Skip 命中会终止后续 fallback。";
    const sequence = document.createElement("ol");
    sequence.className = "failure-priority-list";
    const steps = [
        {
            priority: "4",
            title: "TMDBFailSkip",
            description: "显式终止，不再执行低优先级策略",
            enabled: metadata.season_failure.skip,
        },
        {
            priority: "3",
            title: "TMDBFailBacktrace",
            description: "需要 bgmid；当前 tmdbid + Season 联合匹配失败后，逐层回溯 Bangumi 前传，"
                + "用每个前作的日文名、中文名和开播日期重新搜索并验证完整 tmdbid + Season",
            enabled: metadata.season_failure.backtrace,
        },
        {
            priority: "independent",
            title: "AI 元数据匹配",
            description: "一个任务、一个提示词，统一返回并验证 TMDB Series、Season 和全部文件的 Episode",
            enabled: metadata.ai.use_metadata_match,
            independent: true,
        },
        {
            priority: "2",
            title: "TMDBFailUseTitleSeason",
            description: "前面策略全部失败后，只用本地标题解析器读取任务 title；"
                + "解析成功即使用该本地季度，不验证 TMDB Season；解析不到继续 P1",
            enabled: metadata.season_failure.use_title_season,
        },
        {
            priority: "1",
            title: "TMDBFailUseFirstSeason",
            description: "前序策略全部失败后，勾选即使用本地 S01，不验证 TMDB Season",
            enabled: metadata.season_failure.use_first_season,
        },
    ];
    sequence.replaceChildren(...steps.map((step) => {
        const item = document.createElement("li");
        item.className = `failure-priority-step ${step.enabled ? "enabled" : "disabled"}`
            + (step.independent ? " independent" : "");
        item.dataset.priority = step.priority;
        const badge = document.createElement("span");
        badge.className = "failure-priority-badge";
        badge.textContent = step.independent ? "独立 AI" : `P${step.priority}`;
        const content = document.createElement("span");
        content.className = "failure-priority-content";
        const title = document.createElement("strong");
        title.textContent = step.title;
        const description = document.createElement("small");
        description.textContent = step.description;
        content.append(title, description);
        const state = document.createElement("span");
        state.className = "failure-priority-state";
        state.textContent = enabledLabel(step.enabled);
        item.append(badge, content, state);
        return item;
    }));
    panel.append(caption, sequence);
    return panel;
}
function metadataConfigurationCard(config) {
    const tmdbCredential = config.metadata.tmdb.api_key_configured
        || config.metadata.tmdb.read_access_token_configured;
    const card = configurationCard("TMDB 与季度失败链", [
        ["TMDB", tmdbCredential ? "凭据已配置（值已隐藏）" : "未配置凭据"],
        ["API / 语言", `${config.metadata.tmdb.base_url} · ${config.metadata.tmdb.language}`],
        ["TMDB 代理", config.metadata.tmdb.proxy_url ?? "直连（未配置）"],
        ["超时", `${config.metadata.tmdb.http_timeout_seconds} 秒`],
        ["Bangumi API", config.metadata.bangumi.base_url],
        ["Bangumi 代理", config.metadata.bangumi.proxy_url ?? "直连（未配置）"],
        ["Bangumi 超时", `${config.metadata.bangumi.http_timeout_seconds} 秒`],
        [
            "Bangumi 完全兜底（一般不启用这个）",
            `${enabledLabel(config.metadata.tmdb_failure_use_bangumi)} · `
                + "TMDB 完全失败时用 Bangumi 最终兜底；季度固定 S01；需要 bgmid；"
                + "不输出有效 tmdbid（内部仍按现有逻辑写 0）",
        ],
    ]);
    card.append(seasonFailurePriority(config.metadata));
    return card;
}
async function loadConfiguration() {
    const status = element("#configuration-status");
    const container = element("#configuration");
    status.textContent = "正在读取脱敏后的生效配置…";
    try {
        const response = await fetch("/api/v1/config", { headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        const config = await response.json();
        currentConfiguration = config;
        element("#configuration-reset").disabled =
            config.configuration_revision === 0;
        container.replaceChildren(configurationCard("目录", [
            ["data_path", config.paths.data_path],
            ["download_path", config.paths.download_path],
            ["save_path", config.paths.save_path],
            ["修改生效", config.deployment.paths_restart_required ? "需要重启" : "即时生效"],
        ]), configurationCard("部署与安全", [
            ["容器模式", enabledLabel(config.deployment.running_in_container)],
            ["后台 workers", enabledLabel(config.deployment.background_workers_enabled)],
            ["Access-Key", config.deployment.access_key_configured ? "已配置（值已隐藏）" : "未配置"],
        ]), metadataConfigurationCard(config), configurationCard("AI、偏移与 Torrent", [
            [
                "AI 匹配",
                `任务级 ${enabledLabel(config.metadata.ai.use_metadata_match)} · 单提示词 · `
                    + `${config.metadata.ai.http_timeout_seconds} 秒`,
            ],
            ["可信 offset 缓存", enabledLabel(config.metadata.mikan_trusted_offset_cache_enabled)],
            [
                "Torrent HTTP",
                `${config.torrent_fetch.http_timeout_seconds} 秒 · `
                    + `${config.torrent_fetch.max_redirects} 次跳转 · `
                    + `${config.torrent_fetch.max_response_bytes} bytes`,
            ],
            ["Torrent 暂存 TTL", `${config.torrent_fetch.staging_ttl_seconds} 秒`],
        ]));
        status.textContent = config.restart_required
            ? `存在待重启配置 · 已保存 revision ${config.configuration_revision} · `
                + `当前应用 revision ${config.applied_configuration_revision}`
            : `当前进程的生效值 · revision ${config.configuration_revision}；凭据永不回传。`;
    }
    catch (error) {
        currentConfiguration = null;
        container.replaceChildren();
        status.textContent = `配置读取失败：${errorMessage(error, "未知错误")}`;
    }
}
function configurationSecretLabel(state) {
    switch (state) {
        case "configured": return "当前私密覆盖：已配置（值已隐藏）";
        case "cleared": return "当前私密覆盖：已明确清除";
        default: return "当前私密覆盖：继承部署配置";
    }
}
function setConfigurationValue(id, value) {
    element(id).value = String(value);
}
function setConfigurationChecked(id, value) {
    element(id).checked = value;
}
function syncConfigurationSecretInputs() {
    const clearKey = element("#configuration-tmdb-key-clear").checked;
    const clearToken = element("#configuration-tmdb-token-clear").checked;
    const key = element("#configuration-tmdb-key");
    const token = element("#configuration-tmdb-token");
    const keyLocked = activeConfigurationLockedFields.has("tmdb_api_key");
    const tokenLocked = activeConfigurationLockedFields.has("tmdb_read_access_token");
    key.disabled = keyLocked || clearKey;
    token.disabled = tokenLocked || clearToken;
    element("#configuration-tmdb-key-clear").disabled = keyLocked;
    element("#configuration-tmdb-token-clear").disabled = tokenLocked;
    if (clearKey)
        key.value = "";
    if (clearToken)
        token.value = "";
}
const configurationLockSelectors = {
    tmdb_base_url: ["#configuration-tmdb-url"],
    tmdb_proxy_url: ["#configuration-tmdb-proxy"],
    tmdb_language: ["#configuration-tmdb-language"],
    tmdb_http_timeout_seconds: ["#configuration-tmdb-timeout"],
    tmdb_api_key: ["#configuration-tmdb-key", "#configuration-tmdb-key-clear"],
    tmdb_read_access_token: ["#configuration-tmdb-token", "#configuration-tmdb-token-clear"],
    bangumi_base_url: ["#configuration-bangumi-url"],
    bangumi_proxy_url: ["#configuration-bangumi-proxy"],
    bangumi_http_timeout_seconds: ["#configuration-bangumi-timeout"],
    ai_use_metadata_match: ["#configuration-ai-metadata"],
    ai_http_timeout_seconds: ["#configuration-ai-timeout"],
};
function applyConfigurationLocks(locks) {
    activeConfigurationLockedFields = new Set(locks.map((lock) => lock.field));
    const lockByField = new Map(locks.map((lock) => [lock.field, lock]));
    for (const [field, selectors] of Object.entries(configurationLockSelectors)) {
        const lock = lockByField.get(field);
        for (const selector of selectors) {
            const input = element(selector);
            input.disabled = lock !== undefined;
            const label = input.closest("label");
            label?.classList.toggle("configuration-field-locked", lock !== undefined);
            if (lock) {
                input.title = `由环境变量 ${lock.environment_variables.join(", ")} 控制`;
            }
            else {
                input.removeAttribute("title");
            }
        }
    }
    const summary = element("#configuration-lock-summary");
    if (locks.length === 0) {
        summary.textContent = "当前没有环境变量锁定的可编辑字段。";
        summary.className = "configuration-lock-summary muted";
        return;
    }
    summary.textContent = `以下字段由部署环境控制，Web 只读：${locks
        .map((lock) => `${lock.field} (${lock.environment_variables.join(", ")})`)
        .join("；")}`;
    summary.className = "configuration-lock-summary active";
}
function openConfigurationEditor() {
    if (!currentConfiguration)
        return;
    const editable = currentConfiguration.editable;
    setConfigurationValue("#configuration-tmdb-url", editable.tmdb_base_url);
    setConfigurationValue("#configuration-tmdb-proxy", editable.tmdb_proxy_url ?? "");
    setConfigurationValue("#configuration-tmdb-language", editable.tmdb_language);
    setConfigurationValue("#configuration-tmdb-timeout", editable.tmdb_http_timeout_seconds);
    setConfigurationValue("#configuration-tmdb-key", "");
    setConfigurationChecked("#configuration-tmdb-key-clear", false);
    element("#configuration-tmdb-key-state").textContent =
        configurationSecretLabel(editable.tmdb_api_key_state);
    setConfigurationValue("#configuration-tmdb-token", "");
    setConfigurationChecked("#configuration-tmdb-token-clear", false);
    element("#configuration-tmdb-token-state").textContent =
        configurationSecretLabel(editable.tmdb_read_access_token_state);
    setConfigurationValue("#configuration-bangumi-url", editable.bangumi_base_url);
    setConfigurationValue("#configuration-bangumi-proxy", editable.bangumi_proxy_url ?? "");
    setConfigurationValue("#configuration-bangumi-timeout", editable.bangumi_http_timeout_seconds);
    setConfigurationChecked("#configuration-fail-skip", editable.season_failure_skip);
    setConfigurationChecked("#configuration-fail-backtrace", editable.season_failure_backtrace);
    setConfigurationChecked("#configuration-fail-title", editable.season_failure_use_title_season);
    setConfigurationChecked("#configuration-fail-first", editable.season_failure_use_first_season);
    setConfigurationChecked("#configuration-ai-metadata", editable.ai_use_metadata_match);
    setConfigurationChecked("#configuration-bangumi-fallback", editable.tmdb_failure_use_bangumi);
    setConfigurationChecked("#configuration-offset-cache", editable.mikan_trusted_offset_cache_enabled);
    setConfigurationValue("#configuration-ai-timeout", editable.ai_http_timeout_seconds);
    setConfigurationValue("#configuration-torrent-timeout", editable.torrent_http_timeout_seconds);
    setConfigurationValue("#configuration-torrent-bytes", editable.torrent_max_response_bytes);
    setConfigurationValue("#configuration-torrent-redirects", editable.torrent_max_redirects);
    setConfigurationValue("#configuration-torrent-ttl", editable.torrent_staging_ttl_seconds);
    applyConfigurationLocks(editable.locked_fields);
    element("#configuration-message").textContent =
        `正在编辑 revision ${currentConfiguration.configuration_revision}`;
    syncConfigurationSecretInputs();
    configurationDialog.showModal();
}
async function saveConfiguration(event) {
    event.preventDefault();
    if (!currentConfiguration)
        return;
    const save = element("#configuration-save");
    const message = element("#configuration-message");
    save.disabled = true;
    message.textContent = "正在保存私密配置覆盖…";
    try {
        const requestHeaders = new Headers(headers);
        requestHeaders.set("Content-Type", "application/json");
        const response = await fetch("/api/v1/config", {
            method: "PUT",
            headers: requestHeaders,
            body: JSON.stringify({
                tmdb_base_url: element("#configuration-tmdb-url").value,
                tmdb_proxy_url: element("#configuration-tmdb-proxy").value || null,
                tmdb_language: element("#configuration-tmdb-language").value,
                tmdb_http_timeout_seconds: element("#configuration-tmdb-timeout").valueAsNumber,
                tmdb_api_key: element("#configuration-tmdb-key").value || null,
                clear_tmdb_api_key: element("#configuration-tmdb-key-clear").checked,
                tmdb_read_access_token: element("#configuration-tmdb-token").value || null,
                clear_tmdb_read_access_token: element("#configuration-tmdb-token-clear").checked,
                bangumi_base_url: element("#configuration-bangumi-url").value,
                bangumi_proxy_url: element("#configuration-bangumi-proxy").value || null,
                bangumi_http_timeout_seconds: element("#configuration-bangumi-timeout").valueAsNumber,
                season_failure_skip: element("#configuration-fail-skip").checked,
                season_failure_backtrace: element("#configuration-fail-backtrace").checked,
                season_failure_use_title_season: element("#configuration-fail-title").checked,
                season_failure_use_first_season: element("#configuration-fail-first").checked,
                ai_use_metadata_match: element("#configuration-ai-metadata").checked,
                ai_http_timeout_seconds: element("#configuration-ai-timeout").valueAsNumber,
                tmdb_failure_use_bangumi: element("#configuration-bangumi-fallback").checked,
                mikan_trusted_offset_cache_enabled: element("#configuration-offset-cache").checked,
                torrent_http_timeout_seconds: element("#configuration-torrent-timeout").valueAsNumber,
                torrent_max_response_bytes: element("#configuration-torrent-bytes").valueAsNumber,
                torrent_max_redirects: element("#configuration-torrent-redirects").valueAsNumber,
                torrent_staging_ttl_seconds: element("#configuration-torrent-ttl").valueAsNumber,
                expected_configuration_revision: currentConfiguration.configuration_revision,
            }),
        });
        if (!response.ok)
            throw new Error(await responseError(response));
        const saved = await response.json();
        await loadConfiguration();
        message.textContent = `已保存 revision ${saved.configuration_revision}；重启主程序后生效。`;
    }
    catch (error) {
        message.textContent = `保存失败：${errorMessage(error, "未知错误")}；revision 冲突时请刷新后重试。`;
    }
    finally {
        save.disabled = false;
    }
}
async function resetConfiguration() {
    if (!currentConfiguration || currentConfiguration.configuration_revision === 0)
        return;
    if (!window.confirm("恢复部署默认配置？重启主程序后生效。"))
        return;
    const status = element("#configuration-status");
    status.textContent = "正在移除私密配置覆盖…";
    try {
        const response = await fetch(`/api/v1/config?expected_revision=${currentConfiguration.configuration_revision}`, { method: "DELETE", headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        await loadConfiguration();
    }
    catch (error) {
        status.textContent = `恢复失败：${errorMessage(error, "未知错误")}`;
    }
}
function formatBytes(value) {
    if (value < 1024)
        return `${value} B`;
    const units = ["KiB", "MiB", "GiB", "TiB"];
    let size = value;
    let unit = -1;
    do {
        size /= 1024;
        unit += 1;
    } while (size >= 1024 && unit + 1 < units.length);
    return `${size.toFixed(size >= 10 ? 1 : 2)} ${units[unit]}`;
}
async function loadDownloads() {
    const container = element("#downloads");
    try {
        const response = await fetch("/api/v1/downloads", { headers });
        if (!response.ok)
            throw new Error(`HTTP ${response.status}`);
        const body = await response.json();
        if (body.items.length === 0) {
            const empty = document.createElement("p");
            empty.className = "muted empty";
            empty.textContent = "暂无下载任务";
            container.replaceChildren(empty);
            return;
        }
        const cards = body.items.map((item) => {
            const card = document.createElement("article");
            card.className = `download-card ${item.is_stale ? "stale" : ""}`;
            const heading = document.createElement("div");
            heading.className = "download-heading";
            const title = document.createElement("strong");
            title.textContent = item.title;
            const state = document.createElement("span");
            state.className = `badge ${item.is_stale ? "error" : "ready"}`;
            state.textContent = item.is_stale
                ? `快照过期 · ${item.downloader_failure_code ?? "离线"}`
                : statusLabels[item.business_status] ?? item.business_status;
            heading.append(title, state);
            const progress = document.createElement("progress");
            progress.max = 1;
            progress.value = item.progress;
            const details = document.createElement("p");
            details.className = "download-details";
            details.textContent = `${item.source} → ${item.downloader_id} · ${(item.progress * 100).toFixed(1)}% · ${formatBytes(item.downloaded_bytes)} / ${formatBytes(item.total_bytes)} · ${formatBytes(item.speed_bytes_per_second)}/s · Seeds ${item.seeds} · Peers ${item.peers}`;
            const actions = document.createElement("div");
            actions.className = "download-actions";
            const remove = document.createElement("button");
            remove.type = "button";
            remove.className = "delete-button";
            remove.textContent = "删除…";
            remove.addEventListener("click", () => void openDeletePreview(item.task_id));
            actions.append(remove);
            card.append(heading, progress, details, actions);
            return card;
        });
        container.replaceChildren(...cards);
    }
    catch (error) {
        const failed = document.createElement("p");
        failed.className = "muted empty";
        failed.textContent = `下载状态读取失败：${errorMessage(error, "未知错误")}`;
        container.replaceChildren(failed);
    }
}
async function loadTrustedOffsets() {
    const container = element("#trusted-offsets");
    try {
        const response = await fetch("/api/v1/mikan/trusted-offsets", { headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        const body = await response.json();
        if (body.items.length === 0) {
            const empty = document.createElement("p");
            empty.className = "muted empty";
            empty.textContent = "暂无自动 offset 学习证据";
            container.replaceChildren(empty);
            return;
        }
        container.replaceChildren(...body.items.map((item) => {
            const card = document.createElement("article");
            card.className = "offset-card";
            const summary = document.createElement("div");
            const heading = document.createElement("div");
            heading.className = "download-heading";
            const title = document.createElement("strong");
            title.textContent = `Mikan ${item.mikanid} · Group ${item.groupid}`;
            const state = document.createElement("span");
            state.className = `badge ${item.state}`;
            state.textContent = item.state === "trusted"
                ? "Trusted"
                : item.state === "conflict_reset" ? "Conflict reset" : "Learning";
            heading.append(title, state);
            const details = document.createElement("p");
            details.className = "muted";
            const signedOffset = item.episode_offset >= 0
                ? `+${item.episode_offset}` : `${item.episode_offset}`;
            details.textContent = `TMDB ${item.tmdb_series_id} · S${String(item.tmdb_season_number).padStart(2, "0")} · offset ${signedOffset} · ${item.distinct_episode_count}/${item.required_episode_count}`;
            summary.append(heading, details);
            const clear = document.createElement("button");
            clear.type = "button";
            clear.className = "delete-button";
            clear.textContent = "清理自动缓存";
            clear.addEventListener("click", () => void clearTrustedOffset(item));
            card.append(summary, clear);
            return card;
        }));
    }
    catch (error) {
        const failed = document.createElement("p");
        failed.className = "muted empty";
        failed.textContent = `可信 offset 读取失败：${errorMessage(error, "未知错误")}`;
        container.replaceChildren(failed);
    }
}
async function clearTrustedOffset(item) {
    if (!window.confirm(`清理 Mikan ${item.mikanid} / Group ${item.groupid} 的自动证据与缓存？人工规则、完成记录和媒体文件不会删除。`))
        return;
    try {
        const response = await fetch(`/api/v1/mikan/trusted-offsets/${item.mikanid}/${item.groupid}`, { method: "DELETE", headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        await loadTrustedOffsets();
    }
    catch (error) {
        window.alert(`清理失败：${errorMessage(error, "未知错误")}`);
    }
}
function selectedDeleteInput(flag) {
    return document.querySelector(`#delete-options input[name="${flag}"]`);
}
function updateDeleteConfirm() {
    deleteConfirm.disabled = !activeDeletePreview || !deleteGroups.some(({ flag }) => selectedDeleteInput(flag)?.checked);
}
async function openDeletePreview(taskId) {
    const options = element("#delete-options");
    const targets = element("#delete-targets");
    const message = element("#delete-message");
    options.replaceChildren();
    targets.replaceChildren();
    message.textContent = "正在读取不可变目标…";
    deleteConfirm.disabled = true;
    deleteDialog.showModal();
    try {
        const response = await fetch(`/api/v1/delete/tasks/${encodeURIComponent(taskId)}/preview`, { headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        activeDeletePreview = await response.json();
        element("#delete-summary").textContent = `${activeDeletePreview.title} · ${statusLabels[activeDeletePreview.task_status] ?? activeDeletePreview.task_status}`;
        for (const group of deleteGroups) {
            const groupTargets = activeDeletePreview[group.collection];
            const option = document.createElement("label");
            option.className = "delete-option";
            const input = document.createElement("input");
            input.type = "checkbox";
            input.name = group.flag;
            input.disabled = groupTargets.length === 0;
            input.addEventListener("change", updateDeleteConfirm);
            const text = document.createElement("span");
            const strong = document.createElement("strong");
            strong.textContent = `${group.label} · ${groupTargets.length} 项`;
            const small = document.createElement("small");
            small.textContent = group.help;
            text.append(strong, small);
            option.append(input, text);
            options.append(option);
            if (groupTargets.length > 0) {
                const section = document.createElement("section");
                const heading = document.createElement("h3");
                heading.textContent = group.label;
                const list = document.createElement("ul");
                for (const target of groupTargets) {
                    const row = document.createElement("li");
                    row.textContent = target.display_value;
                    list.append(row);
                }
                section.append(heading, list);
                targets.append(section);
            }
        }
        message.textContent = "目标已冻结预览。勾选动作后才可确认；执行结果可按 execution ID 查询。";
        updateDeleteConfirm();
    }
    catch (error) {
        activeDeletePreview = null;
        message.textContent = `预览失败：${errorMessage(error, "未知错误")}`;
    }
}
deleteConfirm.addEventListener("click", async () => {
    if (!activeDeletePreview)
        return;
    deleteConfirm.disabled = true;
    deleteConfirm.textContent = "正在创建…";
    const request = {
        fingerprint: activeDeletePreview.fingerprint,
        delete_business_record: false,
        delete_downloader_task: false,
        delete_source_files: false,
        delete_media_files: false,
    };
    for (const { flag } of deleteGroups)
        request[flag] = Boolean(selectedDeleteInput(flag)?.checked);
    try {
        const requestHeaders = new Headers(headers);
        requestHeaders.set("Content-Type", "application/json");
        const response = await fetch(`/api/v1/delete/tasks/${encodeURIComponent(activeDeletePreview.task_id)}`, {
            method: "POST",
            headers: requestHeaders,
            body: JSON.stringify(request),
        });
        if (!response.ok)
            throw new Error(await responseError(response));
        const body = await response.json();
        element("#delete-message").textContent = `删除任务已创建：${body.execution_id}（${body.selected_target_count} 项）`;
        deleteConfirm.textContent = "已创建";
        window.setTimeout(() => deleteDialog.close(), 1600);
    }
    catch (error) {
        element("#delete-message").textContent = errorMessage(error, "创建失败");
        deleteConfirm.textContent = "确认创建删除任务";
        updateDeleteConfirm();
    }
});
deleteDialog.addEventListener("close", () => {
    activeDeletePreview = null;
    deleteConfirm.textContent = "确认创建删除任务";
});
function textOrDash(value) {
    return value === null || value === undefined || value === "" ? "—" : String(value);
}
async function retryMetadataTask(taskId, button) {
    button.disabled = true;
    button.textContent = "重新入队中…";
    try {
        const response = await fetch(`/api/v1/metadata/tasks/${encodeURIComponent(taskId)}/retry`, { method: "POST", headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        await loadMetadataTasks();
    }
    catch (error) {
        button.disabled = false;
        button.textContent = errorMessage(error, "重试失败");
    }
}
const expandedMetadataTaskIds = new Set();
async function loadMetadataAttempts(taskId, target, button) {
    expandedMetadataTaskIds.add(taskId);
    button.disabled = true;
    button.textContent = "读取策略时间线…";
    try {
        const response = await fetch(`/api/v1/metadata/tasks/${encodeURIComponent(taskId)}/attempts`, { headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        const body = await response.json();
        if (body.items.length === 0) {
            const empty = document.createElement("p");
            empty.className = "muted metadata-attempt-empty";
            empty.textContent = "尚无策略尝试记录。任务进入元数据阶段后会在这里显示。";
            target.replaceChildren(empty);
        }
        else {
            target.replaceChildren(...body.items.map((attempt) => {
                const row = document.createElement("article");
                row.className = `metadata-attempt ${attempt.result === "failed" ? "failed" : ""}`;
                const heading = document.createElement("div");
                heading.className = "metadata-attempt-heading";
                const strategy = document.createElement("strong");
                strategy.textContent = `${attempt.stage} · ${attempt.strategy}`;
                const result = document.createElement("span");
                result.className = `badge ${attempt.result === "failed" ? "error" : "ready"}`;
                result.textContent = attempt.result;
                heading.append(strategy, result);
                const execution = document.createElement("p");
                execution.textContent = `P${textOrDash(attempt.priority)} · Run #${attempt.run_attempt_number} (${attempt.run_status}) · 尝试 #${attempt.attempt_number} · ${attempt.duration_ms} ms · ${new Date(attempt.created_at_utc).toLocaleString()}`;
                row.append(heading, execution);
                if (attempt.error_code || attempt.reason) {
                    const reason = document.createElement("p");
                    reason.className = "metadata-attempt-reason";
                    reason.textContent = `${textOrDash(attempt.error_code)} · ${textOrDash(attempt.reason)} · ${attempt.retryable ? "可自动重试" : "不可自动重试"}`;
                    row.append(reason);
                }
                return row;
            }));
        }
        button.disabled = false;
        button.textContent = "收起策略时间线";
        button.onclick = () => {
            expandedMetadataTaskIds.delete(taskId);
            target.replaceChildren();
            button.textContent = "查看策略时间线";
            button.onclick = () => void loadMetadataAttempts(taskId, target, button);
        };
    }
    catch (error) {
        target.textContent = `策略时间线读取失败：${errorMessage(error, "未知错误")}`;
        button.disabled = false;
        button.textContent = "重试策略时间线";
    }
}
async function loadMetadataTasks() {
    const container = element("#metadata-tasks");
    try {
        const response = await fetch("/api/v1/metadata/tasks", { headers });
        if (!response.ok)
            throw new Error(`HTTP ${response.status}`);
        const body = await response.json();
        if (body.items.length === 0) {
            const empty = document.createElement("p");
            empty.className = "muted empty";
            empty.textContent = "暂无元数据任务";
            container.replaceChildren(empty);
            return;
        }
        const cards = body.items.map((item) => {
            const card = document.createElement("article");
            card.className = `metadata-card ${item.status === "metadata_failed" ? "failed" : ""}`;
            const heading = document.createElement("div");
            heading.className = "metadata-heading";
            const title = document.createElement("strong");
            title.textContent = item.title;
            const state = document.createElement("span");
            state.className = `badge ${item.status === "metadata_failed" ? "error" : "ready"}`;
            state.textContent = statusLabels[item.status] ?? item.status;
            heading.append(title, state);
            const identity = document.createElement("p");
            identity.className = "metadata-identity";
            identity.textContent = `${item.source} · mikanid ${textOrDash(item.mikanid)} · bgmid ${textOrDash(item.bgmid)} · TMDB ${textOrDash(item.tmdb_series_id)} / S${item.tmdb_season_number === null ? "—" : String(item.tmdb_season_number).padStart(2, "0")}`;
            const stages = document.createElement("dl");
            stages.className = "metadata-stages";
            for (const [label, value] of [
                ["Series", item.series_strategy],
                ["Season", item.season_strategy],
                ["Episode", item.episode_strategy],
            ]) {
                const group = document.createElement("div");
                const term = document.createElement("dt");
                term.textContent = String(label);
                const description = document.createElement("dd");
                description.textContent = textOrDash(value);
                group.append(term, description);
                stages.append(group);
            }
            const files = document.createElement("p");
            files.className = "metadata-files";
            files.textContent = `已确认 ${item.episode_file_count} · 已跳过重复 ${item.duplicate_file_count} · Other ${item.other_file_count} · 待处理 ${item.pending_file_count}`;
            card.append(heading, identity, stages, files);
            if (item.failure_kind || item.failure_reason) {
                const failure = document.createElement("p");
                failure.className = "metadata-failure";
                failure.textContent = `${textOrDash(item.failure_kind)} · ${textOrDash(item.failure_reason)}`;
                card.append(failure);
            }
            const actions = document.createElement("div");
            actions.className = "metadata-actions";
            const attempts = document.createElement("button");
            attempts.type = "button";
            attempts.className = "metadata-attempt-button";
            attempts.textContent = "查看策略时间线";
            const attemptList = document.createElement("div");
            attemptList.className = "metadata-attempt-list";
            attempts.onclick = () => void loadMetadataAttempts(item.task_id, attemptList, attempts);
            actions.append(attempts);
            if (item.status === "metadata_failed") {
                const retry = document.createElement("button");
                retry.type = "button";
                retry.className = "retry-button";
                retry.textContent = "显式重新匹配";
                retry.addEventListener("click", () => void retryMetadataTask(item.task_id, retry));
                actions.append(retry);
            }
            card.append(actions, attemptList);
            if (expandedMetadataTaskIds.has(item.task_id)) {
                void loadMetadataAttempts(item.task_id, attemptList, attempts);
            }
            return card;
        });
        container.replaceChildren(...cards);
    }
    catch (error) {
        const failed = document.createElement("p");
        failed.className = "muted empty";
        failed.textContent = `元数据状态读取失败：${errorMessage(error, "未知错误")}`;
        container.replaceChildren(failed);
    }
}
function pendingStat(label, value) {
    const group = document.createElement("div");
    const term = document.createElement("dt");
    term.textContent = label;
    const description = document.createElement("dd");
    description.textContent = String(value);
    group.append(term, description);
    return group;
}
function positiveInteger(value) {
    const parsed = Number(value);
    return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : null;
}
function createPendingRecoveryForm(bgmid, detail) {
    const heading = document.createElement("h4");
    heading.textContent = "人工 TMDB 恢复";
    if (detail.recovery_candidates.length === 0) {
        const empty = document.createElement("p");
        empty.className = "muted";
        empty.textContent = "尚无已整理完成、可恢复的 fallback 记录。";
        return [heading, empty];
    }
    const explanation = document.createElement("p");
    explanation.className = "pending-recovery-warning";
    explanation.textContent = "提交后会逐项向 TMDB 验证 Series / Season / Episode。收敛到已有集时标记 DuplicateAfterResolution，不重新下载或删除文件。";
    const form = document.createElement("form");
    form.className = "pending-recovery-form";
    const seriesLabel = document.createElement("label");
    seriesLabel.textContent = "TMDB Series ID";
    const seriesInput = document.createElement("input");
    seriesInput.type = "number";
    seriesInput.min = "1";
    seriesInput.required = true;
    seriesInput.inputMode = "numeric";
    seriesInput.autocomplete = "off";
    seriesLabel.append(seriesInput);
    form.append(seriesLabel);
    const defaultSeason = detail.summary.season_numbers.length === 1
        ? detail.summary.season_numbers[0]
        : null;
    const fields = detail.recovery_candidates.map((candidate) => {
        const row = document.createElement("fieldset");
        row.className = "pending-recovery-row";
        const legend = document.createElement("legend");
        legend.textContent = `${candidate.source} · 来源 EP ${textOrDash(candidate.source_episode)} · ${candidate.dedup_boundary}`;
        const seasonLabel = document.createElement("label");
        seasonLabel.textContent = "Season";
        const seasonInput = document.createElement("input");
        seasonInput.type = "number";
        seasonInput.min = "1";
        seasonInput.required = true;
        seasonInput.inputMode = "numeric";
        seasonInput.value = defaultSeason === null ? "" : String(defaultSeason);
        const episodeLabel = document.createElement("label");
        episodeLabel.textContent = "Episode";
        const episodeInput = document.createElement("input");
        episodeInput.type = "number";
        episodeInput.min = "1";
        episodeInput.required = true;
        episodeInput.inputMode = "numeric";
        const sourceEpisode = candidate.source_episode === null
            ? null
            : positiveInteger(candidate.source_episode);
        episodeInput.value = sourceEpisode === null ? "" : String(sourceEpisode);
        seasonLabel.append(seasonInput);
        episodeLabel.append(episodeInput);
        row.append(legend, seasonLabel, episodeLabel);
        form.append(row);
        return { candidate, seasonInput, episodeInput };
    });
    const status = document.createElement("p");
    status.className = "pending-recovery-status";
    status.setAttribute("aria-live", "polite");
    const submit = document.createElement("button");
    submit.type = "submit";
    submit.className = "primary-button";
    submit.textContent = "验证并恢复";
    form.append(status, submit);
    form.addEventListener("submit", async (event) => {
        event.preventDefault();
        const seriesId = positiveInteger(seriesInput.value);
        const mappings = fields.map(({ candidate, seasonInput, episodeInput }) => ({
            fallback_record_id: candidate.fallback_record_id,
            tmdb_season_number: positiveInteger(seasonInput.value),
            tmdb_episode_number: positiveInteger(episodeInput.value),
        }));
        if (seriesId === null
            || mappings.some((mapping) => mapping.tmdb_season_number === null || mapping.tmdb_episode_number === null)) {
            status.textContent = "Series、Season、Episode 都必须是正整数。";
            return;
        }
        if (!window.confirm(`确认用 TMDB Series ${seriesId} 恢复 bgmid ${bgmid} 的 ${mappings.length} 条记录？`))
            return;
        submit.disabled = true;
        submit.textContent = "正在向 TMDB 验证…";
        status.textContent = "";
        try {
            const requestHeaders = new Headers(headers);
            requestHeaders.set("Content-Type", "application/json");
            const response = await fetch(`/api/v1/metadata/pending-tmdb/${encodeURIComponent(String(bgmid))}/recover`, {
                method: "POST",
                headers: requestHeaders,
                body: JSON.stringify({ tmdb_series_id: seriesId, mappings }),
            });
            if (!response.ok)
                throw new Error(await responseError(response));
            const result = await response.json();
            const duplicates = result.items.filter((item) => item.state === "DuplicateAfterResolution").length;
            status.textContent = `已验证并恢复 ${result.items.length} 条；解析后重复 ${duplicates} 条。`;
            await Promise.all([loadPendingTmdb(true), loadMetadataTasks()]);
        }
        catch (error) {
            status.textContent = `恢复失败：${errorMessage(error, "未知错误")}`;
            submit.disabled = false;
            submit.textContent = "验证并恢复";
        }
    });
    return [heading, explanation, form];
}
async function loadPendingTmdbDetail(bgmid, target, button) {
    button.disabled = true;
    button.textContent = "读取中…";
    try {
        const response = await fetch(`/api/v1/metadata/pending-tmdb/${encodeURIComponent(String(bgmid))}`, { headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        const detail = await response.json();
        const sections = [];
        const scopeHeading = document.createElement("h4");
        scopeHeading.textContent = "兜底去重作用域";
        sections.push(scopeHeading);
        if (detail.scopes.length === 0) {
            const empty = document.createElement("p");
            empty.className = "muted";
            empty.textContent = "尚无 claim 或完成记录。";
            sections.push(empty);
        }
        for (const scope of detail.scopes) {
            const row = document.createElement("div");
            row.className = "pending-scope";
            const identity = document.createElement("strong");
            identity.textContent = `${scope.dedup_boundary} · ${scope.state}`;
            const evidence = document.createElement("span");
            evidence.textContent = `${scope.source} · 来源 EP ${textOrDash(scope.source_episode)}`;
            row.append(identity, evidence);
            if (scope.cross_source_duplicate_risk) {
                const warning = document.createElement("em");
                warning.textContent = "可能跨来源重复";
                row.append(warning);
            }
            sections.push(row);
        }
        sections.push(...createPendingRecoveryForm(bgmid, detail));
        const taskHeading = document.createElement("h4");
        taskHeading.textContent = "关联任务";
        sections.push(taskHeading);
        for (const task of detail.tasks) {
            const row = document.createElement("div");
            row.className = "pending-task";
            const title = document.createElement("strong");
            title.textContent = task.title;
            const state = document.createElement("span");
            state.textContent = `${task.source} · ${statusLabels[task.status] ?? task.status} · S${task.season_number === null ? "—" : String(task.season_number).padStart(2, "0")} · Other ${task.other_file_count} · 重复 ${task.duplicate_file_count}`;
            row.append(title, state);
            sections.push(row);
        }
        target.replaceChildren(...sections);
        button.textContent = "收起详情";
        button.disabled = false;
        button.onclick = () => {
            target.replaceChildren();
            button.textContent = "查看详情与人工恢复";
            button.onclick = () => void loadPendingTmdbDetail(bgmid, target, button);
        };
    }
    catch (error) {
        target.textContent = `详情读取失败：${errorMessage(error, "未知错误")}`;
        button.disabled = false;
        button.textContent = "重试详情";
    }
}
async function loadPendingTmdb(force = false) {
    if (!force && document.querySelector(".pending-recovery-form"))
        return;
    const container = element("#pending-tmdb-list");
    try {
        const response = await fetch("/api/v1/metadata/pending-tmdb", { headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        const body = await response.json();
        if (body.items.length === 0) {
            const empty = document.createElement("p");
            empty.className = "muted empty";
            empty.textContent = "暂无待补全 TMDB 的作品";
            container.replaceChildren(empty);
            return;
        }
        container.replaceChildren(...body.items.map((item) => {
            const card = document.createElement("article");
            card.className = "pending-tmdb-card";
            const heading = document.createElement("div");
            heading.className = "metadata-heading";
            const title = document.createElement("strong");
            title.textContent = item.fallback_name;
            const badge = document.createElement("span");
            badge.className = "badge pending";
            badge.textContent = "TMDB 待补全";
            heading.append(title, badge);
            const identity = document.createElement("p");
            identity.className = "metadata-identity";
            const seasons = item.season_numbers.length === 0
                ? "—"
                : item.season_numbers.map((value) => `S${String(value).padStart(2, "0")}`).join("、");
            identity.textContent = `bgmid ${item.bgmid} · 已确认季度 ${seasons}`;
            const stats = document.createElement("dl");
            stats.className = "pending-tmdb-stats";
            stats.append(pendingStat("关联任务", item.task_count), pendingStat("已处理文件", item.processed_file_count), pendingStat("兜底记录", item.fallback_record_count), pendingStat("活动 claim", item.active_claim_count), pendingStat("已完成 claim", item.completed_claim_count), pendingStat("重复文件", item.duplicate_file_count));
            card.append(heading, identity, stats);
            if (item.latest_failure_kind || item.latest_failure_reason) {
                const failure = document.createElement("p");
                failure.className = "metadata-failure";
                failure.textContent = `${textOrDash(item.latest_failure_kind)} · ${textOrDash(item.latest_failure_reason)}`;
                card.append(failure);
            }
            const warning = document.createElement("p");
            warning.className = "pending-progress-warning";
            warning.textContent = "兜底状态，不显示 TMDB Episode 进度。";
            const button = document.createElement("button");
            button.type = "button";
            button.className = "secondary-button";
            button.textContent = "查看详情与人工恢复";
            const detail = document.createElement("div");
            detail.className = "pending-tmdb-detail";
            button.onclick = () => void loadPendingTmdbDetail(item.bgmid, detail, button);
            card.append(warning, button, detail);
            return card;
        }));
    }
    catch (error) {
        const failed = document.createElement("p");
        failed.className = "muted empty";
        failed.textContent = `待补全状态读取失败：${errorMessage(error, "未知错误")}`;
        container.replaceChildren(failed);
    }
}
async function testDownloader(id, button) {
    const status = element("#downloader-status");
    button.disabled = true;
    button.textContent = "测试中…";
    try {
        const response = await fetch(`/api/v1/downloaders/${encodeURIComponent(id)}/test`, {
            method: "POST",
            headers,
        });
        if (!response.ok)
            throw new Error(await responseError(response));
        const result = await response.json();
        status.textContent = result.connected
            ? `${id} 连接成功 · ${textOrDash(result.client_version)} · ${result.task_count ?? 0} 个任务 · ${result.latency_ms} ms · qB 默认路径 ${textOrDash(result.client_default_save_path)}`
            : `${id} 连接失败 · ${result.failure_code ?? "unknown"} · ${result.message}`;
        await loadDownloaders();
    }
    catch (error) {
        status.textContent = `${id} 测试失败：${errorMessage(error, "未知错误")}`;
    }
    finally {
        button.disabled = false;
        button.textContent = "测试连接";
    }
}
async function probeDownloaderPath(id, button) {
    const status = element("#downloader-status");
    button.disabled = true;
    button.textContent = "探测中…";
    try {
        const response = await fetch(`/api/v1/downloaders/${encodeURIComponent(id)}/path-probe`, {
            method: "POST",
            headers,
        });
        if (!response.ok)
            throw new Error(await responseError(response));
        const result = await response.json();
        status.textContent = result.success
            ? `${id} 路径可见且支持硬链接 · ${result.download_path} → ${result.save_path}`
            : `${id} 路径探测失败 · ${result.failure_code ?? "unknown"} · ${result.message}`;
    }
    catch (error) {
        status.textContent = `${id} 路径探测失败：${errorMessage(error, "未知错误")}`;
    }
    finally {
        button.disabled = false;
        button.textContent = "探测路径";
    }
}
function openDownloaderConfig(instance) {
    activeDownloaderId = instance?.id ?? null;
    const id = element("#downloader-config-id");
    id.disabled = instance !== null;
    id.value = instance?.id ?? "";
    element("#downloader-config-url").value = instance?.base_url ?? "http://127.0.0.1:8080/";
    element("#downloader-config-username").value = "";
    element("#downloader-config-password").value = "";
    element("#downloader-config-path").value = instance?.download_path ?? "";
    element("#downloader-config-enabled").checked = instance?.enabled ?? true;
    element("#downloader-config-clear-password").checked = false;
    element("#downloader-config-delete").disabled =
        instance?.configuration_source !== "private_override";
    element("#downloader-config-message").textContent =
        instance?.credentials_configured
            ? "已有凭据已配置；密码字段留空会保留，且不会从服务端读回。"
            : "当前没有已配置凭据。";
    downloaderConfigDialog.showModal();
}
async function saveDownloaderConfig(event) {
    event.preventDefault();
    const id = activeDownloaderId ?? element("#downloader-config-id").value;
    const save = element("#downloader-config-save");
    const message = element("#downloader-config-message");
    save.disabled = true;
    message.textContent = "正在原子写入私有配置…";
    try {
        const requestHeaders = new Headers(headers);
        requestHeaders.set("Content-Type", "application/json");
        const response = await fetch(`/api/v1/downloaders/${encodeURIComponent(id)}`, {
            method: "PUT",
            headers: requestHeaders,
            body: JSON.stringify({
                base_url: element("#downloader-config-url").value,
                username: element("#downloader-config-username").value || null,
                password: element("#downloader-config-password").value || null,
                clear_password: element("#downloader-config-clear-password").checked,
                download_path: element("#downloader-config-path").value,
                enabled: element("#downloader-config-enabled").checked,
                expected_configuration_revision: downloaderConfigurationRevision,
            }),
        });
        if (!response.ok)
            throw new Error(await responseError(response));
        message.textContent = "已保存；请重启主程序以应用新客户端配置。";
        await loadDownloaders();
        window.setTimeout(() => downloaderConfigDialog.close(), 1000);
    }
    catch (error) {
        message.textContent = `保存失败：${errorMessage(error, "未知错误")}`;
    }
    finally {
        save.disabled = false;
    }
}
async function deleteDownloaderOverride() {
    const instance = downloaderInstances.find((item) => item.id === activeDownloaderId);
    if (!instance || instance.configuration_source !== "private_override")
        return;
    if (!window.confirm(`移除 ${instance.id} 的私有覆盖？服务端会拒绝仍有引用的实例。`))
        return;
    const message = element("#downloader-config-message");
    try {
        const response = await fetch(`/api/v1/downloaders/${encodeURIComponent(instance.id)}?expected_configuration_revision=${downloaderConfigurationRevision}`, { method: "DELETE", headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        await loadDownloaders();
        downloaderConfigDialog.close();
    }
    catch (error) {
        message.textContent = `移除失败：${errorMessage(error, "未知错误")}`;
    }
}
async function loadDownloaders() {
    const status = element("#downloader-status");
    const list = element("#downloader-list");
    status.textContent = "正在读取下载器实例…";
    try {
        const response = await fetch("/api/v1/downloaders", { headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        const body = await response.json();
        downloaderInstances = body.items;
        downloaderConfigurationRevision = body.configuration_revision;
        refreshSourceDownloaderOptions();
        list.replaceChildren(...downloaderInstances.map((instance) => {
            const card = document.createElement("article");
            card.className = `downloader-card ${instance.connected === true ? "connected" : instance.connected === false ? "failed" : ""}`;
            const heading = document.createElement("div");
            heading.className = "downloader-card-heading";
            const title = document.createElement("h3");
            title.textContent = instance.id;
            const state = document.createElement("span");
            state.className = `badge ${instance.connected === true ? "ready" : instance.connected === false ? "error" : "pending"}`;
            state.textContent = !instance.enabled ? "已停用" : instance.connected === true ? "已连接" : instance.connected === false ? "连接失败" : "未测试";
            heading.append(title, state);
            const facts = document.createElement("dl");
            for (const [label, value] of [
                ["类型", instance.type],
                ["凭据", instance.credentials_configured ? "已配置" : "未配置"],
                ["来源引用", instance.source_profile_count],
                ["任务 / 下载", `${instance.ingest_task_count} / ${instance.download_job_count}`],
                ["熔断", instance.circuit_state === "closed"
                        ? "关闭"
                        : instance.circuit_state === "open"
                            ? `开启 · 失败 ${instance.circuit_failure_count} 次`
                            : instance.circuit_state === "half_open"
                                ? "等待半开探测"
                                : "未运行"],
            ]) {
                const group = document.createElement("div");
                const term = document.createElement("dt");
                term.textContent = String(label);
                const detail = document.createElement("dd");
                detail.textContent = String(value);
                group.append(term, detail);
                facts.append(group);
            }
            const endpoint = document.createElement("p");
            endpoint.className = "downloader-path";
            endpoint.textContent = `${instance.base_url} · ${instance.download_path}${instance.failure_code ? ` · ${instance.failure_code}` : ""}${instance.circuit_retry_at_utc ? ` · 下次尝试 ${new Date(instance.circuit_retry_at_utc).toLocaleString()}` : ""}`;
            const actions = document.createElement("div");
            actions.className = "downloader-actions";
            const edit = button("配置", () => openDownloaderConfig(instance));
            const test = button("测试连接", () => void testDownloader(instance.id, test));
            const probe = button("探测路径", () => void probeDownloaderPath(instance.id, probe));
            test.disabled = !instance.enabled;
            probe.disabled = !instance.enabled;
            actions.append(edit, test, probe);
            card.append(heading, facts, endpoint, actions);
            return card;
        }));
        status.textContent = body.restart_required
            ? `${body.items.length} 个实例 · 私有配置 revision ${body.configuration_revision} 尚未应用，请重启`
            : `${body.items.length} 个 qBittorrent 实例 · 凭据只显示是否配置`;
    }
    catch (error) {
        const failed = document.createElement("p");
        failed.className = "muted empty";
        failed.textContent = `下载器读取失败：${errorMessage(error, "未知错误")}`;
        list.replaceChildren(failed);
        status.textContent = failed.textContent;
    }
}
function activeSource() {
    return sourceProfiles.find((profile) => profile.id === activeSourceId) ?? null;
}
function refreshSourceDownloaderOptions() {
    const ids = [...new Set([
            ...downloaderInstances.filter((instance) => instance.enabled).map((instance) => instance.id),
            ...sourceProfiles.map((profile) => profile.downloader_id),
        ])].sort();
    element("#source-downloader-options").replaceChildren(...ids.map((id) => {
        const option = document.createElement("option");
        option.value = id;
        return option;
    }));
}
function updateSourceWarning() {
    const strategy = element("#source-strategy").value;
    const seeding = element("#source-seeding-time");
    if (strategy === "move")
        seeding.value = "0";
    seeding.disabled = strategy === "move";
    element("#source-warning").textContent = strategy === "move"
        ? "move 会在下载完成后移动源文件，做种分钟固定为 0；修改只影响之后创建的任务。"
        : "做种分钟：-1 无限、0 不做种、正数为上限；历史任务继续使用原 revision 路由快照。";
}
function populateSourceForm(profile) {
    activeSourceId = profile?.id ?? null;
    const id = element("#source-id");
    const adapter = element("#source-adapter");
    id.disabled = profile !== null;
    adapter.disabled = profile !== null;
    id.value = profile?.id ?? "";
    element("#source-name").value = profile?.display_name ?? "";
    adapter.value = profile?.adapter ?? "u2";
    element("#source-downloader").value = profile?.downloader_id ?? "pt";
    element("#source-strategy").value = profile?.file_strategy ?? "link";
    element("#source-category").value = profile?.category ?? "animegonet";
    element("#source-tags").value = profile?.tags.join(", ") ?? "";
    element("#source-seeding-time").value =
        String(profile?.seeding_time_minutes ?? 0);
    element("#source-hosts").value = profile?.allowed_torrent_hosts.join("\n") ?? "";
    element("#source-enabled").checked = profile?.enabled ?? true;
    element("#source-filter-enabled").checked = profile?.rss_filter_enabled ?? false;
    element("#source-priority-enabled").checked = profile?.rss_priority_enabled ?? false;
    const remove = element("#source-delete");
    remove.disabled = profile === null || profile.is_default;
    remove.title = profile?.is_default ? "默认 Mikan 来源不可删除" : "";
    element("#route-preview-run").disabled = profile === null;
    element("#route-preview-result").textContent = profile === null
        ? "请先保存来源，再按持久化 revision 计算路由。"
        : `${profile.id} revision ${profile.revision}，等待预览。`;
    updateSourceWarning();
    renderSourceList();
}
function renderSourceList() {
    const list = element("#source-list");
    if (sourceProfiles.length === 0) {
        const empty = document.createElement("p");
        empty.className = "muted empty";
        empty.textContent = "暂无来源";
        list.replaceChildren(empty);
        return;
    }
    list.replaceChildren(...sourceProfiles.map((profile) => {
        const card = document.createElement("button");
        card.type = "button";
        card.className = `source-card ${profile.id === activeSourceId ? "active" : ""}`;
        const heading = document.createElement("div");
        heading.className = "source-card-heading";
        const name = document.createElement("strong");
        name.textContent = profile.display_name;
        const revision = document.createElement("span");
        revision.textContent = `rev ${profile.revision}${profile.enabled ? "" : " · 已停用"}`;
        heading.append(name, revision);
        const route = document.createElement("p");
        route.textContent = `${profile.adapter} → ${profile.downloader_id} · ${profile.file_strategy} · ${profile.category} · 做种 ${profile.seeding_time_minutes} 分钟 · 任务 ${profile.ingest_task_count} / RSS ${profile.rss_batch_count}`;
        card.append(heading, route);
        card.addEventListener("click", () => populateSourceForm(profile));
        return card;
    }));
}
function optionalPositiveNumber(selector) {
    const input = element(selector);
    return input.value === "" || !Number.isFinite(input.valueAsNumber) ? null : input.valueAsNumber;
}
async function previewSourceRoute() {
    const current = activeSource();
    if (!current)
        return;
    const output = element("#route-preview-result");
    const run = element("#route-preview-run");
    run.disabled = true;
    output.textContent = "正在计算路由…";
    try {
        const requestHeaders = new Headers(headers);
        requestHeaders.set("Content-Type", "application/json");
        const response = await fetch(`/api/v1/sources/${encodeURIComponent(current.id)}/route-preview`, {
            method: "POST",
            headers: requestHeaders,
            body: JSON.stringify({
                title: element("#source-name").value.trim(),
                source_work_id: element("#route-source-work-id").value.trim() || null,
                mikanid: optionalPositiveNumber("#route-mikanid"),
                bgmid: optionalPositiveNumber("#route-bgmid"),
                anidbid: optionalPositiveNumber("#route-anidbid"),
                imdbid: element("#route-imdbid").value.trim() || null,
            }),
        });
        if (!response.ok)
            throw new Error(await responseError(response));
        const route = await response.json();
        output.textContent = route.valid
            ? [
                `有效 · ${route.source_profile_id} rev ${route.source_profile_revision} (${route.adapter})`,
                `下载器 ${route.downloader_id} · ${route.download_path ?? "路径不可用"}`,
                `媒体库 ${route.save_path}`,
                `策略 ${route.file_strategy} · 分类 ${route.category} · Tags ${route.tags.join(", ") || "—"}`,
                `做种 ${route.seeding_time_minutes} 分钟 · RSS规则 rev ${route.rss_rule_revision ?? "—"}`,
            ].join("\n")
            : `无效\n${route.errors.map((error) => `• ${error}`).join("\n")}`;
    }
    catch (error) {
        output.textContent = `预览失败：${errorMessage(error, "未知错误")}`;
    }
    finally {
        run.disabled = false;
    }
}
async function loadSources(selectedId) {
    const status = element("#source-status");
    status.textContent = "正在读取来源配置…";
    try {
        const response = await fetch("/api/v1/sources", { headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        const body = await response.json();
        sourceProfiles = body.items;
        refreshSourceDownloaderOptions();
        refreshManualSourceOptions();
        const selected = sourceProfiles.find((profile) => profile.id === (selectedId ?? activeSourceId))
            ?? sourceProfiles[0]
            ?? null;
        populateSourceForm(selected);
        status.textContent = `${sourceProfiles.length} 个来源 · 修改采用 revision 乐观并发且不改变历史任务路由`;
    }
    catch (error) {
        sourceProfiles = [];
        activeSourceId = null;
        refreshManualSourceOptions();
        renderSourceList();
        status.textContent = `来源读取失败：${errorMessage(error, "未知错误")}`;
    }
}
function setManualSourceOptions(selector, profiles, emptyLabel) {
    const select = element(selector);
    const previous = select.value;
    const options = profiles.map((profile) => {
        const option = document.createElement("option");
        option.value = profile.id;
        option.textContent =
            `${profile.display_name} (${profile.id} → ${profile.downloader_id}, rev ${profile.revision})`;
        return option;
    });
    if (options.length === 0) {
        const option = document.createElement("option");
        option.value = "";
        option.textContent = emptyLabel;
        option.disabled = true;
        option.selected = true;
        options.push(option);
    }
    select.replaceChildren(...options);
    if (profiles.some((profile) => profile.id === previous))
        select.value = previous;
    select.disabled = profiles.length === 0;
}
function refreshManualSourceOptions() {
    const enabled = sourceProfiles.filter((profile) => profile.enabled);
    setManualSourceOptions("#manual-download-source", enabled, "没有已启用的输入源");
    setManualSourceOptions("#manual-rss-source", enabled.filter((profile) => profile.adapter === "mikan"), "没有已启用的 Mikan 输入源");
    element("#manual-rss-submit").disabled =
        enabled.every((profile) => profile.adapter !== "mikan");
    updateManualDownloadHint();
}
function updateManualDownloadHint() {
    const sourceId = element("#manual-download-source").value;
    const profile = sourceProfiles.find((item) => item.id === sourceId);
    const mikanId = element("#manual-download-mikanid");
    const bangumiId = element("#manual-download-bgmid");
    const submit = element("#manual-download-submit");
    mikanId.required = profile?.adapter === "mikan";
    bangumiId.required = profile?.adapter === "mikan";
    submit.disabled = profile === undefined;
    element("#manual-download-hint").textContent = profile === undefined
        ? "请先启用一个输入源。"
        : profile.adapter === "mikan"
            ? `Mikan 手动导入必须提供 mikanid 与 bgmid；将路由到 ${profile.downloader_id}，使用 ${profile.file_strategy}。`
            : `${profile.adapter.toUpperCase()} 的作品级参考 ID 可选；将路由到 ${profile.downloader_id}，使用 ${profile.file_strategy}。`;
}
function manualResultItem(title, detail, rejected) {
    const row = document.createElement("div");
    row.className = `manual-result-item ${rejected ? "rejected" : ""}`;
    const heading = document.createElement("strong");
    heading.textContent = title;
    const description = document.createElement("span");
    description.textContent = detail;
    row.append(heading, description);
    return row;
}
async function submitManualDownload(event) {
    event.preventDefault();
    const sourceId = element("#manual-download-source").value;
    const url = element("#manual-download-url");
    const submit = element("#manual-download-submit");
    const result = element("#manual-download-result");
    let requestBody = "";
    submit.disabled = true;
    result.replaceChildren(manualResultItem("正在提交", "Torrent URL 已从输入框清除。", false));
    try {
        requestBody = JSON.stringify({
            source: sourceId,
            data: [{
                    torrent: url.value,
                    info: {
                        title: element("#manual-download-title").value.trim(),
                        source_item_id: element("#manual-download-item-id").value.trim() || null,
                        source_work_id: element("#manual-download-work-id").value.trim() || null,
                        mikanid: optionalPositiveNumber("#manual-download-mikanid"),
                        bgmid: optionalPositiveNumber("#manual-download-bgmid"),
                        anidbid: optionalPositiveNumber("#manual-download-anidbid"),
                        imdbid: element("#manual-download-imdbid").value.trim() || null,
                    },
                }],
        });
        url.value = "";
        const requestHeaders = new Headers(headers);
        requestHeaders.set("Content-Type", "application/json");
        const responsePromise = fetch("/api/v1/ingest", {
            method: "POST",
            headers: requestHeaders,
            body: requestBody,
        });
        requestBody = "";
        const response = await responsePromise;
        if (!response.ok)
            throw new Error(await responseError(response));
        const body = await response.json();
        const summary = document.createElement("p");
        summary.className = "manual-result-summary";
        summary.textContent =
            `${body.source || sourceId}：接受 ${body.accepted_count}，拒绝 ${body.rejected_count}`;
        result.replaceChildren(summary, ...body.items.map((item) => manualResultItem(item.ingest_id ? `已接收 · ${item.status}` : `已拒绝 · ${item.status}`, item.ingest_id
            ? [
                `任务 ${item.ingest_id}`,
                `来源 ${item.source_profile_id} rev ${item.source_profile_revision}`,
                `下载器 ${item.downloader_id}`,
                `文件 ${item.file_count ?? "—"}`,
                `info hash ${item.info_hash ?? "—"}`,
                `URL 指纹 ${item.torrent_url_fingerprint ?? "—"}`,
            ].join(" · ")
            : item.errors.join("；") || "未提供失败原因", item.ingest_id === null)));
        void loadDownloads();
        void loadMetadataTasks();
        void loadSources(sourceId);
    }
    catch (error) {
        result.replaceChildren(manualResultItem("提交失败", errorMessage(error, "未知错误"), true));
    }
    finally {
        requestBody = "";
        url.value = "";
        updateManualDownloadHint();
    }
}
async function submitManualRss(event) {
    event.preventDefault();
    const sourceId = element("#manual-rss-source").value;
    const url = element("#manual-rss-url");
    const submit = element("#manual-rss-submit");
    const result = element("#manual-rss-result");
    let requestBody = "";
    submit.disabled = true;
    result.replaceChildren(manualResultItem("正在处理", "RSS URL 已从输入框清除。", false));
    try {
        requestBody = JSON.stringify({
            source_profile_id: sourceId,
            url: url.value,
        });
        url.value = "";
        const requestHeaders = new Headers(headers);
        requestHeaders.set("Content-Type", "application/json");
        const responsePromise = fetch("/api/v1/rss/ingest", {
            method: "POST",
            headers: requestHeaders,
            body: requestBody,
        });
        requestBody = "";
        const response = await responsePromise;
        if (!response.ok)
            throw new Error(await responseError(response));
        const body = await response.json();
        const accepted = body.items.filter((item) => item.ingest_task_id !== null).length;
        const summary = document.createElement("p");
        summary.className = "manual-result-summary";
        summary.textContent =
            `批次 ${body.batch_id} · mikanid ${body.mikanid ?? "未识别"} · 接收 ${accepted}/${body.items.length} · 规则 rev ${body.rule_revision}`;
        result.replaceChildren(summary, ...body.items.map((item, index) => manualResultItem(`候选 ${index + 1} · ${item.status}`, [
            item.decision_kind,
            item.decision_reason,
            item.ingest_task_id ? `任务 ${item.ingest_task_id}` : null,
            item.errors.length > 0 ? item.errors.join("；") : null,
        ].filter((value) => value !== null).join(" · "), item.ingest_task_id === null)));
        void loadDownloads();
        void loadMetadataTasks();
        void loadSources(sourceId);
    }
    catch (error) {
        result.replaceChildren(manualResultItem("RSS 处理失败", errorMessage(error, "未知错误"), true));
    }
    finally {
        requestBody = "";
        url.value = "";
        submit.disabled = sourceProfiles.every((profile) => !profile.enabled || profile.adapter !== "mikan");
    }
}
function optionalInteger(selector) {
    const input = element(selector);
    return input.value === "" || !Number.isInteger(input.valueAsNumber)
        ? null
        : input.valueAsNumber;
}
function currentMikanWorkId() {
    const input = element("#mikan-work-rule-id");
    return Number.isInteger(input.valueAsNumber) && input.valueAsNumber > 0
        ? input.valueAsNumber
        : null;
}
function invalidateMikanWorkRule() {
    loadedMikanWorkId = null;
    activeMikanWorkRule = null;
    activeMikanWorkImpact = null;
    element("#mikan-work-rule-save").disabled = true;
    element("#mikan-work-rule-delete").disabled = true;
    element("#mikan-work-rule-rematch").disabled = true;
    element("#mikan-work-rule-status").textContent =
        "mikanid 已改变，请先读取最新规则与影响，避免覆盖现有 revision。";
    element("#mikan-work-impact-summary").replaceChildren(Object.assign(document.createElement("p"), {
        className: "muted",
        textContent: "尚未读取当前 mikanid。",
    }));
    element("#mikan-work-impact-tasks").replaceChildren();
}
function populateMikanWorkRule(rule) {
    element("#mikan-work-rule-bgmid").value =
        rule?.bgmid?.toString() ?? "";
    element("#mikan-work-rule-series").value =
        rule?.tmdb_series_id?.toString() ?? "";
    element("#mikan-work-rule-season").value =
        rule?.tmdb_season_number?.toString() ?? "";
    element("#mikan-work-rule-offset").value =
        rule?.episode_offset?.toString() ?? "";
    // A sample episode is validation-only and is never persisted with the rule.
    element("#mikan-work-rule-sample").value = "";
    element("#mikan-work-rule-enabled").checked = rule?.enabled ?? true;
    element("#mikan-work-rule-save").disabled = false;
    element("#mikan-work-rule-delete").disabled = rule === null;
}
const mikanImpactLabels = {
    future: "尚未匹配，将自动使用当前规则",
    retryable_failed: "失败，可显式重新匹配",
    active: "处理中，保持当前租约",
    resolved_protected: "已解析保护，不自动回溯",
    completed_protected: "已整理保护，不移动文件",
    other: "其他状态，不自动改写",
};
function mikanImpactStat(value, label) {
    const card = document.createElement("div");
    card.className = "mikan-impact-stat";
    const count = document.createElement("strong");
    count.textContent = String(value);
    const description = document.createElement("span");
    description.textContent = label;
    card.append(count, description);
    return card;
}
function renderMikanWorkImpact(impact) {
    activeMikanWorkImpact = impact;
    const summary = element("#mikan-work-impact-summary");
    summary.replaceChildren(mikanImpactStat(impact.total_task_count, "关联任务"), mikanImpactStat(impact.future_task_count, "未来自动应用"), mikanImpactStat(impact.retryable_failed_task_count, "可显式重试"), mikanImpactStat(impact.active_task_count, "活动中保护"), mikanImpactStat(impact.resolved_protected_task_count, "已解析保护"), mikanImpactStat(impact.completed_protected_task_count, "已整理保护"));
    if (impact.other_task_count > 0 || impact.is_truncated) {
        const note = document.createElement("p");
        note.className = "muted";
        note.textContent = [
            impact.other_task_count > 0 ? `另有 ${impact.other_task_count} 个其他状态` : null,
            impact.is_truncated ? `列表只显示前 ${impact.items.length} 个，统计仍为全量` : null,
        ].filter((value) => value !== null).join("；");
        summary.append(note);
    }
    const tasks = element("#mikan-work-impact-tasks");
    if (impact.items.length === 0) {
        const empty = document.createElement("p");
        empty.className = "muted";
        empty.textContent = "该 mikanid 暂无关联任务。";
        tasks.replaceChildren(empty);
    }
    else {
        tasks.replaceChildren(...impact.items.map((item) => {
            const row = document.createElement("article");
            row.className = `mikan-impact-task ${item.category}`;
            const title = document.createElement("strong");
            title.textContent = item.title;
            const identity = document.createElement("span");
            identity.textContent = [
                item.task_id,
                `${item.source} · ${statusLabels[item.status] ?? item.status}`,
                `bgmid ${item.bgmid ?? "—"}`,
                `TMDB ${item.tmdb_series_id ?? "—"} / S${item.tmdb_season_number ?? "—"}`,
            ].join(" · ");
            const category = document.createElement("span");
            category.textContent =
                `${mikanImpactLabels[item.category]} · 更新 ${new Date(item.updated_at_utc).toLocaleString()}`;
            row.append(title, identity, category);
            return row;
        }));
    }
    element("#mikan-work-rule-rematch").disabled =
        impact.retryable_failed_task_count === 0;
}
async function loadMikanWorkRule() {
    const mikanId = currentMikanWorkId();
    const status = element("#mikan-work-rule-status");
    if (mikanId === null) {
        invalidateMikanWorkRule();
        status.textContent = "mikanid 必须是正整数。";
        return;
    }
    const load = element("#mikan-work-rule-load");
    load.disabled = true;
    status.textContent = "正在读取规则 revision 与全量影响统计…";
    try {
        const [ruleResponse, impactResponse] = await Promise.all([
            fetch(`/api/v1/mikan/work-rules/${mikanId}`, { headers }),
            fetch(`/api/v1/mikan/work-rules/${mikanId}/impact?limit=100`, { headers }),
        ]);
        if (!ruleResponse.ok && ruleResponse.status !== 404) {
            throw new Error(await responseError(ruleResponse));
        }
        if (!impactResponse.ok)
            throw new Error(await responseError(impactResponse));
        const rule = ruleResponse.status === 404
            ? null
            : await ruleResponse.json();
        const impact = await impactResponse.json();
        loadedMikanWorkId = mikanId;
        activeMikanWorkRule = rule;
        populateMikanWorkRule(rule);
        renderMikanWorkImpact(impact);
        status.textContent = rule === null
            ? `mikanid ${mikanId} 尚无人工规则；保存时从 revision 0 创建。`
            : `已读取 revision ${rule.revision} · ${rule.enabled ? "人工规则已启用（最高优先级）" : "人工规则已禁用"} · 更新 ${new Date(rule.updated_at_utc).toLocaleString()}`;
    }
    catch (error) {
        invalidateMikanWorkRule();
        status.textContent = `读取失败：${errorMessage(error, "未知错误")}`;
    }
    finally {
        load.disabled = false;
    }
}
async function saveMikanWorkRule(event) {
    event.preventDefault();
    const mikanId = currentMikanWorkId();
    if (mikanId === null || loadedMikanWorkId !== mikanId) {
        invalidateMikanWorkRule();
        return;
    }
    const save = element("#mikan-work-rule-save");
    const status = element("#mikan-work-rule-status");
    save.disabled = true;
    status.textContent = "正在保存；填写样例 EP 时会先在线验证 TMDB…";
    try {
        const requestHeaders = new Headers(headers);
        requestHeaders.set("Content-Type", "application/json");
        const response = await fetch(`/api/v1/mikan/work-rules/${mikanId}`, {
            method: "PUT",
            headers: requestHeaders,
            body: JSON.stringify({
                bgmid: optionalPositiveNumber("#mikan-work-rule-bgmid"),
                tmdb_series_id: optionalPositiveNumber("#mikan-work-rule-series"),
                tmdb_season_number: optionalPositiveNumber("#mikan-work-rule-season"),
                episode_offset: optionalInteger("#mikan-work-rule-offset"),
                sample_source_episode: optionalPositiveNumber("#mikan-work-rule-sample"),
                enabled: element("#mikan-work-rule-enabled").checked,
                expected_revision: activeMikanWorkRule?.revision ?? 0,
            }),
        });
        if (!response.ok)
            throw new Error(await responseError(response));
        const saved = await response.json();
        activeMikanWorkRule = saved;
        await loadMikanWorkRule();
        status.textContent =
            `已保存 revision ${saved.revision}；规则只影响之后的匹配，已解析/已整理任务未改写。`;
    }
    catch (error) {
        status.textContent =
            `保存失败：${errorMessage(error, "未知错误")}；revision 冲突时请重新读取。`;
        save.disabled = false;
    }
}
async function deleteMikanWorkRule() {
    const mikanId = currentMikanWorkId();
    const rule = activeMikanWorkRule;
    if (mikanId === null || loadedMikanWorkId !== mikanId || rule === null)
        return;
    if (!window.confirm(`清除 mikanid ${mikanId} 的人工规则？已完成记录和媒体文件不会删除或移动。`))
        return;
    const status = element("#mikan-work-rule-status");
    status.textContent = "正在清除人工规则…";
    try {
        const response = await fetch(`/api/v1/mikan/work-rules/${mikanId}?expected_revision=${rule.revision}`, { method: "DELETE", headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        activeMikanWorkRule = null;
        await loadMikanWorkRule();
        status.textContent =
            "人工规则已清除；之后任务恢复自动匹配，既有解析和媒体文件保持不变。";
    }
    catch (error) {
        status.textContent =
            `清除失败：${errorMessage(error, "未知错误")}；revision 冲突时请重新读取。`;
    }
}
async function rematchMikanWorkTasks() {
    const mikanId = currentMikanWorkId();
    const impact = activeMikanWorkImpact;
    if (mikanId === null || loadedMikanWorkId !== mikanId || impact === null)
        return;
    if (impact.retryable_failed_task_count === 0)
        return;
    if (!window.confirm(`重新匹配 ${impact.retryable_failed_task_count} 个失败任务？`
        + ` ${impact.resolved_protected_task_count} 个已解析和`
        + ` ${impact.completed_protected_task_count} 个已整理任务保持不变，媒体文件不会移动。`))
        return;
    const button = element("#mikan-work-rule-rematch");
    const status = element("#mikan-work-rule-status");
    button.disabled = true;
    status.textContent = "正在按当前规则 revision 重新排队失败任务…";
    try {
        const requestHeaders = new Headers(headers);
        requestHeaders.set("Content-Type", "application/json");
        const response = await fetch(`/api/v1/mikan/work-rules/${mikanId}/rematch`, {
            method: "POST",
            headers: requestHeaders,
            body: JSON.stringify({
                expected_rule_revision: activeMikanWorkRule?.revision ?? 0,
            }),
        });
        if (!response.ok)
            throw new Error(await responseError(response));
        const result = await response.json();
        await loadMikanWorkRule();
        status.textContent =
            `已重新排队 ${result.retried_task_count} 个失败任务；已解析/已整理任务与媒体文件未改写。`;
        void loadMetadataTasks();
    }
    catch (error) {
        status.textContent =
            `重新匹配失败：${errorMessage(error, "未知错误")}；请重新读取规则与影响。`;
        button.disabled = false;
    }
}
function sourceHosts() {
    return element("#source-hosts").value
        .split(/[\r\n,，]+/u)
        .map((host) => host.trim().toLowerCase())
        .filter(Boolean);
}
function sourceTags() {
    return element("#source-tags").value
        .split(/[\r\n,，]+/u)
        .map((tag) => tag.trim())
        .filter(Boolean);
}
async function saveSource(event) {
    event.preventDefault();
    const current = activeSource();
    const save = element("#source-save");
    const status = element("#source-status");
    const common = {
        display_name: element("#source-name").value.trim(),
        downloader_id: element("#source-downloader").value.trim(),
        file_strategy: element("#source-strategy").value,
        category: element("#source-category").value.trim(),
        tags: sourceTags(),
        seeding_time_minutes: element("#source-seeding-time").valueAsNumber,
        allowed_torrent_hosts: sourceHosts(),
        rss_filter_enabled: element("#source-filter-enabled").checked,
        rss_priority_enabled: element("#source-priority-enabled").checked,
        enabled: element("#source-enabled").checked,
    };
    const payload = current
        ? { ...common, expected_revision: current.revision }
        : {
            ...common,
            id: element("#source-id").value,
            adapter: element("#source-adapter").value,
        };
    save.disabled = true;
    status.textContent = current ? "正在保存来源…" : "正在创建来源…";
    try {
        const requestHeaders = new Headers(headers);
        requestHeaders.set("Content-Type", "application/json");
        const response = await fetch(current ? `/api/v1/sources/${encodeURIComponent(current.id)}` : "/api/v1/sources", {
            method: current ? "PUT" : "POST",
            headers: requestHeaders,
            body: JSON.stringify(payload),
        });
        if (!response.ok)
            throw new Error(await responseError(response));
        const saved = await response.json();
        await loadSources(saved.id);
        status.textContent = `已保存 ${saved.display_name} · revision ${saved.revision}`;
    }
    catch (error) {
        status.textContent = `保存失败：${errorMessage(error, "未知错误")}；revision 冲突时请重新选择来源。`;
    }
    finally {
        save.disabled = false;
    }
}
async function deleteSource() {
    const current = activeSource();
    if (!current || current.is_default)
        return;
    if (!window.confirm(`删除来源 ${current.display_name}？已有任务或 RSS batch 引用时服务端会拒绝。`))
        return;
    const status = element("#source-status");
    status.textContent = "正在删除来源…";
    try {
        const response = await fetch(`/api/v1/sources/${encodeURIComponent(current.id)}?expected_revision=${current.revision}`, { method: "DELETE", headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        activeSourceId = null;
        await loadSources();
        status.textContent = `来源 ${current.id} 已删除`;
    }
    catch (error) {
        status.textContent = `删除失败：${errorMessage(error, "未知错误")}`;
    }
}
function moveItem(items, index, delta) {
    const target = index + delta;
    if (target < 0 || target >= items.length)
        return;
    [items[index], items[target]] = [items[target], items[index]];
}
function nextRuleId(prefix) {
    ruleIdSequence += 1;
    return `${prefix}-${Date.now().toString(36)}-${ruleIdSequence.toString(36)}`;
}
function button(label, action) {
    const result = document.createElement("button");
    result.type = "button";
    result.textContent = label;
    result.addEventListener("click", action);
    return result;
}
function renderArrayEditor(rule, index, count, onMove, onRemove) {
    const card = document.createElement("article");
    card.className = "rss-array";
    const fields = document.createElement("div");
    fields.className = "rss-array-fields";
    const idLabel = document.createElement("label");
    idLabel.textContent = "ID";
    const id = document.createElement("input");
    id.value = rule.id;
    id.addEventListener("input", () => { rule.id = id.value; });
    idLabel.append(id);
    const nameLabel = document.createElement("label");
    nameLabel.textContent = "名称";
    const name = document.createElement("input");
    name.value = rule.name;
    name.addEventListener("input", () => { rule.name = name.value; });
    nameLabel.append(name);
    const valuesLabel = document.createElement("label");
    valuesLabel.textContent = "匹配值（逗号分隔）";
    const values = document.createElement("input");
    values.value = rule.values.join(", ");
    values.addEventListener("input", () => {
        rule.values = values.value.split(/[,，\n]/u).map((value) => value.trim()).filter(Boolean);
    });
    valuesLabel.append(values);
    const enabledLabel = document.createElement("label");
    enabledLabel.className = "rss-array-enabled";
    const enabled = document.createElement("input");
    enabled.type = "checkbox";
    enabled.checked = rule.enabled;
    enabled.addEventListener("change", () => { rule.enabled = enabled.checked; });
    enabledLabel.append(enabled, "启用");
    fields.append(idLabel, nameLabel, valuesLabel, enabledLabel);
    const actions = document.createElement("div");
    actions.className = "rss-array-actions";
    const up = button("上移", () => onMove(-1));
    up.disabled = index === 0;
    const down = button("下移", () => onMove(1));
    down.disabled = index + 1 === count;
    actions.append(up, down, button("删除", onRemove));
    card.append(fields, actions);
    return card;
}
function renderArrayList(container, rules) {
    container.replaceChildren(...rules.map((rule, index) => renderArrayEditor(rule, index, rules.length, (delta) => { moveItem(rules, index, delta); renderRssRules(); }, () => { rules.splice(index, 1); renderRssRules(); })));
}
function renderRssRules() {
    if (!activeRssRules)
        return;
    element("#rss-rule-status").textContent =
        `revision ${activeRssRules.revision} · 旧过滤 ${activeRssRules.rss_filter_enabled ? "开启" : "关闭"} · 批次优选 ${activeRssRules.rss_priority_enabled ? "开启" : "关闭"}`;
    renderArrayList(element("#rss-whitelist"), activeRssRules.whitelist);
    renderArrayList(element("#rss-blacklist"), activeRssRules.blacklist);
    const groupContainer = element("#rss-priority-groups");
    groupContainer.replaceChildren(...activeRssRules.priority_groups.map((group, groupIndex) => {
        const card = document.createElement("article");
        card.className = "rss-group";
        const heading = document.createElement("div");
        heading.className = "rss-group-heading";
        const idLabel = document.createElement("label");
        idLabel.textContent = "组 ID";
        const id = document.createElement("input");
        id.value = group.id;
        id.addEventListener("input", () => { group.id = id.value; });
        idLabel.append(id);
        const nameLabel = document.createElement("label");
        nameLabel.textContent = "组名称";
        const name = document.createElement("input");
        name.value = group.name;
        name.addEventListener("input", () => { group.name = name.value; });
        nameLabel.append(name);
        const groupActions = document.createElement("div");
        groupActions.className = "rss-group-actions";
        const up = button("上移组", () => { moveItem(activeRssRules.priority_groups, groupIndex, -1); renderRssRules(); });
        up.disabled = groupIndex === 0;
        const down = button("下移组", () => { moveItem(activeRssRules.priority_groups, groupIndex, 1); renderRssRules(); });
        down.disabled = groupIndex + 1 === activeRssRules.priority_groups.length;
        groupActions.append(up, down, button("删除组", () => {
            activeRssRules.priority_groups.splice(groupIndex, 1);
            renderRssRules();
        }));
        heading.append(idLabel, nameLabel, groupActions);
        const arrays = document.createElement("div");
        arrays.className = "rss-array-list";
        renderArrayList(arrays, group.arrays);
        const add = button("添加组内数组", () => {
            group.arrays.push({ id: nextRuleId("array"), name: "新数组", enabled: true, values: [] });
            renderRssRules();
        });
        card.append(heading, arrays, add);
        return card;
    }));
}
async function loadRssRules() {
    const status = element("#rss-rule-status");
    status.textContent = "正在读取 Mikan 规则…";
    try {
        const response = await fetch("/api/v1/rss-rules/mikan", { headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        activeRssRules = await response.json();
        renderRssRules();
    }
    catch (error) {
        activeRssRules = null;
        status.textContent = `规则读取失败：${errorMessage(error, "未知错误")}`;
    }
}
async function saveRssRules() {
    if (!activeRssRules)
        return;
    const save = element("#rss-save");
    const status = element("#rss-rule-status");
    save.disabled = true;
    status.textContent = "正在保存规则…";
    try {
        const requestHeaders = new Headers(headers);
        requestHeaders.set("Content-Type", "application/json");
        const response = await fetch("/api/v1/rss-rules/mikan", {
            method: "PUT",
            headers: requestHeaders,
            body: JSON.stringify({
                expected_revision: activeRssRules.revision,
                whitelist: activeRssRules.whitelist,
                blacklist: activeRssRules.blacklist,
                priority_groups: activeRssRules.priority_groups,
            }),
        });
        if (!response.ok)
            throw new Error(await responseError(response));
        activeRssRules = await response.json();
        renderRssRules();
        status.textContent = `保存成功 · revision ${activeRssRules.revision}`;
    }
    catch (error) {
        status.textContent = `保存失败：${errorMessage(error, "未知错误")}；如有 revision 冲突请重新载入。`;
    }
    finally {
        save.disabled = false;
    }
}
async function previewRssRules() {
    const results = element("#rss-preview-results");
    const titles = element("#rss-preview-titles").value
        .split(/\r?\n/u).map((title) => title.trim()).filter(Boolean);
    if (titles.length === 0) {
        results.textContent = "请先输入至少一个候选标题。";
        return;
    }
    const mikanIdValue = element("#rss-preview-mikanid").valueAsNumber;
    const kind = element("#rss-preview-kind").value.trim();
    const episode = element("#rss-preview-episode").value.trim();
    results.textContent = "正在执行服务端预览…";
    try {
        const requestHeaders = new Headers(headers);
        requestHeaders.set("Content-Type", "application/json");
        const response = await fetch("/api/v1/rss-rules/mikan/preview", {
            method: "POST",
            headers: requestHeaders,
            body: JSON.stringify({ candidates: titles.map((title, index) => ({
                    id: `candidate-${index + 1}`,
                    title,
                    mikanid: Number.isFinite(mikanIdValue) ? mikanIdValue : null,
                    source_episode_kind: kind || null,
                    source_episode: episode || null,
                })) }),
        });
        if (!response.ok)
            throw new Error(await responseError(response));
        const body = await response.json();
        results.replaceChildren(...body.decisions.map((decision, index) => {
            const row = document.createElement("div");
            row.className = `rss-decision ${decision.decision === "winner" ? "winner" : decision.decision.startsWith("rejected") ? "rejected" : "suppressed"}`;
            const groups = decision.evaluated_priority_groups.length > 0
                ? ` · groups ${decision.evaluated_priority_groups.join(" → ")}` : "";
            row.textContent = `${titles[index]} · ${decision.decision} · ${decision.reason}${decision.winner_id ? ` · winner ${decision.winner_id}` : ""}${groups}`;
            return row;
        }));
    }
    catch (error) {
        results.textContent = `预览失败：${errorMessage(error, "未知错误")}`;
    }
}
element("#rss-reload").addEventListener("click", () => void loadRssRules());
element("#library-sort").value = libraryState.sort;
element("#library-direction").value = libraryState.direction;
element("#library-page-size").value = String(libraryState.page_size);
element("#library-episode-filter").value = libraryState.episode_filter;
element("#library-reload").addEventListener("click", () => void loadLibrary());
element("#library-sort").addEventListener("change", changeLibraryOrdering);
element("#library-direction").addEventListener("change", changeLibraryOrdering);
element("#library-page-size").addEventListener("change", changeLibraryOrdering);
element("#library-previous").addEventListener("click", () => {
    if (libraryState.page <= 1)
        return;
    libraryState.page--;
    closeLibraryDetail();
    saveLibraryState();
    void loadLibrary();
});
element("#library-next").addEventListener("click", () => {
    libraryState.page++;
    closeLibraryDetail();
    saveLibraryState();
    void loadLibrary();
});
element("#library-detail-close").addEventListener("click", () => {
    const activeCard = document.querySelector(".library-card.active");
    closeLibraryDetail();
    activeCard?.focus();
});
element("#library-episode-filter").addEventListener("change", () => {
    libraryState.episode_filter = element("#library-episode-filter")
        .value;
    saveLibraryState();
    if (activeLibraryDetail)
        renderLibraryEpisodes(activeLibraryDetail);
});
element("#trusted-offsets-reload").addEventListener("click", () => void loadTrustedOffsets());
element("#pending-tmdb-reload").addEventListener("click", () => void loadPendingTmdb(true));
element("#configuration-reload").addEventListener("click", () => void loadConfiguration());
element("#configuration-edit").addEventListener("click", openConfigurationEditor);
element("#configuration-reset").addEventListener("click", () => void resetConfiguration());
element("#configuration-close").addEventListener("click", () => configurationDialog.close());
element("#configuration-form").addEventListener("submit", (event) => void saveConfiguration(event));
element("#configuration-tmdb-key-clear").addEventListener("change", syncConfigurationSecretInputs);
element("#configuration-tmdb-token-clear").addEventListener("change", syncConfigurationSecretInputs);
element("#rss-save").addEventListener("click", () => void saveRssRules());
element("#rss-add-whitelist").addEventListener("click", () => {
    activeRssRules?.whitelist.push({ id: nextRuleId("whitelist"), name: "新白名单", enabled: true, values: [] });
    renderRssRules();
});
element("#rss-add-blacklist").addEventListener("click", () => {
    activeRssRules?.blacklist.push({ id: nextRuleId("blacklist"), name: "新黑名单", enabled: true, values: [] });
    renderRssRules();
});
element("#rss-add-group").addEventListener("click", () => {
    activeRssRules?.priority_groups.push({ id: nextRuleId("group"), name: "新优先级组", arrays: [] });
    renderRssRules();
});
element("#rss-preview-run").addEventListener("click", () => void previewRssRules());
element("#source-new").addEventListener("click", () => populateSourceForm(null));
element("#source-form").addEventListener("submit", (event) => void saveSource(event));
element("#source-delete").addEventListener("click", () => void deleteSource());
element("#source-strategy").addEventListener("change", updateSourceWarning);
element("#route-preview-run").addEventListener("click", () => void previewSourceRoute());
element("#manual-download-source").addEventListener("change", updateManualDownloadHint);
element("#manual-download-form").addEventListener("submit", (event) => void submitManualDownload(event));
element("#manual-rss-form").addEventListener("submit", (event) => void submitManualRss(event));
element("#mikan-work-rule-id").addEventListener("input", invalidateMikanWorkRule);
element("#mikan-work-rule-load").addEventListener("click", () => void loadMikanWorkRule());
element("#mikan-work-rule-form").addEventListener("submit", (event) => void saveMikanWorkRule(event));
element("#mikan-work-rule-delete").addEventListener("click", () => void deleteMikanWorkRule());
element("#mikan-work-rule-rematch").addEventListener("click", () => void rematchMikanWorkTasks());
element("#downloader-reload").addEventListener("click", () => void loadDownloaders());
element("#downloader-new").addEventListener("click", () => openDownloaderConfig(null));
element("#downloader-config-close").addEventListener("click", () => downloaderConfigDialog.close());
element("#downloader-config-form").addEventListener("submit", (event) => void saveDownloaderConfig(event));
element("#downloader-config-delete").addEventListener("click", () => void deleteDownloaderOverride());
void loadStatus();
void loadLibrary();
void loadConfiguration();
void loadDownloads();
void loadMetadataTasks();
void loadPendingTmdb();
void loadDownloaders();
void loadSources();
void loadRssRules();
void loadTrustedOffsets();
window.setInterval(() => void loadDownloads(), 5000);
window.setInterval(() => void loadMetadataTasks(), 5000);
window.setInterval(() => void loadPendingTmdb(), 10000);
window.setInterval(() => {
    if (!document.hidden && activeLibraryDetail === null)
        void loadLibrary();
}, 15000);
