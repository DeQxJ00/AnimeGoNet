export type LiveLogLevel =
  | "trace"
  | "debug"
  | "information"
  | "warning"
  | "error"
  | "critical"
  | "unknown";

export interface ParsedLiveLogEntry {
  timestamp: string | null;
  level: LiveLogLevel;
  category: string;
  eventId: number | null;
  message: string;
  exception: string | null;
  text: string;
}

export interface LiveLogFilter {
  minimumLevel: LiveLogLevel | "all";
  query: string;
  category: string;
  eventId: string;
}

export const liveLogLevelOrder: Record<LiveLogLevel, number> = {
  trace: 0,
  debug: 1,
  information: 2,
  warning: 3,
  error: 4,
  critical: 5,
  unknown: 2,
};

const levelByCode: Record<string, LiveLogLevel> = {
  TRC: "trace",
  DBG: "debug",
  INF: "information",
  WRN: "warning",
  ERR: "error",
  CRT: "critical",
  NON: "unknown",
};

const formattedLine = /^(\S+) \[(TRC|DBG|INF|WRN|ERR|CRT|NON)\] ([^:]+?)(?: \((\d+)\))?: (.*)$/u;

export function parseLiveLogEntry(line: string): ParsedLiveLogEntry {
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

export function filterLiveLogEntries(
  entries: ParsedLiveLogEntry[],
  filter: LiveLogFilter,
): ParsedLiveLogEntry[] {
  const minimum = filter.minimumLevel === "all"
    ? -1
    : liveLogLevelOrder[filter.minimumLevel];
  const query = filter.query.trim().toLocaleLowerCase();
  const category = filter.category.trim().toLocaleLowerCase();
  const eventId = filter.eventId.trim();
  return entries.filter(entry => {
    if (liveLogLevelOrder[entry.level] < minimum) return false;
    if (category && !entry.category.toLocaleLowerCase().includes(category)) return false;
    if (eventId && String(entry.eventId ?? "") !== eventId) return false;
    return !query || entry.text.toLocaleLowerCase().includes(query);
  });
}
