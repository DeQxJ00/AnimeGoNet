import { expect, test } from "@playwright/test";
import { createHash } from "node:crypto";

const baseUrl = required("ANIMEGONET_WEBUI_BASE_URL").replace(/\/$/, "");
const accessKey = required("ANIMEGONET_WEBUI_ACCESS_KEY");
const taskId = required("ANIMEGONET_FULL_CHAIN_TASK_ID");
const title = required("ANIMEGONET_FULL_CHAIN_TITLE");
const tmdbSeriesId = Number(required("ANIMEGONET_FULL_CHAIN_TMDB_SERIES_ID"));
const legacyAccessKeyHash = createHash("sha256").update(accessKey, "utf8").digest("hex");

function required(name) {
  const value = process.env[name];
  if (!value) throw new Error(`Missing ${name}. Run the full-chain container smoke launcher.`);
  return value;
}

function authenticatedUrl(path = "/") {
  const url = new URL(path, `${baseUrl}/`);
  url.searchParams.set("access_key", legacyAccessKeyHash);
  const value = url.toString();
  expect(value).not.toContain(accessKey);
  return value;
}

function collectBrowserErrors(page) {
  const errors = [];
  page.on("console", message => {
    if (message.type() === "error") errors.push(`console: ${message.text()}`);
  });
  page.on("pageerror", error => errors.push(`pageerror: ${error.message}`));
  return errors;
}

async function apiJson(request, path) {
  const response = await request.get(`${baseUrl}${path}`, {
    headers: { "X-AnimeGo-Access-Key": accessKey },
  });
  expect(response.ok(), `${path}: ${response.status()}`).toBeTruthy();
  return response.json();
}

test("full-chain result is visible through API and the static WebUI", async ({ page }) => {
  const browserErrors = collectBrowserErrors(page);
  const downloads = await apiJson(
    page.request,
    `/api/v1/downloads?page=1&page_size=100&search=${encodeURIComponent(title)}`,
  );
  const download = downloads.items.find(item => item.task_id === taskId);
  expect(download).toMatchObject({
    task_id: taskId,
    title: `${title} S01E01`,
    source: "container-e2e-ci",
    downloader_id: "bt",
    business_status: "organized",
    progress: 1,
    total_bytes: 131072,
  });

  const metadata = await apiJson(
    page.request,
    `/api/v1/metadata/tasks/${encodeURIComponent(taskId)}`,
  );
  expect(metadata.summary).toMatchObject({
    task_id: taskId,
    status: "organized",
    tmdb_series_id: tmdbSeriesId,
    tmdb_season_number: 1,
    series_strategy: "tmdb_title",
    season_strategy: "tmdb_air_date",
    episode_strategy: "tmdb_episode_number",
    episode_file_count: 1,
  });
  expect(metadata.files).toHaveLength(1);
  expect(metadata.files[0]).toMatchObject({
    disposition: "episode",
    tmdb_series_id: tmdbSeriesId,
    tmdb_season_number: 1,
    tmdb_episode_number: 1,
  });

  const library = await apiJson(
    page.request,
    "/api/v1/library/seasons?page=1&page_size=12&sort=last_updated&direction=desc",
  );
  expect(library.items.find(item =>
    item.tmdb_series_id === tmdbSeriesId && item.tmdb_season_number === 1)).toMatchObject({
    display_name: title,
    episode_downloaded: 1,
    episode_total: 1,
  });

  await page.goto(authenticatedUrl(), { waitUntil: "domcontentloaded" });
  await expect(page.locator("#health")).toHaveText("运行中");
  await expect(page.locator("#downloads")).toHaveAttribute("data-ui-state", "ready");
  await expect(page.locator("#downloads")).toContainText(`${title} S01E01`);
  await expect(page.locator("#downloads")).toContainText("已整理入库");
  await expect(page.locator("#metadata-tasks")).toHaveAttribute("data-ui-state", "ready");
  await expect(page.locator("#metadata-tasks")).toContainText(`${title} S01E01`);
  await expect(page.locator("#metadata-tasks")).toContainText(`TMDB ${tmdbSeriesId} / S01`);
  await expect(page.locator("#library-list")).toHaveAttribute("data-ui-state", "ready");
  await expect(page.locator("#library-list")).toContainText(title);
  await expect(page.locator("#library-list")).toContainText("1 / 1 EP");
  expect(browserErrors).toEqual([]);
});
