interface RuntimeStatus {
  database_schema_version: number;
  native_aot: boolean;
  runtime_identifier: string;
  paths: { data_path: string };
  capabilities: Record<string, boolean>;
}

interface DownloadItem {
  task_id: string;
  title: string;
  source: string;
  downloader_id: string;
  business_status: string;
  progress: number;
  downloaded_bytes: number;
  total_bytes: number;
  speed_bytes_per_second: number;
  seeds: number;
  peers: number;
  is_stale: boolean;
  downloader_failure_code: string | null;
}

interface MetadataItem {
  task_id: string;
  title: string;
  source: string;
  status: string;
  mikanid: number | null;
  bgmid: number | null;
  tmdb_series_id: number | null;
  tmdb_season_number: number | null;
  series_strategy: string | null;
  season_strategy: string | null;
  episode_strategy: string | null;
  failure_kind: string | null;
  failure_reason: string | null;
  episode_file_count: number;
  other_file_count: number;
  duplicate_file_count: number;
  pending_file_count: number;
}

interface DeleteTarget {
  display_value: string;
}

type DeleteFlag =
  | "delete_business_record"
  | "delete_downloader_task"
  | "delete_source_files"
  | "delete_media_files";
type DeleteCollection = "business_records" | "downloader_tasks" | "source_files" | "media_files";

interface DeletePreview {
  task_id: string;
  title: string;
  task_status: string;
  fingerprint: string;
  business_records: DeleteTarget[];
  downloader_tasks: DeleteTarget[];
  source_files: DeleteTarget[];
  media_files: DeleteTarget[];
}

interface DeleteCreateResponse {
  execution_id: string;
  selected_target_count: number;
}

interface ApiError {
  message?: string;
}

interface DeleteGroup {
  flag: DeleteFlag;
  label: string;
  collection: DeleteCollection;
  help: string;
}

function element<T extends Element>(selector: string): T {
  const found = document.querySelector<T>(selector);
  if (!found) throw new Error(`Required WebUI element is missing: ${selector}`);
  return found;
}

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}

async function responseError(response: Response): Promise<string> {
  const body = await response.json().catch(() => null) as ApiError | null;
  return body?.message ?? `HTTP ${response.status}`;
}

const accessKey = new URLSearchParams(window.location.search).get("access_key");
const headers = new Headers();
if (accessKey) headers.set("Access-Key", accessKey);
const deleteDialog = element<HTMLDialogElement>("#delete-dialog");
const deleteConfirm = element<HTMLButtonElement>("#delete-confirm");
let activeDeletePreview: DeletePreview | null = null;

