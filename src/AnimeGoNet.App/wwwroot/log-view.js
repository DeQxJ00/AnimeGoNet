export const liveLogLevelOrder = {
    trace: 0,
    debug: 1,
    information: 2,
    warning: 3,
    error: 4,
    critical: 5,
    unknown: 2,
};
const levelByCode = {
    TRC: "trace",
    DBG: "debug",
    INF: "information",
    WRN: "warning",
    ERR: "error",
    CRT: "critical",
    NON: "unknown",
};
const formattedLine = /^(\S+) \[(TRC|DBG|INF|WRN|ERR|CRT|NON)\] ([^:]+?)(?: \((\d+)\))?: (.*)$/u;
export function parseLiveLogEntry(line) {
    const match = formattedLine.exec(line);
    if (!match) {
        return {
            timestamp: null,
            level: "unknown",
            category: "unknown",
            eventId: null,
            message: line,
            exception: null,
            text: line,
        };
    }
    const payload = match[5] ?? "";
    const exceptionSeparator = payload.lastIndexOf(" | ");
    return {
        timestamp: match[1] ?? null,
        level: levelByCode[match[2] ?? ""] ?? "unknown",
        category: (match[3] ?? "unknown").trim(),
        eventId: match[4] ? Number(match[4]) : null,
        message: exceptionSeparator >= 0
            ? payload.slice(0, exceptionSeparator)
            : payload,
        exception: exceptionSeparator >= 0
            ? payload.slice(exceptionSeparator + 3)
            : null,
        text: line,
    };
}
export function filterLiveLogEntries(entries, filter) {
    const minimum = filter.minimumLevel === "all"
        ? -1
        : liveLogLevelOrder[filter.minimumLevel];
    const query = filter.query.trim().toLocaleLowerCase();
    const category = filter.category.trim().toLocaleLowerCase();
    const eventId = filter.eventId.trim();
    const from = parseFilterTime(filter.fromUtc);
    const to = parseFilterTime(filter.toUtc);
    return entries.filter(entry => {
        if (liveLogLevelOrder[entry.level] < minimum)
            return false;
        if (category && !entry.category.toLocaleLowerCase().includes(category))
            return false;
        if (eventId && String(entry.eventId ?? "") !== eventId)
            return false;
        if (filter.domain && filter.domain !== "all" && classifyLiveLogEntry(entry) !== filter.domain) {
            return false;
        }
        const httpDirection = classifyLiveLogHttpDirection(entry);
        if (filter.httpScope === "outbound" && httpDirection !== "outbound")
            return false;
        if (filter.httpScope === "inbound" && httpDirection !== "inbound")
            return false;
        if (filter.httpScope === "non-http" && httpDirection !== "none")
            return false;
        if (filter.exceptionOnly && !entry.exception)
            return false;
        if ((from !== null || to !== null) && entry.timestamp) {
            const timestamp = Date.parse(entry.timestamp);
            if (!Number.isNaN(timestamp) && ((from !== null && timestamp < from) || (to !== null && timestamp > to))) {
                return false;
            }
        }
        else if ((from !== null || to !== null) && !entry.timestamp) {
            return false;
        }
        return !query || entry.text.toLocaleLowerCase().includes(query);
    });
}
export function classifyLiveLogHttpDirection(entry) {
    const category = entry.category.toLocaleLowerCase();
    const message = entry.message.toLocaleLowerCase();
    if (category.startsWith("microsoft.aspnetcore.hosting.diagnostics")
        || category.startsWith("microsoft.aspnetcore.routing.endpointmiddleware")
        || category.startsWith("microsoft.aspnetcore.staticfiles.staticfilemiddleware")
        || category.startsWith("microsoft.aspnetcore.server.kestrel")
        || message.startsWith("request starting http/")
        || message.startsWith("request finished http/")) {
        return "inbound";
    }
    if (category.startsWith("system.net.http")
        || category.startsWith("microsoft.extensions.http")
        || category.startsWith("animegonet.app.http.outbound")
        || message.startsWith("start processing http request")
        || message.startsWith("sending http request")
        || message.startsWith("received http response headers")
        || message.startsWith("end processing http request")) {
        return "outbound";
    }
    return "none";
}
export function classifyLiveLogEntry(entry) {
    const text = `${entry.category} ${entry.message}`.toLocaleLowerCase();
    if (text.includes("ai_metadata") || text.includes("openai") || text.includes(".ai"))
        return "ai";
    if (text.includes("tmdb"))
        return "tmdb";
    if (text.includes("qbittorrent") || text.includes("torrent") || text.includes("download"))
        return "download";
    if (text.includes("rss") || text.includes("mikan"))
        return "rss";
    if (text.includes("organiz") || text.includes("整理") || text.includes("library"))
        return "organize";
    if (text.includes("metadata") || text.includes("bangumi"))
        return "metadata";
    return "system";
}
function parseFilterTime(value) {
    if (!value)
        return null;
    const parsed = Date.parse(value);
    return Number.isNaN(parsed) ? null : parsed;
}
