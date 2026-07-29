interface RuntimeStatus {
  database_schema_version: number;
  native_aot: boolean;
  runtime_identifier: string;
  paths: { data_path: string };
  capabilities: Record<string, boolean>;
}

interface RuntimeConfiguration {
  configuration_revision: number;
  applied_configuration_revision: number;
  restart_required: boolean;
  paths: {
    data_path: string;
    download_path: string;
    save_path: string;
  };
  deployment: {
    running_in_container: boolean;
    background_workers_enabled: boolean;
    access_key_configured: boolean;
    paths_restart_required: boolean;
  };
  metadata: {
    tmdb: {
      base_url: string;
      proxy_url: string | null;
      language: string;
      http_timeout_seconds: number;
      api_key_configured: boolean;
      read_access_token_configured: boolean;
    };
    bangumi: {
      base_url: string;
      proxy_url: string | null;
      http_timeout_seconds: number;
    };
    season_failure: {
      skip: boolean;
      backtrace: boolean;
      use_title_season: boolean;
      use_first_season: boolean;
    };
    ai: {
      use_metadata_match: boolean;
      use_season_match: boolean;
      use_episode_match: boolean;
      http_timeout_seconds: number;
    };
    tmdb_failure_use_bangumi: boolean;
    mikan_trusted_offset_cache_enabled: boolean;
  };
  torrent_fetch: {
    http_timeout_seconds: number;
    max_response_bytes: number;
    max_redirects: number;
    staging_ttl_seconds: number;
  };
  editable: {
    tmdb_base_url: string;
    tmdb_proxy_url: string | null;
    tmdb_language: string;
    tmdb_http_timeout_seconds: number;
    tmdb_api_key_state: "inherit" | "configured" | "cleared";
    tmdb_read_access_token_state: "inherit" | "configured" | "cleared";
    bangumi_base_url: string;
    bangumi_proxy_url: string | null;
    bangumi_http_timeout_seconds: number;
    season_failure_skip: boolean;
    season_failure_backtrace: boolean;
    season_failure_use_title_season: boolean;
    season_failure_use_first_season: boolean;
    ai_use_metadata_match: boolean;
    ai_use_season_match: boolean;
    ai_use_episode_match: boolean;
    ai_http_timeout_seconds: number;
    tmdb_failure_use_bangumi: boolean;
    mikan_trusted_offset_cache_enabled: boolean;
    torrent_http_timeout_seconds: number;
    torrent_max_response_bytes: number;
    torrent_max_redirects: number;
    torrent_staging_ttl_seconds: number;
  };
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

interface PendingTmdbSummary {
  bgmid: number;
  fallback_name: string;
  season_numbers: number[];
  task_count: number;
  processed_file_count: number;
  fallback_record_count: number;
  active_claim_count: number;
  completed_claim_count: number;
  duplicate_file_count: number;
  latest_failure_kind: string | null;
  latest_failure_reason: string | null;
  updated_at_utc: string;
}

interface PendingTmdbTask {
  task_id: string;
  title: string;
  source: string;
  status: string;
  season_number: number | null;
  other_file_count: number;
  duplicate_file_count: number;
  failure_kind: string | null;
  failure_reason: string | null;
  updated_at_utc: string;
}

interface PendingTmdbScope {
  kind: string;
  state: string;
  source: string;
  source_episode: string | null;
  dedup_boundary: string;
  cross_source_duplicate_risk: boolean;
  completed_at_utc: string | null;
}

interface PendingTmdbRecoveryCandidate {
  fallback_record_id: string;
  source: string;
  source_episode: string | null;
  dedup_boundary: string;
  completed_at_utc: string;
}

interface PendingTmdbDetail {
  summary: PendingTmdbSummary;
  tasks: PendingTmdbTask[];
  scopes: PendingTmdbScope[];
  recovery_candidates: PendingTmdbRecoveryCandidate[];
}

interface PendingTmdbRecoveryResult {
  bgmid: number;
  tmdb_series_id: number;
  has_pending_fallback_records: boolean;
  items: Array<{
    fallback_record_id: string;
    tmdb_season_number: number;
    tmdb_episode_number: number;
    state: "Resolved" | "DuplicateAfterResolution";
  }>;
}

interface MikanTrustedOffsetItem {
  mikanid: number;
  groupid: number;
  tmdb_series_id: number;
  tmdb_season_number: number;
  episode_offset: number;
  distinct_episode_count: number;
  required_episode_count: number;
  state: "learning" | "trusted" | "conflict_reset";
  updated_at_utc: string;
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

interface RssNamedArray {
  id: string;
  name: string;
  enabled: boolean;
  values: string[];
}

interface RssPriorityGroup {
  id: string;
  name: string;
  arrays: RssNamedArray[];
}

interface RssRuleSnapshot {
  source_profile_id: string;
  rss_filter_enabled: boolean;
  rss_priority_enabled: boolean;
  revision: number;
  whitelist: RssNamedArray[];
  blacklist: RssNamedArray[];
  priority_groups: RssPriorityGroup[];
}

interface RssRuleDecision {
  candidate_id: string;
  decision: string;
  reason: string;
  winner_id: string | null;
  evaluated_priority_groups: string[];
}

interface SourceProfile {
  id: string;
  display_name: string;
  adapter: "mikan" | "u2" | "ttg";
  downloader_id: string;
  file_strategy: "link" | "link_delete" | "move" | "wait_move";
  allowed_torrent_hosts: string[];
  category: string;
  tags: string[];
  seeding_time_minutes: number;
  rss_filter_enabled: boolean;
  rss_priority_enabled: boolean;
  enabled: boolean;
  revision: number;
  ingest_task_count: number;
  rss_batch_count: number;
  is_default: boolean;
  file_strategy_warning: string | null;
}

interface SourceProfileList {
  items: SourceProfile[];
}

interface SourceRoutePreview {
  valid: boolean;
  errors: string[];
  source_profile_id: string;
  source_profile_revision: number;
  adapter: string;
  downloader_id: string;
  downloader_enabled: boolean;
  download_path: string | null;
  save_path: string;
  file_strategy: string;
  category: string;
  tags: string[];
  seeding_time_minutes: number;
  rss_filter_enabled: boolean;
  rss_priority_enabled: boolean;
  rss_rule_revision: number | null;
}

interface DownloaderInstance {
  id: string;
  type: string;
  base_url: string;
  download_path: string;
  enabled: boolean;
  credentials_configured: boolean;
  configuration_source: string;
  override_revision: number | null;
  restart_required: boolean;
  source_profile_count: number;
  ingest_task_count: number;
  download_job_count: number;
  connected: boolean | null;
  failure_code: string | null;
  last_success_at_utc: string | null;
  circuit_state: string | null;
  circuit_failure_count: number;
  circuit_retry_at_utc: string | null;
}

interface DownloaderInstanceList {
  configuration_revision: number;
  applied_configuration_revision: number;
  restart_required: boolean;
  items: DownloaderInstance[];
}

interface DownloaderConnectionTest {
  id: string;
  connected: boolean;
  task_count: number | null;
  latency_ms: number;
  failure_code: string | null;
  message: string;
  client_version: string | null;
  client_default_save_path: string | null;
}

interface DownloaderPathProbe {
  id: string;
  success: boolean;
  hard_link_supported: boolean;
  download_path: string;
  save_path: string;
  failure_code: string | null;
  message: string;
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
const downloaderConfigDialog = element<HTMLDialogElement>("#downloader-config-dialog");
const configurationDialog = element<HTMLDialogElement>("#configuration-dialog");
let activeDeletePreview: DeletePreview | null = null;
let currentConfiguration: RuntimeConfiguration | null = null;
let activeRssRules: RssRuleSnapshot | null = null;
let sourceProfiles: SourceProfile[] = [];
let activeSourceId: string | null = null;
let downloaderInstances: DownloaderInstance[] = [];
let downloaderConfigurationRevision = 0;
let activeDownloaderId: string | null = null;
let ruleIdSequence = 0;

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

function configurationCard(title: string, fields: Array<[string, string]>): HTMLElement {
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

function enabledLabel(value: boolean): string {
  return value ? "已启用" : "已关闭";
}

function seasonFailurePriority(metadata: RuntimeConfiguration["metadata"]): HTMLElement {
  const panel = document.createElement("section");
  panel.className = "failure-priority";
  panel.setAttribute("aria-label", "TMDB 季度失败优先级");

  const caption = document.createElement("p");
  caption.className = "failure-priority-caption";
  caption.textContent = "由高到低执行；任一策略成功立即停止。Skip 命中会终止后续 fallback。";

  const sequence = document.createElement("ol");
  sequence.className = "failure-priority-list";
  const steps: Array<{
    priority: string;
    title: string;
    description: string;
    enabled: boolean;
    independent?: boolean;
  }> = [
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

function metadataConfigurationCard(config: RuntimeConfiguration): HTMLElement {
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

async function loadConfiguration(): Promise<void> {
  const status = element<HTMLElement>("#configuration-status");
  const container = element<HTMLElement>("#configuration");
  status.textContent = "正在读取脱敏后的生效配置…";
  try {
    const response = await fetch("/api/v1/config", { headers });
    if (!response.ok) throw new Error(await responseError(response));
    const config = await response.json() as RuntimeConfiguration;
    currentConfiguration = config;
    element<HTMLButtonElement>("#configuration-reset").disabled =
      config.configuration_revision === 0;
    container.replaceChildren(
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
      metadataConfigurationCard(config),
      configurationCard("AI、偏移与 Torrent", [
        [
          "AI 匹配",
          `任务级 ${enabledLabel(
            config.metadata.ai.use_metadata_match,
          )} · 单提示词 · `
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
    );
    status.textContent = config.restart_required
      ? `存在待重启配置 · 已保存 revision ${config.configuration_revision} · `
        + `当前应用 revision ${config.applied_configuration_revision}`
      : `当前进程的生效值 · revision ${config.configuration_revision}；凭据永不回传。`;
  } catch (error) {
    currentConfiguration = null;
    container.replaceChildren();
    status.textContent = `配置读取失败：${errorMessage(error, "未知错误")}`;
  }
}

function configurationSecretLabel(state: "inherit" | "configured" | "cleared"): string {
  switch (state) {
    case "configured": return "当前私密覆盖：已配置（值已隐藏）";
    case "cleared": return "当前私密覆盖：已明确清除";
    default: return "当前私密覆盖：继承部署配置";
  }
}

function setConfigurationValue(id: string, value: string | number): void {
  element<HTMLInputElement>(id).value = String(value);
}

function setConfigurationChecked(id: string, value: boolean): void {
  element<HTMLInputElement>(id).checked = value;
}

function syncConfigurationSecretInputs(): void {
  const clearKey = element<HTMLInputElement>("#configuration-tmdb-key-clear").checked;
  const clearToken = element<HTMLInputElement>("#configuration-tmdb-token-clear").checked;
  const key = element<HTMLInputElement>("#configuration-tmdb-key");
  const token = element<HTMLInputElement>("#configuration-tmdb-token");
  key.disabled = clearKey;
  token.disabled = clearToken;
  if (clearKey) key.value = "";
  if (clearToken) token.value = "";
}

function openConfigurationEditor(): void {
  if (!currentConfiguration) return;
  const editable = currentConfiguration.editable;
  setConfigurationValue("#configuration-tmdb-url", editable.tmdb_base_url);
  setConfigurationValue("#configuration-tmdb-proxy", editable.tmdb_proxy_url ?? "");
  setConfigurationValue("#configuration-tmdb-language", editable.tmdb_language);
  setConfigurationValue("#configuration-tmdb-timeout", editable.tmdb_http_timeout_seconds);
  setConfigurationValue("#configuration-tmdb-key", "");
  setConfigurationChecked("#configuration-tmdb-key-clear", false);
  element<HTMLElement>("#configuration-tmdb-key-state").textContent =
    configurationSecretLabel(editable.tmdb_api_key_state);
  setConfigurationValue("#configuration-tmdb-token", "");
  setConfigurationChecked("#configuration-tmdb-token-clear", false);
  element<HTMLElement>("#configuration-tmdb-token-state").textContent =
    configurationSecretLabel(editable.tmdb_read_access_token_state);
  setConfigurationValue("#configuration-bangumi-url", editable.bangumi_base_url);
  setConfigurationValue("#configuration-bangumi-proxy", editable.bangumi_proxy_url ?? "");
  setConfigurationValue(
    "#configuration-bangumi-timeout",
    editable.bangumi_http_timeout_seconds,
  );
  setConfigurationChecked("#configuration-fail-skip", editable.season_failure_skip);
  setConfigurationChecked("#configuration-fail-backtrace", editable.season_failure_backtrace);
  setConfigurationChecked("#configuration-fail-title", editable.season_failure_use_title_season);
  setConfigurationChecked("#configuration-fail-first", editable.season_failure_use_first_season);
  setConfigurationChecked(
    "#configuration-ai-metadata",
    editable.ai_use_metadata_match,
  );
  setConfigurationChecked("#configuration-bangumi-fallback", editable.tmdb_failure_use_bangumi);
  setConfigurationChecked("#configuration-offset-cache", editable.mikan_trusted_offset_cache_enabled);
  setConfigurationValue("#configuration-ai-timeout", editable.ai_http_timeout_seconds);
  setConfigurationValue("#configuration-torrent-timeout", editable.torrent_http_timeout_seconds);
  setConfigurationValue("#configuration-torrent-bytes", editable.torrent_max_response_bytes);
  setConfigurationValue("#configuration-torrent-redirects", editable.torrent_max_redirects);
  setConfigurationValue("#configuration-torrent-ttl", editable.torrent_staging_ttl_seconds);
  element<HTMLElement>("#configuration-message").textContent =
    `正在编辑 revision ${currentConfiguration.configuration_revision}`;
  syncConfigurationSecretInputs();
  configurationDialog.showModal();
}

async function saveConfiguration(event: SubmitEvent): Promise<void> {
  event.preventDefault();
  if (!currentConfiguration) return;
  const save = element<HTMLButtonElement>("#configuration-save");
  const message = element<HTMLElement>("#configuration-message");
  save.disabled = true;
  message.textContent = "正在保存私密配置覆盖…";
  try {
    const requestHeaders = new Headers(headers);
    requestHeaders.set("Content-Type", "application/json");
    const response = await fetch("/api/v1/config", {
      method: "PUT",
      headers: requestHeaders,
      body: JSON.stringify({
        tmdb_base_url: element<HTMLInputElement>("#configuration-tmdb-url").value,
        tmdb_proxy_url:
          element<HTMLInputElement>("#configuration-tmdb-proxy").value || null,
        tmdb_language: element<HTMLInputElement>("#configuration-tmdb-language").value,
        tmdb_http_timeout_seconds:
          element<HTMLInputElement>("#configuration-tmdb-timeout").valueAsNumber,
        tmdb_api_key: element<HTMLInputElement>("#configuration-tmdb-key").value || null,
        clear_tmdb_api_key:
          element<HTMLInputElement>("#configuration-tmdb-key-clear").checked,
        tmdb_read_access_token:
          element<HTMLInputElement>("#configuration-tmdb-token").value || null,
        clear_tmdb_read_access_token:
          element<HTMLInputElement>("#configuration-tmdb-token-clear").checked,
        bangumi_base_url:
          element<HTMLInputElement>("#configuration-bangumi-url").value,
        bangumi_proxy_url:
          element<HTMLInputElement>("#configuration-bangumi-proxy").value || null,
        bangumi_http_timeout_seconds:
          element<HTMLInputElement>("#configuration-bangumi-timeout").valueAsNumber,
        season_failure_skip:
          element<HTMLInputElement>("#configuration-fail-skip").checked,
        season_failure_backtrace:
          element<HTMLInputElement>("#configuration-fail-backtrace").checked,
        season_failure_use_title_season:
          element<HTMLInputElement>("#configuration-fail-title").checked,
        season_failure_use_first_season:
          element<HTMLInputElement>("#configuration-fail-first").checked,
        ai_use_metadata_match:
          element<HTMLInputElement>("#configuration-ai-metadata").checked,
        ai_http_timeout_seconds:
          element<HTMLInputElement>("#configuration-ai-timeout").valueAsNumber,
        tmdb_failure_use_bangumi:
          element<HTMLInputElement>("#configuration-bangumi-fallback").checked,
        mikan_trusted_offset_cache_enabled:
          element<HTMLInputElement>("#configuration-offset-cache").checked,
        torrent_http_timeout_seconds:
          element<HTMLInputElement>("#configuration-torrent-timeout").valueAsNumber,
        torrent_max_response_bytes:
          element<HTMLInputElement>("#configuration-torrent-bytes").valueAsNumber,
        torrent_max_redirects:
          element<HTMLInputElement>("#configuration-torrent-redirects").valueAsNumber,
        torrent_staging_ttl_seconds:
          element<HTMLInputElement>("#configuration-torrent-ttl").valueAsNumber,
        expected_configuration_revision: currentConfiguration.configuration_revision,
      }),
    });
    if (!response.ok) throw new Error(await responseError(response));
    const saved = await response.json() as {
      configuration_revision: number;
      restart_required: boolean;
    };
    await loadConfiguration();
    message.textContent = `已保存 revision ${saved.configuration_revision}；重启主程序后生效。`;
  } catch (error) {
    message.textContent = `保存失败：${errorMessage(error, "未知错误")}；revision 冲突时请刷新后重试。`;
  } finally {
    save.disabled = false;
  }
}

async function resetConfiguration(): Promise<void> {
  if (!currentConfiguration || currentConfiguration.configuration_revision === 0) return;
  if (!window.confirm("恢复部署默认配置？重启主程序后生效。")) return;
  const status = element<HTMLElement>("#configuration-status");
  status.textContent = "正在移除私密配置覆盖…";
  try {
    const response = await fetch(
      `/api/v1/config?expected_revision=${currentConfiguration.configuration_revision}`,
      { method: "DELETE", headers },
    );
    if (!response.ok) throw new Error(await responseError(response));
    await loadConfiguration();
  } catch (error) {
    status.textContent = `恢复失败：${errorMessage(error, "未知错误")}`;
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

async function loadTrustedOffsets(): Promise<void> {
  const container = element<HTMLElement>("#trusted-offsets");
  try {
    const response = await fetch("/api/v1/mikan/trusted-offsets", { headers });
    if (!response.ok) throw new Error(await responseError(response));
    const body = await response.json() as { items: MikanTrustedOffsetItem[] };
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
  } catch (error) {
    const failed = document.createElement("p");
    failed.className = "muted empty";
    failed.textContent = `可信 offset 读取失败：${errorMessage(error, "未知错误")}`;
    container.replaceChildren(failed);
  }
}

async function clearTrustedOffset(item: MikanTrustedOffsetItem): Promise<void> {
  if (!window.confirm(
    `清理 Mikan ${item.mikanid} / Group ${item.groupid} 的自动证据与缓存？人工规则、完成记录和媒体文件不会删除。`,
  )) return;
  try {
    const response = await fetch(
      `/api/v1/mikan/trusted-offsets/${item.mikanid}/${item.groupid}`,
      { method: "DELETE", headers },
    );
    if (!response.ok) throw new Error(await responseError(response));
    await loadTrustedOffsets();
  } catch (error) {
    window.alert(`清理失败：${errorMessage(error, "未知错误")}`);
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
  } catch (error) {
    const failed = document.createElement("p");
    failed.className = "muted empty";
    failed.textContent = `元数据状态读取失败：${errorMessage(error, "未知错误")}`;
    container.replaceChildren(failed);
  }
}

function pendingStat(label: string, value: number): HTMLDivElement {
  const group = document.createElement("div");
  const term = document.createElement("dt");
  term.textContent = label;
  const description = document.createElement("dd");
  description.textContent = String(value);
  group.append(term, description);
  return group;
}

function positiveInteger(value: string): number | null {
  const parsed = Number(value);
  return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : null;
}

function createPendingRecoveryForm(
  bgmid: number,
  detail: PendingTmdbDetail,
): HTMLElement[] {
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
      || mappings.some((mapping) =>
        mapping.tmdb_season_number === null || mapping.tmdb_episode_number === null)) {
      status.textContent = "Series、Season、Episode 都必须是正整数。";
      return;
    }
    if (!window.confirm(
      `确认用 TMDB Series ${seriesId} 恢复 bgmid ${bgmid} 的 ${mappings.length} 条记录？`,
    )) return;

    submit.disabled = true;
    submit.textContent = "正在向 TMDB 验证…";
    status.textContent = "";
    try {
      const requestHeaders = new Headers(headers);
      requestHeaders.set("Content-Type", "application/json");
      const response = await fetch(
        `/api/v1/metadata/pending-tmdb/${encodeURIComponent(String(bgmid))}/recover`,
        {
          method: "POST",
          headers: requestHeaders,
          body: JSON.stringify({ tmdb_series_id: seriesId, mappings }),
        },
      );
      if (!response.ok) throw new Error(await responseError(response));
      const result = await response.json() as PendingTmdbRecoveryResult;
      const duplicates = result.items.filter(
        (item) => item.state === "DuplicateAfterResolution",
      ).length;
      status.textContent = `已验证并恢复 ${result.items.length} 条；解析后重复 ${duplicates} 条。`;
      await Promise.all([loadPendingTmdb(true), loadMetadataTasks()]);
    } catch (error) {
      status.textContent = `恢复失败：${errorMessage(error, "未知错误")}`;
      submit.disabled = false;
      submit.textContent = "验证并恢复";
    }
  });
  return [heading, explanation, form];
}

async function loadPendingTmdbDetail(
  bgmid: number,
  target: HTMLDivElement,
  button: HTMLButtonElement,
): Promise<void> {
  button.disabled = true;
  button.textContent = "读取中…";
  try {
    const response = await fetch(
      `/api/v1/metadata/pending-tmdb/${encodeURIComponent(String(bgmid))}`,
      { headers },
    );
    if (!response.ok) throw new Error(await responseError(response));
    const detail = await response.json() as PendingTmdbDetail;
    const sections: HTMLElement[] = [];
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
  } catch (error) {
    target.textContent = `详情读取失败：${errorMessage(error, "未知错误")}`;
    button.disabled = false;
    button.textContent = "重试详情";
  }
}

async function loadPendingTmdb(force = false): Promise<void> {
  if (!force && document.querySelector(".pending-recovery-form")) return;
  const container = element<HTMLElement>("#pending-tmdb-list");
  try {
    const response = await fetch("/api/v1/metadata/pending-tmdb", { headers });
    if (!response.ok) throw new Error(await responseError(response));
    const body = await response.json() as { items: PendingTmdbSummary[] };
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
      stats.append(
        pendingStat("关联任务", item.task_count),
        pendingStat("已处理文件", item.processed_file_count),
        pendingStat("兜底记录", item.fallback_record_count),
        pendingStat("活动 claim", item.active_claim_count),
        pendingStat("已完成 claim", item.completed_claim_count),
        pendingStat("重复文件", item.duplicate_file_count),
      );
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
  } catch (error) {
    const failed = document.createElement("p");
    failed.className = "muted empty";
    failed.textContent = `待补全状态读取失败：${errorMessage(error, "未知错误")}`;
    container.replaceChildren(failed);
  }
}

async function testDownloader(id: string, button: HTMLButtonElement): Promise<void> {
  const status = element<HTMLElement>("#downloader-status");
  button.disabled = true;
  button.textContent = "测试中…";
  try {
    const response = await fetch(`/api/v1/downloaders/${encodeURIComponent(id)}/test`, {
      method: "POST",
      headers,
    });
    if (!response.ok) throw new Error(await responseError(response));
    const result = await response.json() as DownloaderConnectionTest;
    status.textContent = result.connected
      ? `${id} 连接成功 · ${textOrDash(result.client_version)} · ${result.task_count ?? 0} 个任务 · ${result.latency_ms} ms · qB 默认路径 ${textOrDash(result.client_default_save_path)}`
      : `${id} 连接失败 · ${result.failure_code ?? "unknown"} · ${result.message}`;
    await loadDownloaders();
  } catch (error) {
    status.textContent = `${id} 测试失败：${errorMessage(error, "未知错误")}`;
  } finally {
    button.disabled = false;
    button.textContent = "测试连接";
  }
}

async function probeDownloaderPath(id: string, button: HTMLButtonElement): Promise<void> {
  const status = element<HTMLElement>("#downloader-status");
  button.disabled = true;
  button.textContent = "探测中…";
  try {
    const response = await fetch(`/api/v1/downloaders/${encodeURIComponent(id)}/path-probe`, {
      method: "POST",
      headers,
    });
    if (!response.ok) throw new Error(await responseError(response));
    const result = await response.json() as DownloaderPathProbe;
    status.textContent = result.success
      ? `${id} 路径可见且支持硬链接 · ${result.download_path} → ${result.save_path}`
      : `${id} 路径探测失败 · ${result.failure_code ?? "unknown"} · ${result.message}`;
  } catch (error) {
    status.textContent = `${id} 路径探测失败：${errorMessage(error, "未知错误")}`;
  } finally {
    button.disabled = false;
    button.textContent = "探测路径";
  }
}

function openDownloaderConfig(instance: DownloaderInstance | null): void {
  activeDownloaderId = instance?.id ?? null;
  const id = element<HTMLInputElement>("#downloader-config-id");
  id.disabled = instance !== null;
  id.value = instance?.id ?? "";
  element<HTMLInputElement>("#downloader-config-url").value = instance?.base_url ?? "http://127.0.0.1:8080/";
  element<HTMLInputElement>("#downloader-config-username").value = "";
  element<HTMLInputElement>("#downloader-config-password").value = "";
  element<HTMLInputElement>("#downloader-config-path").value = instance?.download_path ?? "";
  element<HTMLInputElement>("#downloader-config-enabled").checked = instance?.enabled ?? true;
  element<HTMLInputElement>("#downloader-config-clear-password").checked = false;
  element<HTMLButtonElement>("#downloader-config-delete").disabled =
    instance?.configuration_source !== "private_override";
  element<HTMLElement>("#downloader-config-message").textContent =
    instance?.credentials_configured
      ? "已有凭据已配置；密码字段留空会保留，且不会从服务端读回。"
      : "当前没有已配置凭据。";
  downloaderConfigDialog.showModal();
}

async function saveDownloaderConfig(event: SubmitEvent): Promise<void> {
  event.preventDefault();
  const id = activeDownloaderId ?? element<HTMLInputElement>("#downloader-config-id").value;
  const save = element<HTMLButtonElement>("#downloader-config-save");
  const message = element<HTMLElement>("#downloader-config-message");
  save.disabled = true;
  message.textContent = "正在原子写入私有配置…";
  try {
    const requestHeaders = new Headers(headers);
    requestHeaders.set("Content-Type", "application/json");
    const response = await fetch(`/api/v1/downloaders/${encodeURIComponent(id)}`, {
      method: "PUT",
      headers: requestHeaders,
      body: JSON.stringify({
        base_url: element<HTMLInputElement>("#downloader-config-url").value,
        username: element<HTMLInputElement>("#downloader-config-username").value || null,
        password: element<HTMLInputElement>("#downloader-config-password").value || null,
        clear_password: element<HTMLInputElement>("#downloader-config-clear-password").checked,
        download_path: element<HTMLInputElement>("#downloader-config-path").value,
        enabled: element<HTMLInputElement>("#downloader-config-enabled").checked,
        expected_configuration_revision: downloaderConfigurationRevision,
      }),
    });
    if (!response.ok) throw new Error(await responseError(response));
    message.textContent = "已保存；请重启主程序以应用新客户端配置。";
    await loadDownloaders();
    window.setTimeout(() => downloaderConfigDialog.close(), 1000);
  } catch (error) {
    message.textContent = `保存失败：${errorMessage(error, "未知错误")}`;
  } finally {
    save.disabled = false;
  }
}

async function deleteDownloaderOverride(): Promise<void> {
  const instance = downloaderInstances.find((item) => item.id === activeDownloaderId);
  if (!instance || instance.configuration_source !== "private_override") return;
  if (!window.confirm(`移除 ${instance.id} 的私有覆盖？服务端会拒绝仍有引用的实例。`)) return;
  const message = element<HTMLElement>("#downloader-config-message");
  try {
    const response = await fetch(
      `/api/v1/downloaders/${encodeURIComponent(instance.id)}?expected_configuration_revision=${downloaderConfigurationRevision}`,
      { method: "DELETE", headers },
    );
    if (!response.ok) throw new Error(await responseError(response));
    await loadDownloaders();
    downloaderConfigDialog.close();
  } catch (error) {
    message.textContent = `移除失败：${errorMessage(error, "未知错误")}`;
  }
}

async function loadDownloaders(): Promise<void> {
  const status = element<HTMLElement>("#downloader-status");
  const list = element<HTMLElement>("#downloader-list");
  status.textContent = "正在读取下载器实例…";
  try {
    const response = await fetch("/api/v1/downloaders", { headers });
    if (!response.ok) throw new Error(await responseError(response));
    const body = await response.json() as DownloaderInstanceList;
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
  } catch (error) {
    const failed = document.createElement("p");
    failed.className = "muted empty";
    failed.textContent = `下载器读取失败：${errorMessage(error, "未知错误")}`;
    list.replaceChildren(failed);
    status.textContent = failed.textContent;
  }
}

function activeSource(): SourceProfile | null {
  return sourceProfiles.find((profile) => profile.id === activeSourceId) ?? null;
}

function refreshSourceDownloaderOptions(): void {
  const ids = [...new Set([
    ...downloaderInstances.filter((instance) => instance.enabled).map((instance) => instance.id),
    ...sourceProfiles.map((profile) => profile.downloader_id),
  ])].sort();
  element<HTMLDataListElement>("#source-downloader-options").replaceChildren(
    ...ids.map((id) => {
      const option = document.createElement("option");
      option.value = id;
      return option;
    }),
  );
}

function updateSourceWarning(): void {
  const strategy = element<HTMLSelectElement>("#source-strategy").value;
  const seeding = element<HTMLInputElement>("#source-seeding-time");
  if (strategy === "move") seeding.value = "0";
  seeding.disabled = strategy === "move";
  element<HTMLElement>("#source-warning").textContent = strategy === "move"
    ? "move 会在下载完成后移动源文件，做种分钟固定为 0；修改只影响之后创建的任务。"
    : "做种分钟：-1 无限、0 不做种、正数为上限；历史任务继续使用原 revision 路由快照。";
}

function populateSourceForm(profile: SourceProfile | null): void {
  activeSourceId = profile?.id ?? null;
  const id = element<HTMLInputElement>("#source-id");
  const adapter = element<HTMLSelectElement>("#source-adapter");
  id.disabled = profile !== null;
  adapter.disabled = profile !== null;
  id.value = profile?.id ?? "";
  element<HTMLInputElement>("#source-name").value = profile?.display_name ?? "";
  adapter.value = profile?.adapter ?? "u2";
  element<HTMLInputElement>("#source-downloader").value = profile?.downloader_id ?? "pt";
  element<HTMLSelectElement>("#source-strategy").value = profile?.file_strategy ?? "link";
  element<HTMLInputElement>("#source-category").value = profile?.category ?? "animegonet";
  element<HTMLInputElement>("#source-tags").value = profile?.tags.join(", ") ?? "";
  element<HTMLInputElement>("#source-seeding-time").value =
    String(profile?.seeding_time_minutes ?? 0);
  element<HTMLTextAreaElement>("#source-hosts").value = profile?.allowed_torrent_hosts.join("\n") ?? "";
  element<HTMLInputElement>("#source-enabled").checked = profile?.enabled ?? true;
  element<HTMLInputElement>("#source-filter-enabled").checked = profile?.rss_filter_enabled ?? false;
  element<HTMLInputElement>("#source-priority-enabled").checked = profile?.rss_priority_enabled ?? false;
  const remove = element<HTMLButtonElement>("#source-delete");
  remove.disabled = profile === null || profile.is_default;
  remove.title = profile?.is_default ? "默认 Mikan 来源不可删除" : "";
  element<HTMLButtonElement>("#route-preview-run").disabled = profile === null;
  element<HTMLElement>("#route-preview-result").textContent = profile === null
    ? "请先保存来源，再按持久化 revision 计算路由。"
    : `${profile.id} revision ${profile.revision}，等待预览。`;
  updateSourceWarning();
  renderSourceList();
}

function renderSourceList(): void {
  const list = element<HTMLElement>("#source-list");
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

function optionalPositiveNumber(selector: string): number | null {
  const input = element<HTMLInputElement>(selector);
  return input.value === "" || !Number.isFinite(input.valueAsNumber) ? null : input.valueAsNumber;
}

async function previewSourceRoute(): Promise<void> {
  const current = activeSource();
  if (!current) return;
  const output = element<HTMLElement>("#route-preview-result");
  const run = element<HTMLButtonElement>("#route-preview-run");
  run.disabled = true;
  output.textContent = "正在计算路由…";
  try {
    const requestHeaders = new Headers(headers);
    requestHeaders.set("Content-Type", "application/json");
    const response = await fetch(
      `/api/v1/sources/${encodeURIComponent(current.id)}/route-preview`,
      {
        method: "POST",
        headers: requestHeaders,
        body: JSON.stringify({
          title: element<HTMLInputElement>("#source-name").value.trim(),
          source_work_id: element<HTMLInputElement>("#route-source-work-id").value.trim() || null,
          mikanid: optionalPositiveNumber("#route-mikanid"),
          bgmid: optionalPositiveNumber("#route-bgmid"),
          anidbid: optionalPositiveNumber("#route-anidbid"),
          imdbid: element<HTMLInputElement>("#route-imdbid").value.trim() || null,
        }),
      },
    );
    if (!response.ok) throw new Error(await responseError(response));
    const route = await response.json() as SourceRoutePreview;
    output.textContent = route.valid
      ? [
          `有效 · ${route.source_profile_id} rev ${route.source_profile_revision} (${route.adapter})`,
          `下载器 ${route.downloader_id} · ${route.download_path ?? "路径不可用"}`,
          `媒体库 ${route.save_path}`,
          `策略 ${route.file_strategy} · 分类 ${route.category} · Tags ${route.tags.join(", ") || "—"}`,
          `做种 ${route.seeding_time_minutes} 分钟 · RSS规则 rev ${route.rss_rule_revision ?? "—"}`,
        ].join("\n")
      : `无效\n${route.errors.map((error) => `• ${error}`).join("\n")}`;
  } catch (error) {
    output.textContent = `预览失败：${errorMessage(error, "未知错误")}`;
  } finally {
    run.disabled = false;
  }
}

async function loadSources(selectedId?: string): Promise<void> {
  const status = element<HTMLElement>("#source-status");
  status.textContent = "正在读取来源配置…";
  try {
    const response = await fetch("/api/v1/sources", { headers });
    if (!response.ok) throw new Error(await responseError(response));
    const body = await response.json() as SourceProfileList;
    sourceProfiles = body.items;
    refreshSourceDownloaderOptions();
    const selected = sourceProfiles.find((profile) => profile.id === (selectedId ?? activeSourceId))
      ?? sourceProfiles[0]
      ?? null;
    populateSourceForm(selected);
    status.textContent = `${sourceProfiles.length} 个来源 · 修改采用 revision 乐观并发且不改变历史任务路由`;
  } catch (error) {
    sourceProfiles = [];
    activeSourceId = null;
    renderSourceList();
    status.textContent = `来源读取失败：${errorMessage(error, "未知错误")}`;
  }
}

function sourceHosts(): string[] {
  return element<HTMLTextAreaElement>("#source-hosts").value
    .split(/[\r\n,，]+/u)
    .map((host) => host.trim().toLowerCase())
    .filter(Boolean);
}

function sourceTags(): string[] {
  return element<HTMLInputElement>("#source-tags").value
    .split(/[\r\n,，]+/u)
    .map((tag) => tag.trim())
    .filter(Boolean);
}

async function saveSource(event: SubmitEvent): Promise<void> {
  event.preventDefault();
  const current = activeSource();
  const save = element<HTMLButtonElement>("#source-save");
  const status = element<HTMLElement>("#source-status");
  const common = {
    display_name: element<HTMLInputElement>("#source-name").value.trim(),
    downloader_id: element<HTMLInputElement>("#source-downloader").value.trim(),
    file_strategy: element<HTMLSelectElement>("#source-strategy").value,
    category: element<HTMLInputElement>("#source-category").value.trim(),
    tags: sourceTags(),
    seeding_time_minutes: element<HTMLInputElement>("#source-seeding-time").valueAsNumber,
    allowed_torrent_hosts: sourceHosts(),
    rss_filter_enabled: element<HTMLInputElement>("#source-filter-enabled").checked,
    rss_priority_enabled: element<HTMLInputElement>("#source-priority-enabled").checked,
    enabled: element<HTMLInputElement>("#source-enabled").checked,
  };
  const payload = current
    ? { ...common, expected_revision: current.revision }
    : {
        ...common,
        id: element<HTMLInputElement>("#source-id").value,
        adapter: element<HTMLSelectElement>("#source-adapter").value,
      };
  save.disabled = true;
  status.textContent = current ? "正在保存来源…" : "正在创建来源…";
  try {
    const requestHeaders = new Headers(headers);
    requestHeaders.set("Content-Type", "application/json");
    const response = await fetch(
      current ? `/api/v1/sources/${encodeURIComponent(current.id)}` : "/api/v1/sources",
      {
        method: current ? "PUT" : "POST",
        headers: requestHeaders,
        body: JSON.stringify(payload),
      },
    );
    if (!response.ok) throw new Error(await responseError(response));
    const saved = await response.json() as SourceProfile;
    await loadSources(saved.id);
    status.textContent = `已保存 ${saved.display_name} · revision ${saved.revision}`;
  } catch (error) {
    status.textContent = `保存失败：${errorMessage(error, "未知错误")}；revision 冲突时请重新选择来源。`;
  } finally {
    save.disabled = false;
  }
}

async function deleteSource(): Promise<void> {
  const current = activeSource();
  if (!current || current.is_default) return;
  if (!window.confirm(`删除来源 ${current.display_name}？已有任务或 RSS batch 引用时服务端会拒绝。`)) return;
  const status = element<HTMLElement>("#source-status");
  status.textContent = "正在删除来源…";
  try {
    const response = await fetch(
      `/api/v1/sources/${encodeURIComponent(current.id)}?expected_revision=${current.revision}`,
      { method: "DELETE", headers },
    );
    if (!response.ok) throw new Error(await responseError(response));
    activeSourceId = null;
    await loadSources();
    status.textContent = `来源 ${current.id} 已删除`;
  } catch (error) {
    status.textContent = `删除失败：${errorMessage(error, "未知错误")}`;
  }
}

function moveItem<T>(items: T[], index: number, delta: number): void {
  const target = index + delta;
  if (target < 0 || target >= items.length) return;
  [items[index], items[target]] = [items[target], items[index]];
}

function nextRuleId(prefix: string): string {
  ruleIdSequence += 1;
  return `${prefix}-${Date.now().toString(36)}-${ruleIdSequence.toString(36)}`;
}

function button(label: string, action: () => void): HTMLButtonElement {
  const result = document.createElement("button");
  result.type = "button";
  result.textContent = label;
  result.addEventListener("click", action);
  return result;
}

function renderArrayEditor(
  rule: RssNamedArray,
  index: number,
  count: number,
  onMove: (delta: number) => void,
  onRemove: () => void,
): HTMLElement {
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

function renderArrayList(container: HTMLElement, rules: RssNamedArray[]): void {
  container.replaceChildren(...rules.map((rule, index) => renderArrayEditor(
    rule,
    index,
    rules.length,
    (delta) => { moveItem(rules, index, delta); renderRssRules(); },
    () => { rules.splice(index, 1); renderRssRules(); },
  )));
}

function renderRssRules(): void {
  if (!activeRssRules) return;
  element<HTMLElement>("#rss-rule-status").textContent =
    `revision ${activeRssRules.revision} · 旧过滤 ${activeRssRules.rss_filter_enabled ? "开启" : "关闭"} · 批次优选 ${activeRssRules.rss_priority_enabled ? "开启" : "关闭"}`;
  renderArrayList(element<HTMLElement>("#rss-whitelist"), activeRssRules.whitelist);
  renderArrayList(element<HTMLElement>("#rss-blacklist"), activeRssRules.blacklist);
  const groupContainer = element<HTMLElement>("#rss-priority-groups");
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
    const up = button("上移组", () => { moveItem(activeRssRules!.priority_groups, groupIndex, -1); renderRssRules(); });
    up.disabled = groupIndex === 0;
    const down = button("下移组", () => { moveItem(activeRssRules!.priority_groups, groupIndex, 1); renderRssRules(); });
    down.disabled = groupIndex + 1 === activeRssRules!.priority_groups.length;
    groupActions.append(up, down, button("删除组", () => {
      activeRssRules!.priority_groups.splice(groupIndex, 1);
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

async function loadRssRules(): Promise<void> {
  const status = element<HTMLElement>("#rss-rule-status");
  status.textContent = "正在读取 Mikan 规则…";
  try {
    const response = await fetch("/api/v1/rss-rules/mikan", { headers });
    if (!response.ok) throw new Error(await responseError(response));
    activeRssRules = await response.json() as RssRuleSnapshot;
    renderRssRules();
  } catch (error) {
    activeRssRules = null;
    status.textContent = `规则读取失败：${errorMessage(error, "未知错误")}`;
  }
}

async function saveRssRules(): Promise<void> {
  if (!activeRssRules) return;
  const save = element<HTMLButtonElement>("#rss-save");
  const status = element<HTMLElement>("#rss-rule-status");
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
    if (!response.ok) throw new Error(await responseError(response));
    activeRssRules = await response.json() as RssRuleSnapshot;
    renderRssRules();
    status.textContent = `保存成功 · revision ${activeRssRules.revision}`;
  } catch (error) {
    status.textContent = `保存失败：${errorMessage(error, "未知错误")}；如有 revision 冲突请重新载入。`;
  } finally {
    save.disabled = false;
  }
}

async function previewRssRules(): Promise<void> {
  const results = element<HTMLElement>("#rss-preview-results");
  const titles = element<HTMLTextAreaElement>("#rss-preview-titles").value
    .split(/\r?\n/u).map((title) => title.trim()).filter(Boolean);
  if (titles.length === 0) {
    results.textContent = "请先输入至少一个候选标题。";
    return;
  }

  const mikanIdValue = element<HTMLInputElement>("#rss-preview-mikanid").valueAsNumber;
  const kind = element<HTMLInputElement>("#rss-preview-kind").value.trim();
  const episode = element<HTMLInputElement>("#rss-preview-episode").value.trim();
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
    if (!response.ok) throw new Error(await responseError(response));
    const body = await response.json() as { decisions: RssRuleDecision[] };
    results.replaceChildren(...body.decisions.map((decision, index) => {
      const row = document.createElement("div");
      row.className = `rss-decision ${decision.decision === "winner" ? "winner" : decision.decision.startsWith("rejected") ? "rejected" : "suppressed"}`;
      const groups = decision.evaluated_priority_groups.length > 0
        ? ` · groups ${decision.evaluated_priority_groups.join(" → ")}` : "";
      row.textContent = `${titles[index]} · ${decision.decision} · ${decision.reason}${decision.winner_id ? ` · winner ${decision.winner_id}` : ""}${groups}`;
      return row;
    }));
  } catch (error) {
    results.textContent = `预览失败：${errorMessage(error, "未知错误")}`;
  }
}

element<HTMLButtonElement>("#rss-reload").addEventListener("click", () => void loadRssRules());
element<HTMLButtonElement>("#trusted-offsets-reload").addEventListener(
  "click",
  () => void loadTrustedOffsets(),
);
element<HTMLButtonElement>("#pending-tmdb-reload").addEventListener(
  "click",
  () => void loadPendingTmdb(true),
);
element<HTMLButtonElement>("#configuration-reload").addEventListener("click", () => void loadConfiguration());
element<HTMLButtonElement>("#configuration-edit").addEventListener("click", openConfigurationEditor);
element<HTMLButtonElement>("#configuration-reset").addEventListener("click", () => void resetConfiguration());
element<HTMLButtonElement>("#configuration-close").addEventListener("click", () => configurationDialog.close());
element<HTMLFormElement>("#configuration-form").addEventListener(
  "submit",
  (event) => void saveConfiguration(event),
);
element<HTMLInputElement>("#configuration-tmdb-key-clear").addEventListener(
  "change",
  syncConfigurationSecretInputs,
);
element<HTMLInputElement>("#configuration-tmdb-token-clear").addEventListener(
  "change",
  syncConfigurationSecretInputs,
);
element<HTMLButtonElement>("#rss-save").addEventListener("click", () => void saveRssRules());
element<HTMLButtonElement>("#rss-add-whitelist").addEventListener("click", () => {
  activeRssRules?.whitelist.push({ id: nextRuleId("whitelist"), name: "新白名单", enabled: true, values: [] });
  renderRssRules();
});
element<HTMLButtonElement>("#rss-add-blacklist").addEventListener("click", () => {
  activeRssRules?.blacklist.push({ id: nextRuleId("blacklist"), name: "新黑名单", enabled: true, values: [] });
  renderRssRules();
});
element<HTMLButtonElement>("#rss-add-group").addEventListener("click", () => {
  activeRssRules?.priority_groups.push({ id: nextRuleId("group"), name: "新优先级组", arrays: [] });
  renderRssRules();
});
element<HTMLButtonElement>("#rss-preview-run").addEventListener("click", () => void previewRssRules());
element<HTMLButtonElement>("#source-new").addEventListener("click", () => populateSourceForm(null));
element<HTMLFormElement>("#source-form").addEventListener("submit", (event) => void saveSource(event));
element<HTMLButtonElement>("#source-delete").addEventListener("click", () => void deleteSource());
element<HTMLSelectElement>("#source-strategy").addEventListener("change", updateSourceWarning);
element<HTMLButtonElement>("#route-preview-run").addEventListener("click", () => void previewSourceRoute());
element<HTMLButtonElement>("#downloader-reload").addEventListener("click", () => void loadDownloaders());
element<HTMLButtonElement>("#downloader-new").addEventListener("click", () => openDownloaderConfig(null));
element<HTMLButtonElement>("#downloader-config-close").addEventListener("click", () => downloaderConfigDialog.close());
element<HTMLFormElement>("#downloader-config-form").addEventListener("submit", (event) => void saveDownloaderConfig(event));
element<HTMLButtonElement>("#downloader-config-delete").addEventListener("click", () => void deleteDownloaderOverride());

void loadStatus();
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
