export type BrowserStorage = Pick<Storage, "getItem" | "setItem" | "removeItem">;

export interface WebUiAccessKeyStores {
  session: BrowserStorage;
  persistent: BrowserStorage;
}

export interface AuthenticatedFetchOptions {
  fetchImplementation: typeof fetch;
  getAccessKey: () => string | null;
  requestAccessKey: () => Promise<string | null>;
}

const sessionStorageKey = "animegonet.webui_access_key.session";
const persistentStorageKey = "animegonet.webui_access_key.remembered";

export function loadWebUiAccessKey(
  search: string,
  stores: WebUiAccessKeyStores,
): string | null {
  const queryKey = new URLSearchParams(search).get("webui_access_key")?.trim();
  if (queryKey) return queryKey;
  return safeRead(stores.session, sessionStorageKey)
    ?? safeRead(stores.persistent, persistentStorageKey);
}

export function storeWebUiAccessKey(
  accessKeyHash: string,
  remember: boolean,
  stores: WebUiAccessKeyStores,
): void {
  safeWrite(stores.session, sessionStorageKey, accessKeyHash);
  if (remember) {
    safeWrite(stores.persistent, persistentStorageKey, accessKeyHash);
  } else {
    safeRemove(stores.persistent, persistentStorageKey);
  }
}

export function clearStoredWebUiAccessKey(stores: WebUiAccessKeyStores): void {
  safeRemove(stores.session, sessionStorageKey);
  safeRemove(stores.persistent, persistentStorageKey);
}

export async function sha256LowerHex(value: string): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value));
  return Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, "0"))
    .join("");
}

export function createAuthenticatedFetch(options: AuthenticatedFetchOptions): typeof fetch {
  return async (input, init = {}) => {
    if (!isSameOriginPath(input)) return options.fetchImplementation(input, init);

    const response = await options.fetchImplementation(
      input,
      withAccessKey(init, options.getAccessKey()),
    );
    if (response.status !== 401) return response;

    const replacement = await options.requestAccessKey();
    if (!replacement) return response;
    return options.fetchImplementation(input, withAccessKey(init, replacement));
  };
}

function isSameOriginPath(input: RequestInfo | URL): boolean {
  return typeof input === "string"
    && input.startsWith("/")
    && !input.startsWith("//")
    && !input.includes("\\");
}

function withAccessKey(init: RequestInit, accessKey: string | null): RequestInit {
  const headers = new Headers(init.headers);
  if (accessKey) headers.set("WebUI-Access-Key", accessKey);
  else headers.delete("WebUI-Access-Key");
  return { ...init, headers };
}

function safeRead(storage: BrowserStorage, key: string): string | null {
  try {
    return storage.getItem(key)?.trim() || null;
  } catch {
    return null;
  }
}

function safeWrite(storage: BrowserStorage, key: string, value: string): void {
  try {
    storage.setItem(key, value);
  } catch {
    // Private browsing or storage policy may reject persistence; the live value still works.
  }
}

function safeRemove(storage: BrowserStorage, key: string): void {
  try {
    storage.removeItem(key);
  } catch {
    // Storage cleanup is best effort for the same reason as persistence.
  }
}
