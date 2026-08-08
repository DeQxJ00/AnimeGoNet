import { expect, test } from "@playwright/test";
import { createHash } from "node:crypto";
import { readFile } from "node:fs/promises";

const manifest = JSON.parse(await readFile(
  new URL("./fixtures/animegohelper.upstream.json", import.meta.url),
  "utf8"));
const scriptPath = process.env.ANIMEGOHELPER_SCRIPT_PATH;
if (!scriptPath) {
  throw new Error("Missing ANIMEGOHELPER_SCRIPT_PATH. Use the pinned upstream checkout.");
}
const userscript = await readFile(scriptPath, "utf8");
const userscriptHash = createHash("sha256").update(userscript, "utf8").digest("hex");
if (userscriptHash !== manifest.sha256) {
  throw new Error(`AnimeGoHelper SHA-256 mismatch: ${userscriptHash}`);
}

const apiBase = "http://127.0.0.1:7991/api";
const accessKey = "animegohelper-browser-fixture";
const accessKeyHash = createHash("sha256").update(accessKey, "utf8").digest("hex");
const legacyConfiguration = {
  Filiter0: {
    "fixture-global": {
      is_enable_whitelist: true,
      whitelist: ["1080p", "简体"],
      is_enable_blacklist: true,
      blacklist: ["720p"],
    },
  },
  Filiter1: {},
  Filiter2: {},
  Filiter3: {},
  Filiter4: {},
};

const mikanPage = `<!doctype html>
<html lang="zh-CN"><head><meta charset="utf-8"><title>Mikan fixture</title></head>
<body>
  <div class="w-other-c text-right"></div>
  <main id="sk-container">
    <h1 class="bangumi-title">Fixture Anime</h1>
    <section class="episode-row">
      <div class="episode-links">
        <a href="https://mikanani.me/Download/fixture-03.torrent">Torrent</a>
        <a class="magnet-link-wrap" href="https://mikanani.me/Home/Episode/fixture-03">Fixture Anime [03] [1080p]</a>
      </div>
    </section>
    <span class="fixture-group">Fixture Group</span>
    <a class="mikan-rss" href="https://mikanani.me/RSS/Bangumi?bangumiId=3951&subgroupid=370">RSS</a>
  </main>
</body></html>`;

function collectBrowserErrors(page) {
  const errors = [];
  page.on("console", message => {
    if (message.type() === "error") errors.push(`console: ${message.text()}`);
  });
  page.on("pageerror", error => errors.push(`pageerror: ${error.message}`));
  return errors;
}

async function loadOriginalUserscript(page, dispatch, initialConfiguration = legacyConfiguration) {
  await page.exposeFunction("__animegohelperDispatch", dispatch);
  await page.addInitScript(({ apiBaseValue, tokenValue, configuration }) => {
    const values = new Map([
      ["apipath", apiBaseValue],
      ["token", tokenValue],
      ["myFiliters", JSON.stringify(configuration)],
    ]);
    window.unsafeWindow = window;
    window.AdvancedSubscriptionEnabled = false;
    window.GM_getValue = key => values.get(key);
    window.GM_setValue = (key, value) => {
      values.set(key, value);
      return true;
    };
    window.GM_deleteValue = key => values.delete(key);
    window.GM_getResourceText = () => "";
    window.GM_addStyle = () => {};
    window.GM_notification = () => {};
    window.__gmFixtureGet = key => values.get(key) ?? null;

    const encodeUtf8 = value => {
      const bytes = new TextEncoder().encode(value);
      let binary = "";
      for (const byte of bytes) binary += String.fromCharCode(byte);
      return btoa(binary);
    };
    const decodeUtf8 = value => {
      const binary = atob(value);
      const bytes = Uint8Array.from(binary, character => character.charCodeAt(0));
      return new TextDecoder().decode(bytes);
    };
    window.Base64 = { encode: encodeUtf8, decode: decodeUtf8 };
    window.Ladda = {
      create: () => ({ start: () => {}, stop: () => {} }),
    };
    window.Tagify = class {
      constructor() {
        this.values = [];
      }
      removeAllTags() {
        this.values = [];
      }
      addTags(tags) {
        for (const tag of tags ?? []) {
          this.values.push(typeof tag === "string" ? tag : tag.value);
        }
      }
      getCleanValue() {
        return this.values.map(value => ({ value }));
      }
    };
    window.GM_xmlhttpRequest = options => {
      Promise.resolve(window.__animegohelperDispatch({
        method: options.method ?? "GET",
        url: options.url,
        data: options.data ?? null,
        headers: options.headers ?? {},
      })).then(result => {
        const response = {
          status: result.status,
          responseText: result.responseText ?? "",
        };
        options.onload?.(response);
        options.onloadend?.(response);
      }).catch(error => {
        const response = { status: 0, responseText: String(error) };
        options.onerror?.(response);
        options.onloadend?.(response);
      });
    };
  }, {
    apiBaseValue: apiBase,
    tokenValue: accessKey,
    configuration: initialConfiguration,
  });
  await page.route("https://mikanani.me/Home/Bangumi/3951", route => route.fulfill({
    status: 200,
    contentType: "text/html; charset=utf-8",
    body: mikanPage,
  }));
  await page.goto("https://mikanani.me/Home/Bangumi/3951", { waitUntil: "domcontentloaded" });
  await page.addScriptTag({ content: userscript });
  await expect(page.getByText("单", { exact: true })).toBeVisible();
  await expect(page.getByText("全", { exact: true })).toBeVisible();
}

