/**
 * The "Plant to central" strip at the top of the page: seven stages from the machines to the database.
 *
 * Where it sits: `App.tsx` renders it with the latest `Overview` from `useOverview` in `api.ts`
 * and the server-based "now". The stage list is built by the private function `stages`, and
 * `HealthBadge` and `Panel` from `ui.tsx` draw it. It does not call the API. The data comes from
 * `GET /api/overview`; `Overview` and its tiles mirror `ApiModels.cs` in `EventDrivenDesign.Contracts`.
 *
 * Exports: `PipelineStrip`.
 *
 * @module
 */
import type { Overview, ServiceHealth } from "../api";
import {
  agoText,
  formatInt,
  formatSeconds,
  isStale,
  mirrorHealth,
} from "../format";
import { HealthBadge, Panel } from "./ui";

/**
 * What one stage card shows.
 */
interface Stage {
  /** Card title, such as "Plant Kafka" or "Gateways ×5". */
  name: string;
  /** Health to show as a badge, or `null` for a stage with no health of its own (Machines). */
  health: ServiceHealth | null;
  /** Plain text shown instead of the badge when `health` is `null`, such as "Paused" or "Generating". */
  fallbackLabel?: string;
  /** Big number on the card, or `null` for none. Its unit is `rateUnit`. */
  rate: number | null;
  /** Unit text printed after `rate`, such as "sent/s". Empty when there is no rate. */
  rateUnit: string;
  /** First detail line, such as the buffer size or the lag. Empty means the line is not drawn. */
  backlog: string;
  /** Second detail line about how recent the data is. Empty means the line is not drawn. */
  freshness: string;
  /** True when `rate` is only the last known rate of down sources, not a live one. */
  stale?: boolean;
  /** Extra red line, such as which sources are down. Missing means none. */
  detail?: string;
}

/** Order of severity for the three health cases: a higher number is worse. */
const RANK = { Healthy: 0, Degraded: 1, Down: 2 } as const;
/**
 * Picks the worst health in a list.
 *
 * @remarks
 * `Down` beats `Degraded`, which beats `Healthy`. On a tie the earlier item wins.
 * An empty list gives `Healthy`.
 *
 * @param hs - The health values to compare.
 * @returns One of the values in `hs`, or `{ $type: "Healthy" }` if `hs` is empty.
 */
const worst = (hs: ServiceHealth[]): ServiceHealth =>
  hs.reduce(
    (a, b) => (RANK[b.$type] > RANK[a.$type] ? b : a),
    hs[0] ?? { $type: "Healthy" },
  );

/**
 * Builds the seven stage cards, in pipeline order, from one overview.
 *
 * @remarks
 * The order is Machines, Gateways, Plant Kafka, MirrorMaker 2, Central Kafka, Consumers, Database.
 * MirrorMaker 2 has no health in the API, so its badge comes from `mirrorHealth`.
 *
 * @param o - The overview from `GET /api/overview`.
 * @param nowMs - The current time in epoch milliseconds, used for "newest N s ago".
 * @returns The stages as plain data. Drawing is left to `PipelineStrip`.
 */
