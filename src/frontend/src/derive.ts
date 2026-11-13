/**
 * Pure logic behind the redesigned page: the throughput sample buffer, the header status texts,
 * the alarm mix and the KPI strip.
 *
 * Like `format.ts`, every export here is a plain function, type or constant. None reads the clock,
 * calls `fetch`, or touches React, so `derive.test.ts` can test them with set inputs. The panels stay
 * thin: they call these functions and draw the result.
 *
 * Where it sits in the flow: `App.tsx` keeps the throughput samples with `addSample` and builds the
 * header with `apiState`, `plantStatus` and `utcTime`; `KpiStrip` draws `kpis`; `AlarmMixPanel` draws
 * `alarmMix`; `Throughput` uses `drainStatus` and `utcTime`; `CentralSide` uses `newestText`;
 * `ui.tsx` maps each `Tone` to its colour classes with `toneOfHealth`. The inputs are the types in
 * `api.ts`, which mirror `ApiModels.cs` in `EventDrivenDesign.Contracts`; the formatters come from
 * `format.ts`.
 *
 * @module
 */
import {
  ALARM_KINDS,
  type AlarmView,
  type Overview,
  type Reconciliation,
  type ServiceHealth,
} from "./api";
import {
  agoText,
  formatEta,
  formatInt,
  formatSeconds,
  isStale,
  MIRROR_LAG_LIMIT_S,
  mirrorHealth,
} from "./format";

/**
 * What a status colour means. `ok` is green, `warn` amber, `bad` red, `info` the blue accent, and
 * `idle` neutral grey for values that carry no judgement.
 */
export type Tone = "ok" | "warn" | "bad" | "info" | "idle";

/** A short status text with the colour meaning that goes with it. The text is always shown. */
export interface Status {
  /** The words shown to the reader, such as "Running". */
  text: string;
  /** The colour meaning, see `Tone`. */
  tone: Tone;
}

/** One point of the live throughput chart, sampled from one overview poll. */
export interface RateSample {
  /** Server time of the overview (`Overview.at`), in epoch milliseconds. */
  t: number;
  /** `Overview.liveRate`: readings per second that the gateways send. */
  live: number;
  /** `Overview.drainRate`: rows per second that the store writers insert. */
  drain: number;
}

/** How much history the live throughput chart keeps, in milliseconds (3 minutes). */
export const THROUGHPUT_WINDOW_MS = 3 * 60_000;

/**
 * Adds one sample to the end of the throughput buffer and drops the samples that left the window.
 *
 * @remarks
 * A sample that is not newer than the last one is ignored, and the same array comes back, so a
 * React state update with it does nothing. That covers a repeated poll answer and React's
 * development double run of effects. Samples older than `windowMs` before the new one are dropped.
 *
 * @param samples - The buffer so far, oldest first. It is not changed.
 * @param sample - The new sample.
 * @param windowMs - How far back to keep samples, in milliseconds. Defaults to `THROUGHPUT_WINDOW_MS`.
 * @returns A new array with the sample added, or `samples` itself when the sample is ignored.
 * @example
 * `addSample([{ t: 0, … }, { t: 1000, … }], { t: 3000, … }, 2000)` keeps the samples at 1000 and 3000.
 */
export function addSample(
  samples: readonly RateSample[],
  sample: RateSample,
  windowMs = THROUGHPUT_WINDOW_MS,
): readonly RateSample[] {
  const last = samples[samples.length - 1];
  if (last && sample.t <= last.t) return samples;
  return [...samples.filter((x) => x.t >= sample.t - windowMs), sample];
}

/**
 * Maps a health value to its colour meaning.
 *
 * @param h - The health, as sent by the API.
 * @returns `ok` for Healthy, `warn` for Degraded, `bad` for Down, and `warn` for a case this page
 * does not know.
 */
export function toneOfHealth(h: ServiceHealth): Tone {
  switch (h.$type) {
    case "Healthy":
      return "ok";
    case "Down":
      return "bad";
    default:
      return "warn";
  }
}

/**
 * Says whether the page can reach the API, for the header.
 *
 * @param isError - True when the latest overview request failed.
 * @param hasData - True when an overview has arrived at least once.
 * @returns "API unreachable" (bad), "Connecting" before the first answer (idle), or "API connected" (ok).
 */
export function apiState(isError: boolean, hasData: boolean): Status {
  if (isError) return { text: "API unreachable", tone: "bad" };
  if (!hasData) return { text: "Connecting", tone: "idle" };
  return { text: "API connected", tone: "ok" };
}

