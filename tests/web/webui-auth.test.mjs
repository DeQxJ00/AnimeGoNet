import assert from "node:assert/strict";
import test from "node:test";
import {
  clearStoredWebUiAccessKey,
  createAuthenticatedFetch,
  loadWebUiAccessKey,
  sha256LowerHex,
  storeWebUiAccessKey,
} from "../../src/AnimeGoNet.App/wwwroot/webui-auth.js";

function memoryStorage() {
  const values = new Map();
  return {
    getItem: key => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, String(value)),
    removeItem: key => values.delete(key),
  };
}

test("WebUI key uses URL, session, and remembered storage in priority order", () => {
  const stores = { session: memoryStorage(), persistent: memoryStorage() };
  storeWebUiAccessKey("remembered", true, stores);
  assert.equal(loadWebUiAccessKey("", stores), "remembered");

  stores.session.setItem("animegonet.webui_access_key.session", "session");
  assert.equal(loadWebUiAccessKey("", stores), "session");
  assert.equal(loadWebUiAccessKey("?webui_access_key=url", stores), "url");

  clearStoredWebUiAccessKey(stores);
  assert.equal(loadWebUiAccessKey("", stores), null);
});

test("non-remembered key stays in the current session and removes persistence", () => {
  const stores = { session: memoryStorage(), persistent: memoryStorage() };
  storeWebUiAccessKey("old", true, stores);
  storeWebUiAccessKey("current", false, stores);
  stores.session.removeItem("animegonet.webui_access_key.session");
  assert.equal(loadWebUiAccessKey("", stores), null);
});

test("plaintext key is converted to the lowercase SHA-256 accepted by WebUI auth", async () => {
  assert.equal(
    await sha256LowerHex("123456"),
    "8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92",
  );
});

test("401 requests one replacement key and retries the original request", async () => {
  const requests = [];
  let activeKey = "expired";
  const authenticatedFetch = createAuthenticatedFetch({
    getAccessKey: () => activeKey,
    requestAccessKey: async () => {
      activeKey = "replacement";
      return activeKey;
    },
    fetchImplementation: async (input, init) => {
      requests.push({ input, init });
      return init.headers.get("WebUI-Access-Key") === "replacement"
        ? Response.json({ ok: true })
        : Response.json({ code: "unauthorized" }, { status: 401 });
    },
  });

  const response = await authenticatedFetch("/api/v1/status", { method: "GET" });
  assert.equal(response.status, 200);
  assert.equal(requests.length, 2);
  assert.equal(requests[0].init.headers.get("WebUI-Access-Key"), "expired");
  assert.equal(requests[1].init.headers.get("WebUI-Access-Key"), "replacement");
  assert.equal(requests[1].init.method, "GET");
});

test("external URLs never receive the WebUI key or open the login window", async () => {
  let requestedKey = false;
  let capturedHeaders;
  const authenticatedFetch = createAuthenticatedFetch({
    getAccessKey: () => "must-not-leak",
    requestAccessKey: async () => {
      requestedKey = true;
      return "replacement";
    },
    fetchImplementation: async (_input, init) => {
      capturedHeaders = new Headers(init?.headers);
      return new Response(null, { status: 401 });
    },
  });

  const response = await authenticatedFetch("https://example.invalid/api");
  assert.equal(response.status, 401);
  assert.equal(capturedHeaders.has("WebUI-Access-Key"), false);
  assert.equal(requestedKey, false);
});
