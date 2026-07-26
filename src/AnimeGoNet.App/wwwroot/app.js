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
let activeDeletePreview = null;
let activeRssRules = null;
let sourceProfiles = [];
let activeSourceId = null;
let downloaderInstances = [];
let downloaderConfigurationRevision = 0;
let activeDownloaderId = null;
let ruleIdSequence = 0;
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
            if (item.status === "metadata_failed") {
                const retry = document.createElement("button");
                retry.type = "button";
                retry.className = "retry-button";
                retry.textContent = "显式重新匹配";
                retry.addEventListener("click", () => void retryMetadataTask(item.task_id, retry));
                card.append(retry);
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
function updateSourceWarning() {
    const strategy = element("#source-strategy").value;
    element("#source-warning").textContent = strategy === "move"
        ? "move 会在下载完成后移动源文件，无法继续做种；修改只影响之后创建的任务。"
        : "修改只影响之后创建的任务；历史任务继续使用原 revision 路由快照。";
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
        route.textContent = `${profile.adapter} → ${profile.downloader_id} · ${profile.file_strategy} · 任务 ${profile.ingest_task_count} / RSS ${profile.rss_batch_count}`;
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
                `策略 ${route.file_strategy} · RSS规则 rev ${route.rss_rule_revision ?? "—"}`,
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
        const downloaders = [...new Set(sourceProfiles.map((profile) => profile.downloader_id))].sort();
        element("#source-downloader-options").replaceChildren(...downloaders.map((downloader) => {
            const option = document.createElement("option");
            option.value = downloader;
            return option;
        }));
        const selected = sourceProfiles.find((profile) => profile.id === (selectedId ?? activeSourceId))
            ?? sourceProfiles[0]
            ?? null;
        populateSourceForm(selected);
        status.textContent = `${sourceProfiles.length} 个来源 · 修改采用 revision 乐观并发且不改变历史任务路由`;
    }
    catch (error) {
        sourceProfiles = [];
        activeSourceId = null;
        renderSourceList();
        status.textContent = `来源读取失败：${errorMessage(error, "未知错误")}`;
    }
}
function sourceHosts() {
    return element("#source-hosts").value
        .split(/[\r\n,，]+/u)
        .map((host) => host.trim().toLowerCase())
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
element("#downloader-reload").addEventListener("click", () => void loadDownloaders());
element("#downloader-new").addEventListener("click", () => openDownloaderConfig(null));
element("#downloader-config-close").addEventListener("click", () => downloaderConfigDialog.close());
element("#downloader-config-form").addEventListener("submit", (event) => void saveDownloaderConfig(event));
element("#downloader-config-delete").addEventListener("click", () => void deleteDownloaderOverride());
void loadStatus();
void loadDownloads();
void loadMetadataTasks();
void loadDownloaders();
void loadSources();
void loadRssRules();
window.setInterval(() => void loadDownloads(), 5000);
window.setInterval(() => void loadMetadataTasks(), 5000);
