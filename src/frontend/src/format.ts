import type { ChartPoint, Reconciliation, ServiceHealth } from "./api";

const BUCKET_MS = 5000;
// The API reports a source it has never seen as the Unix epoch; anything before 2000 means "never".
const NEVER_BEFORE_MS = Date.UTC(2000, 0, 1);

export function agoText(iso: string, nowMs: number): string {
  const t = Date.parse(iso);
  if (Number.isNaN(t) || t < NEVER_BEFORE_MS) return "never";
  const s = Math.max(0, Math.round((nowMs - t) / 1000));
  if (s < 60) return `${s} s ago`;
  if (s < 3600) return `${Math.floor(s / 60)} min ago`;
  return `${Math.floor(s / 3600)} h ago`;
}

export function healthLabel(h: ServiceHealth, nowMs: number): string {
  switch (h.$type) {
    case "Healthy":
      return "Healthy";
    case "Degraded":
      return `Degraded: ${h.reason}`;
    case "Down": {
      const ago = agoText(h.lastSeen, nowMs);
      return ago === "never" ? "Down, never seen" : `Down, last seen ${ago}`;
    }
    default:
      return "Unknown";
  }
}

// A Down tile still carries its last rates; they are history, not live.
export const isStale = (h: ServiceHealth): boolean => h.$type === "Down";

export const formatSeconds = (s: number | null): string => (s === null ? "—" : `${Math.max(0, s).toFixed(1)} s`);

export function formatEta(s: number | null): string {
  if (s === null) return "—";
  const total = Math.max(0, Math.round(s));
  if (total < 60) return `${total} s`;
  return `${Math.floor(total / 60)} min ${String(total % 60).padStart(2, "0")} s`;
}

export const formatInt = (n: number): string => Math.round(n).toLocaleString("en-US");

// The API returns only buckets that have readings. Fill the whole window with zeros so an outage shows as a hole.
export function fillBuckets(points: readonly ChartPoint[], nowMs: number, minutes: number, watermark: string | null): ChartPoint[] {
  const known = new Map(points.map((p) => [Date.parse(p.bucketStart), p]));
  const wm = watermark === null ? null : Date.parse(watermark);
  const last = Math.floor(nowMs / BUCKET_MS) * BUCKET_MS;
  const out: ChartPoint[] = [];
  for (let t = last - minutes * 60_000; t <= last; t += BUCKET_MS) {
    out.push({
      bucketStart: new Date(t).toISOString(),
      readings: known.get(t)?.readings ?? 0,
      provisional: known.get(t)?.provisional ?? (wm === null || t > wm),
    });
  }
  return out;
}

// MirrorMaker 2 reports no health of its own: derive it from how far central's newest device time trails the plant's.
export const MIRROR_LAG_LIMIT_S = 10;
export function mirrorHealth(lagSeconds: number | null): ServiceHealth {
  if (lagSeconds === null) return { $type: "Degraded", reason: "no data" };
  return lagSeconds > MIRROR_LAG_LIMIT_S ? { $type: "Degraded", reason: "lagging" } : { $type: "Healthy" };
}

// The duplicate scan reads the whole readings table, so it runs only once the plant is paused and drained,
// and at most once per minGapMs while that holds.
export function shouldRequestFull(
  light: Pick<Reconciliation, "paused" | "drained"> | undefined,
  lastAttemptMs: number | null,
  nowMs: number,
  minGapMs = 10_000,
): boolean {
  if (!light || !light.paused || !light.drained) return false;
  return lastAttemptMs === null || nowMs - lastAttemptMs >= minGapMs;
}

// What the proof panel shows. The verdict is always the latest light poll's. The full check contributes only
// its duplicate-row count, and only while the plant is still paused and drained; after that it is stale.
export function proofView(
  light: Reconciliation,
  full: { data: Reconciliation; at: number } | undefined,
  nowMs: number,
): { verdict: Reconciliation["verdict"]; duplicateRows: number | null; checkedAgoSeconds: number | null } {
  const usable = full !== undefined && light.paused && light.drained && full.data.duplicateRows !== null;
  return {
    verdict: light.verdict,
    duplicateRows: usable ? full.data.duplicateRows : light.duplicateRows,
    checkedAgoSeconds: usable ? Math.max(0, Math.round((nowMs - full.at) / 1000)) : null,
  };
}

// A panel keeps its last data when its query fails. Before any success there is no such data to claim.
export function panelNote(isError: boolean, hasData: boolean, reason: string | undefined): string | undefined {
  if (!isError) return undefined;
  if (hasData) return "Not updating, showing the last data received.";
  const why = reason ?? "The API did not answer.";
  return `No data yet. ${/[.!?]$/.test(why) ? why : `${why}.`} Retrying every second.`;
}
