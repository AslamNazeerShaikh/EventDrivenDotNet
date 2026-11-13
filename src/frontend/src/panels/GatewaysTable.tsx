/**
 * The Gateways panel: one table row per shop, built from the overview's gateway tiles.
 *
 * Where it sits: `App.tsx` renders it with the latest `Overview` from `useOverview` in `api.ts` and
 * the server-based "now". It is presentation only and does not call the API. Every column is a field
 * of `GatewayTile` (`ApiModels.cs` in `EventDrivenDesign.Contracts`), from `GET /api/overview`.
 *
 * Exports: `GatewaysTable`.
 *
 * @module
 */
import type { Overview } from "../api";
import { formatInt, formatSeconds, instanceLabel, isStale } from "../format";
import { HealthBadge, Panel } from "./ui";

/**
 * Tailwind classes of a right-aligned number column header. Its words may wrap, so the table fits a
 * narrower panel before it has to scroll.
 */
const num =
  "px-2 text-right align-bottom leading-tight font-medium whitespace-normal";

/**
 * Draws the gateway table: shop, active instance, health, state, sent per second, buffered
 * readings, buffered alarms, oldest unsent age and generated count.
 *
 * @remarks
 * The table scrolls sideways inside the panel when the panel is narrow, never the page.
 * A Down gateway's numbers are its last known values, so they are drawn grey, as in "Buffers and lag".
 *
 * @param props - The component props.
 * @param props.overview - The latest overview, or `undefined` before the first answer. Without it the panel shows "Waiting for the API…".
 * @param props.nowMs - The current time in epoch milliseconds, used by the health badges for "last seen".
 * @returns The panel element.
 * @see GET /api/overview in Program.cs
 */
export function GatewaysTable({
  overview,
  nowMs,
}: {
  overview: Overview | undefined;
  nowMs: number;
}) {
  if (!overview)
    return (
      <Panel title="Gateways">
        <p className="text-muted">Waiting for the API…</p>
      </Panel>
    );
  return (
    <Panel title="Gateways">
      <div className="overflow-x-auto">
        <table className="w-full whitespace-nowrap text-sm">
          <caption className="sr-only">
            One row per shop: its active gateway, health, rate and buffers
          </caption>
          <thead className="text-left align-bottom text-xs text-muted">
            <tr>
              <th scope="col" className="py-1.5 pr-2 font-medium">
                Shop
              </th>
              <th scope="col" className="px-2 font-medium">
                Active
              </th>
              <th scope="col" className="px-2 font-medium">
                Health
              </th>
              <th scope="col" className="px-2 font-medium">
                State
              </th>
              <th scope="col" className={num}>
                Sent/s
              </th>
              <th scope="col" className={num}>
                Buffered readings
              </th>
              <th scope="col" className={num}>
                Buffered alarms
              </th>
              <th scope="col" className={num}>
                Oldest unsent
              </th>
              <th
                scope="col"
                className="pl-2 text-right align-bottom font-medium"
              >
                Generated
              </th>
            </tr>
          </thead>
          <tbody>
            {overview.gateways.map((g) => {
              // A Down gateway still carries its last numbers; draw them grey.
              const dim = isStale(g.health) ? "text-muted" : "";
              return (
                <tr key={g.shop} className="border-t border-line">
                  <td className="py-1.5 pr-2 font-mono font-medium">
                    {g.shop}
                  </td>
                  <td className="px-2 font-mono">
                    {instanceLabel(g.instance)}
                  </td>
                  <td className="px-2">
                    <HealthBadge health={g.health} nowMs={nowMs} />
                  </td>
                  <td className="px-2">{g.paused ? "Paused" : "Running"}</td>
                  <td className={`px-2 text-right ${dim}`}>
                    {formatInt(g.sentPerSecond)}
                  </td>
                  <td className={`px-2 text-right ${dim}`}>
                    {formatInt(g.bufferedReadings)}
                  </td>
                  <td className={`px-2 text-right ${dim}`}>
                    {formatInt(g.bufferedAlarms)}
                  </td>
                  <td className={`px-2 text-right ${dim}`}>
                    {/* The API sends the age in milliseconds; formatSeconds wants seconds. */}
                    {formatSeconds(g.oldestUnsentAgeMs / 1000)}
                  </td>
                  <td className={`pl-2 text-right ${dim}`}>
                    {formatInt(g.generated)}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </Panel>
  );
}
