interface RuntimeStatus {
  database_schema_version: number;
  native_aot: boolean;
  runtime_identifier: string;
  paths: { data_path: string };
  capabilities: Record<string, boolean>;
}

interface DownloadItem {
  title: string;
  source: string;
  downloader_id: string;
  state: string;
  business_status: string;
  progress: number;
  downloaded_bytes: number;
  total_bytes: number;
  speed_bytes_per_second: number;
  eta_seconds: number | null;
  seeds: number;
  peers: number;
  is_stale: boolean;
  downloader_connected: boolean;
  downloader_failure_code: string | null;
}

interface MetadataTaskItem {
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

const accessKey = new URLSearchParams(window.location.search).get("access_key");
const headers = new Headers();
if (accessKey) headers.set("Access-Key", accessKey);

async function loadStatus(): Promise<void> {
  const health = document.querySelector<HTMLElement>("#health");
  try {
    const response = await fetch("/api/v1/status", { headers });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const status = await response.json() as RuntimeStatus;
    document.querySelector<HTMLElement>("#schema")!.textContent = `v${status.database_schema_version}`;
    document.querySelector<HTMLElement>("#runtime")!.textContent = status.native_aot ? `NativeAOT · ${status.runtime_identifier}` : `JIT · ${status.runtime_identifier}`;
    document.querySelector<HTMLElement>("#data-path")!.textContent = status.paths.data_path;
    document.querySelector<HTMLElement>("#modules")!.replaceChildren(...Object.entries(status.capabilities).map(([name, enabled]) => {
      const item = document.createElement("article");
      item.className = `module ${enabled ? "enabled" : ""}`;
      const title = document.createElement("strong");
      title.textContent = name.replaceAll("_", " ");
      const state = document.createElement("span");
      state.textContent = enabled ? "已启用" : "待实现";
      item.append(title, state);
      return item;
    }));
    health!.textContent = "运行中";
    health!.className = "badge ready";
  } catch (error) {
    health!.textContent = error instanceof Error ? error.message : "连接失败";
    health!.className = "badge error";
  }
}

void loadStatus();

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
  const container = document.querySelector<HTMLElement>("#downloads")!;
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

    container.replaceChildren(...body.items.map(item => {
      const card = document.createElement("article");
      card.className = `download-card ${item.is_stale ? "stale" : ""}`;
      const heading = document.createElement("div");
      heading.className = "download-heading";
      const title = document.createElement("strong");
      title.textContent = item.title;
      const state = document.createElement("span");
      state.className = `badge ${item.is_stale ? "error" : "ready"}`;
      state.textContent = item.is_stale ? `快照过期 · ${item.downloader_failure_code ?? "离线"}` : item.business_status;
      heading.append(title, state);
      const progress = document.createElement("progress");
      progress.max = 1;
      progress.value = item.progress;
      const details = document.createElement("p");
      details.className = "download-details";
      details.textContent = `${item.source} → ${item.downloader_id} · ${(item.progress * 100).toFixed(1)}% · ${formatBytes(item.downloaded_bytes)} / ${formatBytes(item.total_bytes)} · ${formatBytes(item.speed_bytes_per_second)}/s · Seeds ${item.seeds} · Peers ${item.peers}`;
      card.append(heading, progress, details);
      return card;
    }));
  } catch (error) {
    const failed = document.createElement("p");
    failed.className = "muted empty";
    failed.textContent = error instanceof Error ? `下载状态读取失败：${error.message}` : "下载状态读取失败";
    container.replaceChildren(failed);
  }
}

void loadDownloads();
window.setInterval(() => void loadDownloads(), 5000);

const statusLabels: Record<string, string> = {
  received: "已接收",
  staged: "种子已暂存",
  dispatching: "正在提交下载器",
  downloading: "下载中",
  downloaded: "等待元数据匹配",
  metadata_resolving: "正在匹配 Series / Season",
  metadata_season_resolved: "季度已确认",
  metadata_episode_resolving: "正在验证 Episode",
  metadata_resolved: "元数据已确认",
  metadata_failed: "元数据失败",
};

function textOrDash(value: string | number | null | undefined): string {
  return value === null || value === undefined || value === "" ? "—" : String(value);
}

async function retryMetadataTask(taskId: string, button: HTMLButtonElement): Promise<void> {
  button.disabled = true;
  button.textContent = "重新入队中…";
  try {
    const response = await fetch(`/api/v1/metadata/tasks/${encodeURIComponent(taskId)}/retry`, {
      method: "POST",
      headers,
    });
    if (!response.ok) {
      const body = await response.json().catch(() => null) as { message?: string } | null;
      throw new Error(body?.message ?? `HTTP ${response.status}`);
    }
    await loadMetadataTasks();
  } catch (error) {
    button.disabled = false;
    button.textContent = error instanceof Error ? error.message : "重试失败";
  }
}

async function loadMetadataTasks(): Promise<void> {
  const container = document.querySelector<HTMLElement>("#metadata-tasks")!;
  try {
    const response = await fetch("/api/v1/metadata/tasks", { headers });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const body = await response.json() as { items: MetadataTaskItem[] };
    if (body.items.length === 0) {
      const empty = document.createElement("p");
      empty.className = "muted empty";
      empty.textContent = "暂无元数据任务";
      container.replaceChildren(empty);
      return;
    }

    container.replaceChildren(...body.items.map(item => {
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
    }));
  } catch (error) {
    const failed = document.createElement("p");
    failed.className = "muted empty";
    failed.textContent = error instanceof Error ? `元数据状态读取失败：${error.message}` : "元数据状态读取失败";
    container.replaceChildren(failed);
  }
}

void loadMetadataTasks();
window.setInterval(() => void loadMetadataTasks(), 5000);