const statusLabels: Record<string, string> = {
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

const deleteGroups: DeleteGroup[] = [
  { flag: "delete_business_record", label: "业务完成记录", collection: "business_records", help: "删除后该 TMDB 单集可重新导入" },
  { flag: "delete_downloader_task", label: "qBittorrent 任务", collection: "downloader_tasks", help: "只删除任务，永不让 qB 删除文件" },
  { flag: "delete_source_files", label: "下载源文件", collection: "source_files", help: "精确删除捕获下载根目录内的文件" },
  { flag: "delete_media_files", label: "媒体库文件", collection: "media_files", help: "精确删除捕获媒体库根目录内的文件" },
];

async function loadStatus(): Promise<void> {
  const health = element<HTMLElement>("#health");
  try {
    const response = await fetch("/api/v1/status", { headers });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const status = await response.json() as RuntimeStatus;
    element<HTMLElement>("#schema").textContent = `v${status.database_schema_version}`;
    element<HTMLElement>("#runtime").textContent = status.native_aot
      ? `NativeAOT · ${status.runtime_identifier}`
      : `JIT · ${status.runtime_identifier}`;
    element<HTMLElement>("#data-path").textContent = status.paths.data_path;
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
    element<HTMLElement>("#modules").replaceChildren(...modules);
    health.textContent = "运行中";
    health.className = "badge ready";
  } catch (error) {
    health.textContent = errorMessage(error, "连接失败");
    health.className = "badge error";
  }
}

function formatBytes(value: number): string {
  if (value < 1024) return `${value} B`;
  const units = ["KiB", "MiB", "GiB", "TiB"];
  let size = value;
  let unit = -1;
  do {
    size /= 1024;
    unit += 1;
  } while (size >= 1024 && unit + 1 < units.length);
  return `${size.toFixed(size >= 10 ? 1 : 2)} ${units[unit]}`;
}

async function loadDownloads(): Promise<void> {
  const container = element<HTMLElement>("#downloads");
  try {
    const response = await fetch("/api/v1/downloads", { headers });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const body = await response.json() as { items: DownloadItem[] };
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
  } catch (error) {
    const failed = document.createElement("p");
    failed.className = "muted empty";
    failed.textContent = `下载状态读取失败：${errorMessage(error, "未知错误")}`;
    container.replaceChildren(failed);
  }
}

function selectedDeleteInput(flag: DeleteFlag): HTMLInputElement | null {
  return document.querySelector<HTMLInputElement>(`#delete-options input[name="${flag}"]`);
}

function updateDeleteConfirm(): void {
  deleteConfirm.disabled = !activeDeletePreview || !deleteGroups.some(({ flag }) => selectedDeleteInput(flag)?.checked);
}

async function openDeletePreview(taskId: string): Promise<void> {
  const options = element<HTMLElement>("#delete-options");
  const targets = element<HTMLElement>("#delete-targets");
  const message = element<HTMLElement>("#delete-message");
  options.replaceChildren();
  targets.replaceChildren();
  message.textContent = "正在读取不可变目标…";
  deleteConfirm.disabled = true;
  deleteDialog.showModal();
  try {
    const response = await fetch(`/api/v1/delete/tasks/${encodeURIComponent(taskId)}/preview`, { headers });
    if (!response.ok) throw new Error(await responseError(response));
    activeDeletePreview = await response.json() as DeletePreview;
    element<HTMLElement>("#delete-summary").textContent = `${activeDeletePreview.title} · ${statusLabels[activeDeletePreview.task_status] ?? activeDeletePreview.task_status}`;
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
  } catch (error) {
    activeDeletePreview = null;
    message.textContent = `预览失败：${errorMessage(error, "未知错误")}`;
  }
}

deleteConfirm.addEventListener("click", async () => {
  if (!activeDeletePreview) return;
  deleteConfirm.disabled = true;
  deleteConfirm.textContent = "正在创建…";
  const request: Record<DeleteFlag, boolean> & { fingerprint: string } = {
    fingerprint: activeDeletePreview.fingerprint,
    delete_business_record: false,
    delete_downloader_task: false,
    delete_source_files: false,
    delete_media_files: false,
  };
  for (const { flag } of deleteGroups) request[flag] = Boolean(selectedDeleteInput(flag)?.checked);
  try {
    const requestHeaders = new Headers(headers);
    requestHeaders.set("Content-Type", "application/json");
    const response = await fetch(`/api/v1/delete/tasks/${encodeURIComponent(activeDeletePreview.task_id)}`, {
      method: "POST",
      headers: requestHeaders,
      body: JSON.stringify(request),
    });
    if (!response.ok) throw new Error(await responseError(response));
    const body = await response.json() as DeleteCreateResponse;
    element<HTMLElement>("#delete-message").textContent = `删除任务已创建：${body.execution_id}（${body.selected_target_count} 项）`;
    deleteConfirm.textContent = "已创建";
    window.setTimeout(() => deleteDialog.close(), 1600);
  } catch (error) {
    element<HTMLElement>("#delete-message").textContent = errorMessage(error, "创建失败");
    deleteConfirm.textContent = "确认创建删除任务";
    updateDeleteConfirm();
  }
});

deleteDialog.addEventListener("close", () => {
  activeDeletePreview = null;
  deleteConfirm.textContent = "确认创建删除任务";
});

function textOrDash(value: unknown): string {
  return value === null || value === undefined || value === "" ? "—" : String(value);
}

async function retryMetadataTask(taskId: string, button: HTMLButtonElement): Promise<void> {
  button.disabled = true;
  button.textContent = "重新入队中…";
  try {
    const response = await fetch(`/api/v1/metadata/tasks/${encodeURIComponent(taskId)}/retry`, { method: "POST", headers });
    if (!response.ok) throw new Error(await responseError(response));
    await loadMetadataTasks();
  } catch (error) {
    button.disabled = false;
    button.textContent = errorMessage(error, "重试失败");
  }
}

async function loadMetadataTasks(): Promise<void> {
  const container = element<HTMLElement>("#metadata-tasks");
  try {
    const response = await fetch("/api/v1/metadata/tasks", { headers });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const body = await response.json() as { items: MetadataItem[] };
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
      ] as const) {
        const group = document.createElement("div");
        const term = document.createElement("dt");
        term.textContent = label;
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
  } catch (error) {
    const failed = document.createElement("p");
    failed.className = "muted empty";
    failed.textContent = `元数据状态读取失败：${errorMessage(error, "未知错误")}`;
    container.replaceChildren(failed);
  }
}

void loadStatus();
void loadDownloads();
void loadMetadataTasks();
window.setInterval(() => void loadDownloads(), 5000);
window.setInterval(() => void loadMetadataTasks(), 5000);
