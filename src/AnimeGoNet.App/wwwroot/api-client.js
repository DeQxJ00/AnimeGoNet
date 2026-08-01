export class ApiHttpError extends Error {
    status;
    code;
    payload;
    constructor(status, payload) {
        super(payload?.message?.trim() || `HTTP ${status}`);
        this.name = "ApiHttpError";
        this.status = status;
        this.code = payload?.code?.trim() || null;
        this.payload = payload;
    }
}
export class ApiProtocolError extends Error {
    code;
    constructor(code, message) {
        super(message);
        this.name = "ApiProtocolError";
        this.code = code;
    }
}
export class ApiClient {
    #fetch;
    #defaultHeaders;
    constructor(accessKey, fetchImplementation = globalThis.fetch.bind(globalThis)) {
        this.#fetch = fetchImplementation;
        this.#defaultHeaders = new Headers({ Accept: "application/json" });
        if (accessKey)
            this.#defaultHeaders.set("Access-Key", accessKey);
    }
    get(path, options = {}) {
        return this.request(path, { ...options, method: "GET" });
    }
    post(path, body, options = {}) {
        return this.request(path, { ...options, method: "POST", body });
    }
    put(path, body, options = {}) {
        return this.request(path, { ...options, method: "PUT", body });
    }
    delete(path, body, options = {}) {
        return this.request(path, { ...options, method: "DELETE", body });
    }
    async request(path, options = {}) {
        assertSameOriginPath(path);
        const { body, headers: requestHeaders, ...requestInit } = options;
        const headers = new Headers(this.#defaultHeaders);
        new Headers(requestHeaders).forEach((value, name) => headers.set(name, value));
        let serializedBody;
        if (body !== undefined) {
            headers.set("Content-Type", "application/json");
            serializedBody = JSON.stringify(body);
        }
        const response = await this.#fetch(path, {
            ...requestInit,
            headers,
            body: serializedBody,
        });
        if (!response.ok)
            throw await readHttpError(response);
        if (response.status === 204)
            return undefined;
        try {
            return await response.json();
        }
        catch {
            throw new ApiProtocolError("invalid_json_response", `API returned invalid JSON for ${requestInit.method ?? "GET"} ${path}`);
        }
    }
}
function assertSameOriginPath(path) {
    if (!path.startsWith("/") || path.startsWith("//") || path.includes("\\")) {
        throw new ApiProtocolError("invalid_api_path", "API path must be an absolute same-origin path.");
    }
}
async function readHttpError(response) {
    let payload = null;
    try {
        const value = await response.json();
        if (value !== null && typeof value === "object" && !Array.isArray(value)) {
            const fields = value;
            payload = {
                code: typeof fields.code === "string" ? fields.code : undefined,
                message: typeof fields.message === "string" ? fields.message : undefined,
                errors: Array.isArray(fields.errors) && fields.errors.every(error => typeof error === "string")
                    ? fields.errors
                    : undefined,
            };
        }
    }
    catch {
        // The stable fallback intentionally exposes only the status, never an arbitrary body.
    }
    return new ApiHttpError(response.status, payload);
}
