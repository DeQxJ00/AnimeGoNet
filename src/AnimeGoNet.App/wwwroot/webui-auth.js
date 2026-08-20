const sessionStorageKey = "animegonet.webui_access_key.session";
const persistentStorageKey = "animegonet.webui_access_key.remembered";
export function loadWebUiAccessKey(search, stores) {
    const queryKey = new URLSearchParams(search).get("webui_access_key")?.trim();
    if (queryKey)
        return queryKey;
    return safeRead(stores.session, sessionStorageKey)
        ?? safeRead(stores.persistent, persistentStorageKey);
}
export function storeWebUiAccessKey(accessKeyHash, remember, stores) {
    safeWrite(stores.session, sessionStorageKey, accessKeyHash);
    if (remember) {
        safeWrite(stores.persistent, persistentStorageKey, accessKeyHash);
    }
    else {
        safeRemove(stores.persistent, persistentStorageKey);
    }
}
export function clearStoredWebUiAccessKey(stores) {
    safeRemove(stores.session, sessionStorageKey);
    safeRemove(stores.persistent, persistentStorageKey);
}
export async function sha256LowerHex(value) {
    const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value));
    return Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, "0"))
        .join("");
}
export function createAuthenticatedFetch(options) {
    return async (input, init = {}) => {
        if (!isSameOriginPath(input))
            return options.fetchImplementation(input, init);
        const response = await options.fetchImplementation(input, withAccessKey(init, options.getAccessKey()));
        if (response.status !== 401)
            return response;
        const replacement = await options.requestAccessKey();
        if (!replacement)
            return response;
        return options.fetchImplementation(input, withAccessKey(init, replacement));
    };
}
function isSameOriginPath(input) {
    return typeof input === "string"
        && input.startsWith("/")
        && !input.startsWith("//")
        && !input.includes("\\");
}
function withAccessKey(init, accessKey) {
    const headers = new Headers(init.headers);
    if (accessKey)
        headers.set("WebUI-Access-Key", accessKey);
    else
        headers.delete("WebUI-Access-Key");
    return { ...init, headers };
}
function safeRead(storage, key) {
    try {
        return storage.getItem(key)?.trim() || null;
    }
    catch {
        return null;
    }
}
function safeWrite(storage, key, value) {
    try {
        storage.setItem(key, value);
    }
    catch {
        // Private browsing or storage policy may reject persistence; the live value still works.
    }
}
function safeRemove(storage, key) {
    try {
        storage.removeItem(key);
    }
    catch {
        // Storage cleanup is best effort for the same reason as persistence.
    }
}
