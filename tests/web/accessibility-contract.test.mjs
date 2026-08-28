import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { parseHTML } from "linkedom";

const htmlPath = new URL("../../src/AnimeGoNet.App/wwwroot/index.html", import.meta.url);
const cssPath = new URL("../../src/AnimeGoNet.App/wwwroot/styles.css", import.meta.url);
const appPath = new URL("../../src/AnimeGoNet.App/wwwroot/app.js", import.meta.url);

async function page() {
  const html = await readFile(htmlPath, "utf8");
  return parseHTML(html).document;
}

function accessibleName(element, document) {
  const label = element.getAttribute("aria-label")?.trim();
  if (label) return label;
  const labelledBy = element.getAttribute("aria-labelledby")?.trim();
  if (labelledBy) {
    return labelledBy.split(/\s+/).map(id => document.getElementById(id)?.textContent?.trim() ?? "").join(" ").trim();
  }
  return element.textContent?.trim() ?? "";
}

test("page has a keyboard skip link, named main sections and dialogs", async () => {
  const document = await page();
  const skip = document.querySelector("a.skip-link");
  const main = document.querySelector("main");
  assert.ok(skip);
  assert.equal(skip.getAttribute("href"), "#main-content");
  assert.equal(main?.id, "main-content");
  assert.equal(main?.getAttribute("tabindex"), "-1");

  for (const section of main.querySelectorAll(":scope > section")) {
    assert.notEqual(accessibleName(section, document), "", `unnamed main section: ${section.outerHTML.slice(0, 120)}`);
  }
  for (const dialog of document.querySelectorAll("dialog")) {
    assert.notEqual(accessibleName(dialog, document), "", `unnamed dialog: #${dialog.id}`);
  }
});

test("static controls have accessible names and no positive tabindex", async () => {
  const document = await page();
  for (const control of document.querySelectorAll("input:not([type=hidden]), select, textarea")) {
    const wrapped = control.closest("label")?.textContent?.trim() ?? "";
    const explicit = control.id
      ? document.querySelector(`label[for="${control.id}"]`)?.textContent?.trim() ?? ""
      : "";
    const aria = accessibleName(control, document);
    assert.notEqual(wrapped || explicit || aria, "", `unlabelled control: #${control.id}`);
  }
  for (const button of document.querySelectorAll("button")) {
    assert.notEqual(accessibleName(button, document), "", `unnamed button: #${button.id}`);
  }
  for (const node of document.querySelectorAll("[tabindex]")) {
    assert.ok(Number(node.getAttribute("tabindex")) <= 0, `positive tabindex: ${node.outerHTML}`);
  }
});