/**
 * Says whether the plant is running, for the header.
 *
 * @param paused - True when every gateway is paused, false when not, `undefined` before the overview arrives.
 * @returns "Running" (ok), "Paused" (info) or "Unknown" (idle).
 */
export function plantStatus(paused: boolean | undefined): Status {
  if (paused === undefined) return { text: "Unknown", tone: "idle" };
  return paused
    ? { text: "Paused", tone: "info" }
    : { text: "Running", tone: "ok" };
}

/**
 * Puts the drain ETA into words.
 *
 * @remarks
 * The API sends 0 when there is no backlog, a positive number while the drain is faster than the
 * live rate, and `null` when it cannot tell (see `Proof.DrainEtaSeconds` in `EventDrivenDesign.Domain`).
 *
 * @param etaSeconds - `Overview.drainEtaSeconds`.
 * @returns "No backlog" (ok), "Draining backlog" (info), or "Unknown at current rates" (idle).
 */
export function drainStatus(etaSeconds: number | null): Status {
  if (etaSeconds === null)
    return { text: "Unknown at current rates", tone: "idle" };
  return etaSeconds > 0
    ? { text: "Draining backlog", tone: "info" }
    : { text: "No backlog", tone: "ok" };
}

/** A name with a count, such as one alarm kind and how many alarms have it. */
export interface Count {
  /** The kind or severity. */
  name: string;
  /** How many alarms have it. */
  count: number;
}

/** What the alarm mix panel shows. */
export interface AlarmMix {
  /** Count of alarms looked at. */
  total: number;
  /** One entry per kind in `ALARM_KINDS` order, then any kind the page does not know. */
  byKind: Count[];
  /** `critical`, `high` and `warning` in that order, then any other severity. */
  bySeverity: Count[];
  /** Latency of the newest stored alarm in seconds, or `null` when there are no alarms. */
  latestLatency: number | null;
  /** Largest latency in seconds, or `null` when there are no alarms. */
  maxLatency: number | null;
}

/** The severities the API sends, from most to least severe (see the alarm kinds table in the run guide). */
const SEVERITIES = ["critical", "high", "warning"];

/**
 * Counts names, starting from a set list so every known name is present, even at zero.
 *
 * @param known - Names that always appear, in this order.
 * @param names - The names to count. One not in `known` is added after them, in first-seen order.
 * @returns One `Count` per name.
 */
function tally(known: readonly string[], names: string[]): Count[] {
  const counts = new Map<string, number>(known.map((k) => [k, 0]));
  for (const n of names) counts.set(n, (counts.get(n) ?? 0) + 1);
  return [...counts].map(([name, count]) => ({ name, count }));
}

/**
 * Counts the alarms by kind and by severity, and picks the latest and the largest latency.
 *
 * @remarks
 * The order of the rows is set, not sorted by count, so the panel does not reshuffle every second.
 * "Latest" is the first alarm, because `GET /api/alarms` lists the newest stored first.
 *
 * @param alarms - The alarms from `useAlarms`, newest stored first.
 * @returns The mix. See `AlarmMix`.
 */
export function alarmMix(alarms: readonly AlarmView[]): AlarmMix {
  return {
    total: alarms.length,
    byKind: tally(
      ALARM_KINDS,
      alarms.map((a) => a.kind),
    ),
    bySeverity: tally(
      SEVERITIES,
      alarms.map((a) => a.severity),
    ),
    latestLatency: alarms[0]?.latencySeconds ?? null,
    maxLatency:
      alarms.length === 0
        ? null
        : Math.max(...alarms.map((a) => a.latencySeconds)),
  };
}

/** One card of the KPI strip. */
export interface Kpi {
  /** The caption, such as "Live rate". */
  label: string;
  /** The big figure as text, such as "1,204,500" or "0.8 s". "—" when there is no data. */
  value: string;
  /** Unit printed small after the value, such as "rows/s". Empty when the value carries its unit or is "—". */
  unit: string;
  /** Short secondary line that says what the value means, such as "2 of 2 writers up". */
  sub: string;
  /** Colour meaning of the card. The `sub` text carries the same meaning in words. */
  tone: Tone;
}

/** Colour meaning of each verdict. A verdict this page does not know is shown as UNKNOWN in amber. */
const VERDICT_TONE: Record<string, Tone> = {
  PASS: "ok",
  WAIT: "warn",
  FAIL: "bad",
};

