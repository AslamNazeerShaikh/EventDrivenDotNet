/**
 * The "Buffers and lag" panel: how much each shop's gateway has buffered, and the lag figures.
 *
 * Where it sits: `App.tsx` renders it with the latest `Overview` from `useOverview` in `api.ts`.
 * It is presentation only and draws from the props. The numbers come from `GET /api/overview`;
 * the types mirror `GatewayTile` and `Overview` in `ApiModels.cs` (`EventDrivenDesign.Contracts`).
 *
 * Exports: `BuffersLag`.
 *
 * @module
 */
import type { Overview } from "../api";
import {
  formatEta,
  formatInt,
  formatSeconds,
  instanceLabel,
  isStale,
} from "../format";
import { Panel, Stat } from "./ui";

/**
 * One row per shop with a bar for its buffered rows, then mirror lag, consumer lag and drain ETA.
 *
 * @remarks
 * The row text reads `rows · oldest age`, with " (last seen)" when the gateway is Down
 * and " (paused)" when it is paused. The bars share one scale: the deepest shop fills the bar.
 * A Down gateway's bar is drawn grey, because its numbers are its last known values.
 *
 * @param props - The component props.
 * @param props.overview - The latest overview, or `undefined` before the first answer. Without it the panel shows "Waiting for the API…".
 * @returns The panel element.
 * @see GET /api/overview in Program.cs
 */
export function BuffersLag({ overview }: { overview: Overview | undefined }) {
  if (!overview)
    return (
      <Panel title="Buffers and lag">
        <p className="text-muted">Waiting for the API…</p>
      </Panel>
    );
  // Buffer depth of a gateway tile: readings plus alarms still waiting to be sent.
  const depth = (g: Overview["gateways"][number]) =>
    g.bufferedReadings + g.bufferedAlarms;
  // Scale for the bars: the deepest buffer. The 1 keeps the width calculation from dividing by zero when every buffer is empty.
  const max = Math.max(1, ...overview.gateways.map(depth));
  return (
    <Panel title="Buffers and lag">
      <ul className="space-y-2">
        {overview.gateways.map((g) => {
          // A Down gateway still carries its last numbers; show them grey and label them "last seen".
          const stale = isStale(g.health);
          return (
            <li
              key={g.shop}
              className="grid grid-cols-[6rem_1fr_auto] items-center gap-3"
            >
              <span className="font-mono font-semibold">
                {g.shop} · {instanceLabel(g.instance)}
              </span>
              <div
                role="meter"
                aria-label={`${g.shop} buffered`}
                aria-valuemin={0}
                aria-valuemax={max}
                aria-valuenow={depth(g)}
                className="h-3 overflow-hidden rounded bg-ink"
              >
                <div
                  className={`h-full ${stale ? "bg-slate-500" : "bg-sky-400"}`}
                  style={{ width: `${(depth(g) / max) * 100}%` }}
                />
              </div>
              <span
                className={`min-w-40 text-right text-sm tabular-nums ${stale ? "text-slate-500" : ""}`}
              >
                {formatInt(depth(g))} ·{" "}
                {/* The API sends the oldest age in milliseconds; formatSeconds wants seconds. */}
                {formatSeconds(g.oldestUnsentAgeMs / 1000)}
                {stale ? " (last seen)" : g.paused ? " (paused)" : ""}
              </span>
            </li>
          );
        })}
      </ul>
      <dl className="mt-4 grid grid-cols-3 gap-3 border-t border-line pt-3">
        <Stat
          label="Mirror lag"
          value={formatSeconds(overview.mirrorLagSeconds)}
        />
        <Stat label="Consumer lag" value={formatInt(overview.consumerLag)} />
        <Stat label="Drain ETA" value={formatEta(overview.drainEtaSeconds)} />
      </dl>
    </Panel>
  );
}
