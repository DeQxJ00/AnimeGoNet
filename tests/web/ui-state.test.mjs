import assert from "node:assert/strict";
import test from "node:test";
import { parseHTML } from "linkedom";
import {
  renderRegionContent,
  renderRegionMessage,
  setRegionState,
  shouldReplacePolledRegion,
} from "../../src/AnimeGoNet.App/wwwroot/ui-state.js";

function region(markup = '<div id="target"></div>') {
  const { document } = parseHTML(markup);
  return document.querySelector("#target");
}

test("loading state exposes a polite busy region", () => {
  const target = region();

  setRegionState(target, "loading");

  assert.equal(target.dataset.uiState, "loading");
  assert.equal(target.getAttribute("aria-busy"), "true");
  assert.equal(target.getAttribute("aria-live"), "polite");
});

test("empty state renders one atomic status without HTML interpretation", () => {
  const target = region('<div id="target" aria-live="polite"><span>old</span></div>');
  const message = '<img src=x onerror="secret()">没有任务';

  const node = renderRegionMessage(target, "empty", message);

  assert.equal(target.dataset.uiState, "empty");
  assert.equal(target.getAttribute("aria-busy"), "false");
  assert.equal(target.children.length, 1);
  assert.equal(node.getAttribute("role"), "status");
  assert.equal(node.getAttribute("aria-atomic"), "true");
  assert.equal(node.textContent, message);
  assert.equal(node.querySelector("img"), null);
});

test("error state uses an alert and does not remain busy", () => {
  const target = region();

  const node = renderRegionMessage(target, "error", "读取失败");

  assert.equal(target.dataset.uiState, "error");
  assert.equal(target.getAttribute("aria-busy"), "false");
  assert.equal(node.getAttribute("role"), "alert");
  assert.match(node.className, /ui-state--error/);
});

test("ready content replaces status messages and clears busy state", () => {
  const target = region('<div id="target" aria-live="polite"><p>loading</p></div>');
  const card = target.ownerDocument.createElement("article");
  card.textContent = "任务卡片";

  renderRegionContent(target, card);

  assert.equal(target.dataset.uiState, "ready");
  assert.equal(target.getAttribute("aria-busy"), "false");
  assert.equal(target.children.length, 1);
  assert.equal(target.firstElementChild, card);
});

test("foreground refresh always replaces the current region", () => {
  assert.equal(shouldReplacePolledRegion({
    background: false,
    signatureChanged: false,
    hasExpandedContent: true,
    hasFocusedEditor: true,
    hasOpenDialog: true,
  }), true);
});

test("background refresh replaces only changed and idle regions", () => {
  const idle = {
    background: true,
    signatureChanged: true,
    hasExpandedContent: false,
    hasFocusedEditor: false,
    hasOpenDialog: false,
  };

  assert.equal(shouldReplacePolledRegion(idle), true);
  assert.equal(shouldReplacePolledRegion({ ...idle, signatureChanged: false }), false);
  assert.equal(shouldReplacePolledRegion({ ...idle, hasExpandedContent: true }), false);
  assert.equal(shouldReplacePolledRegion({ ...idle, hasFocusedEditor: true }), false);
  assert.equal(shouldReplacePolledRegion({ ...idle, hasOpenDialog: true }), false);
});