test("unmodified helper submits single and full Mikan flows with legacy authentication", async ({ page }) => {
  const browserErrors = collectBrowserErrors(page);
  const requests = [];
  await loadOriginalUserscript(page, request => {
    requests.push(request);
    const url = new URL(request.url);
    if (url.pathname === "/Home/Episode/fixture-03") {
      return {
        status: 200,
        responseText: '<a class="mikan-rss" href="https://mikanani.me/RSS/Bangumi?bangumiId=3951&subgroupid=370">RSS</a>',
      };
    }
    return {
      status: 200,
      responseText: JSON.stringify({ code: 200, msg: "fixture accepted", data: {} }),
    };
  });

  await page.getByText("单", { exact: true }).click();
  await expect.poll(() => requests.filter(request => request.url.endsWith("/download/manager")).length)
    .toBe(1);
  await page.getByText("全", { exact: true }).click();
  await expect.poll(() => requests.filter(request => request.url.endsWith("/rss")).length)
    .toBe(1);

  const episodeRequest = requests.find(request => request.url.includes("/Home/Episode/fixture-03"));
  expect(episodeRequest).toBeTruthy();
  const singleRequest = requests.find(request => request.url.endsWith("/download/manager"));
  expect(singleRequest.method).toBe("POST");
  expect(singleRequest.headers["Access-Key"]).toBe(accessKeyHash);
  expect(JSON.parse(singleRequest.data)).toEqual({
    source: "mikan",
    data: [{
      torrent: "https://mikanani.me/Download/fixture-03.torrent",
      info: {
        name: "Fixture Anime [03] [1080p]",
        url: "https://mikanani.me/Home/Episode/fixture-03",
      },
    }],
  });

  const fullRequest = requests.find(request => request.url.endsWith("/rss"));
  expect(fullRequest.method).toBe("POST");
  expect(fullRequest.headers["Access-Key"]).toBe(accessKeyHash);
  expect(JSON.parse(fullRequest.data)).toEqual({
    source: "mikan",
    rss: { url: "https://mikanani.me/RSS/Bangumi?bangumiid=3951&subgroupid=370" },
    is_select_ep: false,
    ep_links: [""],
  });
  expect(browserErrors).toEqual([]);
});

test("unmodified helper uploads and downloads Filiter0-4 without changing JSON", async ({ page }) => {
  const browserErrors = collectBrowserErrors(page);
  const requests = [];
  let storedBase64 = null;
  await loadOriginalUserscript(page, request => {
    requests.push(request);
    const url = new URL(request.url);
    if (url.pathname === "/api/plugin/config" && request.method === "POST") {
      const body = JSON.parse(request.data);
      storedBase64 = body.data;
      return {
        status: 200,
        responseText: JSON.stringify({ code: 200, msg: "uploaded", data: {} }),
      };
    }
    if (url.pathname === "/api/plugin/config" && request.method === "GET") {
      return {
        status: 200,
        responseText: JSON.stringify({
          code: 200,
          msg: "downloaded",
          data: { name: "filter/mikan_tool.py", data: storedBase64 },
        }),
      };
    }
    throw new Error(`Unexpected GM request: ${request.method} ${request.url}`);
  });

  await page.locator(".w-other-c a", { hasText: "AnimeGo设置" }).click();
  await page.getByRole("button", { name: "上传过滤配置" }).click();
  await expect.poll(() => storedBase64).not.toBeNull();
  const uploaded = JSON.parse(Buffer.from(storedBase64, "base64").toString("utf8"));
  expect(uploaded).toEqual(legacyConfiguration);

  await page.getByRole("button", { name: "清除过滤配置" }).click();
  await expect.poll(() => page.evaluate(() => window.__gmFixtureGet("myFiliters"))).toBeNull();
  await page.getByRole("button", { name: "获取过滤配置" }).click();
  await expect.poll(() => page.evaluate(() => window.__gmFixtureGet("myFiliters")))
    .toBe(JSON.stringify(legacyConfiguration));

  const upload = requests.find(request => request.method === "POST");
  const download = requests.find(request => request.method === "GET");
  expect(JSON.parse(upload.data).name).toBe("filter/mikan_tool.py");
  expect(upload.headers["Access-Key"]).toBe(accessKeyHash);
  expect(new URL(download.url).searchParams.get("name")).toBe("filter/mikan_tool.py");
  expect(download.headers["Access-Key"]).toBe(accessKeyHash);
  expect(browserErrors).toEqual([]);
});