function stages(o: Overview, nowMs: number): Stage[] {
  // Down tiles keep their last rates: count them only when nothing is live, and then mark the stage stale.
  const gwStale =
    o.gateways.length > 0 && o.gateways.every((g) => isStale(g.health));
  // Names of the down gateways, for the red detail line.
  const gwDown = o.gateways.filter((g) => isStale(g.health)).map((g) => g.shop);
  // The same rule for the consumers.
  const csStale =
    o.consumers.length > 0 && o.consumers.every((c) => isStale(c.health));
  const csDown = o.consumers
    .filter((c) => isStale(c.health))
    .map((c) => c.consumerId);
  // True only when there is at least one gateway and every one is paused.
  const allPaused = o.gateways.length > 0 && o.gateways.every((g) => g.paused);
  // Rows waiting in all gateway buffers.
  const buffered = o.gateways.reduce(
    (n, g) => n + g.bufferedReadings + g.bufferedAlarms,
    0,
  );
  // Age of the oldest unsent row over all gateways, in milliseconds; 0 when there are no gateways.
  const oldestMs = Math.max(0, ...o.gateways.map((g) => g.oldestUnsentAgeMs));
  // Age text for a newest-record time; null means no record has been seen.
  const newest = (t: string | null) =>
    t === null ? "no data" : agoText(t, nowMs);
  return [
    {
      name: "Machines",
      health: null,
      fallbackLabel: allPaused ? "Paused" : "Generating",
      // Paused machines produce nothing, so show 0 rather than the last live rate.
      rate: allPaused ? 0 : o.liveRate,
      rateUnit: "readings/s",
      backlog: "",
      freshness: "",
    },
    {
      name: `Gateways ×${o.gateways.length}`,
      health: worst(o.gateways.map((g) => g.health)),
      // Live gateways only; if all are down, show their last rates and mark the stage stale.
      rate: o.gateways
        .filter((g) => gwStale || !isStale(g.health))
        .reduce((n, g) => n + g.sentPerSecond, 0),
      stale: gwStale,
      detail: gwDown.length > 0 ? `down: ${gwDown.join(", ")}` : undefined,
      rateUnit: "sent/s",
      backlog: `buffer ${formatInt(buffered)} · ${formatSeconds(oldestMs / 1000)} oldest`,
      freshness: "",
    },
    {
      name: "Plant Kafka",
      health: o.plantKafka.health,
      rate: null,
      rateUnit: "",
      backlog: `${formatInt(o.plantKafka.endOffsetTotal)} records`,
      freshness: `newest ${newest(o.plantKafka.newestDeviceTime)}`,
    },
    {
      name: "MirrorMaker 2",
      health: mirrorHealth(o.mirrorLagSeconds),
      rate: null,
      rateUnit: "",
      backlog: `mirror lag ${formatSeconds(o.mirrorLagSeconds)}`,
      freshness: "",
    },
    {
      name: "Central Kafka",
      health: o.centralKafka.health,
      rate: null,
      rateUnit: "",
      backlog: `${formatInt(o.centralKafka.endOffsetTotal)} records`,
      freshness: `newest ${newest(o.centralKafka.newestDeviceTime)}`,
    },
    {
      name: `Consumers ×${o.consumers.length}`,
      health: worst(o.consumers.map((c) => c.health)),
      // Same rule as for the gateways.
      rate: o.consumers
        .filter((c) => csStale || !isStale(c.health))
        .reduce((n, c) => n + c.insertedPerSecond, 0),
      stale: csStale,
      detail: csDown.length > 0 ? `down: ${csDown.join(", ")}` : undefined,
      rateUnit: "inserted/s",
      backlog: `lag ${formatInt(o.consumerLag)}`,
      freshness: "",
    },
    {
      name: "Database",
      health: o.database.health,
      rate: null,
      rateUnit: "",
      backlog: `${formatInt(o.database.stored)} rows`,
      freshness: `fresh ${formatSeconds(o.database.freshnessSeconds)}`,
    },
  ];
}

/**
 * Draws the pipeline as a row of stage cards inside a panel.
 *
 * @remarks
 * Each card has a name, a health badge (or a plain label for Machines), an optional red detail line,
 * a rate, and up to two detail lines. A card whose sources are down gets a red border, and its
 * numbers are grey with the note "last known rate, not live". The grid is one column on narrow
 * screens and goes to 2, 4 and 7 columns at 600, 900 and 1300 px.
 *
 * @param props - The component props.
 * @param props.overview - The latest overview, or `undefined` before the first answer. Without it the panel shows "Waiting for the API…".
 * @param props.nowMs - The current time in epoch milliseconds, passed to the badges and the age texts.
 * @returns The panel element.
 * @see GET /api/overview in Program.cs
 */
export function PipelineStrip({
  overview,
  nowMs,
}: {
  overview: Overview | undefined;
  nowMs: number;
}) {
  if (!overview)
    return (
      <Panel title="Plant to central">
        <p className="text-muted">Waiting for the API…</p>
      </Panel>
    );
  return (
    <Panel title="Plant to central">
      <ol className="grid grid-cols-1 gap-3 min-[600px]:grid-cols-2 min-[900px]:grid-cols-4 min-[1300px]:grid-cols-7">
        {stages(overview, nowMs).map((s) => {
          // Down when the stage says its rate is stale; otherwise when its own health is Down.
          const down = s.stale ?? (s.health !== null && isStale(s.health));
          return (
            <li
              key={s.name}
              className={`rounded-md border bg-raised p-3 ${down ? "border-bad" : "border-line"}`}
            >
              <div className="mb-1 font-semibold">{s.name}</div>
              {/* A badge when the stage has a health; the plain label for Machines. */}
              {s.health ? (
                <HealthBadge health={s.health} nowMs={nowMs} />
              ) : (
                <span className="text-sm font-medium text-fg/80">
                  {s.fallbackLabel}
                </span>
              )}
              {s.detail && (
                <div className="text-sm text-bad-fg">{s.detail}</div>
              )}
              {s.rate !== null && (
                <div className={`mt-2 ${down ? "text-muted" : ""}`}>
                  <span className="text-xl font-semibold">
                    {formatInt(s.rate)}
                  </span>{" "}
                  <span className="text-sm text-muted">{s.rateUnit}</span>
                </div>
              )}
              {/* Warns that a grey number is not live. */}
              {down && s.rate !== null && (
                <div className="text-xs text-muted">
                  last known rate, not live
                </div>
              )}
              {s.backlog && (
                <div
                  className={`text-sm ${down ? "text-muted" : "text-fg/80"}`}
                >
                  {s.backlog}
                </div>
              )}
              {s.freshness && (
                <div
                  className={`text-sm ${down ? "text-muted" : "text-fg/80"}`}
                >
                  {s.freshness}
                </div>
              )}
            </li>
          );
        })}
      </ol>
    </Panel>
  );
}
