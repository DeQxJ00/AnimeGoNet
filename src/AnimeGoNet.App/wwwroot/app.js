import { ApiClient } from "./api-client.js";
import { renderRegionContent, renderRegionMessage, setRegionState, } from "./ui-state.js";
import { filterLiveLogEntries, parseLiveLogEntry, } from "./log-view.js";
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
const api = new ApiClient(accessKey);
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
let activeLegacyMikanFilter = null;
let sourceProfiles = [];
let activeSourceId = null;
let externalSourceAdapters = [];
let downloaderInstances = [];
let downloaderConfigurationRevision = 0;
let activeDownloaderId = null;
let ruleIdSequence = 0;
const libraryStorageKey = "animegonet.library.v1";
let libraryState = readLibraryState();
const downloadStorageKey = "animegonet.downloads.v1";
let downloadState = readDownloadState();
const metadataStorageKey = "animegonet.metadata-tasks.v1";
let metadataState = readMetadataState();
const expandedDownloadJobIds = new Set();
let activeLibraryDetail = null;
let libraryListRequestSequence = 0;
let libraryDetailRequestSequence = 0;
let activeMikanWorkRule = null;
let loadedMikanWorkId = null;
let activeMikanWorkImpact = null;
let activeConfigurationLockedFields = new Set();
let pendingConfigurationRequest = null;
let cacheDatabase = "bolt";
let cacheBuckets = [];
let activeCacheBucketId = null;
let cachePage = 1;
let cacheTotalCount = 0;
let cacheReadOnly = false;
let cacheRequestSequence = 0;
const cachePageSize = 25;
const maximumRenderedLogs = 500;
let liveLogSocket = null;
let liveLogReconnectTimer = null;
let liveLogReconnectAttempt = 0;
let liveLogShouldReconnect = true;
let liveLogPaused = false;
let liveLogControlPending = false;
let liveLogEntries = [];
let aiTestDefaultPrompt = null;
const aiTestPromptDraftKey = "animegonet.ai-test-prompt.v1";
const workspaceDefinitions = {
    overview: {
        title: "总览",
        description: "运行状态、模块能力和常用管理入口。",
        defaultSubview: "status",
        tabs: [
            { id: "status", label: "运行状态" },
            { id: "shortcuts", label: "管理入口" },
        ],
    },
    library: {
        title: "动画库",
        description: "以 TMDB 为准查看作品、季度、EP 进度和待补全项目。",
        defaultSubview: "seasons",
        tabs: [
            { id: "seasons", label: "作品与季度" },
            { id: "pending", label: "待补全 TMDB" },
        ],
    },
    tasks: {
        title: "任务中心",
        description: "查看下载、匹配、整理、失败原因和实时诊断。",
        defaultSubview: "downloads",
        tabs: [
            { id: "downloads", label: "下载任务" },
            { id: "metadata", label: "匹配与整理" },
            { id: "logs", label: "详细日志" },
        ],
    },
    mikan: {
        title: "Mikan 手动设置",
        description: "统一导入、人工覆盖、候选优选和五级过滤。",
        defaultSubview: "ingest",
        tabs: [
            { id: "ingest", label: "导入任务" },
            { id: "manual-rules", label: "人工规则" },
            { id: "offsets", label: "可信 Offset" },
            { id: "candidate-rules", label: "候选规则" },
            { id: "legacy-filter", label: "五级过滤" },
        ],
    },
    connections: {
        title: "连接与配置",
        description: "管理应用上游、输入源、下载器和外部插件。",
        defaultSubview: "application",
        tabs: [
            { id: "application", label: "应用配置" },
            { id: "sources", label: "输入源" },
            { id: "downloaders", label: "下载器" },
            { id: "plugins", label: "外部插件" },
        ],
    },
    tools: {
        title: "测试工具",
        description: "以只读方式验证生产 Prompt、AI 工具调用与 TMDB 最终校验。",
        defaultSubview: "ai-metadata",
        tabs: [
            { id: "ai-metadata", label: "AI 元数据测试" },
        ],
    },
    system: {
        title: "系统",
        description: "维护数据版本、缓存和后台基础设施。",
        defaultSubview: "updates",
        tabs: [
            { id: "updates", label: "数据更新" },
            { id: "cache", label: "缓存管理" },
        ],
    },
};
function isWorkspaceId(value) {
    return Object.hasOwn(workspaceDefinitions, value);
}
function workspaceFromHash() {
    const [rawWorkspace = "", rawSubview = ""] = window.location.hash
        .replace(/^#\/?/, "")
        .split("/", 2);
    const workspace = isWorkspaceId(rawWorkspace) ? rawWorkspace : "overview";
    const definition = workspaceDefinitions[workspace];
    const subview = definition.tabs.some(tab => tab.id === rawSubview)
        ? rawSubview
        : definition.defaultSubview;
    return { workspace, subview };
}
function closeMobileSidebar() {
    const sidebar = element("#app-sidebar");
    const toggle = element("#sidebar-toggle");
    sidebar.classList.remove("open");
    toggle.setAttribute("aria-expanded", "false");
    toggle.setAttribute("aria-label", "打开菜单");
}
function selectWorkspace(workspace, subview, updateHash = true) {
    const definition = workspaceDefinitions[workspace];
    const selectedSubview = definition.tabs.some(tab => tab.id === subview)
        ? subview
        : definition.defaultSubview;
    document.querySelectorAll("#main-content > section[data-workspace]")
        .forEach(section => {
        section.hidden = section.dataset.workspace !== workspace
            || section.dataset.subview !== selectedSubview;
    });
    document.querySelectorAll("[data-workspace-target]")
        .forEach(button => {
        const selected = button.dataset.workspaceTarget === workspace;
        if (selected)
            button.setAttribute("aria-current", "page");
        else
            button.removeAttribute("aria-current");
    });
    element("#workspace-title").textContent = definition.title;
    element("#workspace-description").textContent = definition.description;
    const tabs = element("#workspace-tabs");
    tabs.replaceChildren(...definition.tabs.map(tab => {
        const button = document.createElement("button");
        button.type = "button";
        button.textContent = tab.label;
        button.dataset.subviewTarget = tab.id;
        if (tab.id === selectedSubview)
            button.setAttribute("aria-current", "page");
        button.addEventListener("click", () => selectWorkspace(workspace, tab.id));
        return button;
    }));
    document.title = `${definition.title} · AnimeGoNet`;
    if (updateHash) {
        const nextHash = `#/${workspace}/${selectedSubview}`;
        if (window.location.hash !== nextHash)
            history.pushState(null, "", nextHash);
    }
    closeMobileSidebar();
    window.scrollTo({ top: 0, behavior: "auto" });
}
function initializeWorkspaceNavigation() {
    document.querySelectorAll("[data-workspace-target]")
        .forEach(button => button.addEventListener("click", () => {
        const requested = button.dataset.workspaceTarget ?? "";
        if (isWorkspaceId(requested)) {
            selectWorkspace(requested, workspaceDefinitions[requested].defaultSubview);
        }
    }));
    element("#sidebar-toggle").addEventListener("click", () => {
        const sidebar = element("#app-sidebar");
        const toggle = element("#sidebar-toggle");
        const open = sidebar.classList.toggle("open");
        toggle.setAttribute("aria-expanded", open ? "true" : "false");
        toggle.setAttribute("aria-label", open ? "关闭菜单" : "打开菜单");
    });
    window.addEventListener("hashchange", () => {
        const target = workspaceFromHash();
        selectWorkspace(target.workspace, target.subview, false);
    });
    const initial = workspaceFromHash();
    selectWorkspace(initial.workspace, initial.subview, true);
}
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
    already_completed: "同一来源集已完成，已跳过",
};
const rssStatusLabels = {
    staged: "已暂存",
    blocked: "规则未选中",
    already_ingested: "批次已导入",
    already_claimed: "正在由另一请求处理",
    already_completed: "同一 mikanid 与来源 EP 已完成，已跳过",
    bgmid_discovery_failed: "Bangumi Subject 获取失败",
    rejected: "导入被拒绝",
};
const deleteGroups = [
    { flag: "delete_business_record", label: "业务完成记录", collection: "business_records", help: "删除后该 TMDB 单集可重新导入" },
    { flag: "delete_downloader_task", label: "qBittorrent 任务", collection: "downloader_tasks", help: "只删除任务，永不让 qB 删除文件" },
    { flag: "delete_source_files", label: "下载源文件", collection: "source_files", help: "精确删除捕获下载根目录内的文件" },
    { flag: "delete_media_files", label: "媒体库文件", collection: "media_files", help: "精确删除捕获媒体库根目录内的文件" },
];
const externalPluginStateLabels = {
    stopped: "未启动",
    starting: "正在启动",
    ready: "运行中",
    backoff: "故障退避",
    auto_disabled: "已自动禁用",
    unknown: "未知状态",
};
function externalPluginPointer(propertyName) {
    return `/${propertyName.replaceAll("~", "~0").replaceAll("/", "~1")}`;
}
function createExternalPluginVarField(propertyName, schema, value, required, configuredWriteOnlyPaths) {
    const field = document.createElement("label");
    field.className = "external-plugin-field";
    const label = document.createElement("span");
    label.textContent = `${schema.title ?? propertyName}${required ? " *" : ""}`;
    field.append(label);
    const pointer = externalPluginPointer(propertyName);
    let control;
    if (schema.writeOnly && (schema.type === "string" || schema.type === undefined)) {
        const input = document.createElement("input");
        input.type = "password";
        input.autocomplete = "off";
        input.value = "";
        input.dataset.pluginVarKind = "write-only";
        if (configuredWriteOnlyPaths.has(pointer)) {
            input.placeholder = "已配置；留空保留";
        }
        control = input;
    }
    else if (schema.writeOnly) {
        const textarea = document.createElement("textarea");
        textarea.rows = 4;
        textarea.value = "";
        textarea.placeholder = configuredWriteOnlyPaths.has(pointer)
            ? "已配置；留空保留"
            : "输入 JSON 值";
        textarea.dataset.pluginVarKind = "write-only-json";
        control = textarea;
    }
    else if (schema.type === "boolean") {
        const input = document.createElement("input");
        input.type = "checkbox";
        input.checked = typeof value === "boolean"
            ? value
            : schema.default === true;
        input.dataset.pluginVarKind = "boolean";
        control = input;
    }
    else if (schema.type === "integer" || schema.type === "number") {
        const input = document.createElement("input");
        input.type = "number";
        input.step = schema.type === "integer" ? "1" : "any";
        input.value = typeof value === "number" ? String(value) : "";
        input.dataset.pluginVarKind = schema.type;
        control = input;
    }
    else if (schema.type === "string" && schema.enum?.every(item => typeof item === "string")) {
        const select = document.createElement("select");
        if (!required) {
            const empty = document.createElement("option");
            empty.value = "";
            empty.textContent = "未设置";
            select.append(empty);
        }
        for (const choice of schema.enum) {
            const option = document.createElement("option");
            option.value = JSON.stringify(choice);
            option.textContent = String(choice);
            option.selected = choice === value;
            select.append(option);
        }
        select.dataset.pluginVarKind = "enum";
        control = select;
    }
    else if (schema.type === "string" || schema.type === undefined) {
        const input = document.createElement("input");
        input.type = "text";
        input.autocomplete = "off";
        input.value = typeof value === "string" ? value : "";
        input.dataset.pluginVarKind = "string";
        control = input;
    }
    else {
        const textarea = document.createElement("textarea");
        textarea.rows = 4;
        textarea.value = value === undefined ? "" : JSON.stringify(value, null, 2);
        textarea.placeholder = schema.type === "array" ? "[]" : "{}";
        textarea.dataset.pluginVarKind = "json";
        control = textarea;
    }
    control.dataset.pluginVar = propertyName;
    control.dataset.pluginVarRequired = required ? "true" : "false";
    field.append(control);
    if (schema.description) {
        const description = document.createElement("small");
        description.className = "muted";
        description.textContent = schema.description;
        field.append(description);
    }
    if (configuredWriteOnlyPaths.has(pointer)) {
        const clear = document.createElement("label");
        clear.className = "external-plugin-clear";
        const checkbox = document.createElement("input");
        checkbox.type = "checkbox";
        checkbox.dataset.clearWriteOnly = pointer;
        const text = document.createElement("span");
        text.textContent = "清除已保存值";
        clear.append(checkbox, text);
        field.append(clear);
    }
    return field;
}
function createExternalPluginNestedSecretClear(path) {
    const clear = document.createElement("label");
    clear.className = "external-plugin-clear";
    const checkbox = document.createElement("input");
    checkbox.type = "checkbox";
    checkbox.dataset.clearWriteOnly = path;
    const text = document.createElement("span");
    text.textContent = `清除已保存值 ${path}`;
    clear.append(checkbox, text);
    return clear;
}
function collectExternalPluginVars(form) {
    const vars = {};
    const controls = Array.from(form.querySelectorAll("[data-plugin-var]"));
    for (const control of controls) {
        const name = control.dataset.pluginVar;
        const kind = control.dataset.pluginVarKind;
        const required = control.dataset.pluginVarRequired === "true";
        if (!name || !kind)
            continue;
        if (kind === "boolean" && control instanceof HTMLInputElement) {
            vars[name] = control.checked;
        }
        else if ((kind === "integer" || kind === "number") && control.value !== "") {
            const parsed = Number(control.value);
            if (!Number.isFinite(parsed) || (kind === "integer" && !Number.isInteger(parsed))) {
                throw new Error(`${name} 必须是${kind === "integer" ? "整数" : "数字"}。`);
            }
            vars[name] = parsed;
        }
        else if (kind === "enum" && control.value !== "") {
            vars[name] = JSON.parse(control.value);
        }
        else if (kind === "json" && control.value.trim() !== "") {
            vars[name] = JSON.parse(control.value);
        }
        else if (kind === "write-only-json" && control.value.trim() !== "") {
            vars[name] = JSON.parse(control.value);
        }
        else if (kind === "write-only") {
            if (control.value !== "")
                vars[name] = control.value;
        }
        else if (kind === "string" && (required || control.value !== "")) {
            vars[name] = control.value;
        }
    }
    const clearWriteOnlyPaths = Array.from(form.querySelectorAll("[data-clear-write-only]")).filter(input => input.checked).map(input => input.dataset.clearWriteOnly);
    return { vars, clearWriteOnlyPaths };
}
function externalPluginConfigurationForm(configuration, configurationRevision) {
    const details = document.createElement("details");
    details.className = "external-plugin-configuration";
    const summary = document.createElement("summary");
    summary.textContent = configuration.configured ? "启停与参数（已保存）" : "启停与参数（默认禁用）";
    const form = document.createElement("form");
    form.className = "external-plugin-form";
    const enableLabel = document.createElement("label");
    enableLabel.className = "checkbox-row";
    const enabled = document.createElement("input");
    enabled.type = "checkbox";
    enabled.checked = configuration.enabled;
    const enabledText = document.createElement("span");
    enabledText.textContent = "启用此插件";
    enableLabel.append(enabled, enabledText);
    const argsLabel = document.createElement("label");
    argsLabel.className = "external-plugin-field";
    const argsTitle = document.createElement("span");
    argsTitle.textContent = "默认 args（JSON 对象）";
    const args = document.createElement("textarea");
    args.rows = 5;
    args.value = JSON.stringify(configuration.args, null, 2);
    const argsHelp = document.createElement("small");
    argsHelp.className = "muted";
    argsHelp.textContent = "实际任务同名字段优先；凭据请放在 schema 标记 writeOnly 的 vars 中。";
    argsLabel.append(argsTitle, args, argsHelp);
    const vars = document.createElement("fieldset");
    vars.className = "external-plugin-vars";
    const legend = document.createElement("legend");
    legend.textContent = "vars / config schema";
    vars.append(legend);
    const configuredSecrets = new Set(configuration.configured_write_only_paths);
    const required = new Set(configuration.schema.required ?? []);
    const directPointers = new Set();
    for (const [name, schema] of Object.entries(configuration.schema.properties ?? {})) {
        directPointers.add(externalPluginPointer(name));
        vars.append(createExternalPluginVarField(name, schema, configuration.vars[name], required.has(name), configuredSecrets));
    }
    for (const path of configuredSecrets) {
        if (!directPointers.has(path)) {
            vars.append(createExternalPluginNestedSecretClear(path));
        }
    }
    if (Object.keys(configuration.schema.properties ?? {}).length === 0) {
        const empty = document.createElement("p");
        empty.className = "muted empty";
        empty.textContent = "该插件没有声明可编辑 vars。";
        vars.append(empty);
    }
    const actions = document.createElement("div");
    actions.className = "external-plugin-actions";
    const save = document.createElement("button");
    save.type = "submit";
    save.textContent = "保存配置";
    actions.append(save);
    if (configuration.configured) {
        const remove = document.createElement("button");
        remove.type = "button";
        remove.className = "danger-button";
        remove.textContent = "恢复默认禁用";
        remove.addEventListener("click", () => void deleteExternalPluginConfiguration(configuration, configurationRevision, remove));
        actions.append(remove);
    }
    const message = document.createElement("small");
    message.className = "external-plugin-form-message muted";
    form.append(enableLabel, argsLabel, vars, actions, message);
    form.addEventListener("submit", event => void saveExternalPluginConfiguration(event, configuration, configurationRevision, enabled, args, message));
    details.append(summary, form);
    return details;
}
async function saveExternalPluginConfiguration(event, configuration, configurationRevision, enabled, argsInput, message) {
    event.preventDefault();
    const form = event.currentTarget;
    const submit = form.querySelector('button[type="submit"]');
    if (submit)
        submit.disabled = true;
    message.textContent = "正在校验并保存…";
    try {
        const args = JSON.parse(argsInput.value);
        if (args === null || Array.isArray(args) || typeof args !== "object") {
            throw new Error("args 必须是 JSON 对象。");
        }
        const collected = collectExternalPluginVars(form);
        const response = await fetch(`/api/v1/plugins/${encodeURIComponent(configuration.id)}/configuration`, {
            method: "PUT",
            headers: new Headers([...headers, ["Content-Type", "application/json"]]),
            body: JSON.stringify({
                expected_revision: configurationRevision,
                enabled: enabled.checked,
                args,
                vars: collected.vars,
                clear_write_only_paths: collected.clearWriteOnlyPaths,
            }),
        });
        if (!response.ok)
            throw new Error(await responseError(response));
        message.textContent = "已保存；运行中的旧会话已停止。";
        await loadStatus();
    }
    catch (error) {
        message.textContent = errorMessage(error, "插件配置保存失败");
        if (submit)
            submit.disabled = false;
    }
}
async function deleteExternalPluginConfiguration(configuration, configurationRevision, button) {
    if (!window.confirm(`恢复 ${configuration.id} 为未配置且默认禁用？已保存 args/vars 将被删除。`))
        return;
    button.disabled = true;
    try {
        const response = await fetch(`/api/v1/plugins/${encodeURIComponent(configuration.id)}/configuration?expected_revision=${configurationRevision}`, { method: "DELETE", headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        await loadStatus();
    }
    catch (error) {
        button.textContent = errorMessage(error, "恢复失败");
        button.disabled = false;
    }
}
function renderExternalPlugins(status, configurations) {
    const target = element("#external-plugin-list");
    const runtimes = new Map(status.runtimes.map(runtime => [runtime.id, runtime]));
    const configurationById = new Map(configurations.items.map(configuration => [configuration.id, configuration]));
    const cards = [];
    for (const plugin of status.packages) {
        const runtime = runtimes.get(plugin.id);
        const card = document.createElement("article");
        card.className = `external-plugin-card ${runtime?.state ?? "unknown"}`;
        const heading = document.createElement("div");
        heading.className = "external-plugin-card-heading";
        const name = document.createElement("strong");
        name.textContent = plugin.name;
        const badge = document.createElement("span");
        badge.className = `badge ${runtime?.state === "ready" ? "ready" : runtime?.state === "auto_disabled" ? "error" : "pending"}`;
        badge.textContent = externalPluginStateLabels[runtime?.state ?? "unknown"];
        heading.append(name, badge);
        const identity = document.createElement("code");
        identity.textContent = plugin.id;
        const metadata = document.createElement("p");
        metadata.className = "muted";
        metadata.textContent = `${plugin.type} · ${plugin.version} · ${plugin.rid} · ${plugin.enabled ? "已启用" : "已禁用"}`;
        card.append(heading, identity, metadata);
        if (plugin.capabilities.length > 0) {
            const capabilities = document.createElement("small");
            capabilities.textContent = `能力：${plugin.capabilities.join("、")}`;
            card.append(capabilities);
        }
        if (runtime && (runtime.consecutive_failures > 0 || runtime.last_failure_code)) {
            const failure = document.createElement("small");
            const retry = runtime.retry_at_utc
                ? `；可重试 ${new Date(runtime.retry_at_utc).toLocaleString()}`
                : "";
            failure.className = "external-plugin-failure";
            failure.textContent = `连续失败 ${runtime.consecutive_failures} 次；${runtime.last_failure_code ?? "未分类"}${retry}`;
            card.append(failure);
            const reset = document.createElement("button");
            reset.type = "button";
            reset.className = "secondary-button";
            reset.textContent = "清除故障状态";
            reset.addEventListener("click", () => void resetExternalPlugin(plugin.id, reset));
            card.append(reset);
        }
        const configuration = configurationById.get(plugin.id);
        if (configuration) {
            card.append(externalPluginConfigurationForm(configuration, configurations.revision));
        }
        else {
            const missing = document.createElement("small");
            missing.className = "external-plugin-failure";
            missing.textContent = "配置模型不可用；请刷新或检查插件 schema。";
            card.append(missing);
        }
        cards.push(card);
    }
    for (const error of status.errors) {
        const card = document.createElement("article");
        card.className = "external-plugin-card invalid";
        const heading = document.createElement("strong");
        heading.textContent = error.package_directory_name;
        const code = document.createElement("code");
        code.textContent = error.code;
        const message = document.createElement("small");
        message.textContent = error.message;
        card.append(heading, code, message);
        cards.push(card);
    }
    if (cards.length === 0) {
        renderRegionMessage(target, "empty", "没有发现外部插件包。内置 C# 插件不在此处重复显示。");
        return;
    }
    renderRegionContent(target, ...cards);
}
async function resetExternalPlugin(pluginId, button) {
    const original = button.textContent ?? "清除故障状态";
    button.disabled = true;
    button.textContent = "正在清除…";
    try {
        const response = await fetch(`/api/v1/plugins/${encodeURIComponent(pluginId)}/reset`, {
            method: "POST",
            headers,
        });
        if (!response.ok)
            throw new Error(await responseError(response));
        await loadStatus();
    }
    catch (error) {
        button.textContent = errorMessage(error, "清除失败");
        button.disabled = false;
        return;
    }
    button.textContent = original;
}
async function loadStatus() {
    const health = element("#health");
    const modulesTarget = element("#modules");
    const pluginsTarget = element("#external-plugin-list");
    setRegionState(modulesTarget, "loading");
    setRegionState(pluginsTarget, "loading");
    try {
        const [status, pluginConfigurations] = await Promise.all([
            api.get("/api/v1/status"),
            api.get("/api/v1/plugins"),
        ]);
        externalSourceAdapters = pluginConfigurations.items.filter((configuration) => configuration.type === "source");
        refreshSourceAdapterOptions();
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
            state.textContent = enabled ? "已启用" : "当前不可用";
            item.append(title, state);
            return item;
        });
        renderRegionContent(modulesTarget, ...modules);
        renderExternalPlugins(status.external_plugins, pluginConfigurations);
        health.textContent = "运行中";
        health.className = "badge ready";
    }
    catch (error) {
        const message = errorMessage(error, "连接失败");
        health.textContent = message;
        health.className = "badge error";
        renderRegionMessage(modulesTarget, "error", `模块状态读取失败：${message}`);
        renderRegionMessage(pluginsTarget, "error", `外部插件状态读取失败：${message}`);
    }
}
async function loadDirectoryDatabase(refresh = false) {
    const target = element("#directory-database-status");
    const button = element("#directory-database-refresh");
    button.disabled = true;
    if (refresh)
        target.textContent = "正在刷新…";
    try {
        const status = await api.request(refresh
            ? "/api/v1/library/directory-database/refresh"
            : "/api/v1/library/directory-database", { method: refresh ? "POST" : "GET" });
        const rejected = status.last_rejected_count > 0
            ? `，拒绝 ${status.last_rejected_count}`
            : "";
        const failure = status.last_failure_code
            ? `，失败 ${status.last_failure_code}`
            : "";
        target.textContent =
            `${status.entry_count} 条索引；最近扫描 ${status.last_scanned_count}，`
                + `写入 ${status.last_indexed_count}${rejected}${failure}；Cron ${status.refresh_cron}`;
    }
    catch (error) {
        target.textContent = errorMessage(error, "目录数据库状态读取失败");
    }
    finally {
        button.disabled = false;
    }
}
let dataUpdateActionRunning = false;
const dataUpdateStatusLabels = {
    checking: "正在检查 manifest",
    update_available: "发现新版本",
    up_to_date: "已是最新版本",
    downloading: "正在下载并校验",
    downloaded: "已下载，等待确认导入",
    importing: "正在校验并导入",
    completed: "更新完成",
    failed: "更新失败",
    rolled_back: "已回滚",
};
function dataUpdateTime(value) {
    if (!value)
        return "—";
    const parsed = new Date(value);
    return Number.isNaN(parsed.valueOf()) ? value : parsed.toLocaleString();
}
function setDataUpdateBusy(busy) {
    dataUpdateActionRunning = busy;
    for (const button of document.querySelectorAll("#data-update button")) {
        if (busy) {
            button.disabled = true;
        }
        else if (button.id === "data-update-reload") {
            button.disabled = false;
        }
        else if (button.id === "data-update-offline-import") {
            const input = element("#data-update-offline-package");
            button.disabled = input.files?.length !== 1;
        }
    }
    element("#data-update-offline-package").disabled = busy;
}
function renderDataUpdateTransfer(status) {
    const target = element("#data-update-transfer");
    const run = status.last_transfer_run;
    if (!run) {
        target.replaceChildren(Object.assign(document.createElement("p"), {
            className: "muted empty",
            textContent: "暂无检查或下载记录。",
        }));
        return;
    }
    const heading = document.createElement("div");
    heading.className = "data-update-transfer-heading";
    const title = document.createElement("strong");
    title.textContent = dataUpdateStatusLabels[run.status] ?? run.status;
    const identity = document.createElement("span");
    identity.className = `badge ${run.status === "failed" ? "error" : "ready"}`;
    identity.textContent =
        `${run.trigger_kind === "scheduled" ? "定时" : "手动"} · ${run.requested_action}`;
    heading.append(title, identity);
    const details = document.createElement("p");
    details.className = "muted";
    details.textContent =
        `版本 ${run.data_version ?? "—"} · 开始 ${dataUpdateTime(run.started_at_utc)}`
            + `${run.completed_at_utc ? ` · 完成 ${dataUpdateTime(run.completed_at_utc)}` : ""}`
            + `${run.failure_code ? ` · 失败码 ${run.failure_code}` : ""}`;
    const progress = document.createElement("progress");
    progress.max = Math.max(run.total_bytes, 1);
    progress.value = Math.min(run.downloaded_bytes, progress.max);
    progress.setAttribute("aria-label", `数据包下载 ${formatBytes(run.downloaded_bytes)} / ${formatBytes(run.total_bytes)}`);
    const progressText = document.createElement("small");
    progressText.textContent = run.total_bytes > 0
        ? `${formatBytes(run.downloaded_bytes)} / ${formatBytes(run.total_bytes)}`
        : "当前阶段没有下载字节";
    target.replaceChildren(heading, details, progress, progressText);
}
function renderDataUpdateVersions(status) {
    const target = element("#data-update-versions");
    if (status.versions.length === 0) {
        target.replaceChildren(Object.assign(document.createElement("p"), {
            className: "muted empty",
            textContent: "暂无已安装版本。",
        }));
        return;
    }
    target.replaceChildren(...status.versions.map(version => {
        const item = document.createElement("div");
        item.className = `data-update-item ${version.state}`;
        const heading = document.createElement("strong");
        heading.textContent = version.data_version;
        const state = document.createElement("span");
        state.className = `badge ${version.state === "active" ? "ready" : "pending"}`;
        state.textContent = version.state === "active" ? "当前 active" : "可回滚版本";
        const counts = document.createElement("small");
        counts.textContent =
            `${version.subject_count.toLocaleString()} Subject · `
                + `${version.episode_count.toLocaleString()} Episode · `
                + `安装 ${dataUpdateTime(version.installed_at_utc)}`;
        item.append(heading, state, counts);
        return item;
    }));
}
function renderDataUpdateDownloads(status) {
    const target = element("#data-update-downloads");
    if (status.downloads.length === 0) {
        target.replaceChildren(Object.assign(document.createElement("p"), {
            className: "muted empty",
            textContent: "暂无已下载数据包。",
        }));
        return;
    }
    target.replaceChildren(...status.downloads.map(download => {
        const item = document.createElement("div");
        item.className = "data-update-item";
        const heading = document.createElement("strong");
        heading.textContent = download.data_version;
        const state = document.createElement("span");
        state.className = `badge ${download.state === "imported" ? "ready" : "pending"}`;
        state.textContent = download.state === "imported" ? "已导入" : "已验证，待导入";
        const timestamp = document.createElement("small");
        timestamp.textContent = `下载 ${dataUpdateTime(download.downloaded_at_utc)}`;
        item.append(heading, state, timestamp);
        if (download.state === "verified") {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "primary-button";
            button.textContent = "导入此版本";
            button.disabled = dataUpdateActionRunning;
            button.addEventListener("click", () => void runDataUpdateAction(`/api/v1/data-update/downloads/${encodeURIComponent(download.data_version)}/import`, `正在导入 ${download.data_version}…`));
            item.append(button);
        }
        return item;
    }));
}
async function loadDataUpdate(silent = false) {
    const message = element("#data-update-status");
    if (!silent)
        message.textContent = "正在读取数据版本…";
    try {
        const response = await fetch("/api/v1/data-update", { headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        const status = await response.json();
        const policy = !status.scheduled_enabled
            ? "定时更新关闭（手动可用）"
            : `定时 ${status.cron} · ${!status.auto_download
                ? "仅检查"
                : status.auto_import ? "自动下载并导入" : "自动下载后等待确认"}`;
        message.textContent =
            `${policy} · manifest ${status.manifest_configured ? "已配置" : "未配置"} · `
                + `保留 ${status.keep_versions} 版`;
        element("#data-update-summary").replaceChildren(configurationCard("版本状态", [
            ["当前 active", status.active_version ?? "尚未导入"],
            ["上一可用版", status.previous_version ?? "无"],
            ["状态更新时间", dataUpdateTime(status.state_updated_at_utc)],
            [
                "最近本地导入",
                status.last_package_run
                    ? `${status.last_package_run.operation} · ${status.last_package_run.status}`
                        + `${status.last_package_run.failure_code
                            ? ` · ${status.last_package_run.failure_code}` : ""}`
                    : "无",
            ],
        ]));
        renderDataUpdateTransfer(status);
        renderDataUpdateVersions(status);
        renderDataUpdateDownloads(status);
        if (!dataUpdateActionRunning) {
            const requiresManifest = !status.manifest_configured;
            element("#data-update-check").disabled = requiresManifest;
            element("#data-update-download").disabled = requiresManifest;
            element("#data-update-apply").disabled = requiresManifest;
            element("#data-update-rollback").disabled =
                status.previous_version === null;
        }
    }
    catch (error) {
        message.textContent = errorMessage(error, "数据更新状态读取失败");
    }
}
async function runDataUpdateAction(endpoint, pendingMessage, confirmation) {
    if (confirmation && !window.confirm(confirmation))
        return;
    const message = element("#data-update-status");
    setDataUpdateBusy(true);
    message.textContent = pendingMessage;
    try {
        const response = await fetch(endpoint, { method: "POST", headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        const result = await response.json();
        message.textContent =
            `${dataUpdateStatusLabels[result.status] ?? result.status} · `
                + `版本 ${result.data_version ?? result.active_version ?? "—"}`;
    }
    catch (error) {
        message.textContent = errorMessage(error, "数据更新操作失败");
    }
    finally {
        setDataUpdateBusy(false);
        await loadDataUpdate(true);
    }
}
async function importOfflineDataPackage(event) {
    event.preventDefault();
    if (dataUpdateActionRunning)
        return;
    const input = element("#data-update-offline-package");
    const file = input.files?.item(0);
    const message = element("#data-update-status");
    if (!file) {
        message.textContent = "请选择一个离线数据包 ZIP。";
        return;
    }
    const displayName = file.name;
    input.value = "";
    setDataUpdateBusy(true);
    message.textContent = `正在上传、校验并导入 ${displayName}…`;
    try {
        const uploadHeaders = new Headers(headers);
        uploadHeaders.set("Content-Type", "application/zip");
        const response = await fetch("/api/v1/data-update/offline/import", {
            method: "POST",
            headers: uploadHeaders,
            body: file,
        });
        if (!response.ok)
            throw new Error(await responseError(response));
        const result = await response.json();
        message.textContent =
            `离线数据包已导入 · 版本 ${result.data_version ?? result.active_version ?? "—"}`;
    }
    catch (error) {
        message.textContent = errorMessage(error, "离线数据包导入失败");
    }
    finally {
        setDataUpdateBusy(false);
        await loadDataUpdate(true);
    }
}
function cacheDigestLabel(kind, digest) {
    return `${kind} sha256:${digest.slice(0, 12)}…`;
}
function setCacheBusy(busy) {
    element("#cache-database").disabled = busy;
    element("#cache-reload").disabled = busy;
    for (const target of [
        element("#cache-buckets"),
        element("#cache-entries"),
    ]) {
        if (busy) {
            setRegionState(target, "loading");
        }
        else if (target.dataset.uiState === "loading") {
            setRegionState(target, "ready");
        }
    }
}
function renderCacheBuckets() {
    const target = element("#cache-buckets");
    if (cacheBuckets.length === 0) {
        renderRegionMessage(target, "empty", "当前命名空间没有 bucket。");
        return;
    }
    renderRegionContent(target, ...cacheBuckets.map(bucket => {
        const button = document.createElement("button");
        button.type = "button";
        button.className = "secondary-button cache-bucket-button";
        button.setAttribute("aria-current", String(bucket.bucket_id === activeCacheBucketId));
        const label = document.createElement("code");
        label.textContent = cacheDigestLabel("bucket", bucket.bucket_id);
        const count = document.createElement("span");
        count.textContent = `${bucket.entry_count} 项`;
        button.append(label, count);
        button.addEventListener("click", () => {
            if (activeCacheBucketId === bucket.bucket_id)
                return;
            activeCacheBucketId = bucket.bucket_id;
            cachePage = 1;
            renderCacheBuckets();
            void loadCacheEntries();
        });
        return button;
    }));
}
function renderCacheEntries(page) {
    const target = element("#cache-entries");
    if (page.items.length === 0) {
        renderRegionMessage(target, "empty", page.bucket_id === ""
            ? "当前命名空间没有 bucket。"
            : page.total_count === 0 ? "此 bucket 没有有效条目。" : "当前页没有条目。");
    }
    else {
        renderRegionContent(target, ...page.items.map(item => {
            const card = document.createElement("article");
            card.className = "cache-entry";
            const details = document.createElement("div");
            const identity = document.createElement("code");
            identity.textContent = cacheDigestLabel("key", item.entry_id);
            const metadata = document.createElement("p");
            metadata.className = "muted";
            metadata.textContent = `${formatBytes(item.value_bytes)} · 更新 ${dataUpdateTime(item.updated_at_utc)}`
                + ` · ${item.expires_at_utc ? `过期 ${dataUpdateTime(item.expires_at_utc)}` : "永久"}`;
            details.append(identity, metadata);
            card.append(details);
            if (!page.read_only) {
                const remove = document.createElement("button");
                remove.type = "button";
                remove.className = "danger-button";
                remove.textContent = "删除此缓存项";
                remove.addEventListener("click", () => void deleteCacheEntry(item, remove));
                card.append(remove);
            }
            else {
                const state = document.createElement("span");
                state.className = "badge pending";
                state.textContent = "只读";
                card.append(state);
            }
            return card;
        }));
    }
    cacheTotalCount = page.total_count;
    const totalPages = Math.max(1, Math.ceil(cacheTotalCount / cachePageSize));
    element("#cache-page-label").textContent =
        `第 ${page.page} / ${totalPages} 页 · ${page.total_count} 项`;
    element("#cache-previous").disabled = page.page <= 1;
    element("#cache-next").disabled = page.page >= totalPages;
}
async function loadCacheBuckets() {
    const sequence = ++cacheRequestSequence;
    const status = element("#cache-status");
    setCacheBusy(true);
    status.textContent = "正在读取安全缓存索引…";
    try {
        const result = await api.get(`/api/v1/cache/buckets?database=${cacheDatabase}`);
        if (sequence !== cacheRequestSequence)
            return;
        cacheReadOnly = result.read_only;
        cacheBuckets = result.items;
        if (!cacheBuckets.some(bucket => bucket.bucket_id === activeCacheBucketId)) {
            activeCacheBucketId = cacheBuckets[0]?.bucket_id ?? null;
            cachePage = 1;
        }
        renderCacheBuckets();
        status.textContent = `${result.database} · ${result.read_only ? "只读" : "可精确删除"} · ${result.items.length} 个 bucket`;
        if (activeCacheBucketId) {
            await loadCacheEntries(sequence);
        }
        else {
            renderCacheEntries({
                database: cacheDatabase,
                read_only: cacheReadOnly,
                bucket_id: "",
                page: 1,
                page_size: cachePageSize,
                total_count: 0,
                items: [],
            });
        }
    }
    catch (error) {
        if (sequence !== cacheRequestSequence)
            return;
        status.textContent = errorMessage(error, "缓存索引读取失败");
        cacheBuckets = [];
        activeCacheBucketId = null;
        renderRegionMessage(element("#cache-buckets"), "error", "缓存 bucket 读取失败，请刷新。");
        renderRegionMessage(element("#cache-entries"), "error", "缓存索引读取失败，无法读取条目。");
    }
    finally {
        if (sequence === cacheRequestSequence)
            setCacheBusy(false);
    }
}
async function loadCacheEntries(parentSequence) {
    if (!activeCacheBucketId)
        return;
    const sequence = parentSequence ?? ++cacheRequestSequence;
    const status = element("#cache-status");
    const entries = element("#cache-entries");
    setRegionState(entries, "loading");
    try {
        const query = new URLSearchParams({
            database: cacheDatabase,
            bucket_id: activeCacheBucketId,
            page: String(cachePage),
            page_size: String(cachePageSize),
        });
        const result = await api.get(`/api/v1/cache/entries?${query}`);
        if (sequence !== cacheRequestSequence)
            return;
        cacheReadOnly = result.read_only;
        renderCacheEntries(result);
    }
    catch (error) {
        if (sequence !== cacheRequestSequence)
            return;
        status.textContent = errorMessage(error, "缓存条目读取失败");
        renderRegionMessage(entries, "error", "缓存条目读取失败，请刷新。");
        element("#cache-previous").disabled = true;
        element("#cache-next").disabled = true;
    }
    finally {
        if (sequence === cacheRequestSequence && entries.dataset.uiState === "loading") {
            setRegionState(entries, "ready");
        }
    }
}
async function deleteCacheEntry(item, button) {
    if (!activeCacheBucketId || cacheReadOnly)
        return;
    const label = cacheDigestLabel("key", item.entry_id);
    if (!window.confirm(`确认删除 ${label}？只删除这一条 bolt 缓存，不删除业务记录或文件。`))
        return;
    button.disabled = true;
    const status = element("#cache-status");
    status.textContent = `正在删除 ${label}…`;
    try {
        const result = await api.delete(`/api/v1/cache/entries/${item.entry_id}`, {
            database: cacheDatabase,
            bucket_id: activeCacheBucketId,
            delete_token: item.delete_token,
        });
        if (!result.deleted || result.entry_id !== item.entry_id) {
            throw new Error("缓存删除响应无效，请刷新后确认条目状态。");
        }
        const remainingAfterDelete = Math.max(0, cacheTotalCount - 1);
        if (cachePage > 1 && (cachePage - 1) * cachePageSize >= remainingAfterDelete) {
            cachePage--;
        }
        await loadCacheBuckets();
        status.textContent = `${label} 已删除；列表已刷新。`;
    }
    catch (error) {
        status.textContent = errorMessage(error, "缓存删除失败");
        button.disabled = false;
        await loadCacheEntries();
    }
}
function liveLogWebSocketUrl() {
    const url = new URL("/websocket/log", window.location.href);
    url.protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
    url.search = "";
    if (accessKey)
        url.searchParams.set("access_key", accessKey);
    return url.toString();
}
function setLiveLogStatus(message, state) {
    const target = element("#live-log-status");
    target.textContent = message;
    target.dataset.state = state;
}
function liveLogFilter() {
    const minimum = element("#live-log-level").value;
    return {
        minimumLevel: minimum === "all" ? "all" : minimum,
        query: element("#live-log-search").value,
        category: element("#live-log-category").value,
        eventId: element("#live-log-event-id").value,
    };
}
function visibleLiveLogEntries() {
    return filterLiveLogEntries(liveLogEntries, liveLogFilter());
}
function liveLogTime(timestamp) {
    if (!timestamp)
        return "时间未知";
    const parsed = new Date(timestamp);
    return Number.isNaN(parsed.getTime())
        ? timestamp
        : parsed.toLocaleTimeString("zh-CN", {
            hour12: false,
            hour: "2-digit",
            minute: "2-digit",
            second: "2-digit",
            fractionalSecondDigits: 3,
        });
}
function liveLogDetail(label, value) {
    const row = document.createElement("div");
    const term = document.createElement("dt");
    term.textContent = label;
    const description = document.createElement("dd");
    description.textContent = value;
    row.append(term, description);
    return row;
}
function renderLiveLogs() {
    const visible = visibleLiveLogEntries();
    const stream = element("#live-log-stream");
    stream.classList.toggle("nowrap", !element("#live-log-wrap").checked);
    if (visible.length === 0) {
        stream.replaceChildren(Object.assign(document.createElement("p"), {
            className: "muted empty",
            textContent: liveLogEntries.length === 0
                ? "等待日志…"
                : "当前组合筛选下没有日志。",
        }));
    }
    else {
        const nodes = visible.map(entry => {
            const line = document.createElement("details");
            line.className = `live-log-entry ${entry.level}`;
            const summary = document.createElement("summary");
            const time = document.createElement("time");
            time.textContent = liveLogTime(entry.timestamp);
            if (entry.timestamp)
                time.dateTime = entry.timestamp;
            const level = document.createElement("strong");
            level.textContent = entry.level.toUpperCase();
            const category = document.createElement("span");
            category.className = "live-log-category";
            category.textContent = entry.category;
            const eventId = document.createElement("span");
            eventId.className = "live-log-event";
            eventId.textContent = entry.eventId === null ? "—" : `#${entry.eventId}`;
            const message = document.createElement("span");
            message.className = "live-log-message";
            message.textContent = entry.message;
            summary.append(time, level, category, eventId, message);
            const details = document.createElement("dl");
            details.className = "live-log-detail";
            details.append(liveLogDetail("UTC 时间", entry.timestamp ?? "未知"), liveLogDetail("级别", entry.level), liveLogDetail("类别", entry.category), liveLogDetail("Event ID", entry.eventId === null ? "无" : String(entry.eventId)), liveLogDetail("消息", entry.message));
            if (entry.exception)
                details.append(liveLogDetail("异常", entry.exception));
            details.append(liveLogDetail("脱敏原文", entry.text));
            line.append(summary, details);
            return line;
        });
        stream.replaceChildren(...nodes);
        if (element("#live-log-auto-scroll").checked) {
            stream.scrollTop = stream.scrollHeight;
        }
    }
    element("#live-log-count").textContent =
        `本页 ${liveLogEntries.length} / ${maximumRenderedLogs} 条`
            + (visible.length === liveLogEntries.length ? "" : ` · 显示 ${visible.length}`);
}
function appendLiveLogs(lines) {
    const entries = lines
        .filter(line => line.length > 0)
        .map(parseLiveLogEntry);
    if (entries.length === 0)
        return;
    liveLogEntries.push(...entries);
    if (liveLogEntries.length > maximumRenderedLogs) {
        liveLogEntries.splice(0, liveLogEntries.length - maximumRenderedLogs);
    }
    renderLiveLogs();
}
async function copyVisibleLiveLogs() {
    const visible = visibleLiveLogEntries();
    if (visible.length === 0) {
        setLiveLogStatus("当前没有可复制的日志", "empty");
        return;
    }
    try {
        await navigator.clipboard.writeText(visible.map(entry => entry.text).join("\n"));
        setLiveLogStatus(`已复制 ${visible.length} 条脱敏日志`, "connected");
    }
    catch {
        setLiveLogStatus("浏览器拒绝剪贴板访问，请使用系统选择复制", "error");
    }
}
function updateLiveLogPauseButton() {
    const button = element("#live-log-pause");
    button.textContent = liveLogPaused ? "恢复" : "暂停";
    button.disabled =
        liveLogControlPending || liveLogSocket?.readyState !== WebSocket.OPEN;
}
function handleLiveLogControl(header) {
    liveLogControlPending = false;
    if (header.status !== "ok") {
        setLiveLogStatus(`日志流控制失败：${header.code ?? "unknown_error"}`, "error");
        updateLiveLogPauseButton();
        return;
    }
    if (header.action === "pause") {
        liveLogPaused = true;
        setLiveLogStatus("已暂停；服务器正在缓存最新 1000 条", "paused");
    }
    else if (header.action === "resume") {
        liveLogPaused = false;
        setLiveLogStatus("日志流已连接", "connected");
    }
    updateLiveLogPauseButton();
}
function handleLiveLogMessage(payload) {
    const parts = payload.split("\n\n");
    let header;
    try {
        header = JSON.parse(parts[0] ?? "");
    }
    catch {
        setLiveLogStatus("收到无法解析的日志帧，已忽略", "error");
        return;
    }
    if (header.type === "control") {
        handleLiveLogControl(header);
        return;
    }
    if (header.type !== "log"
        || !Number.isInteger(header.count)
        || (header.count ?? 0) < 1
        || (header.count ?? 0) > 1000
        || parts.length - 1 < (header.count ?? 0)) {
        setLiveLogStatus("收到无效日志帧，已忽略", "error");
        return;
    }
    appendLiveLogs(parts.slice(1, 1 + header.count));
}
function scheduleLiveLogReconnect() {
    if (!liveLogShouldReconnect || liveLogReconnectTimer !== null)
        return;
    const delay = Math.min(30000, 1000 * (2 ** liveLogReconnectAttempt));
    liveLogReconnectAttempt++;
    setLiveLogStatus(`连接已断开，${Math.ceil(delay / 1000)} 秒后重试`, "disconnected");
    liveLogReconnectTimer = window.setTimeout(() => {
        liveLogReconnectTimer = null;
        connectLiveLogs();
    }, delay);
}
function disconnectCurrentLiveLogSocket() {
    const socket = liveLogSocket;
    liveLogSocket = null;
    if (!socket)
        return;
    socket.onopen = null;
    socket.onmessage = null;
    socket.onerror = null;
    socket.onclose = null;
    try {
        socket.close(1000, "reconnect");
    }
    catch {
        // A connecting browser socket can reject close; detached callbacks keep it harmless.
    }
}
function connectLiveLogs(manual = false) {
    if (manual)
        liveLogReconnectAttempt = 0;
    if (liveLogReconnectTimer !== null) {
        window.clearTimeout(liveLogReconnectTimer);
        liveLogReconnectTimer = null;
    }
    disconnectCurrentLiveLogSocket();
    setLiveLogStatus("正在连接日志流…", "connecting");
    updateLiveLogPauseButton();
    let socket;
    try {
        socket = new WebSocket(liveLogWebSocketUrl());
    }
    catch {
        scheduleLiveLogReconnect();
        return;
    }
    liveLogSocket = socket;
    socket.onopen = () => {
        if (liveLogSocket !== socket)
            return;
        liveLogReconnectAttempt = 0;
        if (liveLogPaused) {
            liveLogControlPending = true;
            socket.send(JSON.stringify({ action: "pause" }));
            setLiveLogStatus("已重连，正在恢复暂停状态…", "connecting");
        }
        else {
            setLiveLogStatus("日志流已连接", "connected");
        }
        updateLiveLogPauseButton();
    };
    socket.onmessage = event => {
        if (liveLogSocket === socket && typeof event.data === "string") {
            handleLiveLogMessage(event.data);
        }
    };
    socket.onerror = () => {
        if (liveLogSocket === socket) {
            setLiveLogStatus("日志流连接发生错误", "error");
        }
    };
    socket.onclose = () => {
        if (liveLogSocket !== socket)
            return;
        liveLogSocket = null;
        liveLogControlPending = false;
        updateLiveLogPauseButton();
        scheduleLiveLogReconnect();
    };
}
function toggleLiveLogPause() {
    if (!liveLogSocket || liveLogSocket.readyState !== WebSocket.OPEN) {
        setLiveLogStatus("日志流尚未连接，请重新连接", "disconnected");
        return;
    }
    liveLogControlPending = true;
    updateLiveLogPauseButton();
    liveLogSocket.send(JSON.stringify({
        action: liveLogPaused ? "resume" : "pause",
    }));
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
function readDownloadState() {
    const defaults = {
        page: 1,
        page_size: 25,
        search: "",
        state: "",
        business_status: "",
        downloader_id: "",
        source: "",
    };
    try {
        const raw = window.localStorage.getItem(downloadStorageKey);
        if (!raw)
            return defaults;
        const stored = JSON.parse(raw);
        const pageSizes = [10, 25, 50];
        return {
            page: Number.isInteger(stored.page) && (stored.page ?? 0) > 0
                ? stored.page : defaults.page,
            page_size: pageSizes.includes(stored.page_size)
                ? stored.page_size : defaults.page_size,
            search: typeof stored.search === "string"
                ? stored.search.slice(0, 200) : "",
            state: typeof stored.state === "string"
                ? stored.state.slice(0, 64) : "",
            business_status: typeof stored.business_status === "string"
                ? stored.business_status.slice(0, 64) : "",
            downloader_id: typeof stored.downloader_id === "string"
                ? stored.downloader_id.slice(0, 64) : "",
            source: typeof stored.source === "string"
                ? stored.source.slice(0, 64) : "",
        };
    }
    catch {
        return defaults;
    }
}
function saveDownloadState() {
    try {
        window.localStorage.setItem(downloadStorageKey, JSON.stringify(downloadState));
    }
    catch {
        // Browser storage is an optional UI preference; business state remains server-side.
    }
}
function readMetadataState() {
    const defaults = {
        page: 1,
        page_size: 25,
        search: "",
        status: "",
        handling: "all",
        failure_stage: "",
        error_code: "",
        retryability: "all",
        sort: "updated",
        direction: "desc",
    };
    try {
        const raw = window.localStorage.getItem(metadataStorageKey);
        if (!raw)
            return defaults;
        const stored = JSON.parse(raw);
        const pageSizes = [10, 25, 50];
        const sorts = ["updated", "title", "status", "failure"];
        return {
            page: Number.isInteger(stored.page) && (stored.page ?? 0) > 0
                ? stored.page : 1,
            page_size: pageSizes.includes(stored.page_size)
                ? stored.page_size : 25,
            search: typeof stored.search === "string" ? stored.search.slice(0, 200) : "",
            status: typeof stored.status === "string" ? stored.status.slice(0, 64) : "",
            handling: typeof stored.handling === "string" ? stored.handling : "all",
            failure_stage: typeof stored.failure_stage === "string"
                ? stored.failure_stage.slice(0, 64) : "",
            error_code: typeof stored.error_code === "string"
                ? stored.error_code.slice(0, 128) : "",
            retryability: typeof stored.retryability === "string"
                ? stored.retryability : "all",
            sort: sorts.includes(stored.sort)
                ? stored.sort : "updated",
            direction: stored.direction === "asc" ? "asc" : "desc",
        };
    }
    catch {
        return defaults;
    }
}
function saveMetadataState() {
    try {
        window.localStorage.setItem(metadataStorageKey, JSON.stringify(metadataState));
    }
    catch {
        // Optional UI preference only.
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
        manual_mikan_override: "人工 Mikan TMDB 覆盖",
        tmdb_title: "TMDB 标题搜索",
        tmdb_air_date: "TMDB 开播日期验证",
        backtrace: "P3 Bangumi 回溯验证",
        ai_metadata: "AI 统一匹配 + TMDB 验证",
        title_season: "P2 本地任务 title 季度（未验证）",
        first_season: "P1 本地 S01（未验证）",
        pending_tmdb_manual: "待补全 TMDB 人工恢复",
        pending_tmdb_automatic: "待补全 TMDB 自动恢复",
        manual_mikan_offset: "人工 Mikan EP offset + TMDB 验证",
        trusted_mikan_offset: "可信 Mikan EP offset + TMDB 验证",
        tmdb_episode_number: "文件名 EP + TMDB Episode 验证",
        subtitle_association: "字幕关联已确认 EP",
    };
    return value ? labels[value] ?? value : "未记录";
}
function resolutionReference(runId, attemptId) {
    if (!runId || !attemptId)
        return "证据引用未记录";
    return `Run ${runId.slice(0, 8)}… · Attempt ${attemptId.slice(0, 8)}…`;
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
    const pageCount = Math.max(1, Math.ceil(page.total_items / page.page_size));
    element("#library-status").textContent =
        `${page.total_items} 个季度 · ${librarySortLabel(page.sort)} · `
            + (page.direction === "asc" ? "升序" : "降序");
    element("#library-page-label").textContent =
        `第 ${page.page} / ${pageCount} 页`;
    element("#library-previous").disabled = page.page <= 1;
    element("#library-next").disabled = page.page >= pageCount;
    if (page.items.length === 0) {
        renderRegionMessage(list, "empty", "作品库暂时为空。只有已确认 TMDB Series 与普通 Season 的作品会显示在这里；tmdbid=0 条目请到“待补全 TMDB”处理。");
        return;
    }
    renderRegionContent(list, ...page.items.map((item) => {
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
function libraryAuditGroup(title, total, truncated, items, open = false) {
    const group = document.createElement("details");
    group.className = "library-audit-group";
    group.open = open;
    const summary = document.createElement("summary");
    summary.textContent = `${title} · ${total}${truncated ? "（仅显示最新一部分）" : ""}`;
    const content = document.createElement("div");
    content.className = "library-audit-list";
    if (items.length === 0) {
        const empty = document.createElement("p");
        empty.className = "muted empty";
        empty.textContent = "暂无记录。";
        content.append(empty);
    }
    else {
        content.append(...items);
    }
    group.append(summary, content);
    return group;
}
function renderLibraryAudit(detail) {
    const container = element("#library-audit");
    const heading = document.createElement("div");
    heading.className = "library-audit-heading";
    const title = document.createElement("h4");
    title.textContent = "元数据审计";
    const status = document.createElement("span");
    status.className = "muted";
    status.textContent =
        `${detail.related_task_total} 个关联任务 · ${detail.resolution_attempt_total} 次策略验证`;
    heading.append(title, status);
    const offsets = detail.manual_offsets.map((offset) => {
        const row = document.createElement("article");
        row.className = `library-audit-item ${offset.enabled ? "ready" : "disabled"}`;
        const name = document.createElement("strong");
        const signedOffset = offset.episode_offset >= 0
            ? `+${offset.episode_offset}` : String(offset.episode_offset);
        name.textContent = `mikanid ${offset.mikanid} · EP ${signedOffset}`;
        const scope = document.createElement("p");
        scope.textContent =
            `当前规则 ${offset.enabled ? "已启用（人工优先级最高）" : "已禁用"}`
                + ` · TMDB ${textOrDash(offset.tmdb_series_id)}`
                + ` / ${offset.tmdb_season_number === null
                    ? "全部季度" : `S${String(offset.tmdb_season_number).padStart(2, "0")}`}`;
        const metadata = document.createElement("p");
        metadata.textContent =
            `bgmid ${textOrDash(offset.bgmid)} · revision ${offset.revision}`
                + ` · 更新 ${libraryDate(offset.updated_at_utc, true)}`;
        row.append(name, scope, metadata);
        return row;
    });
    const tasks = detail.related_tasks.map((task) => {
        const row = document.createElement("article");
        row.className = "library-audit-item";
        const name = document.createElement("strong");
        name.textContent = task.title;
        const identity = document.createElement("p");
        identity.textContent =
            `${task.task_id} · ${task.source_id} · ${task.status}`
                + ` · mikanid ${textOrDash(task.mikanid)} · bgmid ${textOrDash(task.bgmid)}`;
        const run = document.createElement("p");
        run.textContent = task.latest_run_attempt_number === null
            ? `尚无解析 Run · 更新 ${libraryDate(task.updated_at_utc, true)}`
            : `最近 Run #${task.latest_run_attempt_number}`
                + `（${textOrDash(task.latest_run_status)}）`
                + ` · 更新 ${libraryDate(task.updated_at_utc, true)}`;
        row.append(name, identity, run);
        return row;
    });
    const attempts = detail.resolution_attempts.map((attempt) => {
        const row = document.createElement("article");
        row.className =
            `metadata-attempt ${attempt.result === "failed" ? "failed" : ""}`;
        const attemptHeading = document.createElement("div");
        attemptHeading.className = "metadata-attempt-heading";
        const strategy = document.createElement("strong");
        strategy.textContent =
            `${attempt.stage} · ${libraryStrategy(attempt.strategy)}`;
        const result = document.createElement("span");
        result.className = `badge ${attempt.result === "failed" ? "error" : "ready"}`;
        result.textContent = attempt.result;
        attemptHeading.append(strategy, result);
        const task = document.createElement("p");
        task.textContent = `${attempt.task_title} · ${attempt.task_id}`;
        const execution = document.createElement("p");
        execution.textContent =
            `P${textOrDash(attempt.priority)} · Run #${attempt.run_attempt_number}`
                + `（${attempt.run_status}）· 尝试 #${attempt.attempt_number}`
                + ` · ${attempt.duration_ms} ms · ${libraryDate(attempt.created_at_utc, true)}`;
        row.append(attemptHeading, task, execution);
        if (attempt.error_code || attempt.reason) {
            const reason = document.createElement("p");
            reason.className = "metadata-attempt-reason";
            reason.textContent =
                `${textOrDash(attempt.error_code)} · ${textOrDash(attempt.reason)}`
                    + ` · ${attempt.retryable ? "可重试" : "不可重试"}`;
            row.append(reason);
        }
        return row;
    });
    container.replaceChildren(heading, libraryAuditGroup("当前人工 EP offset", detail.manual_offsets.length, false, offsets, detail.manual_offsets.length > 0), libraryAuditGroup("关联任务", detail.related_task_total, detail.related_tasks_truncated, tasks), libraryAuditGroup("季度级逐次验证时间线", detail.resolution_attempt_total, detail.resolution_attempts_truncated, attempts));
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
        [
            "Series 取得",
            `${libraryStrategy(detail.series_resolution_source)} · ${resolutionReference(detail.series_resolution_run_id, detail.series_resolution_attempt_id)}`,
        ],
        [
            "Season 取得",
            `${libraryStrategy(detail.season_resolution_source)} · ${resolutionReference(detail.season_resolution_run_id, detail.season_resolution_attempt_id)}`,
        ],
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
    element("#library-detail-refresh").disabled = false;
    element("#library-detail-delete").disabled = false;
    element("#library-detail-action-status").textContent =
        "刷新只更新 TMDB 权威投影；删除不处理业务记录、下载器任务或文件。";
    renderLibraryAudit(detail);
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
    element("#library-audit").replaceChildren();
    element("#library-episodes").replaceChildren();
    element("#library-episode-status").textContent = "";
    element("#library-detail-refresh").disabled = true;
    element("#library-detail-delete").disabled = true;
    element("#library-detail-action-status").textContent = "";
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
    setRegionState(list, "loading");
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
        renderRegionMessage(list, "error", `作品库读取失败：${errorMessage(error, "未知错误")}`);
        element("#library-status").textContent = "读取失败";
    }
}
async function createLibrarySeason(event) {
    event.preventDefault();
    const buttonElement = element("#library-create");
    const status = element("#library-admin-status");
    const tmdbSeriesId = element("#library-create-series").valueAsNumber;
    const seasonNumber = element("#library-create-season").valueAsNumber;
    if (!Number.isInteger(tmdbSeriesId) || tmdbSeriesId <= 0
        || !Number.isInteger(seasonNumber) || seasonNumber <= 0) {
        status.textContent = "TMDB Series ID 与 Season 必须是正整数。";
        return;
    }
    buttonElement.disabled = true;
    status.textContent = `正在通过 TMDB 验证 Series ${tmdbSeriesId} / Season ${seasonNumber}…`;
    try {
        const requestHeaders = new Headers(headers);
        requestHeaders.set("Content-Type", "application/json");
        const response = await fetch("/api/v1/library/seasons", {
            method: "POST",
            headers: requestHeaders,
            body: JSON.stringify({
                tmdb_series_id: tmdbSeriesId,
                tmdb_season_number: seasonNumber,
            }),
        });
        if (!response.ok)
            throw new Error(await responseError(response));
        libraryState.active_series_id = tmdbSeriesId;
        libraryState.active_season_number = seasonNumber;
        libraryState.page = 1;
        saveLibraryState();
        status.textContent =
            `已添加 TMDB ${tmdbSeriesId} / S${String(seasonNumber).padStart(2, "0")}，正在刷新作品库。`;
        await loadLibrary();
        await loadLibraryDetail(tmdbSeriesId, seasonNumber, true);
    }
    catch (error) {
        status.textContent = `添加失败：${errorMessage(error, "未知错误")}`;
    }
    finally {
        buttonElement.disabled = false;
    }
}
async function refreshLibrarySeason() {
    if (!activeLibraryDetail)
        return;
    const detail = activeLibraryDetail;
    if (!window.confirm(`从 TMDB 重新获取 Series ${detail.tmdb_series_id} / Season ${detail.tmdb_season_number}？`
        + " 名称、封面、季度和 EP snapshot 将以 TMDB 当前返回值为准；完成记录不会删除。"))
        return;
    const refresh = element("#library-detail-refresh");
    const remove = element("#library-detail-delete");
    const status = element("#library-detail-action-status");
    refresh.disabled = true;
    remove.disabled = true;
    status.textContent = "正在验证并刷新 TMDB 权威投影…";
    try {
        const requestHeaders = new Headers(headers);
        requestHeaders.set("Content-Type", "application/json");
        const response = await fetch(`/api/v1/library/seasons/${detail.tmdb_series_id}/${detail.tmdb_season_number}`, {
            method: "PUT",
            headers: requestHeaders,
            body: JSON.stringify({ expected_revision: detail.resource_revision }),
        });
        if (!response.ok)
            throw new Error(await responseError(response));
        status.textContent = "TMDB 权威投影已刷新。";
        await loadLibrary();
        await loadLibraryDetail(detail.tmdb_series_id, detail.tmdb_season_number);
    }
    catch (error) {
        status.textContent =
            `刷新失败：${errorMessage(error, "未知错误")}；revision 冲突时请重新载入。`;
        refresh.disabled = false;
        remove.disabled = false;
    }
}
async function deleteLibrarySeason() {
    if (!activeLibraryDetail)
        return;
    const detail = activeLibraryDetail;
    if (!window.confirm(`仅删除 ${detail.display_name} / ${detail.season_name} 的本地 TMDB 投影？`
        + " 服务端会拒绝仍有任务、完成记录、claim、人工规则或待写 NFO 引用的季度。"
        + " 此操作不会删除下载器任务、下载源文件或媒体文件。"))
        return;
    const refresh = element("#library-detail-refresh");
    const remove = element("#library-detail-delete");
    const status = element("#library-detail-action-status");
    refresh.disabled = true;
    remove.disabled = true;
    status.textContent = "正在检查引用并删除投影…";
    try {
        const query = new URLSearchParams({
            expected_revision: detail.resource_revision,
        });
        const response = await fetch(`/api/v1/library/seasons/${detail.tmdb_series_id}/${detail.tmdb_season_number}?${query}`, { method: "DELETE", headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        closeLibraryDetail();
        element("#library-admin-status").textContent =
            `已删除 TMDB ${detail.tmdb_series_id} / S${String(detail.tmdb_season_number).padStart(2, "0")} 的无引用投影。`;
        await loadLibrary();
    }
    catch (error) {
        status.textContent =
            `删除失败：${errorMessage(error, "未知错误")}；有业务引用时请使用四类删除流程。`;
        refresh.disabled = false;
        remove.disabled = false;
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
        ["超时", `${config.metadata.tmdb.http_timeout_seconds} 秒`],
        [
            "TMDB 重试",
            `${config.metadata.tmdb.retry_count} 次 · 间隔 `
                + `${config.metadata.tmdb.retry_delay_seconds} 秒`,
        ],
        ["TMDB 缓存", `${config.metadata.tmdb.cache_hours} 小时（仅缓存验证成功的响应）`],
        ["Bangumi API", config.metadata.bangumi.base_url],
        ["Bangumi 超时", `${config.metadata.bangumi.http_timeout_seconds} 秒`],
        [
            "Bangumi 重试",
            `${config.metadata.bangumi.retry_count} 次 · 间隔 `
                + `${config.metadata.bangumi.retry_delay_seconds} 秒`,
        ],
        [
            "Bangumi 完全兜底（一般不启用这个）",
            `${enabledLabel(config.metadata.tmdb_failure_use_bangumi)} · `
                + "TMDB 完全失败时用 Bangumi 最终兜底；季度固定 S01；需要 bgmid；"
                + "不输出有效 tmdbid（内部仍按现有逻辑写 0）",
        ],
        [
            "TMDB 成功时写 Bangumi ID",
            `${enabledLabel(config.metadata.write_bangumi_id_when_tmdb_matched)} · `
                + "默认关闭；关闭时仅 tmdbid=0 的 Bangumi 完全兜底写入 bangumiid",
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
        const cards = [
            configurationCard("目录", [
                ["data_path", config.paths.data_path],
                ["download_path", config.paths.download_path],
                ["save_path", config.paths.save_path],
                ["修改生效", config.deployment.paths_restart_required ? "需要重启" : "即时生效"],
            ]),
            configurationCard("部署与安全", [
                ["容器模式", enabledLabel(config.deployment.running_in_container)],
                ["后台 workers", enabledLabel(config.deployment.background_workers_enabled)],
                ["Access-Key", config.deployment.access_key_configured ? "已配置（值已隐藏）" : "未配置"],
            ]),
            configurationCard("全局选择性代理", [
                ["代理地址", config.outbound_proxy.url ?? "未配置（全部直连）"],
                [
                    "代理域名",
                    config.outbound_proxy.hosts.length === 0
                        ? "未配置"
                        : config.outbound_proxy.hosts.join("、"),
                ],
                ["匹配规则", "精确域名或 *.example.com；未命中的地址保持直连"],
            ]),
            metadataConfigurationCard(config),
            configurationCard("AI、偏移与 Torrent", [
                ["OpenAI API", config.metadata.ai.base_url ?? "未配置"],
                ["模型", config.metadata.ai.model ?? "未配置"],
                ["API Key", config.metadata.ai.api_key_configured ? "已配置（值已隐藏）" : "未配置"],
                ["TMDB MCP", config.metadata.ai.tmdb_mcp_url],
                ["Bangumi MCP", config.metadata.ai.bangumi_mcp_url],
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
            ]),
            configurationCard("AnimeGoNetData 更新", [
                ["定时更新", enabledLabel(config.data_update.enabled)],
                ["Cron", config.data_update.cron],
                ["Manifest", config.data_update.manifest_url ?? "未配置（仍可离线导入）"],
                [
                    "策略",
                    !config.data_update.auto_download
                        ? "仅检查"
                        : config.data_update.auto_import
                            ? "自动下载并导入"
                            : "自动下载后等待确认",
                ],
                ["保留版本", `${config.data_update.keep_versions} 版`],
                ["HTTP 超时", `${config.data_update.http_timeout_seconds} 秒`],
                ["修改生效", config.data_update.hot_reload_supported ? "即时热重排" : "需要重启"],
            ]),
        ];
        if (config.migration_diagnostics.length > 0) {
            cards.unshift(configurationCard("旧配置迁移阻断", config.migration_diagnostics.map((item) => [
                item.code,
                `${item.legacy_downloader_type} · ${item.source} · ${item.message}`,
            ])));
        }
        container.replaceChildren(...cards);
        status.textContent = config.downloads_blocked
            ? "检测到不支持或无法安全读取的旧下载器配置；下载与后台 workers 已强制停用，请先按迁移提示修复并重启。"
            : config.restart_required
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
    const clearAiKey = element("#configuration-ai-key-clear").checked;
    const key = element("#configuration-tmdb-key");
    const token = element("#configuration-tmdb-token");
    const aiKey = element("#configuration-ai-key");
    const keyLocked = activeConfigurationLockedFields.has("tmdb_api_key");
    const tokenLocked = activeConfigurationLockedFields.has("tmdb_read_access_token");
    const aiKeyLocked = activeConfigurationLockedFields.has("ai_api_key");
    key.disabled = keyLocked || clearKey;
    token.disabled = tokenLocked || clearToken;
    aiKey.disabled = aiKeyLocked || clearAiKey;
    element("#configuration-tmdb-key-clear").disabled = keyLocked;
    element("#configuration-tmdb-token-clear").disabled = tokenLocked;
    element("#configuration-ai-key-clear").disabled = aiKeyLocked;
    if (clearKey)
        key.value = "";
    if (clearToken)
        token.value = "";
    if (clearAiKey)
        aiKey.value = "";
}
const configurationLockSelectors = {
    outbound_proxy_url: ["#configuration-outbound-proxy-url"],
    outbound_proxy_hosts: ["#configuration-outbound-proxy-hosts"],
    mikan_base_url: ["#configuration-mikan-url"],
    tmdb_base_url: ["#configuration-tmdb-url"],
    tmdb_image_base_url: ["#configuration-tmdb-image-url"],
    tmdb_language: ["#configuration-tmdb-language"],
    tmdb_http_timeout_seconds: ["#configuration-tmdb-timeout"],
    tmdb_retry_count: ["#configuration-tmdb-retry-count"],
    tmdb_retry_delay_seconds: ["#configuration-tmdb-retry-delay"],
    tmdb_cache_hours: ["#configuration-tmdb-cache-hours"],
    tmdb_api_key: ["#configuration-tmdb-key", "#configuration-tmdb-key-clear"],
    tmdb_read_access_token: ["#configuration-tmdb-token", "#configuration-tmdb-token-clear"],
    bangumi_base_url: ["#configuration-bangumi-url"],
    bangumi_http_timeout_seconds: ["#configuration-bangumi-timeout"],
    bangumi_retry_count: ["#configuration-bangumi-retry-count"],
    bangumi_retry_delay_seconds: ["#configuration-bangumi-retry-delay"],
    ai_base_url: ["#configuration-ai-base-url"],
    ai_model: ["#configuration-ai-model"],
    ai_api_key: ["#configuration-ai-key", "#configuration-ai-key-clear"],
    ai_tmdb_mcp_url: ["#configuration-ai-tmdb-mcp-url"],
    ai_bangumi_mcp_url: ["#configuration-ai-bangumi-mcp-url"],
    ai_use_metadata_match: ["#configuration-ai-metadata"],
    ai_http_timeout_seconds: ["#configuration-ai-timeout"],
    data_update_enabled: ["#configuration-data-update-enabled"],
    data_update_cron: ["#configuration-data-update-cron"],
    data_update_manifest_url: ["#configuration-data-update-manifest"],
    data_update_auto_download: ["#configuration-data-update-auto-download"],
    data_update_auto_import: ["#configuration-data-update-auto-import"],
    data_update_keep_versions: ["#configuration-data-update-keep"],
    data_update_http_timeout_seconds: ["#configuration-data-update-timeout"],
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
                input.title = `由部署键 ${lock.controlling_keys.join(", ")} 控制`;
            }
            else {
                input.removeAttribute("title");
            }
        }
    }
    const summary = element("#configuration-lock-summary");
    if (locks.length === 0) {
        summary.textContent = "当前没有环境变量或命令行锁定的可编辑字段。";
        summary.className = "configuration-lock-summary muted";
        return;
    }
    summary.textContent = `以下字段由部署环境或命令行控制，Web 只读：${locks
        .map((lock) => `${lock.field} (${lock.controlling_keys.join(", ")})`)
        .join("；")}`;
    summary.className = "configuration-lock-summary active";
}
function openConfigurationEditor() {
    if (!currentConfiguration)
        return;
    clearConfigurationPreview();
    const editable = currentConfiguration.editable;
    setConfigurationValue("#configuration-outbound-proxy-url", editable.outbound_proxy_url ?? "");
    element("#configuration-outbound-proxy-hosts").value =
        editable.outbound_proxy_hosts.join("\n");
    setConfigurationValue("#configuration-mikan-url", editable.mikan_base_url);
    setConfigurationValue("#configuration-tmdb-url", editable.tmdb_base_url);
    setConfigurationValue("#configuration-tmdb-image-url", editable.tmdb_image_base_url);
    setConfigurationValue("#configuration-tmdb-language", editable.tmdb_language);
    setConfigurationValue("#configuration-tmdb-timeout", editable.tmdb_http_timeout_seconds);
    setConfigurationValue("#configuration-tmdb-retry-count", editable.tmdb_retry_count);
    setConfigurationValue("#configuration-tmdb-retry-delay", editable.tmdb_retry_delay_seconds);
    setConfigurationValue("#configuration-tmdb-cache-hours", editable.tmdb_cache_hours);
    setConfigurationValue("#configuration-tmdb-key", "");
    setConfigurationChecked("#configuration-tmdb-key-clear", false);
    element("#configuration-tmdb-key-state").textContent =
        configurationSecretLabel(editable.tmdb_api_key_state);
    setConfigurationValue("#configuration-tmdb-token", "");
    setConfigurationChecked("#configuration-tmdb-token-clear", false);
    element("#configuration-tmdb-token-state").textContent =
        configurationSecretLabel(editable.tmdb_read_access_token_state);
    setConfigurationValue("#configuration-bangumi-url", editable.bangumi_base_url);
    setConfigurationValue("#configuration-bangumi-timeout", editable.bangumi_http_timeout_seconds);
    setConfigurationValue("#configuration-bangumi-retry-count", editable.bangumi_retry_count);
    setConfigurationValue("#configuration-bangumi-retry-delay", editable.bangumi_retry_delay_seconds);
    setConfigurationChecked("#configuration-fail-skip", editable.season_failure_skip);
    setConfigurationChecked("#configuration-fail-backtrace", editable.season_failure_backtrace);
    setConfigurationChecked("#configuration-fail-title", editable.season_failure_use_title_season);
    setConfigurationChecked("#configuration-fail-first", editable.season_failure_use_first_season);
    setConfigurationValue("#configuration-ai-base-url", editable.ai_base_url ?? "");
    setConfigurationValue("#configuration-ai-model", editable.ai_model ?? "");
    setConfigurationValue("#configuration-ai-key", "");
    setConfigurationChecked("#configuration-ai-key-clear", false);
    element("#configuration-ai-key-state").textContent =
        configurationSecretLabel(editable.ai_api_key_state);
    setConfigurationValue("#configuration-ai-tmdb-mcp-url", editable.ai_tmdb_mcp_url);
    setConfigurationValue("#configuration-ai-bangumi-mcp-url", editable.ai_bangumi_mcp_url);
    setConfigurationChecked("#configuration-ai-metadata", editable.ai_use_metadata_match);
    setConfigurationChecked("#configuration-bangumi-fallback", editable.tmdb_failure_use_bangumi);
    setConfigurationChecked("#configuration-write-bangumi-with-tmdb", editable.write_bangumi_id_when_tmdb_matched);
    setConfigurationChecked("#configuration-offset-cache", editable.mikan_trusted_offset_cache_enabled);
    setConfigurationValue("#configuration-ai-timeout", editable.ai_http_timeout_seconds);
    setConfigurationValue("#configuration-torrent-timeout", editable.torrent_http_timeout_seconds);
    setConfigurationValue("#configuration-torrent-bytes", editable.torrent_max_response_bytes);
    setConfigurationValue("#configuration-torrent-redirects", editable.torrent_max_redirects);
    setConfigurationValue("#configuration-torrent-ttl", editable.torrent_staging_ttl_seconds);
    setConfigurationChecked("#configuration-data-update-enabled", editable.data_update_enabled);
    setConfigurationValue("#configuration-data-update-cron", editable.data_update_cron);
    setConfigurationValue("#configuration-data-update-manifest", editable.data_update_manifest_url ?? "");
    setConfigurationChecked("#configuration-data-update-auto-download", editable.data_update_auto_download);
    setConfigurationChecked("#configuration-data-update-auto-import", editable.data_update_auto_import);
    setConfigurationValue("#configuration-data-update-keep", editable.data_update_keep_versions);
    setConfigurationValue("#configuration-data-update-timeout", editable.data_update_http_timeout_seconds);
    applyConfigurationLocks(editable.locked_fields);
    element("#configuration-message").textContent =
        `正在编辑 revision ${currentConfiguration.configuration_revision}`;
    syncConfigurationSecretInputs();
    configurationDialog.showModal();
}
const configurationFieldLabels = {
    outbound_proxy_url: "全局代理地址",
    outbound_proxy_hosts: "使用代理的域名",
    mikan_base_url: "Mikan 地址",
    tmdb_base_url: "TMDB API 地址",
    tmdb_image_base_url: "TMDB 图片地址",
    tmdb_language: "TMDB 语言",
    tmdb_http_timeout_seconds: "TMDB 超时（秒）",
    tmdb_retry_count: "TMDB 额外重试次数",
    tmdb_retry_delay_seconds: "TMDB 重试间隔（秒）",
    tmdb_cache_hours: "TMDB 成功响应缓存（小时）",
    tmdb_api_key: "TMDB API Key",
    tmdb_read_access_token: "TMDB Read Token",
    bangumi_base_url: "Bangumi API 地址",
    bangumi_http_timeout_seconds: "Bangumi 超时（秒）",
    bangumi_retry_count: "Bangumi 额外重试次数",
    bangumi_retry_delay_seconds: "Bangumi 重试间隔（秒）",
    season_failure_skip: "TMDBFailSkip",
    season_failure_backtrace: "TMDBFailBacktrace",
    season_failure_use_title_season: "TMDBFailUseTitleSeason",
    season_failure_use_first_season: "TMDBFailUseFirstSeason",
    ai_base_url: "OpenAI-compatible API 地址",
    ai_model: "AI 模型",
    ai_api_key: "AI API Key",
    ai_tmdb_mcp_url: "TMDB MCP 地址",
    ai_bangumi_mcp_url: "Bangumi MCP 地址",
    ai_use_metadata_match: "AI 元数据匹配",
    ai_http_timeout_seconds: "AI 超时（秒）",
    tmdb_failure_use_bangumi: "Bangumi 完全兜底",
    write_bangumi_id_when_tmdb_matched: "TMDB 成功时写 Bangumi ID",
    mikan_trusted_offset_cache_enabled: "可信 offset 缓存",
    torrent_http_timeout_seconds: "Torrent HTTP 超时（秒）",
    torrent_max_response_bytes: "Torrent 最大响应（bytes）",
    torrent_max_redirects: "Torrent 最大跳转",
    torrent_staging_ttl_seconds: "Torrent 暂存 TTL（秒）",
    data_update_enabled: "AnimeGoNetData 定时更新",
    data_update_cron: "AnimeGoNetData Cron",
    data_update_manifest_url: "AnimeGoNetData Manifest URL",
    data_update_auto_download: "AnimeGoNetData 自动下载",
    data_update_auto_import: "AnimeGoNetData 自动导入",
    data_update_keep_versions: "AnimeGoNetData 保留版本数",
    data_update_http_timeout_seconds: "AnimeGoNetData HTTP 超时（秒）",
};
function configurationRequest() {
    if (!currentConfiguration) {
        throw new Error("配置尚未载入");
    }
    return {
        outbound_proxy_url: element("#configuration-outbound-proxy-url").value || null,
        outbound_proxy_hosts: element("#configuration-outbound-proxy-hosts")
            .value.split(/[,;\r\n]+/u)
            .map(value => value.trim().toLowerCase())
            .filter(value => value.length > 0),
        mikan_base_url: element("#configuration-mikan-url").value,
        tmdb_base_url: element("#configuration-tmdb-url").value,
        tmdb_image_base_url: element("#configuration-tmdb-image-url").value,
        tmdb_language: element("#configuration-tmdb-language").value,
        tmdb_http_timeout_seconds: element("#configuration-tmdb-timeout").valueAsNumber,
        tmdb_retry_count: element("#configuration-tmdb-retry-count").valueAsNumber,
        tmdb_retry_delay_seconds: element("#configuration-tmdb-retry-delay").valueAsNumber,
        tmdb_cache_hours: element("#configuration-tmdb-cache-hours").valueAsNumber,
        tmdb_api_key: element("#configuration-tmdb-key").value || null,
        clear_tmdb_api_key: element("#configuration-tmdb-key-clear").checked,
        tmdb_read_access_token: element("#configuration-tmdb-token").value || null,
        clear_tmdb_read_access_token: element("#configuration-tmdb-token-clear").checked,
        bangumi_base_url: element("#configuration-bangumi-url").value,
        bangumi_http_timeout_seconds: element("#configuration-bangumi-timeout").valueAsNumber,
        bangumi_retry_count: element("#configuration-bangumi-retry-count").valueAsNumber,
        bangumi_retry_delay_seconds: element("#configuration-bangumi-retry-delay").valueAsNumber,
        season_failure_skip: element("#configuration-fail-skip").checked,
        season_failure_backtrace: element("#configuration-fail-backtrace").checked,
        season_failure_use_title_season: element("#configuration-fail-title").checked,
        season_failure_use_first_season: element("#configuration-fail-first").checked,
        ai_base_url: element("#configuration-ai-base-url").value || null,
        ai_model: element("#configuration-ai-model").value || null,
        ai_api_key: element("#configuration-ai-key").value || null,
        clear_ai_api_key: element("#configuration-ai-key-clear").checked,
        ai_tmdb_mcp_url: element("#configuration-ai-tmdb-mcp-url").value,
        ai_bangumi_mcp_url: element("#configuration-ai-bangumi-mcp-url").value,
        ai_use_metadata_match: element("#configuration-ai-metadata").checked,
        ai_http_timeout_seconds: element("#configuration-ai-timeout").valueAsNumber,
        tmdb_failure_use_bangumi: element("#configuration-bangumi-fallback").checked,
        write_bangumi_id_when_tmdb_matched: element("#configuration-write-bangumi-with-tmdb").checked,
        mikan_trusted_offset_cache_enabled: element("#configuration-offset-cache").checked,
        torrent_http_timeout_seconds: element("#configuration-torrent-timeout").valueAsNumber,
        torrent_max_response_bytes: element("#configuration-torrent-bytes").valueAsNumber,
        torrent_max_redirects: element("#configuration-torrent-redirects").valueAsNumber,
        torrent_staging_ttl_seconds: element("#configuration-torrent-ttl").valueAsNumber,
        data_update_enabled: element("#configuration-data-update-enabled").checked,
        data_update_cron: element("#configuration-data-update-cron").value,
        data_update_manifest_url: element("#configuration-data-update-manifest").value || null,
        data_update_auto_download: element("#configuration-data-update-auto-download").checked,
        data_update_auto_import: element("#configuration-data-update-auto-import").checked,
        data_update_keep_versions: element("#configuration-data-update-keep").valueAsNumber,
        data_update_http_timeout_seconds: element("#configuration-data-update-timeout").valueAsNumber,
        expected_configuration_revision: currentConfiguration.configuration_revision,
    };
}
function clearConfigurationPreview(message) {
    pendingConfigurationRequest = null;
    const preview = element("#configuration-preview");
    preview.hidden = true;
    element("#configuration-preview-summary").textContent = "";
    element("#configuration-diff-list").replaceChildren();
    element("#configuration-confirm").disabled = true;
    if (message) {
        element("#configuration-message").textContent = message;
    }
}
function configurationPreviewValue(value, sensitive) {
    if (sensitive) {
        switch (value) {
            case "inherit": return "继承部署配置";
            case "configured": return "已配置（值已隐藏）";
            case "cleared": return "已明确清除";
            default: return "值已隐藏";
        }
    }
    if (value === null || value.length === 0)
        return "未配置";
    if (value === "true")
        return "已启用";
    if (value === "false")
        return "已关闭";
    return value;
}
function renderConfigurationPreview(preview) {
    const panel = element("#configuration-preview");
    const summary = element("#configuration-preview-summary");
    const list = element("#configuration-diff-list");
    panel.hidden = false;
    const restartChanges = preview.changes.filter((change) => change.effect === "restart").length;
    const hotChanges = preview.changes.filter((change) => change.effect === "hot_reload").length;
    summary.textContent = preview.changes.length === 0
        ? "没有检测到配置差异，无需保存。"
        : `共 ${preview.changes.length} 项：${hotChanges} 项保存后即时生效，`
            + `${restartChanges} 项需要重启；写入前会备份当前私有 revision。`;
    if (preview.changes.length === 0) {
        list.replaceChildren();
        return;
    }
    list.replaceChildren(...preview.changes.map((change) => {
        const item = document.createElement("article");
        item.className = "configuration-diff-item";
        const heading = document.createElement("div");
        heading.className = "configuration-diff-heading";
        const field = document.createElement("strong");
        field.textContent = configurationFieldLabels[change.field] ?? change.field;
        const effect = document.createElement("span");
        effect.className = `configuration-effect ${change.effect}`;
        effect.textContent = change.effect === "hot_reload" ? "即时生效" : "重启生效";
        heading.append(field, effect);
        const values = document.createElement("div");
        values.className = "configuration-diff-values";
        const before = document.createElement("span");
        before.textContent = configurationPreviewValue(change.before, change.sensitive);
        const arrow = document.createElement("span");
        arrow.className = "configuration-diff-arrow";
        arrow.textContent = "→";
        const after = document.createElement("span");
        after.textContent = configurationPreviewValue(change.after, change.sensitive);
        values.append(before, arrow, after);
        item.append(heading, values);
        return item;
    }));
}
async function previewConfiguration(event) {
    event.preventDefault();
    if (!currentConfiguration)
        return;
    const previewButton = element("#configuration-save");
    const message = element("#configuration-message");
    clearConfigurationPreview();
    previewButton.disabled = true;
    message.textContent = "正在验证并生成脱敏差异…";
    try {
        const request = configurationRequest();
        const requestHeaders = new Headers(headers);
        requestHeaders.set("Content-Type", "application/json");
        const response = await fetch("/api/v1/config/preview", {
            method: "POST",
            headers: requestHeaders,
            body: JSON.stringify(request),
        });
        if (!response.ok)
            throw new Error(await responseError(response));
        const preview = await response.json();
        renderConfigurationPreview(preview);
        pendingConfigurationRequest = preview.changes.length > 0 ? request : null;
        element("#configuration-confirm").disabled =
            pendingConfigurationRequest === null;
        message.textContent = preview.changes.length === 0
            ? "服务端验证通过；当前表单与已保存配置一致。"
            : preview.restart_required
                ? "服务端验证通过；确认后保存，进程仍需重启以应用非热更新字段。"
                : "服务端验证通过；确认后保存，所列字段可即时生效。";
    }
    catch (error) {
        clearConfigurationPreview();
        message.textContent =
            `预览失败：${errorMessage(error, "未知错误")}；revision 冲突时请刷新后重试。`;
    }
    finally {
        previewButton.disabled = false;
    }
}
async function confirmConfiguration() {
    const request = pendingConfigurationRequest;
    if (!request)
        return;
    const previewButton = element("#configuration-save");
    const confirm = element("#configuration-confirm");
    const message = element("#configuration-message");
    previewButton.disabled = true;
    confirm.disabled = true;
    message.textContent = "正在备份当前 revision 并写入私密配置覆盖…";
    try {
        const requestHeaders = new Headers(headers);
        requestHeaders.set("Content-Type", "application/json");
        const response = await fetch("/api/v1/config", {
            method: "PUT",
            headers: requestHeaders,
            body: JSON.stringify(request),
        });
        if (!response.ok)
            throw new Error(await responseError(response));
        const saved = await response.json();
        clearConfigurationPreview();
        configurationDialog.close();
        await loadConfiguration();
        const backup = saved.backup_revision === null
            ? "这是首个私有 revision，无旧版本需要备份"
            : `已备份 revision ${saved.backup_revision}`;
        element("#configuration-status").textContent = saved.restart_required
            ? `已保存 revision ${saved.configuration_revision}；${backup}；非热更新字段需重启。`
            : `已保存 revision ${saved.configuration_revision}；${backup}；修改已即时生效。`;
    }
    catch (error) {
        clearConfigurationPreview();
        message.textContent =
            `保存失败：${errorMessage(error, "未知错误")}；请重新预览后再保存。`;
    }
    finally {
        previewButton.disabled = false;
    }
}
async function resetConfiguration() {
    if (!currentConfiguration || currentConfiguration.configuration_revision === 0)
        return;
    if (!window.confirm("恢复部署默认配置？当前私有 revision 会先备份；数据更新策略会立即恢复，其他修改仍需重启。"))
        return;
    const status = element("#configuration-status");
    status.textContent = "正在移除私密配置覆盖…";
    try {
        const response = await fetch(`/api/v1/config?expected_revision=${currentConfiguration.configuration_revision}`, { method: "DELETE", headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        const saved = await response.json();
        await loadConfiguration();
        const backup = saved.backup_revision === null
            ? "没有需要备份的私有 revision"
            : `已备份 revision ${saved.backup_revision}`;
        status.textContent = saved.restart_required
            ? `已恢复部署默认；${backup}；非热更新字段需重启。`
            : `已恢复部署默认；${backup}；修改已即时生效。`;
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
function formatDuration(totalSeconds) {
    const seconds = Math.max(0, Math.floor(totalSeconds));
    const days = Math.floor(seconds / 86400);
    const hours = Math.floor((seconds % 86400) / 3600);
    const minutes = Math.floor((seconds % 3600) / 60);
    if (days > 0)
        return `${days}天 ${hours}小时`;
    if (hours > 0)
        return `${hours}小时 ${minutes}分钟`;
    if (minutes > 0)
        return `${minutes}分钟`;
    return `${seconds}秒`;
}
function seedingDescription(item) {
    if (item.seeding_target_minutes === 0)
        return "做种：不要求";
    const state = {
        waiting: "等待开始",
        seeding: "进行中",
        completed: "已完成",
        not_required: "不要求",
    }[item.seeding_state];
    const elapsed = formatDuration(item.seeding_elapsed_seconds);
    if (item.seeding_target_minutes === -1) {
        return `做种：${state} · 无限目标 · 已 ${elapsed}`;
    }
    const targetSeconds = item.seeding_target_minutes * 60;
    const percentage = Math.min(100, 100 * item.seeding_elapsed_seconds / targetSeconds);
    return `做种：${state} · ${elapsed} / ${formatDuration(targetSeconds)} · ${percentage.toFixed(1)}%`;
}
function downloadControlButton(item, action, label) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "secondary-button";
    button.textContent = label;
    button.addEventListener("click", () => void controlDownload(item, action, button));
    return button;
}
async function controlDownload(item, action, button) {
    button.disabled = true;
    const original = button.textContent ?? action;
    button.textContent = `${original}…`;
    try {
        const requestHeaders = new Headers(headers);
        requestHeaders.set("Content-Type", "application/json");
        const response = await fetch(`/api/v1/downloads/${encodeURIComponent(item.job_id)}/${action}`, {
            method: "POST",
            headers: requestHeaders,
            body: JSON.stringify({ expected_revision: item.revision }),
        });
        if (!response.ok)
            throw new Error(await responseError(response));
        await loadDownloads();
    }
    catch (error) {
        button.disabled = false;
        button.textContent = errorMessage(error, `${original}失败`);
    }
}
async function loadDownloadDetail(item, target, button) {
    expandedDownloadJobIds.add(item.job_id);
    button.disabled = true;
    button.textContent = "读取文件与时间线…";
    button.setAttribute("aria-expanded", "true");
    try {
        const response = await fetch(`/api/v1/downloads/${encodeURIComponent(item.job_id)}`, { headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        const detail = await response.json();
        const stages = document.createElement("dl");
        stages.className = "download-stage-grid";
        for (const [label, stage] of [
            ["下载前准备", detail.preparation],
            ["整理与清理", detail.organization],
        ]) {
            const group = document.createElement("div");
            const term = document.createElement("dt");
            term.textContent = label;
            const value = document.createElement("dd");
            value.textContent = `${stage.state} · 尝试 ${stage.attempt_count}`
                + (stage.failure_code ? ` · ${stage.failure_code}` : "")
                + (stage.next_attempt_at_utc
                    ? ` · 下次 ${new Date(stage.next_attempt_at_utc).toLocaleString()}`
                    : "");
            group.append(term, value);
            if (stage.phase) {
                const phase = document.createElement("small");
                phase.className = "download-stage-phase";
                phase.textContent = organizationPhaseLabel(stage.phase)
                    + (stage.total_units && stage.completed_units !== null
                        ? ` · ${stage.completed_units}/${stage.total_units}`
                        : "");
                group.append(phase);
                if (stage.total_units && stage.progress !== null) {
                    const progress = document.createElement("progress");
                    progress.className = "download-stage-progress";
                    progress.max = 1;
                    progress.value = Math.min(1, Math.max(0, stage.progress));
                    progress.setAttribute("aria-label", `${label}：${phase.textContent}`);
                    group.append(progress);
                }
            }
            stages.append(group);
        }
        const seedingGroup = document.createElement("div");
        const seedingTerm = document.createElement("dt");
        seedingTerm.textContent = "做种目标";
        const seedingValue = document.createElement("dd");
        seedingValue.textContent = seedingDescription(detail.summary)
            + (detail.summary.seeding_completed_at_utc
                ? ` · 完成于 ${new Date(detail.summary.seeding_completed_at_utc).toLocaleString()}`
                : "");
        seedingGroup.append(seedingTerm, seedingValue);
        stages.append(seedingGroup);
        if (detail.task_failure_kind || detail.task_failure_reason) {
            const failure = document.createElement("p");
            failure.className = "download-detail-failure";
            failure.textContent =
                `${textOrDash(detail.task_failure_kind)} · ${textOrDash(detail.task_failure_reason)}`;
            stages.append(failure);
        }
        const snapshot = document.createElement("p");
        snapshot.className = detail.file_snapshot_state === "live"
            ? "download-file-snapshot ready"
            : "download-file-snapshot error";
        snapshot.textContent = detail.file_snapshot_state === "live"
            ? "文件进度：qBittorrent 实时快照"
            : `文件进度不可用：${textOrDash(detail.file_snapshot_failure_code)}；仍显示 SQLite 已保存的 wanted / priority。`;
        const files = document.createElement("div");
        files.className = "download-file-list";
        if (detail.files.length === 0) {
            const empty = document.createElement("p");
            empty.className = "muted metadata-attempt-empty";
            empty.textContent = "该任务尚无文件分配记录。";
            files.append(empty);
        }
        else {
            for (const file of detail.files) {
                const row = document.createElement("article");
                row.className = `download-file ${file.wanted === false ? "unwanted" : ""}`;
                const heading = document.createElement("div");
                heading.className = "download-file-heading";
                const name = document.createElement("strong");
                name.textContent = file.relative_path;
                const wanted = document.createElement("span");
                wanted.className = `badge ${file.wanted === false ? "error" : "ready"}`;
                wanted.textContent = file.wanted === null
                    ? "尚未分配"
                    : file.wanted ? `Wanted · P${file.priority}` : "跳过下载";
                heading.append(name, wanted);
                const progress = document.createElement("progress");
                progress.max = 1;
                progress.value = file.progress ?? 0;
                const evidence = document.createElement("p");
                evidence.textContent = `#${textOrDash(file.file_index)} · ${formatBytes(file.downloaded_bytes ?? 0)} / ${formatBytes(file.size_bytes)} · ${(100 * (file.progress ?? 0)).toFixed(1)}% · ${file.disposition}${file.other_reason ? ` · ${file.other_reason}` : ""}`;
                row.append(heading, progress, evidence);
                files.append(row);
            }
        }
        const timelineHeading = document.createElement("h4");
        timelineHeading.textContent = "状态时间线";
        const timeline = document.createElement("div");
        timeline.className = "download-timeline";
        if (detail.timeline.length === 0) {
            const empty = document.createElement("p");
            empty.className = "muted metadata-attempt-empty";
            empty.textContent = "尚无下载审计事件。";
            timeline.append(empty);
        }
        else {
            for (const event of detail.timeline) {
                const row = document.createElement("article");
                row.className = `download-event ${event.result === "failed" ? "failed" : ""}`;
                const heading = document.createElement("strong");
                heading.textContent = `${event.kind} · ${event.result}`;
                const state = document.createElement("p");
                state.textContent = `${textOrDash(event.from_state)} → ${textOrDash(event.to_state)} · ${new Date(event.created_at_utc).toLocaleString()}${event.failure_code ? ` · ${event.failure_code}` : ""}`;
                row.append(heading, state);
                timeline.append(row);
            }
        }
        const controls = document.createElement("div");
        controls.className = "download-detail-actions";
        if (detail.can_pause) {
            controls.append(downloadControlButton(detail.summary, "pause", "暂停"));
        }
        if (detail.can_resume) {
            controls.append(downloadControlButton(detail.summary, "resume", "恢复"));
        }
        if (detail.can_retry) {
            controls.append(downloadControlButton(detail.summary, "retry", "业务重试"));
        }
        target.replaceChildren(stages, snapshot, files, timelineHeading, timeline, controls);
        button.disabled = false;
        button.textContent = "收起文件与时间线";
        button.onclick = () => {
            expandedDownloadJobIds.delete(item.job_id);
            target.replaceChildren();
            button.textContent = "查看文件与时间线";
            button.setAttribute("aria-expanded", "false");
            button.onclick = () => void loadDownloadDetail(item, target, button);
        };
    }
    catch (error) {
        target.textContent = `下载详情读取失败：${errorMessage(error, "未知错误")}`;
        button.disabled = false;
        button.textContent = "重试文件与时间线";
    }
}
function organizationPhaseLabel(phase) {
    return {
        not_started: "尚未开始",
        rename_planning: "文件解析与重命名规划",
        media_transfer: "媒体移动或链接",
        subtitle_transfer: "字幕关联与移动",
        nfo_write: "NFO 写入",
        directory_index: "目录数据库与索引",
        cleanup_downloader: "下载器清理",
        completed: "整理完成",
    }[phase] ?? phase;
}
function renderDownloadSummary(body) {
    const summary = body.summary;
    const metrics = [
        ["活动", String(summary.active_jobs)],
        ["暂停", String(summary.paused_jobs)],
        ["失败", String(summary.failed_jobs), summary.latest_failure_code ?? undefined],
        ["等待整理", String(summary.waiting_organization_jobs)],
        ["已完成", String(summary.completed_jobs)],
        ["连接速度", formatBytes(summary.connected_download_speed_bytes_per_second) + "/s"],
        ["过期快照", String(summary.stale_jobs)],
        ["离线实例", String(summary.offline_instance_count)],
    ];
    const cards = metrics.map(([label, value, detail]) => {
        const card = document.createElement("article");
        card.className = label === "失败" && summary.failed_jobs > 0
            ? "download-summary-card error"
            : "download-summary-card";
        const term = document.createElement("span");
        term.textContent = label;
        const strong = document.createElement("strong");
        strong.textContent = value;
        card.append(term, strong);
        if (detail) {
            const note = document.createElement("small");
            note.textContent = detail;
            card.append(note);
        }
        return card;
    });
    const footer = document.createElement("p");
    footer.className = "download-summary-footer";
    footer.textContent = `共 ${summary.total_jobs} 个任务`
        + ` · 准备失败 ${summary.preparation_failed_jobs}`
        + ` · 整理失败 ${summary.organization_failed_jobs}`
        + ` · 最近同步成功 ${summary.last_downloader_success_at_utc
            ? new Date(summary.last_downloader_success_at_utc).toLocaleString()
            : "尚无"}`;
    element("#download-summary").replaceChildren(...cards, footer);
}
function renderDownloadPage(body) {
    renderDownloadSummary(body);
    const container = element("#downloads");
    const totalPages = Math.max(1, Math.ceil(body.total_items / body.page_size));
    element("#download-list-status").textContent =
        `${body.total_items} 个任务 · 第 ${body.page} 页`;
    element("#download-page-status").textContent =
        `第 ${body.page} / ${totalPages} 页`;
    element("#download-previous").disabled = body.page <= 1;
    element("#download-next").disabled = body.page >= totalPages;
    if (body.items.length === 0) {
        renderRegionMessage(container, "empty", body.total_items === 0
            ? "暂无符合筛选条件的下载任务"
            : "当前页没有任务，请返回上一页。");
        return;
    }
    renderRegionContent(container, ...body.items.map((item) => {
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
            : `${item.state} · ${statusLabels[item.business_status] ?? item.business_status}`;
        heading.append(title, state);
        const progress = document.createElement("progress");
        progress.max = 1;
        progress.value = item.progress;
        const details = document.createElement("p");
        details.className = "download-details";
        details.textContent = `${item.source} → ${item.downloader_id} · ${(item.progress * 100).toFixed(1)}% · ${formatBytes(item.downloaded_bytes)} / ${formatBytes(item.total_bytes)} · ${formatBytes(item.speed_bytes_per_second)}/s · Seeds ${item.seeds} · Peers ${item.peers}`;
        const seeding = document.createElement("p");
        seeding.className = `download-seeding ${item.seeding_state}`;
        seeding.textContent = seedingDescription(item);
        const dynamicTags = document.createElement("p");
        dynamicTags.className = `download-dynamic-tags ${item.dynamic_tag_state}`;
        dynamicTags.textContent = item.dynamic_tag_state === "applied"
            ? `动态 Tags：${item.dynamic_tags.join(", ")}`
            : item.dynamic_tag_state === "skipped"
                ? `动态 Tags：已跳过（${item.dynamic_tag_failure_code ?? "未知原因"}）`
                : item.dynamic_tag_state === "pending"
                    ? "动态 Tags：等待元数据确认"
                    : "动态 Tags：未配置";
        const actions = document.createElement("div");
        actions.className = "download-actions";
        const expand = document.createElement("button");
        expand.type = "button";
        expand.className = "secondary-button";
        expand.textContent = "查看文件与时间线";
        expand.setAttribute("aria-expanded", "false");
        const detailTarget = document.createElement("div");
        detailTarget.className = "download-detail";
        expand.onclick = () => void loadDownloadDetail(item, detailTarget, expand);
        actions.append(expand);
        if (item.state === "paused") {
            actions.append(downloadControlButton(item, "resume", "恢复"));
        }
        else if (["waiting", "downloading", "moving", "seeding"].includes(item.state)) {
            actions.append(downloadControlButton(item, "pause", "暂停"));
        }
        else if (item.state === "error") {
            actions.append(downloadControlButton(item, "retry", "业务重试"));
        }
        const remove = document.createElement("button");
        remove.type = "button";
        remove.className = "delete-button";
        remove.textContent = "删除…";
        remove.addEventListener("click", () => void openDeletePreview(item.task_id));
        actions.append(remove);
        card.append(heading, progress, details, seeding, dynamicTags, actions, detailTarget);
        if (expandedDownloadJobIds.has(item.job_id)) {
            void loadDownloadDetail(item, detailTarget, expand);
        }
        return card;
    }));
}
async function loadDownloads() {
    const container = element("#downloads");
    setRegionState(container, "loading");
    const query = new URLSearchParams({
        page: String(downloadState.page),
        page_size: String(downloadState.page_size),
    });
    if (downloadState.search)
        query.set("search", downloadState.search);
    if (downloadState.state)
        query.set("state", downloadState.state);
    if (downloadState.business_status) {
        query.set("business_status", downloadState.business_status);
    }
    if (downloadState.downloader_id) {
        query.set("downloader_id", downloadState.downloader_id);
    }
    if (downloadState.source)
        query.set("source", downloadState.source);
    try {
        const response = await fetch(`/api/v1/downloads?${query}`, { headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        const body = await response.json();
        if (body.items.length === 0 && body.total_items > 0 && downloadState.page > 1) {
            downloadState.page = Math.max(1, Math.ceil(body.total_items / body.page_size));
            saveDownloadState();
            await loadDownloads();
            return;
        }
        downloadState.page = body.page;
        downloadState.page_size = body.page_size;
        saveDownloadState();
        renderDownloadPage(body);
    }
    catch (error) {
        renderRegionMessage(container, "error", `下载状态读取失败：${errorMessage(error, "未知错误")}`);
        element("#download-list-status").textContent = "下载任务读取失败";
    }
}
async function loadTrustedOffsets() {
    const container = element("#trusted-offsets");
    setRegionState(container, "loading");
    try {
        const response = await fetch("/api/v1/mikan/trusted-offsets", { headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        const body = await response.json();
        if (body.items.length === 0) {
            renderRegionMessage(container, "empty", "暂无自动 offset 学习证据");
            return;
        }
        renderRegionContent(container, ...body.items.map((item) => {
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
        renderRegionMessage(container, "error", `可信 offset 读取失败：${errorMessage(error, "未知错误")}`);
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
const bangumiFallbackDenialLabels = {
    tmdb_access_not_attempted: "尚未访问 TMDB",
    tmdb_access_not_confirmed: "TMDB 权威访问未确认（网络、服务、认证、配置或协议失败）",
    bangumi_subject_missing: "缺少有效 bgmid",
    bangumi_fallback_disabled: "Bangumi 完全兜底开关未启用",
    tmdb_series_resolved: "已经取得有效 TMDB Series；完全兜底不适用",
    metadata_lease_expired: "解析租约过期，必须重新匹配",
    tmdb_episode_validation_failed: "TMDB Episode 验证失败；不能降级为完全兜底",
    bangumi_fallback_pending: "满足前置条件，等待 Bangumi 完全兜底",
};
function metadataFallbackDecision(item) {
    if (item.latest_run_status !== "failed"
        && item.latest_run_status !== "fallback_resolved") {
        return null;
    }
    const decision = document.createElement("p");
    const used = item.latest_run_status === "fallback_resolved";
    const eligible = item.bangumi_fallback_eligible === true;
    decision.className = `metadata-fallback-decision ${used || eligible ? "allowed" : "denied"}`;
    if (used) {
        decision.textContent =
            "Bangumi 完全兜底：已允许并使用 · TMDB 权威访问已确认 · 固定本地 S01 · 不提供有效 tmdbid";
        return decision;
    }
    const access = item.tmdb_access_confirmed === true
        ? "TMDB 权威访问已确认"
        : "TMDB 权威访问未确认";
    const eligibility = eligible ? "允许" : "拒绝";
    const reason = item.bangumi_fallback_denial_reason === null
        ? "未记录原因"
        : bangumiFallbackDenialLabels[item.bangumi_fallback_denial_reason]
            ?? item.bangumi_fallback_denial_reason;
    decision.textContent = `Bangumi 完全兜底：${eligibility} · ${access} · ${reason}`;
    return decision;
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
const expandedMetadataDetailIds = new Set();
async function loadMetadataDetail(taskId, target, button) {
    expandedMetadataDetailIds.add(taskId);
    button.disabled = true;
    button.textContent = "读取来源 / TMDB 对照…";
    button.setAttribute("aria-expanded", "true");
    try {
        const response = await fetch(`/api/v1/metadata/tasks/${encodeURIComponent(taskId)}`, { headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        const detail = await response.json();
        const ai = document.createElement("article");
        ai.className = `metadata-ai ${detail.ai.status === "matched" ? "verified" : ""}`;
        const aiHeading = document.createElement("strong");
        aiHeading.textContent = `AI：${detail.ai.status === "not_attempted" ? "未调用" : detail.ai.status}`;
        const confidence = document.createElement("span");
        confidence.className =
            `badge ${detail.ai.confidence_basis === "tmdb_verified" ? "ready" : ""}`;
        confidence.textContent = detail.ai.confidence_basis === "tmdb_verified"
            ? "可信依据：TMDB 已验证"
            : "可信依据：未建立";
        const aiMeta = document.createElement("p");
        aiMeta.textContent = detail.ai.attempted_at_utc
            ? `${textOrDash(detail.ai.stage)} 阶段 · ${detail.ai.duration_ms} ms · ${new Date(detail.ai.attempted_at_utc).toLocaleString()}`
            : "模型自报置信度不被采信；只有 TMDB Series / Season / Episode 验证通过才建立可信结果。";
        ai.append(aiHeading, confidence, aiMeta);
        if (detail.ai.model !== null) {
            const usage = document.createElement("p");
            usage.className = "metadata-ai-usage";
            usage.textContent =
                `模型 ${detail.ai.model} · Prompt ${textOrDash(detail.ai.prompt_tokens)}`
                    + ` · Completion ${textOrDash(detail.ai.completion_tokens)}`
                    + ` · Total ${textOrDash(detail.ai.total_tokens)}`
                    + ` · HTTP ${textOrDash(detail.ai.request_count)}`
                    + ` · Tool calls ${textOrDash(detail.ai.tool_call_count)}`;
            ai.append(usage);
        }
        if (detail.ai.error_code || detail.ai.reason) {
            const reason = document.createElement("p");
            reason.className = "metadata-detail-reason";
            reason.textContent =
                `${textOrDash(detail.ai.error_code)} · ${textOrDash(detail.ai.reason)}`;
            ai.append(reason);
        }
        const sourceEvidence = document.createElement("section");
        sourceEvidence.className = "metadata-source-evidence";
        const sourceHeading = document.createElement("h4");
        sourceHeading.textContent = "来源持久证据（不作为 TMDB 规范字段）";
        const sourceTitle = document.createElement("strong");
        sourceTitle.textContent = detail.source_evidence.source_title;
        const sourceRoute = document.createElement("p");
        sourceRoute.textContent =
            `${detail.source_evidence.source_id} / ${detail.source_evidence.source_profile_id}`
                + ` rev ${detail.source_evidence.source_profile_revision}`;
        const sourceIds = document.createElement("p");
        sourceIds.textContent =
            `mikanid ${detail.source_evidence.mikanid ?? "—"} · groupid ${detail.source_evidence.groupid ?? "—"}`
                + ` · bgmid ${detail.source_evidence.bgmid ?? "—"} · AniDB ${detail.source_evidence.anidbid ?? "—"}`
                + ` · IMDb ${detail.source_evidence.imdbid ?? "—"}`;
        const sourceOpaqueIds = document.createElement("p");
        sourceOpaqueIds.textContent =
            `来源条目指纹 ${detail.source_evidence.source_item_id_fingerprint?.slice(0, 12) ?? "—"}`
                + ` · 来源作品指纹 ${detail.source_evidence.source_work_id_fingerprint?.slice(0, 12) ?? "—"}`;
        const sourcePublished = document.createElement("p");
        sourcePublished.textContent = detail.source_evidence.published_at
            ? `来源发布时间 ${new Date(detail.source_evidence.published_at).toLocaleString()}`
            : detail.source_evidence.published_at_raw_available
                ? "来源发布时间原文已保存，但没有可靠的规范时间"
                : "没有来源发布时间证据";
        sourceEvidence.append(sourceHeading, sourceTitle, sourceRoute, sourceIds, sourceOpaqueIds, sourcePublished);
        const rssEvidence = document.createElement("section");
        rssEvidence.className = "metadata-rss-evidence";
        if (detail.rss_evidence.length > 0) {
            const rssHeading = document.createElement("h4");
            rssHeading.textContent = "RSS 入口与文件候选审计";
            const rssExplanation = document.createElement("p");
            rssExplanation.className = "muted";
            rssExplanation.textContent =
                "按持久化关联展示 RSS 批次、筛选决策和统一导入任务；下方逐文件候选来自该任务实际 Torrent 文件名解析。";
            rssEvidence.append(rssHeading, rssExplanation);
            for (const evidence of detail.rss_evidence) {
                const row = document.createElement("article");
                row.className = "metadata-rss-evidence-row";
                const heading = document.createElement("div");
                const title = document.createElement("strong");
                title.textContent =
                    `${evidence.source_profile_id} · batch ${evidence.batch_id.slice(0, 12)}… · entry ${evidence.entry_ordinal}`;
                title.title = `batch_id=${evidence.batch_id}\nentry_ordinal=${evidence.entry_ordinal}`;
                const state = document.createElement("span");
                state.className = `badge ${evidence.effect_state === "ingested" ? "ready" : ""}`;
                state.textContent = evidence.effect_state;
                heading.append(title, state);
                const identity = document.createElement("p");
                identity.textContent =
                    `mikanid ${evidence.mikanid ?? "—"} · RSS EP ${textOrDash(evidence.source_episode_kind)}:${textOrDash(evidence.source_episode)} · ${new Date(evidence.batch_created_at_utc).toLocaleString()}`;
                const rules = document.createElement("p");
                rules.textContent =
                    `规则 rev ${evidence.rule_revision}（优选${evidence.priority_enabled ? "开启" : "关闭"}） · Legacy rev ${evidence.legacy_filter_revision}（${evidence.legacy_filter_enabled ? "开启" : "关闭"}）`;
                const decision = document.createElement("p");
                decision.textContent =
                    `${evidence.decision_kind} · ${evidence.decision_reason} · 有序规则组 ${evidence.evaluated_priority_groups.length === 0 ? "未执行" : evidence.evaluated_priority_groups.join(" → ")}`;
                const legacy = document.createElement("p");
                legacy.textContent =
                    `Legacy ${evidence.legacy_filter_state} · ${evidence.legacy_filter_reason} · ${textOrDash(evidence.legacy_filter_scope)} · identity ${evidence.identity_mikanid ?? "—"}/${evidence.identity_groupid ?? "—"}`;
                row.append(heading, identity, rules, decision, legacy);
                rssEvidence.append(row);
            }
        }
        const nfoRewrites = document.createElement("section");
        nfoRewrites.className = "metadata-nfo-rewrites";
        if (detail.nfo_rewrites.length > 0) {
            const nfoHeading = document.createElement("h4");
            nfoHeading.textContent = "TMDB 恢复后的 NFO 重写";
            nfoRewrites.append(nfoHeading);
            const stateLabels = {
                pending: "等待写入",
                writing: "正在写入",
                failed: "失败，等待自动重试",
                completed: "已完成",
            };
            for (const rewrite of detail.nfo_rewrites) {
                const row = document.createElement("article");
                row.className = `metadata-nfo-rewrite ${rewrite.state}`;
                const heading = document.createElement("div");
                const title = document.createElement("strong");
                title.textContent = `TMDB ${rewrite.tmdb_series_id} · bgmid ${rewrite.bgmid}`;
                const state = document.createElement("span");
                state.className = `badge ${rewrite.state === "failed" ? "error" : "ready"}`;
                state.textContent = stateLabels[rewrite.state] ?? rewrite.state;
                heading.append(title, state);
                const audit = document.createElement("p");
                audit.textContent = `已尝试 ${rewrite.attempt_count} 次 · 更新 ${new Date(rewrite.updated_at_utc).toLocaleString()}`;
                row.append(heading, audit);
                if (rewrite.failure_code !== null || rewrite.next_attempt_at_utc !== null) {
                    const retry = document.createElement("p");
                    retry.className = "metadata-detail-reason";
                    retry.textContent = `${textOrDash(rewrite.failure_code)} · 下次重试 ${rewrite.next_attempt_at_utc === null ? "—" : new Date(rewrite.next_attempt_at_utc).toLocaleString()}`;
                    row.append(retry);
                }
                if (rewrite.completed_at_utc !== null) {
                    const completed = document.createElement("p");
                    completed.textContent = `完成于 ${new Date(rewrite.completed_at_utc).toLocaleString()}`;
                    row.append(completed);
                }
                nfoRewrites.append(row);
            }
        }
        const files = document.createElement("div");
        files.className = "metadata-file-comparisons";
        if (detail.files.length === 0) {
            const empty = document.createElement("p");
            empty.className = "muted metadata-attempt-empty";
            empty.textContent = "该任务尚无文件条目。";
            files.append(empty);
        }
        else {
            for (const file of detail.files) {
                const row = document.createElement("article");
                row.className = `metadata-file-comparison ${file.disposition}`;
                const source = document.createElement("div");
                source.className = "metadata-file-source";
                const sourceName = document.createElement("strong");
                sourceName.textContent = file.source_name;
                const sourceEvidence = document.createElement("p");
                sourceEvidence.textContent =
                    `来源 EP ${textOrDash(file.source_episode)} · 文件名候选 ${textOrDash(file.file_episode_candidate)} · ${formatBytes(file.size_bytes)}`;
                source.append(sourceName, sourceEvidence);
                const arrow = document.createElement("span");
                arrow.className = "metadata-file-arrow";
                arrow.textContent = "→";
                arrow.setAttribute("aria-hidden", "true");
                const canonical = document.createElement("div");
                canonical.className = "metadata-file-canonical";
                const canonicalName = document.createElement("strong");
                canonicalName.textContent = file.tmdb_series_name
                    ? `${file.tmdb_series_name} / ${textOrDash(file.tmdb_season_name)}`
                    : "尚无经验证的 TMDB 映射";
                const canonicalEpisode = document.createElement("p");
                canonicalEpisode.textContent = file.tmdb_episode_number === null
                    ? `${file.disposition} · ${textOrDash(file.other_reason)}`
                    : `TMDB ${file.tmdb_series_id} · S${String(file.tmdb_season_number).padStart(2, "0")}E${String(file.tmdb_episode_number).padStart(3, "0")} · ${textOrDash(file.tmdb_episode_name)}`;
                canonical.append(canonicalName, canonicalEpisode);
                if (file.episode_strategy) {
                    const evidence = document.createElement("p");
                    evidence.className = "metadata-resolution-evidence";
                    evidence.textContent =
                        `Episode 取得：${libraryStrategy(file.episode_strategy)} · `
                            + resolutionReference(file.episode_run_id, file.episode_attempt_id);
                    evidence.title =
                        `run_id=${file.episode_run_id ?? "未记录"}\n`
                            + `attempt_id=${file.episode_attempt_id ?? "未记录"}`;
                    canonical.append(evidence);
                }
                row.append(source, arrow, canonical);
                files.append(row);
            }
        }
        target.replaceChildren(sourceEvidence, ai, ...(detail.rss_evidence.length > 0 ? [rssEvidence] : []), ...(detail.nfo_rewrites.length > 0 ? [nfoRewrites] : []), files);
        button.disabled = false;
        button.textContent = "收起来源 / TMDB 对照";
        button.onclick = () => {
            expandedMetadataDetailIds.delete(taskId);
            target.replaceChildren();
            button.textContent = "查看来源 / TMDB 对照";
            button.setAttribute("aria-expanded", "false");
            button.onclick = () => void loadMetadataDetail(taskId, target, button);
        };
    }
    catch (error) {
        target.textContent =
            `任务详情读取失败：${errorMessage(error, "未知错误")}`;
        button.disabled = false;
        button.textContent = "重试来源 / TMDB 对照";
    }
}
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
                if (attempt.ai_model !== null) {
                    const usage = document.createElement("p");
                    usage.className = "metadata-ai-usage";
                    usage.textContent =
                        `AI ${attempt.ai_model} · Prompt ${textOrDash(attempt.ai_prompt_tokens)}`
                            + ` · Completion ${textOrDash(attempt.ai_completion_tokens)}`
                            + ` · Total ${textOrDash(attempt.ai_total_tokens)}`
                            + ` · HTTP ${textOrDash(attempt.ai_request_count)}`
                            + ` · Tool calls ${textOrDash(attempt.ai_tool_call_count)}`;
                    row.append(usage);
                }
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
    setRegionState(container, "loading");
    const query = new URLSearchParams({
        page: String(metadataState.page),
        page_size: String(metadataState.page_size),
        handling: metadataState.handling,
        retryability: metadataState.retryability,
        sort: metadataState.sort,
        direction: metadataState.direction,
    });
    if (metadataState.search)
        query.set("search", metadataState.search);
    if (metadataState.status)
        query.set("status", metadataState.status);
    if (metadataState.failure_stage) {
        query.set("failure_stage", metadataState.failure_stage);
    }
    if (metadataState.error_code)
        query.set("error_code", metadataState.error_code);
    try {
        const response = await fetch(`/api/v1/metadata/tasks?${query}`, { headers });
        if (!response.ok)
            throw new Error(`HTTP ${response.status}`);
        const body = await response.json();
        if (body.items.length === 0 && body.total_items > 0 && metadataState.page > 1) {
            metadataState.page = Math.max(1, Math.ceil(body.total_items / body.page_size));
            saveMetadataState();
            await loadMetadataTasks();
            return;
        }
        metadataState.page = body.page;
        metadataState.page_size = body.page_size;
        const totalPages = Math.max(1, Math.ceil(body.total_items / body.page_size));
        element("#metadata-list-status").textContent =
            `${body.total_items} 个任务 · 第 ${body.page} 页 · ${body.sort} ${body.direction}`;
        element("#metadata-page-status").textContent =
            `第 ${body.page} / ${totalPages} 页`;
        element("#metadata-previous").disabled = body.page <= 1;
        element("#metadata-next").disabled = body.page >= totalPages;
        if (body.items.length === 0) {
            renderRegionMessage(container, "empty", "暂无符合筛选条件的元数据任务");
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
            const handling = document.createElement("p");
            handling.className = `metadata-handling ${item.handling_category}`;
            const handlingLabels = {
                explicit_retry: "可安全重试（需显式）",
                configuration: "需修复配置",
                manual: "需人工处理",
                skipped: "已跳过",
                fallback: "已兜底 · 待补全 TMDB",
                active: "处理中",
                resolved: "已解析",
                other: "其他",
            };
            handling.textContent = handlingLabels[item.handling_category]
                ?? item.handling_category;
            const identity = document.createElement("p");
            identity.className = "metadata-identity";
            identity.textContent = `${item.source} · mikanid ${textOrDash(item.mikanid)} · bgmid ${textOrDash(item.bgmid)} · TMDB ${textOrDash(item.tmdb_series_id)} / S${item.tmdb_season_number === null ? "—" : String(item.tmdb_season_number).padStart(2, "0")}`;
            const stages = document.createElement("dl");
            stages.className = "metadata-stages";
            for (const [label, value, runId, attemptId, mixed] of [
                ["Series", item.series_strategy, item.series_run_id, item.series_attempt_id, false],
                ["Season", item.season_strategy, item.season_run_id, item.season_attempt_id, false],
                [
                    "Episode",
                    item.episode_strategy,
                    item.episode_run_id,
                    item.episode_attempt_id,
                    item.episode_resolution_mixed,
                ],
            ]) {
                const group = document.createElement("div");
                const term = document.createElement("dt");
                term.textContent = String(label);
                const description = document.createElement("dd");
                description.textContent = mixed
                    ? "多个文件使用不同来源或证据（见文件详情）"
                    : libraryStrategy(value);
                if (!mixed && value) {
                    description.title =
                        `${resolutionReference(runId, attemptId)}\n`
                            + `run_id=${runId ?? "未记录"}\n`
                            + `attempt_id=${attemptId ?? "未记录"}`;
                    const reference = document.createElement("small");
                    reference.className = "metadata-resolution-reference";
                    reference.textContent = resolutionReference(runId, attemptId);
                    description.append(document.createElement("br"), reference);
                }
                group.append(term, description);
                stages.append(group);
            }
            const files = document.createElement("p");
            files.className = "metadata-files";
            files.textContent = `已确认 ${item.episode_file_count} · 已跳过重复 ${item.duplicate_file_count} · Other ${item.other_file_count} · 待处理 ${item.pending_file_count}`;
            card.append(heading, handling, identity, stages, files);
            if (item.failure_kind || item.failure_reason) {
                const failure = document.createElement("p");
                failure.className = "metadata-failure";
                failure.textContent =
                    `${textOrDash(item.failure_stage)} · ${textOrDash(item.failure_code ?? item.failure_kind)}`
                        + ` · ${item.failure_retryable === null
                            ? "可重试性未建立"
                            : item.failure_retryable ? "可重试" : "不可重试"}`
                        + ` · ${textOrDash(item.failure_reason)}`;
                card.append(failure);
            }
            const fallbackDecision = metadataFallbackDecision(item);
            if (fallbackDecision)
                card.append(fallbackDecision);
            const actions = document.createElement("div");
            actions.className = "metadata-actions";
            const detailButton = document.createElement("button");
            detailButton.type = "button";
            detailButton.className = "metadata-attempt-button";
            detailButton.textContent = "查看来源 / TMDB 对照";
            detailButton.setAttribute("aria-expanded", "false");
            const detailTarget = document.createElement("div");
            detailTarget.className = "metadata-detail";
            detailButton.onclick = () => void loadMetadataDetail(item.task_id, detailTarget, detailButton);
            const attempts = document.createElement("button");
            attempts.type = "button";
            attempts.className = "metadata-attempt-button";
            attempts.textContent = "查看策略时间线";
            const attemptList = document.createElement("div");
            attemptList.className = "metadata-attempt-list";
            attempts.onclick = () => void loadMetadataAttempts(item.task_id, attemptList, attempts);
            actions.append(detailButton, attempts);
            if (item.status === "metadata_failed") {
                const retry = document.createElement("button");
                retry.type = "button";
                retry.className = "retry-button";
                retry.textContent = "显式重新匹配";
                retry.addEventListener("click", () => void retryMetadataTask(item.task_id, retry));
                actions.append(retry);
            }
            card.append(actions, detailTarget, attemptList);
            if (expandedMetadataDetailIds.has(item.task_id)) {
                void loadMetadataDetail(item.task_id, detailTarget, detailButton);
            }
            if (expandedMetadataTaskIds.has(item.task_id)) {
                void loadMetadataAttempts(item.task_id, attemptList, attempts);
            }
            return card;
        });
        renderRegionContent(container, ...cards);
    }
    catch (error) {
        renderRegionMessage(container, "error", `元数据状态读取失败：${errorMessage(error, "未知错误")}`);
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
    setRegionState(container, "loading");
    try {
        const response = await fetch("/api/v1/metadata/pending-tmdb", { headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        const body = await response.json();
        if (body.items.length === 0) {
            renderRegionMessage(container, "empty", "暂无待补全 TMDB 的作品");
            return;
        }
        renderRegionContent(container, ...body.items.map((item) => {
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
        renderRegionMessage(container, "error", `待补全状态读取失败：${errorMessage(error, "未知错误")}`);
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
const downloaderEditableFields = [
    ["base_url", "#downloader-config-url"],
    ["username", "#downloader-config-username"],
    ["password", "#downloader-config-password"],
    ["download_path", "#downloader-config-path"],
    ["enabled", "#downloader-config-enabled"],
];
function applyDownloaderFieldLocks(instance) {
    const locks = new Map((instance?.locked_fields ?? []).map((lock) => [lock.field, lock]));
    for (const [field, selector] of downloaderEditableFields) {
        const input = element(selector);
        const lock = locks.get(field);
        input.disabled = lock !== undefined;
        const label = input.closest("label");
        label?.classList.toggle("configuration-field-locked", lock !== undefined);
        if (lock) {
            label?.setAttribute("title", `由 ${lock.source} 控制：${lock.controlling_keys.join(", ")}`);
        }
        else {
            label?.removeAttribute("title");
        }
    }
    const passwordLocked = locks.has("password");
    const clearPassword = element("#downloader-config-clear-password");
    clearPassword.disabled = passwordLocked;
    clearPassword.closest("label")?.classList.toggle("configuration-field-locked", passwordLocked);
    element("#downloader-config-save").disabled =
        instance !== null
            && downloaderEditableFields.every(([field]) => locks.has(field));
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
    applyDownloaderFieldLocks(instance);
    element("#downloader-config-delete").disabled =
        instance?.configuration_source !== "private_override";
    const credentialState = instance?.credentials_configured
        ? "已有凭据已配置；密码字段留空会保留，且不会从服务端读回。"
        : "当前没有已配置凭据。";
    const lockState = instance && instance.locked_fields.length > 0
        ? ` 部署锁：${instance.locked_fields.map((lock) => `${lock.field}（${lock.controlling_keys.join(" / ")}）`).join("、")}；锁定字段只读且不会写入私有覆盖。`
        : "";
    element("#downloader-config-message").textContent =
        credentialState + lockState;
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
        const instance = downloaderInstances.find((item) => item.id === activeDownloaderId) ?? null;
        const allFieldsLocked = instance !== null
            && downloaderEditableFields.every(([field]) => instance.locked_fields.some((lock) => lock.field === field));
        save.disabled = allFieldsLocked;
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
    setRegionState(list, "loading");
    status.textContent = "正在读取下载器实例…";
    try {
        const response = await fetch("/api/v1/downloaders", { headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        const body = await response.json();
        downloaderInstances = body.items;
        downloaderConfigurationRevision = body.configuration_revision;
        refreshSourceDownloaderOptions();
        const cards = downloaderInstances.map((instance) => {
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
                ["部署锁", instance.locked_fields.length === 0
                        ? "无"
                        : instance.locked_fields.map((lock) => lock.field).join("、")],
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
        });
        if (cards.length === 0) {
            renderRegionMessage(list, "empty", "尚未配置 qBittorrent 实例。");
        }
        else {
            renderRegionContent(list, ...cards);
        }
        status.textContent = body.downloads_blocked
            ? `下载已被 ${body.migration_diagnostics.map((item) => item.code).join("、")} 阻断；不会连接或启动任何下载器任务`
            : body.restart_required
                ? `${body.items.length} 个实例 · 私有配置 revision ${body.configuration_revision} 尚未应用，请重启`
                : `${body.items.length} 个 qBittorrent 实例 · 凭据只显示是否配置`;
    }
    catch (error) {
        const message = `下载器读取失败：${errorMessage(error, "未知错误")}`;
        renderRegionMessage(list, "error", message);
        status.textContent = message;
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
function updateSourceCredentialInputs() {
    const adapter = element("#source-adapter").value;
    const input = element("#source-mikan-cookie");
    const clear = element("#source-mikan-cookie-clear");
    const current = activeSource();
    const isMikan = adapter === "mikan";
    const cookieLock = current?.locked_fields.find((lock) => lock.field === "mikan_identity_cookie");
    input.disabled = !isMikan || clear.checked || cookieLock !== undefined;
    clear.disabled = !isMikan || current === null || cookieLock !== undefined;
    if (!isMikan || clear.checked)
        input.value = "";
    element("#source-mikan-cookie-state").textContent = cookieLock
        ? `部署锁只读（${cookieLock.controlling_keys.join(" / ")}），值永不回显。`
        : !isMikan
            ? "仅 Mikan 适配器可配置登录 Cookie。"
            : current?.mikan_identity_cookie_configured
                ? "已配置（值永不回显）；留空保持不变。"
                : "未配置；可粘贴 Cookie 值或完整 Cookie。";
    const rssUrl = element("#source-rss-url");
    const clearRssUrl = element("#source-rss-url-clear");
    const rssCron = element("#source-rss-cron");
    const scheduleEnabled = element("#source-rss-schedule-enabled");
    const sourceEnabled = element("#source-enabled").checked;
    rssUrl.disabled = !isMikan || clearRssUrl.checked;
    clearRssUrl.disabled = !isMikan || current === null;
    rssCron.disabled = !isMikan;
    scheduleEnabled.disabled = !isMikan || !sourceEnabled;
    if (!isMikan || clearRssUrl.checked)
        rssUrl.value = "";
    if (!isMikan || !sourceEnabled || clearRssUrl.checked)
        scheduleEnabled.checked = false;
    element("#source-rss-url-state").textContent = !isMikan
        ? "仅 Mikan 适配器可配置 RSS URL。"
        : clearRssUrl.checked
            ? "保存后明确清除 RSS URL，并关闭自动调度。"
            : current?.rss_feed_url_configured
                ? "已保存于服务端数据目录（值永不回显）；留空保持不变。"
                : "未配置；启用自动调度前必须填写。";
    const scheduleState = element("#source-rss-schedule-state");
    if (!scheduleEnabled.checked) {
        scheduleState.textContent = current?.rss_schedule_enabled
            ? "保存后关闭并移除自动调度。"
            : "RSS 自动调度未启用。";
    }
    else if (!current || !current.rss_schedule_enabled) {
        scheduleState.textContent = "保存后注册自动调度。";
    }
    else {
        const registered = current.rss_schedule_registered
            ? `已注册 · 下次 ${dataUpdateTime(current.rss_schedule_next_at_utc)}`
            : "已配置但当前未注册（后台工作器未运行）";
        const last = current.rss_last_run_state === "never"
            ? "尚未执行"
            : `${current.rss_last_run_state} · 完成 ${dataUpdateTime(current.rss_last_completed_at_utc)}`;
        scheduleState.textContent = `${registered} · ${last}${current.rss_last_failure_code ? ` · ${current.rss_last_failure_code}` : ""}${current.rss_last_batch_id ? ` · batch ${current.rss_last_batch_id}` : ""}`;
    }
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
    const category = element("#source-category");
    const dynamicTag = element("#source-dynamic-tag");
    const categoryLock = profile?.locked_fields.find((lock) => lock.field === "category");
    const dynamicTagLock = profile?.locked_fields.find((lock) => lock.field === "dynamic_tag_template");
    category.value = profile?.category ?? "animegonet";
    category.disabled = categoryLock !== undefined;
    category.title = categoryLock
        ? `部署锁：${categoryLock.controlling_keys.join(" / ")}`
        : "";
    element("#source-tags").value = profile?.tags.join(", ") ?? "";
    dynamicTag.value = profile?.dynamic_tag_template ?? "";
    dynamicTag.disabled = dynamicTagLock !== undefined;
    dynamicTag.title = dynamicTagLock
        ? `部署锁：${dynamicTagLock.controlling_keys.join(" / ")}`
        : "";
    element("#source-seeding-time").value =
        String(profile?.seeding_time_minutes ?? 0);
    element("#source-hosts").value = profile?.allowed_torrent_hosts.join("\n") ?? "";
    element("#source-enabled").checked = profile?.enabled ?? true;
    element("#source-filter-enabled").checked = profile?.rss_filter_enabled ?? false;
    element("#source-priority-enabled").checked = profile?.rss_priority_enabled ?? false;
    element("#source-duplicate-notification-enabled").checked =
        profile?.duplicate_notification_enabled ?? true;
    element("#source-mikan-cookie").value = "";
    element("#source-mikan-cookie-clear").checked = false;
    element("#source-rss-url").value = "";
    element("#source-rss-url-clear").checked = false;
    element("#source-rss-cron").value =
        profile?.rss_schedule_cron ?? "0 0/15 * * * ?";
    element("#source-rss-schedule-enabled").checked =
        profile?.rss_schedule_enabled ?? false;
    const remove = element("#source-delete");
    remove.disabled = profile === null || profile.is_default;
    remove.title = profile?.is_default ? "默认 Mikan 来源不可删除" : "";
    element("#route-preview-run").disabled = profile === null;
    element("#route-preview-result").textContent = profile === null
        ? "请先保存来源，再按持久化 revision 计算路由。"
        : `${profile.id} revision ${profile.revision}，等待预览。`;
    updateSourceWarning();
    updateSourceCredentialInputs();
    renderSourceList();
}
function renderSourceList() {
    const list = element("#source-list");
    if (sourceProfiles.length === 0) {
        renderRegionMessage(list, "empty", "暂无来源");
        return;
    }
    renderRegionContent(list, ...sourceProfiles.map((profile) => {
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
        const lockState = profile.locked_fields.length > 0
            ? ` · 部署锁 ${profile.locked_fields.map((lock) => lock.field).join("/")}`
            : "";
        route.textContent = `${profile.adapter} → ${profile.downloader_id} · ${profile.file_strategy} · ${profile.category} · 重复通知 ${profile.duplicate_notification_enabled ? "开启" : "关闭"} · 动态 Tag ${profile.dynamic_tag_template ?? "关闭"} · 做种 ${profile.seeding_time_minutes} 分钟 · Mikan Cookie ${profile.mikan_identity_cookie_configured ? "已配置" : "未配置"}${lockState} · RSS 调度 ${profile.rss_schedule_enabled ? profile.rss_last_run_state : "关闭"} · 任务 ${profile.ingest_task_count} / RSS ${profile.rss_batch_count}`;
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
                `动态 Tag 模板 ${route.dynamic_tag_template ?? "关闭"}`,
                `做种 ${route.seeding_time_minutes} 分钟 · RSS规则 rev ${route.rss_rule_revision ?? "—"}`,
                `重复命中通知 ${route.duplicate_notification_enabled ? "开启" : "关闭"}（不改变全局去重）`,
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
    const list = element("#source-list");
    setRegionState(list, "loading");
    status.textContent = "正在读取来源配置…";
    try {
        const response = await fetch("/api/v1/sources", { headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        const body = await response.json();
        sourceProfiles = body.items;
        refreshSourceAdapterOptions();
        refreshSourceDownloaderOptions();
        refreshManualSourceOptions();
        const selected = sourceProfiles.find((profile) => profile.id === (selectedId ?? activeSourceId))
            ?? sourceProfiles[0]
            ?? null;
        populateSourceForm(selected);
        status.textContent = `${sourceProfiles.length} 个来源 · 修改采用 revision 乐观并发且不改变历史任务路由`;
        if (activeLegacyMikanFilter)
            renderLegacyMikanFilter();
    }
    catch (error) {
        sourceProfiles = [];
        activeSourceId = null;
        refreshSourceAdapterOptions();
        refreshManualSourceOptions();
        const message = `来源读取失败：${errorMessage(error, "未知错误")}`;
        renderRegionMessage(list, "error", message);
        status.textContent = message;
    }
}
function refreshSourceAdapterOptions() {
    const select = element("#source-adapter");
    const previous = select.value;
    const entries = new Map([
        ["mikan", { label: "Mikan", enabled: true }],
        ["u2", { label: "U2", enabled: true }],
        ["ttg", { label: "TTG", enabled: true }],
    ]);
    for (const adapter of externalSourceAdapters) {
        entries.set(adapter.id, {
            label: `${adapter.name} (${adapter.id})${adapter.enabled ? "" : " · 插件未启用"}`,
            enabled: adapter.enabled,
        });
    }
    for (const profile of sourceProfiles) {
        if (!entries.has(profile.adapter)) {
            entries.set(profile.adapter, {
                label: `${profile.adapter} · 插件包不可用`,
                enabled: false,
            });
        }
    }
    const options = [...entries.entries()].map(([id, entry]) => {
        const option = document.createElement("option");
        option.value = id;
        option.textContent = entry.label;
        option.disabled = !entry.enabled;
        return option;
    });
    select.replaceChildren(...options);
    select.value = entries.has(previous) ? previous : "u2";
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
            `批次 ${body.batch_id} · mikanid ${body.mikanid ?? "未识别"} · `
                + `bgmid ${body.bgmid ?? "未取得"}（${body.bgmid_discovery_state}`
                + `${body.bgmid_discovery_failure_code ? ` / ${body.bgmid_discovery_failure_code}` : ""}）`
                + ` · 接收 ${accepted}/${body.items.length} · 规则 rev ${body.rule_revision}`;
        result.replaceChildren(summary, ...body.items.map((item, index) => manualResultItem(`候选 ${index + 1} · ${rssStatusLabels[item.status] ?? item.status}`, [
            item.decision_kind,
            item.decision_reason,
            item.status === "already_completed"
                ? "命中完成记录的来源别名，未抓取 Torrent"
                : null,
            item.ingest_task_id ? `任务 ${item.ingest_task_id}` : null,
            item.errors.length > 0 ? item.errors.join("；") : null,
        ].filter((value) => value !== null).join(" · "), !["staged", "blocked", "already_ingested", "already_completed"]
            .includes(item.status))));
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
        dynamic_tag_template: element("#source-dynamic-tag").value,
        seeding_time_minutes: element("#source-seeding-time").valueAsNumber,
        allowed_torrent_hosts: sourceHosts(),
        rss_filter_enabled: element("#source-filter-enabled").checked,
        rss_priority_enabled: element("#source-priority-enabled").checked,
        duplicate_notification_enabled: element("#source-duplicate-notification-enabled").checked,
        enabled: element("#source-enabled").checked,
        mikan_identity_cookie: element("#source-mikan-cookie").value || null,
        rss_feed_url: element("#source-rss-url").value || null,
        rss_schedule_enabled: element("#source-rss-schedule-enabled").checked,
        rss_schedule_cron: element("#source-rss-cron").value.trim(),
    };
    const payload = current
        ? {
            ...common,
            clear_mikan_identity_cookie: element("#source-mikan-cookie-clear").checked,
            clear_rss_feed_url: element("#source-rss-url-clear").checked,
            expected_revision: current.revision,
        }
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
    const snapshots = element("#rss-rule-snapshots");
    snapshots.replaceChildren(...activeRssRules.snapshots.map((snapshot) => {
        const option = document.createElement("option");
        option.value = String(snapshot.revision);
        option.textContent =
            `r${snapshot.revision} · ${new Date(snapshot.created_at_utc).toLocaleString()}`;
        return option;
    }));
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
async function rollbackRssRules() {
    if (!activeRssRules)
        return;
    const target = Number(element("#rss-rule-snapshots").value);
    if (!Number.isInteger(target) || target < 1 || target === activeRssRules.revision)
        return;
    if (!window.confirm(`将候选规则回滚为 revision ${target}？系统会创建新的 revision，历史快照不会删除。`))
        return;
    const status = element("#rss-rule-status");
    const rollback = element("#rss-rule-rollback");
    rollback.disabled = true;
    status.textContent = `正在回滚到 revision ${target}…`;
    try {
        const requestHeaders = new Headers(headers);
        requestHeaders.set("Content-Type", "application/json");
        const response = await fetch("/api/v1/rss-rules/mikan/rollback", {
            method: "POST",
            headers: requestHeaders,
            body: JSON.stringify({
                expected_revision: activeRssRules.revision,
                target_revision: target,
            }),
        });
        if (!response.ok)
            throw new Error(await responseError(response));
        activeRssRules = await response.json();
        renderRssRules();
        status.textContent = `已回滚并创建 revision ${activeRssRules.revision}`;
    }
    catch (error) {
        status.textContent =
            `回滚失败：${errorMessage(error, "未知错误")}；revision 冲突时请重新载入。`;
    }
    finally {
        rollback.disabled = false;
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
function legacyTierRules(tier) {
    return (activeLegacyMikanFilter?.rules ?? [])
        .filter((rule) => rule.tier === tier)
        .sort((left, right) => left.position - right.position);
}
function normalizeLegacyTier(tier) {
    legacyTierRules(tier).forEach((rule, position) => { rule.position = position; });
}
function moveLegacyRule(rule, delta) {
    const rules = legacyTierRules(rule.tier);
    const index = rules.indexOf(rule);
    const target = index + delta;
    if (index < 0 || target < 0 || target >= rules.length)
        return;
    [rules[index].position, rules[target].position] =
        [rules[target].position, rules[index].position];
    renderLegacyMikanFilter();
}
function removeLegacyRule(rule) {
    if (!activeLegacyMikanFilter)
        return;
    const index = activeLegacyMikanFilter.rules.indexOf(rule);
    if (index >= 0)
        activeLegacyMikanFilter.rules.splice(index, 1);
    normalizeLegacyTier(rule.tier);
    renderLegacyMikanFilter();
}
function renderLegacyRule(rule) {
    const tierRules = legacyTierRules(rule.tier);
    const tierIndex = tierRules.indexOf(rule);
    const card = document.createElement("div");
    card.className = "legacy-filter-rule";
    card.dataset.ruleIndex = String(activeLegacyMikanFilter.rules.indexOf(rule));
    const keyLabel = document.createElement("label");
    keyLabel.textContent = "旧版键（区分大小写）";
    const key = document.createElement("input");
    key.value = rule.key;
    key.maxLength = 1024;
    key.addEventListener("input", () => { rule.key = key.value; });
    keyLabel.append(key);
    const switches = document.createElement("div");
    switches.className = "legacy-filter-rule-switches";
    const whitelistSwitch = document.createElement("input");
    whitelistSwitch.type = "checkbox";
    whitelistSwitch.checked = rule.whitelist_enabled;
    whitelistSwitch.addEventListener("change", () => {
        rule.whitelist_enabled = whitelistSwitch.checked;
        renderLegacyWarnings();
    });
    const whitelistSwitchLabel = document.createElement("label");
    whitelistSwitchLabel.append(whitelistSwitch, "启用白名单");
    const blacklistSwitch = document.createElement("input");
    blacklistSwitch.type = "checkbox";
    blacklistSwitch.checked = rule.blacklist_enabled;
    blacklistSwitch.addEventListener("change", () => {
        rule.blacklist_enabled = blacklistSwitch.checked;
        renderLegacyWarnings();
    });
    const blacklistSwitchLabel = document.createElement("label");
    blacklistSwitchLabel.append(blacklistSwitch, "启用黑名单");
    switches.append(whitelistSwitchLabel, blacklistSwitchLabel);
    const valueEditor = (title, kind, values) => {
        const label = document.createElement("label");
        label.textContent = `${title}（JSON 字符串数组）`;
        const textarea = document.createElement("textarea");
        textarea.className = "legacy-filter-values";
        textarea.dataset.kind = kind;
        textarea.rows = 3;
        textarea.spellcheck = false;
        textarea.value = JSON.stringify(values);
        textarea.addEventListener("input", () => {
            textarea.classList.remove("invalid");
        });
        label.append(textarea);
        return label;
    };
    const actions = document.createElement("div");
    actions.className = "legacy-filter-rule-actions";
    const up = button("上移", () => moveLegacyRule(rule, -1));
    up.disabled = tierIndex === 0;
    const down = button("下移", () => moveLegacyRule(rule, 1));
    down.disabled = tierIndex + 1 === tierRules.length;
    actions.append(up, down, button("删除", () => removeLegacyRule(rule)));
    card.append(keyLabel, switches, valueEditor("白名单", "whitelist", rule.whitelist), valueEditor("黑名单", "blacklist", rule.blacklist), actions);
    return card;
}
function renderLegacyWarnings() {
    const warning = element("#legacy-filter-warning");
    if (!activeLegacyMikanFilter) {
        warning.textContent = "";
        return;
    }
    const messages = [];
    if (legacyTierRules(0).length > 1) {
        messages.push("F0 有多条规则：上游语义是全部执行、最后一条结果覆盖前面结果，不是 AND。");
    }
    const emptyRules = activeLegacyMikanFilter.rules.filter((rule) => (rule.whitelist_enabled && rule.whitelist.includes(""))
        || (rule.blacklist_enabled && rule.blacklist.includes("")));
    if (emptyRules.length > 0) {
        messages.push(`有 ${emptyRules.length} 条启用规则包含空关键词；空字符串会匹配所有标题。`);
    }
    warning.textContent = messages.join(" ");
}
function renderLegacyMikanFilter() {
    if (!activeLegacyMikanFilter)
        return;
    for (let tier = 0; tier <= 4; tier += 1) {
        normalizeLegacyTier(tier);
        element(`#legacy-filter-tier-${tier}`).replaceChildren(...legacyTierRules(tier).map(renderLegacyRule));
    }
    const source = sourceProfiles.find((profile) => profile.id === "mikan");
    const enabled = element("#legacy-filter-enabled");
    enabled.checked = source?.rss_filter_enabled ?? false;
    enabled.disabled = source === undefined;
    element("#legacy-filter-status").textContent =
        `revision ${activeLegacyMikanFilter.revision} · 更新来源 ${activeLegacyMikanFilter.updated_source}`
            + ` · 总开关 ${enabled.checked ? "开启" : "关闭"} · 匹配区分大小写`;
    element("#legacy-filter-json").value =
        activeLegacyMikanFilter.legacy_json;
    const snapshots = element("#legacy-filter-snapshots");
    snapshots.replaceChildren(...activeLegacyMikanFilter.snapshots.map((snapshot) => {
        const option = document.createElement("option");
        option.value = String(snapshot.revision);
        option.textContent =
            `r${snapshot.revision} · ${snapshot.updated_source} · ${new Date(snapshot.created_at_utc).toLocaleString()}`;
        return option;
    }));
    renderLegacyWarnings();
}
function readLegacyFilterDraft() {
    if (!activeLegacyMikanFilter)
        throw new Error("规则尚未载入。");
    for (const card of document.querySelectorAll(".legacy-filter-rule")) {
        const index = Number(card.dataset.ruleIndex);
        const rule = activeLegacyMikanFilter.rules[index];
        if (!rule)
            throw new Error("规则编辑器状态已过期，请重新载入。");
        for (const textarea of card.querySelectorAll(".legacy-filter-values")) {
            try {
                const parsed = JSON.parse(textarea.value);
                if (!Array.isArray(parsed) || !parsed.every((value) => typeof value === "string")) {
                    throw new Error("必须是 JSON 字符串数组");
                }
                rule[textarea.dataset.kind] = parsed;
                textarea.classList.remove("invalid");
            }
            catch {
                textarea.classList.add("invalid");
                throw new Error(`F${rule.tier} / ${rule.key || "空键"} 的名单不是有效 JSON 字符串数组。`);
            }
        }
    }
    for (let tier = 0; tier <= 4; tier += 1)
        normalizeLegacyTier(tier);
    renderLegacyWarnings();
    return activeLegacyMikanFilter.rules;
}
async function loadLegacyMikanFilter() {
    const status = element("#legacy-filter-status");
    status.textContent = "正在读取旧 Mikan 过滤规则…";
    try {
        const response = await fetch("/api/v1/mikan/legacy-filter", { headers });
        if (!response.ok)
            throw new Error(await responseError(response));
        activeLegacyMikanFilter = await response.json();
        renderLegacyMikanFilter();
    }
    catch (error) {
        activeLegacyMikanFilter = null;
        status.textContent = `读取失败：${errorMessage(error, "未知错误")}`;
    }
}
async function saveLegacyMikanFilter() {
    if (!activeLegacyMikanFilter)
        return;
    const buttonElement = element("#legacy-filter-save");
    const status = element("#legacy-filter-status");
    try {
        const rules = readLegacyFilterDraft();
        buttonElement.disabled = true;
        status.textContent = "正在保存五级过滤规则…";
        const requestHeaders = new Headers(headers);
        requestHeaders.set("Content-Type", "application/json");
        const response = await fetch("/api/v1/mikan/legacy-filter", {
            method: "PUT",
            headers: requestHeaders,
            body: JSON.stringify({
                expected_revision: activeLegacyMikanFilter.revision,
                rules,
            }),
        });
        if (!response.ok)
            throw new Error(await responseError(response));
        activeLegacyMikanFilter = await response.json();
        renderLegacyMikanFilter();
        status.textContent = `保存成功 · revision ${activeLegacyMikanFilter.revision}`;
    }
    catch (error) {
        status.textContent =
            `保存失败：${errorMessage(error, "未知错误")}；revision 冲突时请重新载入。`;
    }
    finally {
        buttonElement.disabled = false;
    }
}
function addLegacyMikanRule(tier) {
    if (!activeLegacyMikanFilter)
        return;
    const rules = legacyTierRules(tier);
    let sequence = rules.length + 1;
    let key = tier === 1 ? "key_3951_12" : tier === 2 ? "3951" : tier === 3 ? "12"
        : tier === 4 ? "Group" : `global-${sequence}`;
    while (rules.some((rule) => rule.key === key)) {
        sequence += 1;
        key = `rule-${sequence}`;
    }
    activeLegacyMikanFilter.rules.push({
        tier,
        position: rules.length,
        key,
        whitelist_enabled: false,
        blacklist_enabled: false,
        whitelist: [],
        blacklist: [],
    });
    renderLegacyMikanFilter();
}
async function importLegacyMikanFilter() {
    if (!activeLegacyMikanFilter)
        return;
    const status = element("#legacy-filter-status");
    status.textContent = "正在导入旧版 JSON…";
    try {
        const requestHeaders = new Headers(headers);
        requestHeaders.set("Content-Type", "application/json");
        const response = await fetch("/api/v1/mikan/legacy-filter/import", {
            method: "POST",
            headers: requestHeaders,
            body: JSON.stringify({
                expected_revision: activeLegacyMikanFilter.revision,
                legacy_json: element("#legacy-filter-json").value,
            }),
        });
        if (!response.ok)
            throw new Error(await responseError(response));
        activeLegacyMikanFilter = await response.json();
        renderLegacyMikanFilter();
        status.textContent = `导入成功 · revision ${activeLegacyMikanFilter.revision}`;
    }
    catch (error) {
        status.textContent =
            `导入失败：${errorMessage(error, "未知错误")}；原规则未修改。`;
    }
}
async function rollbackLegacyMikanFilter() {
    if (!activeLegacyMikanFilter)
        return;
    const target = Number(element("#legacy-filter-snapshots").value);
    if (!Number.isInteger(target) || target < 1 || target === activeLegacyMikanFilter.revision)
        return;
    if (!window.confirm(`将当前规则回滚为 revision ${target}？系统会创建新的审计 revision，不删除历史。`))
        return;
    const status = element("#legacy-filter-status");
    status.textContent = `正在回滚到 revision ${target}…`;
    try {
        const requestHeaders = new Headers(headers);
        requestHeaders.set("Content-Type", "application/json");
        const response = await fetch("/api/v1/mikan/legacy-filter/rollback", {
            method: "POST",
            headers: requestHeaders,
            body: JSON.stringify({
                expected_revision: activeLegacyMikanFilter.revision,
                target_revision: target,
            }),
        });
        if (!response.ok)
            throw new Error(await responseError(response));
        activeLegacyMikanFilter = await response.json();
        renderLegacyMikanFilter();
        status.textContent = `已回滚并创建 revision ${activeLegacyMikanFilter.revision}`;
    }
    catch (error) {
        status.textContent =
            `回滚失败：${errorMessage(error, "未知错误")}；revision 冲突时请重新载入。`;
    }
}
async function previewLegacyMikanFilter() {
    const result = element("#legacy-filter-preview-result");
    try {
        const rules = readLegacyFilterDraft();
        result.textContent = "正在执行服务端预览…";
        const numberOrNull = (selector) => {
            const input = element(selector);
            return input.value === "" ? null : input.valueAsNumber;
        };
        const requestHeaders = new Headers(headers);
        requestHeaders.set("Content-Type", "application/json");
        const response = await fetch("/api/v1/mikan/legacy-filter/preview", {
            method: "POST",
            headers: requestHeaders,
            body: JSON.stringify({
                title: element("#legacy-filter-preview-title").value,
                mikanid: numberOrNull("#legacy-filter-preview-mikanid"),
                groupid: numberOrNull("#legacy-filter-preview-groupid"),
                group_name: element("#legacy-filter-preview-group-name").value || null,
                rules,
            }),
        });
        if (!response.ok)
            throw new Error(await responseError(response));
        const preview = await response.json();
        const summary = document.createElement("strong");
        summary.textContent =
            `${preview.accepted ? "接受" : "拒绝"} · ${preview.reason}`
                + ` · 字幕组名 ${preview.derived_group_name || "（空）"}`
                + (preview.matched_scope ? ` · 最后命中 ${preview.matched_scope}/${preview.matched_key ?? ""}` : "");
        result.replaceChildren(summary, ...preview.steps.map((step) => {
            const row = document.createElement("div");
            row.className = `legacy-filter-trace ${step.accepted === true ? "accepted" : step.accepted === false ? "rejected" : ""}`;
            row.textContent =
                `${step.tier}${step.key === null ? "" : ` / ${step.key}`}`
                    + ` · ${step.applicable ? (step.accepted ? "通过" : "拒绝") : "未执行"}`
                    + ` · ${step.reason}`
                    + (step.whitelist_matches.length > 0 ? ` · 白名单命中 ${JSON.stringify(step.whitelist_matches)}` : "")
                    + (step.blacklist_matches.length > 0 ? ` · 黑名单命中 ${JSON.stringify(step.blacklist_matches)}` : "");
            return row;
        }));
    }
    catch (error) {
        result.textContent = `预览失败：${errorMessage(error, "未知错误")}`;
    }
}
async function updateLegacyFilterSwitch() {
    const profile = sourceProfiles.find((item) => item.id === "mikan");
    const toggle = element("#legacy-filter-enabled");
    if (!profile)
        return;
    toggle.disabled = true;
    const status = element("#legacy-filter-status");
    status.textContent = "正在更新 Mikan 来源总开关…";
    try {
        const requestHeaders = new Headers(headers);
        requestHeaders.set("Content-Type", "application/json");
        const response = await fetch("/api/v1/sources/mikan", {
            method: "PUT",
            headers: requestHeaders,
            body: JSON.stringify({
                display_name: profile.display_name,
                downloader_id: profile.downloader_id,
                file_strategy: profile.file_strategy,
                category: profile.category,
                tags: profile.tags,
                seeding_time_minutes: profile.seeding_time_minutes,
                allowed_torrent_hosts: profile.allowed_torrent_hosts,
                rss_filter_enabled: toggle.checked,
                rss_priority_enabled: profile.rss_priority_enabled,
                enabled: profile.enabled,
                expected_revision: profile.revision,
            }),
        });
        if (!response.ok)
            throw new Error(await responseError(response));
        const saved = await response.json();
        const index = sourceProfiles.findIndex((item) => item.id === saved.id);
        if (index >= 0)
            sourceProfiles[index] = saved;
        renderLegacyMikanFilter();
        void loadRssRules();
    }
    catch (error) {
        toggle.checked = profile.rss_filter_enabled;
        status.textContent =
            `总开关更新失败：${errorMessage(error, "未知错误")}；请重新载入来源。`;
    }
    finally {
        toggle.disabled = false;
    }
}
function optionalPositiveInteger(selector) {
    const raw = element(selector).value.trim();
    return raw === "" ? null : Number(raw);
}
function toLocalDateTimeValue(value) {
    const date = new Date(value);
    if (Number.isNaN(date.getTime()))
        return "";
    const pad = (number) => String(number).padStart(2, "0");
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
        + `T${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
}
function readAiPromptDraft(version) {
    try {
        const raw = localStorage.getItem(aiTestPromptDraftKey);
        if (!raw)
            return null;
        const value = JSON.parse(raw);
        return value.version === version && typeof value.template === "string"
            ? value.template
            : null;
    }
    catch {
        return null;
    }
}
function saveAiPromptDraft() {
    if (!aiTestDefaultPrompt)
        return;
    try {
        localStorage.setItem(aiTestPromptDraftKey, JSON.stringify({
            version: aiTestDefaultPrompt.prompt_version,
            template: element("#ai-test-prompt-template").value,
        }));
    }
    catch {
        // Browser storage is optional; the current in-memory edit remains usable.
    }
}
async function loadAiTestPrompt() {
    const editor = element("#ai-test-prompt-template");
    const status = element("#ai-test-prompt-status");
    try {
        const prompt = await api.get("/api/v1/ai-test/prompt");
        aiTestDefaultPrompt = prompt;
        editor.maxLength = prompt.maximum_length;
        editor.value = readAiPromptDraft(prompt.prompt_version) ?? prompt.template;
        editor.disabled = false;
        status.textContent = `当前 ${prompt.prompt_version}；上限 ${prompt.maximum_length} 字符。编辑内容只用于测试请求。`;
    }
    catch (error) {
        editor.disabled = true;
        status.textContent = `Prompt 读取失败：${errorMessage(error, "未知错误")}`;
    }
}
function resetAiTestPrompt() {
    if (!aiTestDefaultPrompt)
        return;
    const editor = element("#ai-test-prompt-template");
    editor.value = aiTestDefaultPrompt.template;
    try {
        localStorage.removeItem(aiTestPromptDraftKey);
    }
    catch {
    }
    element("#ai-test-prompt-status").textContent =
        `已恢复 ${aiTestDefaultPrompt.prompt_version} 程序默认；尚未运行。`;
}
async function importAiTestMikanEpisode() {
    const button = element("#ai-test-mikan-import");
    const status = element("#ai-test-mikan-status");
    const episodeUrl = element("#ai-test-mikan-url").value.trim();
    button.disabled = true;
    status.textContent = "正在读取 Mikan 页面、RSS、作品关联和 Torrent…";
    try {
        const imported = await api.post("/api/v1/ai-test/mikan-import", { episode_url: episodeUrl });
        element("#ai-test-title").value = imported.title;
        element("#ai-test-files").value = imported.files
            .map(file => `${file.name} | ${file.size_bytes}`)
            .join("\n");
        element("#ai-test-bgmid").value =
            imported.bgmid == null ? "" : String(imported.bgmid);
        element("#ai-test-file-count").value =
            String(imported.torrent_file_count);
        element("#ai-test-published-at").value =
            imported.published_at ? toLocalDateTimeValue(imported.published_at) : "";
        status.textContent = `解析完成：mikanid=${imported.mikanid}，groupid=${imported.groupid}，`
            + `bgmid=${imported.bgmid ?? "未找到"}，视频 ${imported.files.length} / Torrent 文件 ${imported.torrent_file_count}。`
            + "已填入表单，尚未运行 AI。";
    }
    catch (error) {
        status.textContent = `Mikan 解析失败：${errorMessage(error, "未知错误")}`;
    }
    finally {
        button.disabled = false;
    }
}
function parseAiTestFiles() {
    return element("#ai-test-files").value
        .split(/\r?\n/)
        .map(line => line.trim())
        .filter(Boolean)
        .map((line, index) => {
        const separator = line.lastIndexOf("|");
        if (separator < 1)
            throw new Error(`文件第 ${index + 1} 行缺少“| bytes”`);
        const name = line.slice(0, separator).trim();
        const size = Number(line.slice(separator + 1).trim());
        if (!name || !Number.isSafeInteger(size) || size < 0) {
            throw new Error(`文件第 ${index + 1} 行的名称或 bytes 无效`);
        }
        return { name, size_bytes: size };
    });
}
function aiTestSummaryItem(label, value) {
    const container = document.createElement("div");
    const term = document.createElement("span");
    const content = document.createElement("strong");
    term.textContent = label;
    content.textContent = value;
    container.append(term, content);
    return container;
}
function renderAiTestResult(result) {
    const summary = element("#ai-test-summary");
    const usage = result.usage;
    summary.replaceChildren(aiTestSummaryItem("最终结论", result.succeeded ? "TMDB 验证通过" : "未通过"), aiTestSummaryItem("模型", usage?.model ?? "—"), aiTestSummaryItem("总耗时", `${result.duration_ms} ms`), aiTestSummaryItem("请求 / 工具", `${usage?.request_count ?? 0} / ${usage?.tool_call_count ?? 0}`), aiTestSummaryItem("输入 Token", String(usage?.prompt_tokens ?? "—")), aiTestSummaryItem("输出 Token", String(usage?.completion_tokens ?? "—")), aiTestSummaryItem("总 Token", String(usage?.total_tokens ?? "—")), aiTestSummaryItem("错误", result.error_code ?? "—"));
    summary.dataset.uiState = result.succeeded ? "ready" : "error";
    const badge = element("#ai-test-prompt-version");
    badge.textContent = result.prompt_version;
    badge.className = `badge ${result.succeeded ? "ok" : "error"}`;
    element("#ai-test-raw-output").textContent =
        result.raw_output ?? "模型未返回可解析的最终内容。";
    element("#ai-test-parsed-output").textContent = JSON.stringify({
        candidate: result.candidate,
        validation: result.validation,
        error_kind: result.error_kind,
        error_code: result.error_code,
    }, null, 2);
    element("#ai-test-rendered-prompt").textContent = result.rendered_prompt;
    const trace = element("#ai-test-trace");
    trace.replaceChildren(...result.trace.map(item => {
        const row = document.createElement("div");
        row.className = "ai-test-trace-row";
        const head = document.createElement("strong");
        head.textContent = `${item.sequence}. ${item.stage}`;
        const detail = document.createElement("code");
        detail.textContent = item.detail;
        const duration = document.createElement("span");
        duration.textContent = item.duration_ms == null ? "" : `${item.duration_ms} ms`;
        row.append(head, detail, duration);
        return row;
    }));
}
async function runAiMetadataTest(event) {
    event.preventDefault();
    const button = element("#ai-test-run");
    const message = element("#ai-test-message");
    button.disabled = true;
    message.textContent = "正在调用生产 AI 匹配链；可能需要数分钟…";
    try {
        const files = parseAiTestFiles();
        if (files.length === 0)
            throw new Error("至少需要一个 Torrent 文件");
        const publishedAt = element("#ai-test-published-at").value;
        const result = await api.post("/api/v1/ai-test/run", {
            title: element("#ai-test-title").value,
            files,
            bgmid: optionalPositiveInteger("#ai-test-bgmid"),
            anidbid: optionalPositiveInteger("#ai-test-anidbid"),
            imdbid: element("#ai-test-imdbid").value.trim() || null,
            torrent_file_count: optionalPositiveInteger("#ai-test-file-count") ?? files.length,
            published_at: publishedAt ? new Date(publishedAt).toISOString() : null,
            bgm_episode_candidate: optionalPositiveInteger("#ai-test-bgm-episode"),
            use_bangumi_pubdate_first: element("#ai-test-use-bgm-pubdate").checked,
            expected_tmdbid: optionalPositiveInteger("#ai-test-expected-tmdbid"),
            expected_season: optionalPositiveInteger("#ai-test-expected-season"),
            prompt_template: element("#ai-test-prompt-template").value || null,
        });
        renderAiTestResult(result);
        message.textContent = result.succeeded
            ? "测试完成：模型候选已通过 TMDB 最终验证。"
            : `测试完成但未通过：${result.error_code ?? "unknown"}`;
    }
    catch (error) {
        message.textContent = `测试失败：${errorMessage(error, "未知错误")}`;
    }
    finally {
        button.disabled = false;
    }
}
function fillAiMetadataTestExample() {
    element("#ai-test-title").value =
        "[黒ネズミたち] 说出这边交给我你们先走以后十年过去成了传说。 / Kokoore - 06 (CR 1920x1080 AVC AAC MKV)";
    element("#ai-test-files").value =
        "Kokoore - 06.mkv | 734003200";
    element("#ai-test-bgmid").value = "590786";
    element("#ai-test-file-count").value = "1";
    element("#ai-test-bgm-episode").value = "6";
    element("#ai-test-published-at").value = "2026-08-10T12:00";
    element("#ai-test-use-bgm-pubdate").checked = true;
}
element("#rss-reload").addEventListener("click", () => void loadRssRules());
element("#ai-test-form").addEventListener("submit", event => void runAiMetadataTest(event));
element("#ai-test-fill-example").addEventListener("click", fillAiMetadataTestExample);
element("#ai-test-mikan-import").addEventListener("click", () => void importAiTestMikanEpisode());
element("#ai-test-prompt-reset").addEventListener("click", resetAiTestPrompt);
element("#ai-test-prompt-template").addEventListener("input", saveAiPromptDraft);
element("#library-sort").value = libraryState.sort;
element("#library-direction").value = libraryState.direction;
element("#library-page-size").value = String(libraryState.page_size);
element("#library-episode-filter").value = libraryState.episode_filter;
element("#metadata-search").value = metadataState.search;
element("#metadata-status-filter").value = metadataState.status;
element("#metadata-handling-filter").value = metadataState.handling;
element("#metadata-failure-stage").value = metadataState.failure_stage;
element("#metadata-error-code").value = metadataState.error_code;
element("#metadata-retryability-filter").value =
    metadataState.retryability;
element("#metadata-sort").value = metadataState.sort;
element("#metadata-direction").value = metadataState.direction;
element("#metadata-page-size").value = String(metadataState.page_size);
element("#metadata-filters").addEventListener("submit", (event) => {
    event.preventDefault();
    metadataState.search = element("#metadata-search").value.trim();
    metadataState.status = element("#metadata-status-filter").value;
    metadataState.handling =
        element("#metadata-handling-filter").value;
    metadataState.failure_stage =
        element("#metadata-failure-stage").value.trim().toLowerCase();
    metadataState.error_code =
        element("#metadata-error-code").value.trim().toLowerCase();
    metadataState.retryability =
        element("#metadata-retryability-filter").value;
    metadataState.sort =
        element("#metadata-sort").value;
    metadataState.direction =
        element("#metadata-direction").value;
    metadataState.page_size = Number(element("#metadata-page-size").value);
    metadataState.page = 1;
    saveMetadataState();
    void loadMetadataTasks();
});
element("#metadata-filter-reset").addEventListener("click", () => {
    metadataState = {
        page: 1,
        page_size: 25,
        search: "",
        status: "",
        handling: "all",
        failure_stage: "",
        error_code: "",
        retryability: "all",
        sort: "updated",
        direction: "desc",
    };
    element("#metadata-search").value = "";
    element("#metadata-status-filter").value = "";
    element("#metadata-handling-filter").value = "all";
    element("#metadata-failure-stage").value = "";
    element("#metadata-error-code").value = "";
    element("#metadata-retryability-filter").value = "all";
    element("#metadata-sort").value = "updated";
    element("#metadata-direction").value = "desc";
    element("#metadata-page-size").value = "25";
    saveMetadataState();
    void loadMetadataTasks();
});
element("#metadata-previous").addEventListener("click", () => {
    if (metadataState.page <= 1)
        return;
    metadataState.page--;
    saveMetadataState();
    void loadMetadataTasks();
});
element("#metadata-next").addEventListener("click", () => {
    metadataState.page++;
    saveMetadataState();
    void loadMetadataTasks();
});
element("#download-search").value = downloadState.search;
element("#download-state").value = downloadState.state;
element("#download-business-status").value =
    downloadState.business_status;
element("#download-downloader").value = downloadState.downloader_id;
element("#download-source").value = downloadState.source;
element("#download-page-size").value = String(downloadState.page_size);
element("#download-filters").addEventListener("submit", (event) => {
    event.preventDefault();
    downloadState.search = element("#download-search").value.trim();
    downloadState.state = element("#download-state").value;
    downloadState.business_status =
        element("#download-business-status").value;
    downloadState.downloader_id =
        element("#download-downloader").value.trim().toLowerCase();
    downloadState.source =
        element("#download-source").value.trim().toLowerCase();
    downloadState.page_size = Number(element("#download-page-size").value);
    downloadState.page = 1;
    saveDownloadState();
    void loadDownloads();
});
element("#download-filter-reset").addEventListener("click", () => {
    downloadState = {
        page: 1,
        page_size: 25,
        search: "",
        state: "",
        business_status: "",
        downloader_id: "",
        source: "",
    };
    element("#download-search").value = "";
    element("#download-state").value = "";
    element("#download-business-status").value = "";
    element("#download-downloader").value = "";
    element("#download-source").value = "";
    element("#download-page-size").value = "25";
    saveDownloadState();
    void loadDownloads();
});
element("#download-previous").addEventListener("click", () => {
    if (downloadState.page <= 1)
        return;
    downloadState.page--;
    saveDownloadState();
    void loadDownloads();
});
element("#download-next").addEventListener("click", () => {
    downloadState.page++;
    saveDownloadState();
    void loadDownloads();
});
element("#library-reload").addEventListener("click", () => void loadLibrary());
element("#library-create-form").addEventListener("submit", (event) => void createLibrarySeason(event));
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
element("#library-detail-refresh").addEventListener("click", () => void refreshLibrarySeason());
element("#library-detail-delete").addEventListener("click", () => void deleteLibrarySeason());
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
element("#configuration-form").addEventListener("submit", (event) => void previewConfiguration(event));
element("#configuration-confirm").addEventListener("click", () => void confirmConfiguration());
element("#configuration-form").addEventListener("input", () => {
    const preview = element("#configuration-preview");
    if (pendingConfigurationRequest || !preview.hidden) {
        clearConfigurationPreview("配置已修改，请重新预览差异。");
    }
});
configurationDialog.addEventListener("close", () => {
    clearConfigurationPreview();
    element("#configuration-tmdb-key").value = "";
    element("#configuration-tmdb-token").value = "";
    element("#configuration-ai-key").value = "";
});
element("#configuration-tmdb-key-clear").addEventListener("change", syncConfigurationSecretInputs);
element("#configuration-tmdb-token-clear").addEventListener("change", syncConfigurationSecretInputs);
element("#configuration-ai-key-clear").addEventListener("change", syncConfigurationSecretInputs);
element("#rss-save").addEventListener("click", () => void saveRssRules());
element("#rss-rule-rollback").addEventListener("click", () => void rollbackRssRules());
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
element("#legacy-filter-reload").addEventListener("click", () => void loadLegacyMikanFilter());
element("#legacy-filter-save").addEventListener("click", () => void saveLegacyMikanFilter());
element("#legacy-filter-export").addEventListener("click", () => {
    if (activeLegacyMikanFilter) {
        element("#legacy-filter-json").value =
            activeLegacyMikanFilter.legacy_json;
    }
});
element("#legacy-filter-import").addEventListener("click", () => void importLegacyMikanFilter());
element("#legacy-filter-rollback").addEventListener("click", () => void rollbackLegacyMikanFilter());
element("#legacy-filter-preview-run").addEventListener("click", () => void previewLegacyMikanFilter());
element("#legacy-filter-enabled").addEventListener("change", () => void updateLegacyFilterSwitch());
for (const addButton of document.querySelectorAll("[data-legacy-add-tier]")) {
    addButton.addEventListener("click", () => {
        addLegacyMikanRule(Number(addButton.dataset.legacyAddTier));
    });
}
element("#source-new").addEventListener("click", () => populateSourceForm(null));
element("#source-form").addEventListener("submit", (event) => void saveSource(event));
element("#source-adapter").addEventListener("change", updateSourceCredentialInputs);
element("#source-mikan-cookie-clear").addEventListener("change", updateSourceCredentialInputs);
element("#source-rss-url-clear").addEventListener("change", updateSourceCredentialInputs);
element("#source-enabled").addEventListener("change", updateSourceCredentialInputs);
element("#source-rss-schedule-enabled").addEventListener("change", updateSourceCredentialInputs);
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
element("#directory-database-refresh").addEventListener("click", () => void loadDirectoryDatabase(true));
element("#external-plugin-reload").addEventListener("click", () => void loadStatus());
element("#data-update-reload").addEventListener("click", () => void loadDataUpdate());
element("#data-update-check").addEventListener("click", () => void runDataUpdateAction("/api/v1/data-update/check", "正在检查 manifest…"));
element("#data-update-download").addEventListener("click", () => void runDataUpdateAction("/api/v1/data-update/download", "正在下载并校验数据包…"));
element("#data-update-apply").addEventListener("click", () => void runDataUpdateAction("/api/v1/data-update/update", "正在下载、校验并导入数据包…"));
element("#data-update-rollback").addEventListener("click", () => void runDataUpdateAction("/api/v1/data-update/rollback", "正在回滚上一可用版本…", "确认把上一可用数据版本切换为 active？当前版本仍会保留，可再次回滚。"));
element("#data-update-offline-package").addEventListener("change", () => {
    element("#data-update-offline-import").disabled =
        dataUpdateActionRunning
            || element("#data-update-offline-package").files?.length !== 1;
});
element("#data-update-offline-form").addEventListener("submit", (event) => void importOfflineDataPackage(event));
element("#cache-database").addEventListener("change", event => {
    cacheDatabase = event.currentTarget.value;
    activeCacheBucketId = null;
    cachePage = 1;
    void loadCacheBuckets();
});
element("#cache-reload").addEventListener("click", () => void loadCacheBuckets());
element("#cache-previous").addEventListener("click", () => {
    if (cachePage <= 1)
        return;
    cachePage--;
    void loadCacheEntries();
});
element("#cache-next").addEventListener("click", () => {
    if (cachePage * cachePageSize >= cacheTotalCount)
        return;
    cachePage++;
    void loadCacheEntries();
});
element("#live-log-level").addEventListener("change", renderLiveLogs);
for (const selector of [
    "#live-log-search",
    "#live-log-category",
    "#live-log-event-id",
]) {
    element(selector).addEventListener("input", renderLiveLogs);
}
element("#live-log-auto-scroll").addEventListener("change", renderLiveLogs);
element("#live-log-wrap").addEventListener("change", renderLiveLogs);
element("#live-log-reconnect").addEventListener("click", () => connectLiveLogs(true));
element("#live-log-pause").addEventListener("click", toggleLiveLogPause);
element("#live-log-copy").addEventListener("click", () => void copyVisibleLiveLogs());
element("#live-log-clear").addEventListener("click", () => {
    liveLogEntries = [];
    renderLiveLogs();
});
window.addEventListener("beforeunload", () => {
    liveLogShouldReconnect = false;
    if (liveLogReconnectTimer !== null) {
        window.clearTimeout(liveLogReconnectTimer);
        liveLogReconnectTimer = null;
    }
    disconnectCurrentLiveLogSocket();
});
initializeWorkspaceNavigation();
void loadAiTestPrompt();
void loadStatus();
void loadDirectoryDatabase();
void loadDataUpdate();
void loadCacheBuckets();
connectLiveLogs();
void loadLibrary();
void loadConfiguration();
void loadDownloads();
void loadMetadataTasks();
void loadPendingTmdb();
void loadDownloaders();
void loadSources();
void loadRssRules();
void loadLegacyMikanFilter();
void loadTrustedOffsets();
window.setInterval(() => void loadDownloads(), 5000);
window.setInterval(() => void loadMetadataTasks(), 5000);
window.setInterval(() => void loadPendingTmdb(), 10000);
window.setInterval(() => {
    if (!document.hidden)
        void loadDataUpdate(true);
}, 3000);
window.setInterval(() => {
    if (!document.hidden && activeLibraryDetail === null)
        void loadLibrary();
}, 15000);