/**
 * Builds the eight cards of the KPI strip from the overview and the light proof.
 *
 * @remarks
 * Order: Live rate, Drain rate, Rows stored, Mirror lag, Consumer lag, Drain ETA (from the overview),
 * then Duplicates absorbed and Proof (from the reconciliation). A card whose source has not arrived
 * shows "—", no unit, a "waiting" line and the neutral tone.
 * A gateway is live when it is neither down nor paused, the same rule the API uses for `liveRate`.
 * A store writer is up when it is not down.
 *
 * @param o - The latest overview, or `undefined` before the first answer.
 * @param p - The latest light reconciliation, or `undefined` before the first answer.
 * @returns Eight `Kpi` values in the order above.
 * @see Overview and Reconciliation in ApiModels.cs
 */
export function kpis(
  o: Overview | undefined,
  p: Reconciliation | undefined,
): Kpi[] {
  /** A card with no data yet. */
  const wait = (label: string, sub: string): Kpi => ({
    label,
    value: "—",
    unit: "",
    sub,
    tone: "idle",
  });
  const fromOverview: Kpi[] = o
    ? overviewCards(o)
    : [
        "Live rate",
        "Drain rate",
        "Rows stored",
        "Mirror lag",
        "Consumer lag",
        "Drain ETA",
      ].map((l) => wait(l, "waiting for the API"));
  if (!p)
    return [
      ...fromOverview,
      wait("Duplicates absorbed", "waiting for data"),
      wait("Proof", "waiting for data"),
    ];
  const tone = VERDICT_TONE[p.verdict];
  return [
    ...fromOverview,
    {
      label: "Duplicates absorbed",
      value: formatInt(p.duplicatesAbsorbed),
      unit: "",
      sub: "redeliveries stored once",
      tone: "idle",
    },
    {
      label: "Proof",
      value: tone ? p.verdict : "UNKNOWN",
      unit: "",
      sub: `plant ${p.paused ? "paused" : "running"}, ${p.drained ? "drained" : "not drained"}`,
      tone: tone ?? "warn",
    },
  ];
}

/**
 * Builds the six overview cards of the KPI strip. See `kpis`.
 *
 * @param o - The latest overview.
 * @returns The Live rate, Drain rate, Rows stored, Mirror lag, Consumer lag and Drain ETA cards.
 */
function overviewCards(o: Overview): Kpi[] {
  const gws = o.gateways.length;
  const live = o.gateways.filter((g) => !isStale(g.health) && !g.paused).length;
  const allPaused = gws > 0 && o.gateways.every((g) => g.paused);
  const writers = o.consumers.length;
  const up = o.consumers.filter((c) => !isStale(c.health)).length;
  const mirror = mirrorHealth(o.mirrorLagSeconds);
  const drain = drainStatus(o.drainEtaSeconds);
  return [
    {
      label: "Live rate",
      value: formatInt(o.liveRate),
      unit: "readings/s",
      sub: allPaused ? "plant paused" : `${live} of ${gws} gateways live`,
      tone: allPaused ? "info" : live === gws && gws > 0 ? "ok" : "warn",
    },
    {
      label: "Drain rate",
      value: formatInt(o.drainRate),
      unit: "rows/s",
      sub: `${up} of ${writers} writers up`,
      tone: up === 0 ? "bad" : up === writers ? "ok" : "warn",
    },
    {
      label: "Rows stored",
      value: formatInt(o.database.stored),
      unit: "",
      sub: `fresh ${formatSeconds(o.database.freshnessSeconds)}`,
      tone: toneOfHealth(o.database.health),
    },
    {
      label: "Mirror lag",
      value: formatSeconds(o.mirrorLagSeconds),
      unit: "",
      sub:
        mirror.$type === "Degraded"
          ? mirror.reason
          : `within the ${MIRROR_LAG_LIMIT_S} s limit`,
      tone: toneOfHealth(mirror),
    },
    {
      label: "Consumer lag",
      value: formatInt(o.consumerLag),
      unit: "records",
      sub: "not read at central yet",
      tone: "idle",
    },
    {
      label: "Drain ETA",
      value: formatEta(o.drainEtaSeconds),
      unit: "",
      sub: drain.text,
      tone: drain.tone,
    },
  ];
}

/**
 * Formats a time as a 24-hour clock time in UTC.
 *
 * @param t - An ISO-8601 timestamp, or epoch milliseconds.
 * @returns For example `"11:59:30"`.
 */
export const utcTime = (t: string | number): string =>
  new Date(t).toLocaleTimeString("en-GB", { hour12: false, timeZone: "UTC" });

/**
 * Says how old a cluster's newest record is.
 *
 * @param t - `ClusterTile.newestDeviceTime`, or `null` when no record has been seen.
 * @param nowMs - The current time in epoch milliseconds.
 * @returns `"no data"` for `null`, otherwise `agoText`, such as `"3 s ago"`.
 */
export const newestText = (t: string | null, nowMs: number): string =>
  t === null ? "no data" : agoText(t, nowMs);
