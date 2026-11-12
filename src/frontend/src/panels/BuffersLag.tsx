import type { Overview } from "../api";
import { formatEta, formatInt, formatSeconds, isStale } from "../format";
import { Panel, Stat } from "./ui";

export function BuffersLag({ overview }: { overview: Overview | undefined }) {
  if (!overview) return <Panel title="Buffers and lag"><p className="text-muted">Waiting for the API…</p></Panel>;
  const depth = (g: Overview["gateways"][number]) => g.bufferedReadings + g.bufferedAlarms;
  const max = Math.max(1, ...overview.gateways.map(depth));
  return (
    <Panel title="Buffers and lag">
      <ul className="space-y-2">
        {overview.gateways.map((g) => {
          const stale = isStale(g.health);
          return (
            <li key={g.shop} className="grid grid-cols-[3rem_1fr_auto] items-center gap-3">
              <span className="font-mono font-semibold">{g.shop}</span>
              <div
                role="meter"
                aria-label={`${g.shop} buffered`}
                aria-valuemin={0}
                aria-valuemax={max}
                aria-valuenow={depth(g)}
                className="h-3 overflow-hidden rounded bg-ink"
              >
                <div className={`h-full ${stale ? "bg-slate-500" : "bg-sky-400"}`} style={{ width: `${(depth(g) / max) * 100}%` }} />
              </div>
              <span className={`min-w-40 text-right text-sm tabular-nums ${stale ? "text-slate-500" : ""}`}>
                {formatInt(depth(g))} · {formatSeconds(g.oldestUnsentAgeMs / 1000)}
                {stale ? " (last seen)" : g.paused ? " (paused)" : ""}
              </span>
            </li>
          );
        })}
      </ul>
      <dl className="mt-4 grid grid-cols-3 gap-3 border-t border-line pt-3">
        <Stat label="Mirror lag" value={formatSeconds(overview.mirrorLagSeconds)} />
        <Stat label="Consumer lag" value={formatInt(overview.consumerLag)} />
        <Stat label="Drain ETA" value={formatEta(overview.drainEtaSeconds)} />
      </dl>
    </Panel>
  );
}