test("WebUI authentication uses the login dialog instead of a dedicated URL field", async () => {
  const [document, app] = await Promise.all([
    page(),
    readFile(appPath, "utf8"),
  ]);
  assert.ok(document.querySelector("#webui-authentication-access-key"));
  const host = document.querySelector("#webui-listen-host");
  const port = document.querySelector("#webui-listen-port");
  assert.ok(host);
  assert.ok(port);
  assert.equal(host.getAttribute("maxlength"), "253");
  assert.equal(port.getAttribute("min"), "0");
  assert.equal(port.getAttribute("max"), "65535");
  assert.equal(document.querySelector("#webui-authentication-url"), null);
  assert.ok(document.querySelector("#webui-access-key-open"));
  assert.ok(document.querySelector("#webui-access-key-dialog"));
  assert.doesNotMatch(app, /webUiAuthenticatedUrl/);
  assert.doesNotMatch(app, /#webui-authentication-url/);
  assert.match(app, /web\.host = requestedHost/);
  assert.match(app, /web\.port = requestedPort/);
  assert.match(app, /监听端口必须是 0–65535 之间的整数/);
});

test("Mikan and U2 show the same AnimeGoHelper API base URL", async () => {
  const [document, app] = await Promise.all([
    page(),
    readFile(appPath, "utf8"),
  ]);
  assert.ok(document.querySelector("#web-api-compatibility-url"));
  assert.ok(document.querySelector("#u2-web-api-url"));
  assert.match(app, /#web-api-compatibility-url"\)\.value = animeGoHelperApiUrl\(\)/);
  assert.match(app, /#u2-web-api-url"\)\.value = animeGoHelperApiUrl\(\)/);
  assert.doesNotMatch(app, /function animeGoHelperU2ApiUrl/);
  assert.match(
    document.querySelector("#u2-web-api-url")?.closest("label")?.textContent ?? "",
    /脚本会自动追加 U2 专用/,
  );
});

test("configuration archive exposes explicit daily backup automation", async () => {
  const [document, app] = await Promise.all([
    page(),
    readFile(appPath, "utf8"),
  ]);
  const form = document.querySelector("#configuration-backup-automation-form");
  const enabled = document.querySelector("#configuration-backup-automation-enabled");
  const retention = document.querySelector("#configuration-backup-automation-retention");
  assert.ok(form);
  assert.ok(enabled);
  assert.equal(retention?.getAttribute("value"), "10");
  assert.equal(retention?.getAttribute("min"), "1");
  assert.equal(retention?.getAttribute("max"), "100");
  assert.match(app, /\/api\/v1\/configuration-archive\/automation/);
  assert.match(app, /backup\.kind === "automatic" \? "每日自动备份"/);
});

test("ids are unique and initial async regions expose valid state", async () => {
  const document = await page();
  const ids = [...document.querySelectorAll("[id]")].map(node => node.id);
  assert.equal(new Set(ids).size, ids.length);

  const regions = [...document.querySelectorAll("[data-ui-state]")];
  assert.ok(regions.length >= 9);
  for (const region of regions) {
    assert.equal(region.dataset.uiState, "loading");
    assert.equal(region.getAttribute("aria-busy"), "true");
    assert.equal(region.getAttribute("aria-live"), "polite");
    assert.equal(region.querySelector('[role="status"]')?.getAttribute("aria-atomic"), "true");
  }
});

test("stylesheet provides focus, responsive and reduced-motion contracts", async () => {
  const css = await readFile(cssPath, "utf8");
  assert.match(css, /:root\s*\{[^}]*font-size:\s*16px/s);
  assert.match(css, /\.skip-link:focus\s*\{/);
  assert.match(css, /:focus-visible\s*\{/);
  assert.match(css, /@media\s*\(max-width:\s*620px\)/);
  assert.match(css, /@media\s*\(prefers-reduced-motion:\s*reduce\)/);
  assert.match(css, /min-height:\s*44px/);
});

test("metadata detail visibly separates source evidence from TMDB authority", async () => {
  const [app, css] = await Promise.all([
    readFile(appPath, "utf8"),
    readFile(cssPath, "utf8"),
  ]);
  assert.match(app, /来源持久证据（不作为 TMDB 规范字段）/);
  assert.match(app, /source_item_id_fingerprint/);
  assert.match(app, /source_work_id_fingerprint/);
  assert.match(css, /\.metadata-source-evidence\s*\{/);
});

test("metadata attention counters are prominent buttons with direct filters", async () => {
  const [document, app, css] = await Promise.all([
    page(),
    readFile(appPath, "utf8"),
    readFile(cssPath, "utf8"),
  ]);
  const summary = document.querySelector("#metadata-attention-summary");
  assert.ok(summary);
  for (const id of [
    "metadata-attention-other",
    "metadata-attention-failed",
    "metadata-attention-review",
  ]) {
    const button = summary.querySelector(`#${id}`);
    assert.ok(button);
    assert.equal(button.tagName, "BUTTON");
    assert.equal(button.getAttribute("aria-pressed"), "false");
  }
  assert.ok(document.querySelector("#metadata-review-filter"));
  assert.match(app, /applyMetadataAttentionFilter/);
  assert.match(app, /attention\.other_items/);
  assert.match(app, /attention\.failed_items/);
  assert.match(app, /attention\.review_pending_items/);
  assert.match(css, /\.metadata-attention-summary\s*\{[^}]*grid-template-columns:\s*repeat\(3/s);
  assert.match(css, /\.metadata-attention-card strong\s*\{[^}]*font-size:\s*clamp\(2rem/s);
});

test("overview exposes operational statistics as direct navigation and filters", async () => {
  const [document, app, css] = await Promise.all([
    page(),
    readFile(appPath, "utf8"),
    readFile(cssPath, "utf8"),
  ]);
  const summary = document.querySelector("#overview-metadata-attention-summary");
  assert.ok(summary);
  const groups = [...document.querySelectorAll("#overview-statistics-groups > .overview-statistics-group")];
  assert.equal(groups[0]?.querySelector("h3")?.textContent.trim(), "系统资源");
  assert.equal(groups[0]?.querySelectorAll(".overview-resource-card").length, 3);
  for (const id of [
    "overview-download-active",
    "overview-download-paused",
    "overview-download-failed",
    "overview-download-waiting-organization",
    "overview-download-skipped-duplicate",
    "overview-download-completed",
    "overview-download-stale",
    "overview-attention-other",
    "overview-attention-failed",
    "overview-attention-review",
    "overview-pending-tmdb",
    "overview-library-seasons",
    "overview-metadata-total",
    "overview-sources-enabled",
    "overview-downloaders-offline",
    "overview-runtime-memory",
    "overview-runtime-cpu",
    "overview-data-path-size",
  ]) {
    assert.equal(document.querySelector(`#${id}`)?.tagName, "BUTTON");
  }
  assert.match(app, /openMetadataAttentionFromOverview/);
  assert.match(app, /openDownloadSummaryFromOverview/);
  assert.match(app, /openAllMetadataFromOverview/);
  assert.match(app, /selectWorkspace\("tasks", "metadata"\)/);
  assert.match(app, /selectWorkspace\("tasks", "downloads"\)/);
  assert.match(app, /selectWorkspace\("library", "pending"\)/);
  assert.match(app, /selectWorkspace\("library", "seasons"\)/);
  assert.match(app, /selectWorkspace\("sources", "manage"\)/);
  assert.match(app, /selectWorkspace\("download-tools", "qbittorrent"\)/);
  assert.match(app, /resources\.working_set_bytes/);
  assert.match(app, /resources\.cpu_percent/);
  assert.match(app, /resources\.data_path_bytes/);
  assert.match(app, /selectWorkspace\("tasks", "runtime"\)/);
  assert.match(app, /selectWorkspace\("connections", "paths"\)/);
  assert.match(app, /loadOverviewStatistics/);
  assert.match(app, /Promise\.allSettled/);
  assert.match(css, /\.overview-statistics-grid\s*\{[^}]*grid-template-columns:\s*repeat\(4/s);
  assert.match(css, /\.overview-resource-grid\s*\{[^}]*grid-template-columns:\s*repeat\(3/s);
  assert.match(css, /\.overview-resource-card strong\s*\{[^}]*grid-column:\s*auto[^}]*white-space:\s*nowrap/s);
  assert.match(css, /@media \(max-width: 760px\)[\s\S]*\.overview-statistics-grid\s*\{[^}]*grid-template-columns:\s*1fr/s);
  assert.match(css, /@media \(max-width: 760px\)[\s\S]*\.overview-resource-grid\s*\{[^}]*grid-template-columns:\s*1fr/s);
});

test("download summary cards provide direct, accessible list filters", async () => {
  const [app, css] = await Promise.all([
    readFile(appPath, "utf8"),
    readFile(cssPath, "utf8"),
  ]);
  for (const bucket of [
    "active",
    "paused",
    "failed",
    "waiting_organization",
    "skipped_duplicate",
    "completed",
    "stale",
  ]) {
    assert.match(app, new RegExp(`bucket: "${bucket}"`));
  }
  assert.match(app, /query\.set\("summary_bucket", downloadState\.summary_bucket\)/);
  assert.match(app, /card\.setAttribute\("aria-pressed"/);
  assert.match(app, /快捷筛选/);
  assert.match(css, /\.download-summary-card\.filterable\.selected\s*\{/);
});

test("downloads expose verified TMDB metadata and link to persistent matching logs", async () => {
  const [document, app, css] = await Promise.all([
    page(),
    readFile(appPath, "utf8"),
    readFile(cssPath, "utf8"),
  ]);
  assert.equal(
    document.querySelector('[data-workspace="tasks"][data-subview="matching"]')
      ?.getAttribute("data-nav-label"),
    "匹配日志",
  );
  assert.ok(document.querySelector("#matching-log-filters"));
  assert.ok(document.querySelector("#matching-log-list"));
  assert.match(app, /item\.tmdb_metadata\.length === 0/);
  assert.match(app, /已确认 TMDB 元数据/);
  assert.match(app, /openMatchingLogTask\(item\.task_id\)/);
  assert.match(app, /Series、Season、Episode 匹配流程/);
  assert.match(app, /loadMetadataDetail\(item\.task_id/);
  assert.match(app, /loadMetadataAttempts\(item\.task_id/);
  assert.match(css, /\.download-tmdb-metadata\s*\{/);
  assert.match(css, /\.matching-log-flow\s*\{[^}]*grid-template-columns:\s*repeat\(3/s);
});

test("matching attempt timeline exposes concrete task filenames", async () => {
  const [app, css] = await Promise.all([
    readFile(appPath, "utf8"),
    readFile(cssPath, "utf8"),
  ]);
  assert.match(app, /taskFiles\s*=\s*detail\.files/);
  assert.match(app, /file\.episode_attempt_id === attempt\.attempt_id/);
  assert.match(app, /关联文件（\$\{fileNames\.length\}）：\$\{filePreview\}/);
  assert.match(app, /本次任务文件（\$\{fileNames\.length\}，当前阶段尚未绑定单文件）：\$\{filePreview\}/);
  assert.match(css, /\.metadata-attempt-file-list\s*\{/);
  assert.match(css, /\.metadata-attempt-file\s*\{/);
});

test("anime library exposes auditable task and file deletion without merging projection deletion", async () => {
  const [document, app, css] = await Promise.all([
    page(),
    readFile(appPath, "utf8"),
    readFile(cssPath, "utf8"),
  ]);
  const contentDelete = document.querySelector("#library-detail-delete-content");
  const projectionDelete = document.querySelector("#library-detail-delete");
  assert.ok(contentDelete);
  assert.ok(projectionDelete);
  assert.match(contentDelete.textContent, /删除任务\/文件/);
  assert.match(projectionDelete.textContent, /仅删除无引用投影/);
  assert.match(app, /openLibraryContentDeletion/);
  assert.match(app, /library-related-task-delete-group/);
  assert.match(app, /dataset\.libraryDeleteTask/);
  assert.match(app, /openDeletePreview\(task\.task_id\)/);
  assert.match(css, /\.library-audit-actions\s*\{/);
});

test("anime library card titles stay aligned and expose their complete name", async () => {
  const [app, css] = await Promise.all([
    readFile(appPath, "utf8"),
    readFile(cssPath, "utf8"),
  ]);
  assert.match(app, /title\.className = "library-card-title"/);
  assert.match(app, /title\.title = item\.display_name/);
  assert.match(css, /\.library-card-title\s*\{[^}]*min-block-size:\s*2\.7em[^}]*overflow:\s*hidden[^}]*-webkit-line-clamp:\s*2/s);
  assert.match(css, /\.library-poster\s*\{[^}]*height:\s*auto[^}]*aspect-ratio:\s*2\s*\/\s*3/s);
  assert.match(css, /@media \(max-width: 760px\)[\s\S]*\.library-list\s*\{[^}]*grid-template-columns:\s*1fr/s);
});

test("anime library can sort by the latest episode change", async () => {
  const [document, app] = await Promise.all([
    page(),
    readFile(appPath, "utf8"),
  ]);
  const option = document.querySelector(
    '#library-sort option[value="episode_changed_at"]',
  );
  assert.ok(option);
  assert.match(option.textContent, /最后 EP 变动时间/);
  assert.match(app, /episode_changed_at: "最后 EP 变动时间"/);
  assert.match(app, /last_episode_changed_at_utc/);
});

test("anime library exposes an accessible Mikan season completion picker", async () => {
  const [document, app, css] = await Promise.all([
    page(),
    readFile(appPath, "utf8"),
    readFile(cssPath, "utf8"),
  ]);
  const open = document.querySelector("#library-detail-mikan-completion");
  const dialog = document.querySelector("#mikan-season-completion-dialog");
  assert.ok(open);
  assert.ok(dialog);
  assert.equal(dialog.getAttribute("aria-labelledby"), "mikan-season-completion-title");
  assert.ok(dialog.querySelector("#mikan-season-completion-binding"));
  assert.ok(dialog.querySelector("#mikan-season-completion-groups"));
  assert.ok(dialog.querySelector("#mikan-season-completion-preview"));
  assert.ok(dialog.querySelector("#mikan-season-completion-items"));
  assert.match(dialog.textContent, /历史中已有的 groupid 默认勾选/);
  assert.match(dialog.textContent, /超过 12/);
  assert.match(app, /discoverMikanSeasonCompletionGroups/);
  assert.match(app, /previewMikanSeasonCompletion/);
  assert.match(app, /selected\.length > 12/);
  assert.match(app, /expected_resource_revision: preview\.resource_revision/);
  assert.match(css, /\.mikan-season-completion-table-wrap\s*\{/);
  assert.match(css, /#mikan-season-completion-groups\s*\{/);
});

test("anime library links a confirmed season to its canonical TMDB page", async () => {
  const [document, app] = await Promise.all([
    page(),
    readFile(appPath, "utf8"),
  ]);
  const link = document.querySelector("#library-detail-tmdb-link");
  assert.ok(link);
  assert.equal(link.getAttribute("target"), "_blank");
  assert.equal(link.getAttribute("rel"), "noopener noreferrer");
  assert.match(
    app,
    /https:\/\/www\.themoviedb\.org\/tv\/\$\{detail\.tmdb_series_id\}\/season\/\$\{detail\.tmdb_season_number\}/,
  );
  assert.match(app, /tmdbLink\.setAttribute\(\s*"aria-label"/);
});

test("subtitle archive upload safely transports Unicode names and displays parsed episodes", async () => {
  const app = await readFile(appPath, "utf8");
  assert.match(app, /"Content-Type": "application\/octet-stream"/);
  assert.match(app, /"X-AnimeGo-Archive-Name-Encoded": encodeURIComponent\(file\.name\)/);
  assert.match(app, /candidate\.parsed_episode !== null/);
  assert.match(app, /`读取 EP \$\{candidate\.parsed_episode\}`/);
  assert.doesNotMatch(app, /"X-AnimeGo-Archive-Name": file\.name/);
});

test("movie library remains distinct from TV seasons and exposes TMDB Movie identity", async () => {
  const [document, app] = await Promise.all([page(), readFile(appPath, "utf8")]);
  assert.equal(
    document.querySelector('[data-workspace="library"][data-subview="movies"]')
      ?.getAttribute("data-nav-label"),
    "动画电影",
  );
  assert.ok(document.querySelector("#movie-library-list"));
  assert.match(app, /\/api\/v1\/library\/movies/);
  assert.match(app, /themoviedb\.org\/movie\/\$\{item\.tmdb_movie_id\}/);
  assert.match(app, /TMDB Movie \$\{item\.tmdb_movie_id\}/);
  assert.match(app, /元数据已确认 · 等待整理完成/);
});

test("manual ingest uses the WebUI-authenticated route instead of the plugin boundary", async () => {
  const [document, app] = await Promise.all([page(), readFile(appPath, "utf8")]);
  assert.match(app, /authenticatedFetch\("\/api\/v1\/ingest\/manual"/);
  assert.deepEqual(
    [...document.querySelectorAll("#manual-download-media-type option")]
      .map(option => option.getAttribute("value")),
    ["tv", "movie"],
  );
  assert.deepEqual(
    [...document.querySelectorAll("#manual-rss-media-type option")]
      .map(option => option.getAttribute("value")),
    ["tv", "movie"],
  );
  assert.match(app, /media_type:\s*element\("#manual-download-media-type"\)\.value/);
  assert.match(app, /media_type:\s*element\("#manual-rss-media-type"\)\.value/);
});

test("task deletion waits for a durable execution result and reports item states", async () => {
  const [document, app] = await Promise.all([
    page(),
    readFile(appPath, "utf8"),
  ]);
  assert.match(document.querySelector("#delete-confirm")?.textContent ?? "", /确认删除并等待结果/);
  assert.match(app, /delete\/tasks\/.*\/execute/);
  assert.match(app, /正在删除，请等待/);
  assert.match(app, /删除完成/);
  assert.match(app, /重试并等待结果/);
  assert.match(app, /已接管已有执行/);
  assert.match(app, /body\.items\.filter\(item => item\.state === "failed"\)/);
});

test("Bangumi archive hit details are collapsed by default and use a compact table", async () => {
  const [document, app, css] = await Promise.all([
    page(),
    readFile(appPath, "utf8"),
    readFile(cssPath, "utf8"),
  ]);
  const details = document.querySelector("#data-update-usage-detail");
  assert.ok(details);
  assert.equal(details.tagName, "DETAILS");
  assert.equal(details.hasAttribute("open"), false);
  assert.ok(details.querySelector("summary.data-update-usage-heading"));
  const table = details.querySelector("table.data-update-usage-table");
  assert.ok(table);
  assert.deepEqual(
    [...table.querySelectorAll("thead th")].map(cell => cell.textContent?.trim()),
    ["命中类型", "bgmid", "返回数", "数据版本", "命中时间", "序号"],
  );
  assert.equal(table.querySelector("tbody")?.id, "data-update-usage-list");
  assert.match(app, /document\.createElement\("tr"\)/);
  assert.match(app, /data-update-usage-kind/);
  assert.match(css, /\.data-update-usage-table-wrap\s*\{[^}]*max-height:\s*31rem/s);
  assert.match(css, /\.data-update-usage-table\s*\{[^}]*border-collapse:\s*collapse/s);
  assert.match(css, /\.data-update-usage-detail\[open\]/);
});

test("cache workspace groups Bangumi, AniDB and other caches", async () => {
  const [document, app] = await Promise.all([page(), readFile(appPath, "utf8")]);
  assert.equal(
    document.querySelector('[data-workspace-target="system"]')?.textContent?.trim(),
    "◉缓存",
  );
  assert.equal(document.querySelector('[data-workspace-target="bangumi-cache"]'), null);
  assert.ok(document.querySelector('[data-workspace="system"][data-subview="bangumi"]'));
  assert.ok(document.querySelector('[data-workspace="system"][data-subview="anidb"]'));
  assert.ok(document.querySelector('[data-workspace="system"][data-subview="other"]'));
  assert.match(app, /title: "缓存"/);
  assert.match(app, /\{ id: "bangumi", label: "Bangumi缓存" \}/);
  assert.match(app, /\{ id: "anidb", label: "AniDB缓存" \}/);
  assert.match(app, /\{ id: "other", label: "其他缓存管理" \}/);
  assert.match(app, /\/api\/v1\/cache\/anidb\/refresh/);
});

test("Other readaptation review confirms a server-provided before and after comparison", async () => {
  const [document, app, css] = await Promise.all([
    page(),
    readFile(appPath, "utf8"),
    readFile(cssPath, "utf8"),
  ]);
  const dialog = document.querySelector("#other-readaptation-review-dialog");
  assert.ok(dialog);
  assert.equal(dialog.getAttribute("aria-labelledby"), "other-readaptation-review-title");
  assert.ok(dialog.querySelector("#other-readaptation-review-confirm"));
  assert.ok(dialog.querySelector("#other-readaptation-review-cancel"));
  const scrollbar = dialog.querySelector("#other-readaptation-review-scrollbar");
  assert.ok(scrollbar);
  assert.equal(scrollbar.getAttribute("role"), "scrollbar");
  assert.equal(scrollbar.getAttribute("aria-controls"), "other-readaptation-review-files");
  assert.match(app, /other-readaptation\/review/);
  assert.match(app, /other-attention\/ignore/);
  assert.match(app, /"忽略处理"/);
  assert.match(app, /mixed-media-postprocess\/preview/);
  assert.match(app, /tmdb\/movies\/search/);
  assert.match(app, /"TV\+Movie 后处理"/);
  const mixedDialog = document.querySelector("#mixed-media-postprocess-dialog");
  assert.ok(mixedDialog);
  assert.equal(mixedDialog.getAttribute("aria-labelledby"), "mixed-media-postprocess-title");
  assert.ok(mixedDialog.querySelector("#mixed-media-postprocess-confirm"));
  assert.ok(mixedDialog.querySelector("#mixed-media-postprocess-edit"));
  assert.ok(mixedDialog.querySelector("#mixed-media-postprocess-review"));
  assert.ok(mixedDialog.querySelector("#mixed-media-anitomy-parse"));
  assert.ok(mixedDialog.querySelector("#mixed-media-anitomy-preview[aria-live='polite']"));
  assert.match(mixedDialog.textContent, /必须且只能指定一个 Movie 正片/);
  assert.match(mixedDialog.textContent, /最大文件会预选为正片/);
  assert.match(mixedDialog.textContent, /Movie Extras/);
  assert.match(app, /select\.name = "mixed-media-file-role"/);
  assert.match(app, /file\.size_bytes > current\.size_bytes/);
  assert.match(app, /\/api\/v1\/metadata\/anitomy\/parse-title/);
  assert.match(app, /document\.createElement\("mark"\)/);
  assert.match(app, /className = "mixed-media-keyword-highlight"/);
  assert.match(app, /sourceName\.matchAll\(pattern\)/);
  assert.match(app, /replace\(\/劇場版\|剧场版\/gu, " "\)/);
  assert.match(app, /movie\(\?=\$\|\[\^a-z0-9\]\)\/giu/);
  assert.match(mixedDialog.textContent, /自动填入搜索框时会移除/);
  assert.match(app, /movie_task_file_id: assignments\.movieTaskFileId/);
  assert.match(app, /movie_extra_task_file_ids: assignments\.movieExtraTaskFileIds/);
  assert.doesNotMatch(app, /mixed-media-file-role"] option:checked/);
  assert.match(app, /已选择 \$\{movie\.title\} · TMDB \$\{movie\.tmdb_movie_id\}/);
  assert.match(app, /canInspectReadonlyPlan = activeMixedMediaPreview\?\.mode === "readonly"/);
  assert.match(app, /方案已锁定（仅查看）/);
  assert.match(app, /整理已开始，只允许检查方案/);
  assert.match(app, /if \(preview\.mode === "readonly"\)\s+return/);
  assert.match(css, /#mixed-media-postprocess-review\[hidden\][^{]*\{[^}]*display:\s*none\s*!important/);
  assert.match(css, /#mixed-media-postprocess-edit\[hidden\][^{]*\{[^}]*display:\s*none\s*!important/);
  assert.match(app, /"edit_pending"/);
  assert.match(app, /readaptation-review-table/);
  assert.match(app, /\["信息项", "适配前", "适配后"\]/);
  assert.match(app, /"TMDB Series"/);
  assert.match(app, /"媒体位置"/);
  assert.match(app, /"Episode 取得"/);
  assert.match(app, /"Other 处理"/);
  assert.match(app, /人工修正 TMDB 归属/);
  assert.match(app, /TMDB Series ID/);
  assert.match(app, /验证并重新整理/);
  assert.match(app, /manual-override/);
  assert.match(app, /目标 Episode 已完成或被占用；保留当前 Other，不自动删除/);
  assert.match(app, /完成后状态/);
  assert.match(app, /重新适配审核完成/);
  assert.match(app, /查看审核结果/);
  assert.match(app, /复制整理并保留共享源文件/);
  assert.match(app, /otherReadaptationReviewDialog\.showModal\(\)/);
  assert.match(css, /\.readaptation-review-table\s*\{/);
  assert.match(css, /\.readaptation-manual-fields\s*\{/);
  assert.match(css, /#other-readaptation-review-dialog\s*\{[^}]*height:\s*min\(/s);
  assert.match(css, /\.readaptation-review-panel\s*\{[^}]*grid-template-rows:\s*auto auto minmax\(0,1fr\) auto auto/s);
  assert.match(css, /\.readaptation-review-panel\s*\{[^}]*overflow:\s*hidden/s);
  assert.match(css, /\.readaptation-review-panel\s*>\s*\.delete-heading\s*\{[^}]*position:\s*sticky/s);
  assert.match(css, /\.readaptation-review-files-shell\s*\{[^}]*position:\s*relative/s);
  assert.match(css, /\.readaptation-review-files\s*\{[^}]*overflow-y:\s*scroll/s);
  assert.match(css, /\.readaptation-review-files\s*\{[^}]*scrollbar-width:\s*none/s);
  assert.match(css, /\.readaptation-review-files\s*\{[^}]*display:\s*flex/s);
  assert.match(css, /\.readaptation-review-file\s*\{[^}]*flex:\s*0 0 auto/s);
  assert.match(css, /\.readaptation-review-actions\s*\{[^}]*position:\s*sticky/s);
  assert.match(css, /\.readaptation-review-scrollbar\s*\{[^}]*position:\s*absolute/s);
  assert.match(css, /\.readaptation-review-scrollbar-thumb\s*\{[^}]*min-height:\s*58px/s);
  assert.match(app, /updateOtherReadaptationReviewScrollbar/);
  assert.match(app, /otherReadaptationReviewScrollbar\.addEventListener\("keydown"/);
});

test("AI test page exposes verified Responses compatibility controls and usage", async () => {
  const [document, app, css] = await Promise.all([
    page(),
    readFile(appPath, "utf8"),
    readFile(cssPath, "utf8"),
  ]);
  const reasoning = document.querySelector("#ai-test-reasoning-effort");
  const webSearch = document.querySelector("#ai-test-web-search");
  assert.ok(reasoning);
  assert.ok(webSearch);
  const apiMode = document.querySelector("#ai-test-api-mode");
  assert.ok(apiMode);
  assert.deepEqual(
    [...reasoning.querySelectorAll("option")].map(option => option.value),
    ["medium", "low", "high", "none"],
  );
  assert.equal(webSearch.getAttribute("type"), "checkbox");
  assert.equal(webSearch.hasAttribute("checked"), true);
  assert.equal(apiMode.value, "responses");
  assert.match(app, /reasoning_effort:/);
  assert.match(app, /web_search_enabled:/);
  assert.match(app, /reasoning_tokens/);
  assert.match(app, /function aiTesterPayloadDisclosure/);
  assert.match(app, /document\.createElement\("details"\)/);
  assert.match(app, /完整请求 Content/);
  assert.match(css, /\.ai-test-audit-summary\s*\{[^}]*cursor:\s*pointer/s);
  assert.match(css, /\.ai-test-payload-disclosure\s*\{/);
  assert.match(css, /\.ai-test-payload-disclosure\s*>\s*summary\s*\{[^}]*cursor:\s*pointer/s);
  assert.match(app, /ai-test-source-state \$\{tmdbEnabled \? "enabled" : "disabled"\}/);
  assert.match(app, /ai-test-source-state \$\{mikan \|\| bgmEnabled \? "enabled" : "disabled"\}/);
  assert.match(app, /ai-test-source-state \$\{anidbEnabled \? "enabled" : "disabled"\}/);
  assert.match(css, /\.badge\.ai-test-source-state\.enabled\s*\{[^}]*color:\s*#8ff0c4/s);
  assert.match(css, /\.badge\.ai-test-source-state\.disabled\s*\{[^}]*color:\s*#aebbd3/s);
});

test("AI matching workspace exposes metadata and subtitle matching entries", async () => {
  const [document, app] = await Promise.all([
    page(),
    readFile(appPath, "utf8"),
  ]);
  assert.match(document.querySelector('[data-workspace-target="tools"]')?.textContent ?? "", /AI 匹配测试/);
  assert.equal(
    document.querySelector('[data-workspace="tools"][data-subview="ai-subtitle"]')
      ?.getAttribute("data-nav-label"),
    "AI 字幕匹配",
  );
  assert.ok(document.querySelector("#ai-subtitle-open-library"));
  assert.match(app, /id: "ai-subtitle", label: "AI 字幕匹配"/);
  assert.match(app, /selectWorkspace\("library", "seasons"\)/);
});

test("subtitle matching previews every original and renamed file name", async () => {
  const [document, app, css] = await Promise.all([
    page(),
    readFile(appPath, "utf8"),
    readFile(cssPath, "utf8"),
  ]);

  assert.ok(document.querySelector("#library-subtitle-file-preview"));
  assert.match(
    document.querySelector("#library-subtitle-file-preview-name")?.textContent ?? "",
    /原文件名和重命名文件名/,
  );
  assert.match(app, /`E\$\{String\(value\)\.padStart\(3, "0"\)\}\$\{subtitleRenameSuffix\(candidate\.file_name\)\}`/);
  assert.match(app, /`Extras\/\$\{candidate\.file_name\}`/);
  assert.match(app, /renamePreview\.textContent = `重命名：\$\{target\}`/);
  assert.match(app, /enabled\.addEventListener\("change", updateRenamePreview\)/);
  assert.match(app, /episode\.addEventListener\("input", updateRenamePreview\)/);
  assert.match(app, /input\?\.dispatchEvent\(new Event\("input"\)\)/);
  assert.match(css, /\.library-subtitle-file-preview strong\s*\{[^}]*overflow-wrap:\s*anywhere/s);
  assert.match(css, /\.library-subtitle-rename-preview\s*\{/);
});

test("AI test request omits Mikan pubDate when Bangumi date priority is disabled", async () => {
  const app = await readFile(appPath, "utf8");

  assert.match(app, /const useBangumiPubDateFirst = .*#ai-test-use-bgm-pubdate.*\.checked;/);
  assert.match(app, /\.\.\.\(useBangumiPubDateFirst\s*\? \{ mikan_pub_date:/);
  assert.match(app, /use_bangumi_pubdate_first: useBangumiPubDateFirst/);
});
