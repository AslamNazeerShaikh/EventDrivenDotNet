import type { Overview, ServiceHealth } from "../api";
import { agoText, formatInt, formatSeconds, isStale } from "../format";
import { HealthBadge, Panel } from "./ui";

interface Stage {
  name: string;
  health: ServiceHealth | null;
  fallbackLabel?: string;
  rate: number | null;
  rateUnit: string;
  backlog: string;
  freshness: string;
  stale?: boolean;
  detail?: string;
}

const RANK = { Healthy: 0, Degraded: 1, Down: 2 } as const;
const worst = (hs: ServiceHealth[]): ServiceHealth =>
  hs.reduce((a, b) => (RANK[b.$type] > RANK[a.$type] ? b : a), hs[0] ?? { $type: "Healthy" });

function stages(o: Overview, nowMs: number): Stage[] {
  // Down tiles keep their last rates: count them only when nothing is live, and then mark the stage stale.
  const gwStale = o.gateways.length > 0 && o.gateways.every((g) => isStale(g.health));
  const gwDown = o.gateways.filter((g) => isStale(g.health)).map((g) => g.shop);
  const csStale = o.consumers.length > 0 && o.consumers.every((c) => isStale(c.health));
  const csDown = o.consumers.filter((c) => isStale(c.health)).map((c) => c.consumerId);
  const allPaused = o.gateways.length > 0 && o.gateways.every((g) => g.paused);
  const buffered = o.gateways.reduce((n, g) => n + g.bufferedReadings + g.bufferedAlarms, 0);
  const oldestMs = Math.max(0, ...o.gateways.map((g) => g.oldestUnsentAgeMs));
  const mirror = o.mirrorLagSeconds;
  const newest = (t: string | null) => (t === null ? "no data" : agoText(t, nowMs));
  return [
    {
      name: "Machines",
      health: null,
      fallbackLabel: allPaused ? "Paused" : "Generating",
      rate: allPaused ? 0 : o.liveRate,
      rateUnit: "readings/s",
      backlog: "",
      freshness: "",
    },
    {
      name: `Gateways ×${o.gateways.length}`,
      health: worst(o.gateways.map((g) => g.health)),
      rate: o.gateways.filter((g) => gwStale || !isStale(g.health)).reduce((n, g) => n + g.sentPerSecond, 0),
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
      health: null,
      fallbackLabel: mirror === null ? "No data" : mirror <= 10 ? "Healthy" : "Lagging",
      rate: null,
      rateUnit: "",
      backlog: `mirror lag ${formatSeconds(mirror)}`,
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
      rate: o.consumers.filter((c) => csStale || !isStale(c.health)).reduce((n, c) => n + c.insertedPerSecond, 0),
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

export function PipelineStrip({ overview, nowMs }: { overview: Overview | undefined; nowMs: number }) {
  if (!overview) return <Panel title="Plant to central"><p className="text-muted">Waiting for the API…</p></Panel>;
  return (
    <Panel title="Plant to central">
      <ol className="grid grid-cols-1 gap-2 min-[600px]:grid-cols-2 min-[900px]:grid-cols-4 min-[1300px]:grid-cols-7">
        {stages(overview, nowMs).map((s) => {
          const down = s.stale ?? (s.health !== null && isStale(s.health));
          return (
            <li key={s.name} className={`rounded border bg-ink p-3 ${down ? "border-red-500" : "border-line"}`}>
              <div className="mb-1 font-semibold">{s.name}</div>
              {s.health ? (
                <HealthBadge health={s.health} nowMs={nowMs} />
              ) : (
                <span className="text-sm font-medium text-slate-300">{s.fallbackLabel}</span>
              )}
              {s.detail && <div className="text-sm text-red-300">{s.detail}</div>}
              {s.rate !== null && (
                <div className={`mt-2 tabular-nums ${down ? "text-slate-500" : ""}`}>
                  <span className="text-xl font-semibold">{formatInt(s.rate)}</span> {s.rateUnit}
                </div>
              )}
              {down && s.rate !== null && <div className="text-xs text-slate-400">last known rate, not live</div>}
              {s.backlog && <div className={`text-sm ${down ? "text-slate-500" : "text-slate-300"}`}>{s.backlog}</div>}
              {s.freshness && <div className={`text-sm ${down ? "text-slate-500" : "text-slate-300"}`}>{s.freshness}</div>}
            </li>
          );
        })}
      </ol>
    </Panel>
  );
}
