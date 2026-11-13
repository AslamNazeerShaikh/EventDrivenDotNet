/**
 * Pure logic for the dashboard: text formatters, the chart bucket filler, and the small decision
 * rules the panels and hooks use.
 *
 * Every export here is a plain function or constant. None reads the clock, calls `fetch`, or
 * touches React: the current time always comes in as an argument (`nowMs`). That is why
 * `format.test.ts` can test them with set inputs.
 *
 * Where it sits in the flow: `api.ts` imports `shouldRequestFull` (the rule for the full duplicate
 * scan), `App.tsx` imports `agoText` and `panelNote`, and the panels in `src/panels/` import the
 * formatters. This file imports only types from `api.ts`, which mirror the C# contracts in
 * `src/backend/EventDrivenDesign.Contracts`.
 *
 * @module
 */
import type { ChartPoint, Reconciliation, ServiceHealth } from "./api";

/**
 * Width of one chart bucket, in milliseconds (5 seconds).
 *
 * @remarks
 * It matches the 5-second `date_bin` that the API uses to group readings for `GET /api/chart`.
 */
const BUCKET_MS = 5000;

/**
 * Time before which a timestamp means "never seen", as epoch milliseconds (1 January 2000, UTC).
 *
 * @remarks
 * The API reports a source it has never seen as the Unix epoch; anything before 2000 means "never".
 */
const NEVER_BEFORE_MS = Date.UTC(2000, 0, 1);

/**
 * Says how long ago a timestamp was, in coarse units.
 *
 * @remarks
 * Under a minute it counts seconds, under an hour it counts whole minutes, otherwise whole hours.
 * A timestamp slightly ahead of `nowMs` (clock skew) counts as 0 s, never negative.
 * A timestamp that does not parse, or is before the year 2000, gives `"never"`.
 *
 * @param iso - An ISO-8601 timestamp, as the API sends them.
 * @param nowMs - The current time in epoch milliseconds.
 * @returns For example `"3 s ago"`, `"3 min ago"`, `"2 h ago"`, or `"never"`.
 * @example
 * With `nowMs` at 2026-11-12T12:00:00Z:
 * `agoText("2026-11-12T11:59:57Z", nowMs)` is `"3 s ago"`,
 * `agoText("2026-11-12T11:57:00Z", nowMs)` is `"3 min ago"`,
 * `agoText("2026-11-12T12:00:02Z", nowMs)` is `"0 s ago"`, and
 * `agoText("1970-01-01T00:00:00Z", nowMs)` is `"never"`.
 */
export function agoText(iso: string, nowMs: number): string {
  const t = Date.parse(iso);
  if (Number.isNaN(t) || t < NEVER_BEFORE_MS) return "never";
  // Rounded to whole seconds; max(0, ...) hides a clock that runs slightly ahead.
  const s = Math.max(0, Math.round((nowMs - t) / 1000));
  if (s < 60) return `${s} s ago`;
  if (s < 3600) return `${Math.floor(s / 60)} min ago`;
  return `${Math.floor(s / 3600)} h ago`;
}

/**
 * Turns a health value into the text shown beside its coloured dot.
 *
 * @param h - The health, as sent by the API (see `ServiceHealth` in `api.ts`).
 * @param nowMs - The current time in epoch milliseconds, used to say when a down source was last seen.
 * @returns `"Healthy"`, `"Degraded: <reason>"`, `"Down, last seen <ago>"`, `"Down, never seen"`,
 * or `"Unknown"` for a `$type` this page does not know.
 * @example
 * `healthLabel({ $type: "Degraded", reason: "buffer climbing" }, now)` is `"Degraded: buffer climbing"`.
 * `healthLabel({ $type: "Down", lastSeen: "2026-11-12T11:59:30Z" }, now)` is `"Down, last seen 30 s ago"`
 * when `now` is 2026-11-12T12:00:00Z.
 * @see ServiceHealth.cs in EventDrivenDesign.Contracts
 */
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
      // A newer API may send a case this page does not know yet.
      return "Unknown";
  }
}

/**
 * Shortens a gateway instance id to its last part, the letter that tells the pair members apart.
 *
 * @remarks
 * "gateway-bdy-b" → "b": which of the pair is active. A shop without one shows a dash.
 * An id with no `-` is returned unchanged.
 *
 * @param instance - The instance id from `GatewayTile.instance`, or `null` when the shop has none.
 * @returns The text after the last `-`, or `"—"` for `null`.
 * @example
 * `instanceLabel("gateway-bdy-b")` is `"b"`, and `instanceLabel(null)` is `"—"`.
 */
export const instanceLabel = (instance: string | null): string => {
  if (instance === null) return "—";
  const i = instance.lastIndexOf("-");
  return i >= 0 ? instance.slice(i + 1) : instance;
};

/**
 * Tells whether a tile's numbers are old history rather than live values.
 *
 * @remarks
 * A Down tile still carries its last rates; they are history, not live.
 * Only `Down` counts as stale. `Degraded` tiles are still live.
 *
 * @param h - The tile's health.
 * @returns True only when `h.$type` is `"Down"`.
 */
