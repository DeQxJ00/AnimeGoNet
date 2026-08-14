import { expect, test } from "@playwright/test";
import { createHash } from "node:crypto";

const baseUrl = required("ANIMEGONET_WEBUI_BASE_URL").replace(/\/$/, "");
const accessKey = required("ANIMEGONET_WEBUI_ACCESS_KEY");
const webUiAccessKeyHash = createHash("sha256").update(accessKey, "utf8").digest("hex");

function required(name) {
  const value = process.env[name];
  if (!value) throw new Error(`Missing ${name}. Run the WebUI smoke launcher.`);
  return value;
}

function authenticatedUrl(path = "/") {
  const url = new URL(path, `${baseUrl}/`);
  url.searchParams.set("webui_access_key", webUiAccessKeyHash);
  const authenticated = url.toString();
  expect(authenticated).not.toContain(accessKey);
  return authenticated;
}

function collectBrowserErrors(page) {
  const errors = [];
  page.on("console", message => {
    if (message.type() === "error") errors.push(`console: ${message.text()}`);
  });
  page.on("pageerror", error => errors.push(`pageerror: ${error.message}`));
  return errors;
}

test("NativeAOT release renders live dashboard and explicit TMDB fallback order", async ({ page }) => {
  const browserErrors = collectBrowserErrors(page);
  const response = await page.request.get(`${baseUrl}/api/v1/status`, {
    headers: { "X-AnimeGo-WebUI-Access-Key": accessKey },
  });
  expect(response.ok()).toBeTruthy();
  const status = await response.json();
  expect(status.native_aot).toBe(true);
  expect(JSON.stringify(status)).not.toContain(accessKey);

  await page.setViewportSize({ width: 1280, height: 800 });
  await page.goto(authenticatedUrl(), { waitUntil: "domcontentloaded" });
  await expect(page).toHaveTitle("AnimeGoNet");
  await expect(page.locator("#health")).toHaveText("运行中");
  await expect(page.locator("#runtime")).toContainText("NativeAOT");
  await expect(page.locator("#schema")).toHaveText(/^v\d+$/);
  await expect(page.locator("#modules")).toHaveAttribute("data-ui-state", "ready");
  await expect(page.locator("#downloads")).not.toHaveAttribute("data-ui-state", "loading");
  await expect(page.locator("#metadata-tasks")).not.toHaveAttribute("data-ui-state", "loading");
  await expect(page.locator("#live-log-status")).toContainText("已连接");

  await page.locator("#configuration-edit").click();
  await expect(page.locator("#configuration-dialog")).toBeVisible();
  const fallbackSteps = page.locator("#configuration-dialog .failure-priority-step");
  await expect(fallbackSteps).toHaveCount(5);
  expect(await fallbackSteps.evaluateAll(items =>
    items.map(item => item.getAttribute("data-priority"))))
    .toEqual(["4", "3", "independent", "2", "1"]);
  await expect(page.locator("#configuration-fail-backtrace + span small"))
    .toContainText("需要 bgmid");
  await expect(page.locator("#configuration-fail-title + span small"))
    .toContainText("只用本地标题解析器读取任务 title");
  await expect(page.locator("#configuration-fail-first + span small"))
    .toContainText("本地 S01，不验证 TMDB Season");
  await expect(page.locator("#configuration-bangumi-fallback + span small"))
    .toContainText("不输出有效 tmdbid");
  await page.locator("#configuration-close").click();
  await expect(page.locator("#configuration-dialog")).not.toBeVisible();

  expect(browserErrors).toEqual([]);
});

test("mobile viewport has no horizontal overflow and keeps keyboard entry usable", async ({ page }) => {
  const browserErrors = collectBrowserErrors(page);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(authenticatedUrl(), { waitUntil: "domcontentloaded" });
  await expect(page.locator("#health")).toHaveText("运行中");
  await expect(page.locator("#modules")).toHaveAttribute("data-ui-state", "ready");
  expect(await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    viewportWidth: window.innerWidth,
  }))).toEqual({ scrollWidth: 390, viewportWidth: 390 });

  const skipLink = page.getByRole("link", { name: "跳到主要内容" });
  await page.keyboard.press("Tab");
  await expect(skipLink).toBeFocused();
  await page.keyboard.press("Enter");
  await expect(page.locator("#main-content")).toBeFocused();
  await page.locator("#configuration-edit").click();
  await expect(page.locator("#configuration-dialog")).toBeVisible();
  const dialogBounds = await page.locator("#configuration-dialog").evaluate(dialog => {
    const bounds = dialog.getBoundingClientRect();
    return {
      left: Math.round(bounds.left),
      right: Math.round(bounds.right),
      viewportWidth: window.innerWidth,
      scrollWidth: dialog.scrollWidth,
      clientWidth: dialog.clientWidth,
    };
  });
  expect(dialogBounds.left).toBeGreaterThanOrEqual(0);
  expect(dialogBounds.right).toBeLessThanOrEqual(dialogBounds.viewportWidth);
  expect(dialogBounds.scrollWidth).toBeLessThanOrEqual(dialogBounds.clientWidth);
  await page.locator("#configuration-close").click();

  expect(browserErrors).toEqual([]);
});
