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

test("AI test page exposes verified Responses compatibility controls and usage", async () => {
  const [document, app] = await Promise.all([
    page(),
    readFile(appPath, "utf8"),
  ]);
  const reasoning = document.querySelector("#ai-test-reasoning-effort");
  const webSearch = document.querySelector("#ai-test-web-search");
  assert.ok(reasoning);
  assert.ok(webSearch);
  assert.deepEqual(
    [...reasoning.querySelectorAll("option")].map(option => option.value),
    ["", "none", "low", "medium", "high"],
  );
  assert.deepEqual(
    [...webSearch.querySelectorAll("option")].map(option => option.value),
    ["", "false", "true"],
  );
  assert.match(app, /reasoning_effort:/);
  assert.match(app, /web_search_enabled:/);
  assert.match(app, /reasoning_tokens/);
});