export const isStale = (h: ServiceHealth): boolean => h.$type === "Down";

/**
 * Formats a number of seconds with one decimal and the unit.
 *
 * @param s - Seconds, or `null` when unknown. A negative value is shown as 0.
 * @returns For example `"0.8 s"`, or `"—"` for `null`.
 * @example
 * `formatSeconds(0.84)` is `"0.8 s"`, `formatSeconds(-0.2)` is `"0.0 s"`, and `formatSeconds(null)` is `"—"`.
 */
export const formatSeconds = (s: number | null): string =>
  s === null ? "—" : `${Math.max(0, s).toFixed(1)} s`;

/**
 * Formats the drain ETA: seconds under a minute, otherwise minutes and seconds.
 *
 * @param s - Seconds until the backlog is empty, or `null` when unknown. It is rounded to whole seconds; a negative value is shown as 0.
 * @returns For example `"42 s"` or `"2 min 05 s"`, or `"—"` for `null`. The seconds after a minute are padded to two digits.
 * @example
 * `formatEta(42.4)` is `"42 s"`, `formatEta(125)` is `"2 min 05 s"`, `formatEta(0)` is `"0 s"`,
 * and `formatEta(null)` is `"—"`.
 * @see DrainEtaSeconds in the API's `Overview`
 */
export function formatEta(s: number | null): string {
  if (s === null) return "—";
  const total = Math.max(0, Math.round(s));
  if (total < 60) return `${total} s`;
  return `${Math.floor(total / 60)} min ${String(total % 60).padStart(2, "0")} s`;
}

/**
 * Formats a count as a whole number with comma thousands separators.
 *
 * @remarks
 * The locale is set to `en-US`, so the output does not depend on the viewer's browser settings.
 *
 * @param n - The number. It is rounded to the nearest whole number.
 * @returns For example `"1,204,500"`.
 * @example
 * `formatInt(1204500)` is `"1,204,500"`, `formatInt(7)` is `"7"`, and `formatInt(499.6)` is `"500"`.
 */
export const formatInt = (n: number): string =>
  Math.round(n).toLocaleString("en-US");

/**
 * Builds the full list of chart buckets for the window, with empty ones filled in.
 *
 * @remarks
 * The API returns only buckets that have readings. Fill the whole window with zeros so an outage shows as a hole.
 * The window ends at `nowMs` rounded down to a 5-second boundary and starts `minutes` earlier,
 * so it has `minutes * 12 + 1` buckets, oldest first. API timestamps are matched by their parsed
 * time, so an offset such as `+00:00` still lands on its bucket.
 * A bucket the API sent keeps its own `provisional` flag. A bucket made up here is provisional when
 * it is after the watermark, or when there is no watermark.
 *
 * @param points - The buckets from the API, in any order.
 * @param nowMs - The current time in epoch milliseconds, which sets where the window ends.
 * @param minutes - Length of the window in minutes.
 * @param watermark - The chart's watermark as an ISO-8601 string, or `null` when unknown.
 * @returns One `ChartPoint` per 5-second step, with `bucketStart` as an ISO-8601 UTC string.
 * @example
 * With `nowMs` at 2026-11-12T12:00:00Z and `minutes` 1, the result has 13 points. The first starts at
 * 11:59:00 and the last at 12:00:00. A bucket the API did not send has `readings` 0.
 * @see ChartPoint in ApiModels.cs
 */
export function fillBuckets(
  points: readonly ChartPoint[],
  nowMs: number,
  minutes: number,
  watermark: string | null,
): ChartPoint[] {
  // Keyed by epoch milliseconds, so the timestamp's text format does not matter.
  const known = new Map(points.map((p) => [Date.parse(p.bucketStart), p]));
  const wm = watermark === null ? null : Date.parse(watermark);
  // The start of the current 5-second bucket.
  const last = Math.floor(nowMs / BUCKET_MS) * BUCKET_MS;
  const out: ChartPoint[] = [];
  for (let t = last - minutes * 60_000; t <= last; t += BUCKET_MS) {
    out.push({
      bucketStart: new Date(t).toISOString(),
      readings: known.get(t)?.readings ?? 0,
      // Use the API's flag when it sent the bucket; otherwise it is provisional after the watermark.
      provisional: known.get(t)?.provisional ?? (wm === null || t > wm),
    });
  }
  return out;
}

/**
 * Largest mirror lag, in seconds, that still counts as healthy.
 *
 * @remarks
 * MirrorMaker 2 reports no health of its own: derive it from how far central's newest device time trails the plant's.
 * A lag of exactly this value is still healthy.
 */
export const MIRROR_LAG_LIMIT_S = 10;

/**
 * Derives a health value for MirrorMaker 2 from the mirror lag.
 *
 * @param lagSeconds - `Overview.mirrorLagSeconds`, or `null` when unknown.
 * @returns `Degraded` with reason `"no data"` for `null`, `Degraded` with reason `"lagging"` above
 * `MIRROR_LAG_LIMIT_S`, otherwise `Healthy`. It never returns `Down`.
 * @example
 * `mirrorHealth(10)` is `{ $type: "Healthy" }`, `mirrorHealth(10.1)` is
 * `{ $type: "Degraded", reason: "lagging" }`, and `mirrorHealth(-0.4)` is `{ $type: "Healthy" }`.
 */
