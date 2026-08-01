export interface ApiErrorPayload {
  code?: string;
  message?: string;
  errors?: readonly string[];
}

export type ApiFetch = (
  input: RequestInfo | URL,
  init?: RequestInit,
) => Promise<Response>;

export type JsonRequestOptions<TBody = never> = Omit<RequestInit, "body" | "headers"> & {
  body?: TBody;
  headers?: HeadersInit;
};

export class ApiHttpError extends Error {
  readonly status: number;
  readonly code: string | null;
  readonly payload: ApiErrorPayload | null;

  constructor(status: number, payload: ApiErrorPayload | null) {
    super(payload?.message?.trim() || `HTTP ${status}`);
    this.name = "ApiHttpError";
    this.status = status;
    this.code = payload?.code?.trim() || null;
    this.payload = payload;
  }
}

export class ApiProtocolError extends Error {
  readonly code: "invalid_api_path" | "invalid_json_response";

  constructor(code: ApiProtocolError["code"], message: string) {
    super(message);
    this.name = "ApiProtocolError";
    this.code = code;
  }
}

export class ApiClient {
  readonly #fetch: ApiFetch;
  readonly #defaultHeaders: Headers;

  constructor(
    accessKey: string | null,
    fetchImplementation: ApiFetch = globalThis.fetch.bind(globalThis),
  ) {
    this.#fetch = fetchImplementation;
    this.#defaultHeaders = new Headers({ Accept: "application/json" });
    if (accessKey) this.#defaultHeaders.set("Access-Key", accessKey);
  }

  get<TResponse>(path: string, options: JsonRequestOptions = {}): Promise<TResponse> {
    return this.request<TResponse>(path, { ...options, method: "GET" });
  }

  post<TResponse, TBody = never>(
    path: string,
    body?: TBody,
    options: JsonRequestOptions = {},
  ): Promise<TResponse> {
    return this.request<TResponse, TBody>(path, { ...options, method: "POST", body });
  }

  put<TResponse, TBody>(
    path: string,
    body: TBody,
    options: JsonRequestOptions = {},
  ): Promise<TResponse> {
    return this.request<TResponse, TBody>(path, { ...options, method: "PUT", body });
  }

  delete<TResponse>(path: string, options: JsonRequestOptions = {}): Promise<TResponse> {
    return this.request<TResponse>(path, { ...options, method: "DELETE" });
  }

  async request<TResponse, TBody = never>(
    path: string,
    options: JsonRequestOptions<TBody> = {},
  ): Promise<TResponse> {
    assertSameOriginPath(path);
    const { body, headers: requestHeaders, ...requestInit } = options;
    const headers = new Headers(this.#defaultHeaders);
    new Headers(requestHeaders).forEach((value, name) => headers.set(name, value));
    let serializedBody: string | undefined;
    if (body !== undefined) {
      headers.set("Content-Type", "application/json");
      serializedBody = JSON.stringify(body);
    }

    const response = await this.#fetch(path, {
      ...requestInit,
      headers,
      body: serializedBody,
    });
    if (!response.ok) throw await readHttpError(response);
    if (response.status === 204) return undefined as TResponse;

    try {
      return await response.json() as TResponse;
    } catch {
      throw new ApiProtocolError(
        "invalid_json_response",
        `API returned invalid JSON for ${requestInit.method ?? "GET"} ${path}`,
      );
    }
  }
}

function assertSameOriginPath(path: string): void {
  if (!path.startsWith("/") || path.startsWith("//") || path.includes("\\")) {
    throw new ApiProtocolError(
      "invalid_api_path",
      "API path must be an absolute same-origin path.",
    );
  }
}

async function readHttpError(response: Response): Promise<ApiHttpError> {
  let payload: ApiErrorPayload | null = null;
  try {
    const value = await response.json() as unknown;
    if (value !== null && typeof value === "object" && !Array.isArray(value)) {
      const fields = value as Record<string, unknown>;
      payload = {
        code: typeof fields.code === "string" ? fields.code : undefined,
        message: typeof fields.message === "string" ? fields.message : undefined,
        errors: Array.isArray(fields.errors) && fields.errors.every(error => typeof error === "string")
          ? fields.errors
          : undefined,
      };
    }
  } catch {
    // The stable fallback intentionally exposes only the status, never an arbitrary body.
  }
  return new ApiHttpError(response.status, payload);
}
