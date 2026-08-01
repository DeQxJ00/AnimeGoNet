import assert from "node:assert/strict";
import test from "node:test";
import {
  ApiClient,
  ApiHttpError,
  ApiProtocolError,
} from "../../src/AnimeGoNet.App/wwwroot/api-client.js";

test("GET sends the configured access key only to a same-origin path", async () => {
  const requests = [];
  const client = new ApiClient("local-key", async (input, init) => {
    requests.push({ input, init });
    return Response.json({ value: 7 });
  });

  assert.deepEqual(await client.get("/api/v1/status"), { value: 7 });
  assert.equal(requests.length, 1);
  assert.equal(requests[0].input, "/api/v1/status");
  assert.equal(requests[0].init.method, "GET");
  assert.equal(requests[0].init.headers.get("Access-Key"), "local-key");
  assert.equal(requests[0].init.headers.get("Accept"), "application/json");
  assert.equal(requests[0].init.headers.has("Content-Type"), false);

  await assert.rejects(
    client.get("https://attacker.invalid/api/v1/status"),
    error => error instanceof ApiProtocolError && error.code === "invalid_api_path",
  );
  await assert.rejects(
    client.get("//attacker.invalid/api/v1/status"),
    error => error instanceof ApiProtocolError && error.code === "invalid_api_path",
  );
  await assert.rejects(
    client.get("/\\attacker.invalid/api/v1/status"),
    error => error instanceof ApiProtocolError && error.code === "invalid_api_path",
  );
  assert.equal(requests.length, 1);
});

test("JSON mutations preserve request options and serialize one typed body", async () => {
  let request;
  const client = new ApiClient(null, async (input, init) => {
    request = { input, init };
    return Response.json({ revision: 2 });
  });
  const controller = new AbortController();

  const result = await client.put(
    "/api/v1/config",
    { expected_revision: 1, enabled: true },
    { signal: controller.signal, headers: { "X-Correlation-Id": "test" } },
  );

  assert.deepEqual(result, { revision: 2 });
  assert.equal(request.input, "/api/v1/config");
  assert.equal(request.init.method, "PUT");
  assert.equal(request.init.signal, controller.signal);
  assert.equal(request.init.headers.get("Content-Type"), "application/json");
  assert.equal(request.init.headers.get("X-Correlation-Id"), "test");
  assert.equal(request.init.body, '{"expected_revision":1,"enabled":true}');
});

test("structured API failures become stable typed errors", async () => {
  const client = new ApiClient(null, async () => Response.json(
    { code: "revision_conflict", message: "配置已变化", errors: ["reload"] },
    { status: 409 },
  ));

  await assert.rejects(client.get("/api/v1/config"), error => {
    assert.ok(error instanceof ApiHttpError);
    assert.equal(error.status, 409);
    assert.equal(error.code, "revision_conflict");
    assert.equal(error.message, "配置已变化");
    assert.deepEqual(error.payload.errors, ["reload"]);
    return true;
  });
});

test("untrusted failure bodies and invalid success JSON are not displayed", async () => {
  const htmlFailure = new ApiClient(null, async () => new Response(
    "<html>proxy secret</html>",
    { status: 502, headers: { "Content-Type": "text/html" } },
  ));
  await assert.rejects(htmlFailure.get("/api/v1/status"), error => {
    assert.ok(error instanceof ApiHttpError);
    assert.equal(error.message, "HTTP 502");
    assert.equal(error.message.includes("proxy secret"), false);
    return true;
  });

  const malformedFailure = new ApiClient(null, async () => Response.json(
    { code: 17, message: { secret: true }, errors: ["valid", 42] },
    { status: 400 },
  ));
  await assert.rejects(malformedFailure.get("/api/v1/status"), error => {
    assert.ok(error instanceof ApiHttpError);
    assert.equal(error.code, null);
    assert.equal(error.message, "HTTP 400");
    assert.equal(error.payload.errors, undefined);
    return true;
  });

  const invalidJson = new ApiClient(null, async () => new Response("not-json"));
  await assert.rejects(
    invalidJson.get("/api/v1/status"),
    error => error instanceof ApiProtocolError && error.code === "invalid_json_response",
  );
});

test("204 responses support typed void operations", async () => {
  let body;
  const client = new ApiClient(null, async (_input, init) => {
    body = init.body;
    return new Response(null, { status: 204 });
  });
  assert.equal(await client.delete("/api/v1/items/1", { revision: 3 }), undefined);
  assert.equal(body, '{"revision":3}');
});