export function mirrorHealth(lagSeconds: number | null): ServiceHealth {
  if (lagSeconds === null) return { $type: "Degraded", reason: "no data" };
  return lagSeconds > MIRROR_LAG_LIMIT_S
    ? { $type: "Degraded", reason: "lagging" }
    : { $type: "Healthy" };
}

/**
 * Decides whether the full duplicate scan should start now.
 *
 * @remarks
 * The duplicate scan reads the whole readings table, so it runs only once the plant is paused and drained,
 * and at most once per minGapMs while that holds.
 *
 * @param light - The latest light reconciliation (only `paused` and `drained` are read), or `undefined` before it arrives.
 * @param lastAttemptMs - Epoch milliseconds of the last scan attempt, or `null` if there was none.
 * @param nowMs - The current time in epoch milliseconds.
 * @param minGapMs - Smallest time between two attempts, in milliseconds. Defaults to 10 000 (10 s).
 * @returns True when `light` says paused and drained, and there was no attempt or the last one is at least `minGapMs` old.
 * @example
 * With `light` paused and drained: `shouldRequestFull(light, null, now)` is true,
 * `shouldRequestFull(light, now - 9_999, now)` is false, and `shouldRequestFull(light, now - 10_000, now)` is true.
 * `shouldRequestFull(undefined, null, now)` is false.
 * @see useFullCheck in `api.ts`
 */
export function shouldRequestFull(
  light: Pick<Reconciliation, "paused" | "drained"> | undefined,
  lastAttemptMs: number | null,
  nowMs: number,
  minGapMs = 10_000,
): boolean {
  if (!light || !light.paused || !light.drained) return false;
  return lastAttemptMs === null || nowMs - lastAttemptMs >= minGapMs;
}

/**
 * Picks what the proof panel shows from the light poll and the last full check.
 *
 * @remarks
 * What the proof panel shows. The verdict is always the latest light poll's. The full check contributes only
 * its duplicate-row count, and only while the plant is still paused and drained; after that it is stale.
 * The full result is used only if the light poll says paused and drained, and the full result has a
 * duplicate-row count that is not `null`. Otherwise the light poll's `duplicateRows` is shown, and no age.
 *
 * @param light - The latest light reconciliation.
 * @param full - The last full result with the epoch-millisecond time it arrived, or `undefined` if none yet.
 * @param nowMs - The current time in epoch milliseconds.
 * @returns `verdict`, always `light.verdict`; `duplicateRows`, a count or `null` for "not checked";
 * and `checkedAgoSeconds`, the age of the full check in whole seconds (never below 0), or `null`
 * when the full result is not used.
 * @example
 * With `light` paused, drained and PASS, and a full result with 0 duplicate rows that arrived 4 s ago,
 * the result is `{ verdict: "PASS", duplicateRows: 0, checkedAgoSeconds: 4 }`.
 * If the light poll is no longer paused and drained, it is `{ verdict: "WAIT", duplicateRows: null, checkedAgoSeconds: null }`.
 * @see Reconciliation in ApiModels.cs
 */
export function proofView(
  light: Reconciliation,
  full: { data: Reconciliation; at: number } | undefined,
  nowMs: number,
): {
  verdict: Reconciliation["verdict"];
  duplicateRows: number | null;
  checkedAgoSeconds: number | null;
} {
  const usable =
    full !== undefined &&
    light.paused &&
    light.drained &&
    full.data.duplicateRows !== null;
  return {
    verdict: light.verdict,
    duplicateRows: usable ? full.data.duplicateRows : light.duplicateRows,
    checkedAgoSeconds: usable
      ? Math.max(0, Math.round((nowMs - full.at) / 1000))
      : null,
  };
}

/**
 * Builds the note a panel shows when its request has failed.
 *
 * @remarks
 * A panel keeps its last data when its query fails. Before any success there is no such data to claim.
 * When a reason is shown, a full stop is added if the reason does not already end in `.`, `!` or `?`.
 *
 * @param isError - True when the panel's latest request failed.
 * @param hasData - True when the panel has data from an earlier success.
 * @param reason - The error's message, or `undefined` if there is none. Without one the text is "The API did not answer."
 * @returns `undefined` when there is no error. The "Not updating" text when there is old data.
 * Otherwise "No data yet. <reason>. Retrying every second."
 * @example
 * `panelNote(true, true, "x")` is `"Not updating, showing the last data received."`.
 * `panelNote(true, false, "Bad Gateway")` is `"No data yet. Bad Gateway. Retrying every second."`.
 */
export function panelNote(
  isError: boolean,
  hasData: boolean,
  reason: string | undefined,
): string | undefined {
  if (!isError) return undefined;
  if (hasData) return "Not updating, showing the last data received.";
  const why = reason ?? "The API did not answer.";
  return `No data yet. ${/[.!?]$/.test(why) ? why : `${why}.`} Retrying every second.`;
}
