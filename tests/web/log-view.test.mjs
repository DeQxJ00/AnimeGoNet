import assert from "node:assert/strict";
import test from "node:test";
import {
  classifyLiveLogEntry,
  classifyLiveLogHttpDirection,
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

test("HTTP direction filter separates outbound services from inbound WebUI traffic", () => {
  const entries = [
    parseLiveLogEntry("2026-08-13T10:00:00Z [INF] System.Net.Http.HttpClient.tmdb (100): Sending HTTP request GET http://api.tmdb.local/3/tv/65942"),
    parseLiveLogEntry("2026-08-13T10:00:01Z [INF] Microsoft.AspNetCore.Hosting.Diagnostics (1): Request starting HTTP/1.1 GET http://127.0.0.1:6180/api/v1/configuration"),
    parseLiveLogEntry("2026-08-13T10:00:02Z [INF] AnimeGoNet.App.Metadata (4301): metadata task completed"),
    parseLiveLogEntry("2026-08-13T10:00:03Z [INF] System.Net.Http.HttpClient.mikan (100): Sending HTTP request GET http://mikan.local/RSS/MyBangumi"),
    parseLiveLogEntry("2026-08-13T10:00:04Z [INF] System.Net.Http.HttpClient.bangumi (101): Received HTTP response headers after 25ms - 200"),
    parseLiveLogEntry("2026-08-13T10:00:05Z [INF] Microsoft.Hosting.Lifetime (14): Now listening on: http://127.0.0.1:6180"),
  ];

  assert.equal(classifyLiveLogHttpDirection(entries[0]), "outbound");
  assert.equal(classifyLiveLogHttpDirection(entries[1]), "inbound");
  assert.equal(classifyLiveLogHttpDirection(entries[2]), "none");
  assert.equal(classifyLiveLogHttpDirection(entries[3]), "outbound");
  assert.equal(classifyLiveLogHttpDirection(entries[4]), "outbound");
  assert.equal(classifyLiveLogHttpDirection(entries[5]), "none");
  assert.deepEqual(
    filterLiveLogEntries(entries, {
      minimumLevel: "all",
      query: "",
      category: "",
      eventId: "",
      httpScope: "outbound",
    }).map(entry => entry.category),
    [
      "System.Net.Http.HttpClient.tmdb",
      "System.Net.Http.HttpClient.mikan",
      "System.Net.Http.HttpClient.bangumi",
    ],
  );
  assert.deepEqual(
    filterLiveLogEntries(entries, {
      minimumLevel: "all",
      query: "",
      category: "",
      eventId: "",
      httpScope: "non-http",
    }).map(entry => entry.category),
    ["AnimeGoNet.App.Metadata", "Microsoft.Hosting.Lifetime"],
  );
});

test("domain, time, and exception filters expose useful runtime diagnostics", () => {
  const entries = [
    parseLiveLogEntry("2026-08-09T09:15:21Z [INF] AnimeGoNet.App.Ai (4400): ai_metadata request completed"),
    parseLiveLogEntry("2026-08-09T09:15:22Z [ERR] AnimeGoNet.App.Download (4200): qBittorrent failed | HttpRequestException: refused"),
    parseLiveLogEntry("legacy safe line"),
  ];

  assert.equal(classifyLiveLogEntry(entries[0]), "ai");
  assert.equal(classifyLiveLogEntry(entries[1]), "download");
  const filtered = filterLiveLogEntries(entries, {
    minimumLevel: "all",
    query: "",
    category: "",
    eventId: "",
    domain: "download",
    fromUtc: "2026-08-09T09:15:21.500Z",
    toUtc: "2026-08-09T09:15:23Z",
    exceptionOnly: true,
  });

  assert.equal(filtered.length, 1);
  assert.equal(filtered[0].eventId, 4200);
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
