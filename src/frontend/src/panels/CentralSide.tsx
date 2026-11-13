/**
 * The "Kafka, writers and database" panel: the cluster, consumer and database tiles of the overview.
 *
 * Where it sits: `App.tsx` renders it with the latest `Overview` from `useOverview` in `api.ts` and
 * the server-based "now". It is presentation only and does not call the API. The figures are the
 * fields of `ClusterTile`, `ConsumerTile` and `DatabaseTile` (`ApiModels.cs` in
 * `EventDrivenDesign.Contracts`), from `GET /api/overview`.
 *
 * Exports: `CentralSide`.
 *
 * @module
 */
import type { ReactNode } from "react";
import type { Overview, ServiceHealth } from "../api";
import { newestText } from "../derive";
import { formatInt, formatSeconds, isStale } from "../format";
import { HealthBadge, Panel } from "./ui";

/**
 * One line of the panel: a name with its health badge, and a detail line under them.
 *
 * @param props - The component props.
 * @param props.name - What the line is about, such as "Central Kafka".
 * @param props.mono - True when the name is an id, drawn in the mono font.
 * @param props.health - The health to show in the badge.
 * @param props.nowMs - The current time in epoch milliseconds, for "last seen".
 * @param props.children - The detail line. It is drawn grey when the source is Down, as its figures are then old.
 * @returns A list item.
 */
function Row({
  name,
  mono,
  health,
  nowMs,
  children,
}: {
  name: string;
  mono?: boolean;
  health: ServiceHealth;
  nowMs: number;
  children: ReactNode;
}) {
  return (
    <li className="border-t border-line py-2 first:border-t-0 first:pt-0">
      <div className="flex flex-wrap items-start justify-between gap-x-3">
        <span className={`font-medium ${mono ? "font-mono" : ""}`}>{name}</span>
        <HealthBadge health={health} nowMs={nowMs} />
      </div>
      <div
        className={`text-sm ${isStale(health) ? "text-muted" : "text-fg/80"}`}
      >
        {children}
      </div>
    </li>
  );
}

/**
 * A small heading over one group of lines.
 *
 * @param props - The component props.
 * @param props.children - The heading text.
 * @returns An `<h3>`.
 */
const Group = ({ children }: { children: ReactNode }) => (
  <h3 className="mb-1.5 text-xs font-medium text-muted">{children}</h3>
);

/**
 * Shows the two Kafka clusters, the store-writer consumers with the consumer lag, and the database.
 *
 * @remarks
 * Clusters: records written and the age of the newest record. Store writers: rows inserted per
 * second, records received and rows inserted. Database: rows stored and freshness.
 *
 * @param props - The component props.
 * @param props.overview - The latest overview, or `undefined` before the first answer. Without it the panel shows "Waiting for the API…".
 * @param props.nowMs - The current time in epoch milliseconds, for "newest N s ago" and "last seen".
 * @returns The panel element.
 * @see GET /api/overview in Program.cs
 */
export function CentralSide({
  overview,
  nowMs,
}: {
  overview: Overview | undefined;
  nowMs: number;
}) {
  const title = "Kafka, writers and database";
  if (!overview)
    return (
      <Panel title={title}>
        <p className="text-muted">Waiting for the API…</p>
      </Panel>
    );
  const o = overview;
  return (
    <Panel title={title}>
      <div className="space-y-4">
        <div>
          <Group>Kafka clusters</Group>
          <ul>
            <Row name="Plant Kafka" health={o.plantKafka.health} nowMs={nowMs}>
              {formatInt(o.plantKafka.endOffsetTotal)} records · newest{" "}
              {newestText(o.plantKafka.newestDeviceTime, nowMs)}
            </Row>
            <Row
              name="Central Kafka"
              health={o.centralKafka.health}
              nowMs={nowMs}
            >
              {formatInt(o.centralKafka.endOffsetTotal)} records · newest{" "}
              {newestText(o.centralKafka.newestDeviceTime, nowMs)}
            </Row>
          </ul>
        </div>
        <div>
          <Group>
            Store writers · consumer lag {formatInt(o.consumerLag)} records
          </Group>
          <ul>
            {o.consumers.map((c) => (
              <Row
                key={c.consumerId}
                name={c.consumerId}
                mono
                health={c.health}
                nowMs={nowMs}
              >
                {formatInt(c.insertedPerSecond)} inserted/s ·{" "}
                {formatInt(c.received)} received · {formatInt(c.inserted)}{" "}
                inserted
              </Row>
            ))}
          </ul>
        </div>
        <div>
          <Group>Database</Group>
          <ul>
            <Row name="PostgreSQL" health={o.database.health} nowMs={nowMs}>
              {formatInt(o.database.stored)} rows · fresh{" "}
              {formatSeconds(o.database.freshnessSeconds)}
            </Row>
          </ul>
        </div>
      </div>
    </Panel>
  );
}
