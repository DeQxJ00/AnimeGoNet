import assert from "node:assert/strict";
import test from "node:test";
import {
  filterLiveLogEntries,
  parseLiveLogEntry,
} from "../../src/AnimeGoNet.App/wwwroot/log-view.js";

test("formatted log lines split into safe diagnostic fields", () => {
  const parsed = parseLiveLogEntry(
    "2026-08-09T09:15:21.1234567+00:00 [ERR] AnimeGoNet.App.Metadata (4301): Duplicate skipped | InvalidOperationException: safe failure",
  );

  assert.equal(parsed.timestamp, "2026-08-09T09:15:21.1234567+00:00");
  assert.equal(parsed.level, "error");
  assert.equal(parsed.category, "AnimeGoNet.App.Metadata");
  assert.equal(parsed.eventId, 4301);
  assert.equal(parsed.message, "Duplicate skipped");
  assert.equal(parsed.exception, "InvalidOperationException: safe failure");
});

test("unstructured compatibility lines remain visible without inventing fields", () => {
  const parsed = parseLiveLogEntry("legacy safe line");

  assert.equal(parsed.timestamp, null);
  assert.equal(parsed.level, "unknown");
  assert.equal(parsed.category, "unknown");
  assert.equal(parsed.message, "legacy safe line");
  assert.equal(parsed.text, "legacy safe line");
});

test("combined level, category, event and keyword filters are deterministic", () => {
  const entries = [
    parseLiveLogEntry("2026-08-09T09:15:21Z [INF] AnimeGoNet.App.Download (4200): queued task alpha"),
    parseLiveLogEntry("2026-08-09T09:15:22Z [WRN] AnimeGoNet.App.Metadata (4301): duplicate task alpha"),
    parseLiveLogEntry("2026-08-09T09:15:23Z [ERR] AnimeGoNet.App.Metadata (4302): failed task beta"),
  ];

  const filtered = filterLiveLogEntries(entries, {
    minimumLevel: "warning",
    query: "alpha",
    category: "metadata",
    eventId: "4301",
  });

  assert.equal(filtered.length, 1);
  assert.equal(filtered[0].eventId, 4301);
});
