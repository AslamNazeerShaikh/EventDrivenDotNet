/**
 * The readings chart panel: a bar chart of readings stored per 5 seconds of device time, last 10 minutes.
 *
 * Where it sits: `App.tsx` renders it with the `Chart` from `useChart` in `api.ts` and the
 * server-based "now". It uses Recharts for drawing and `fillBuckets` in `format.ts` to add the empty
 * buckets. It does not call the API. The data comes from `GET /api/chart?minutes=10`;
 * `Chart` and `ChartPoint` mirror the records of the same names in `ApiModels.cs`.
 *
 * Exports: `ReadingsChart`.
 *
 * @module
 */
import { useMemo } from "react";
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  ReferenceArea,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import type { Chart } from "../api";
import { fillBuckets, formatInt } from "../format";
import { Panel } from "./ui";

/**
 * Formats an ISO timestamp as a 24-hour clock time in UTC, for axis ticks and the tooltip.
 *
 * @param iso - An ISO-8601 timestamp.
 * @returns For example `"11:59:30"`.
 */
const clock = (iso: string) =>
  new Date(iso).toLocaleTimeString("en-GB", { hour12: false, timeZone: "UTC" });

/**
 * Bar chart of readings per 5-second bucket, with settled and provisional buckets in different colours.
 *
 * @remarks
 * Readings are placed by device time (when the machine produced them), so an outage shows as a hole
 * that fills in when the backlog drains. Blue bars are settled. Amber bars are provisional, which
 * means after the watermark. An amber band spans from the first provisional bucket to the last bucket.
 * The window is set at 10 minutes and matches the `minutes=10` that `useChart` asks for.
 *
 * @param props - The component props.
 * @param props.chart - The chart data, or `undefined` before the first answer. Without it the panel shows "Waiting for data…".
 * @param props.nowMs - The current time in epoch milliseconds. It sets where the 10-minute window ends.
 * @param props.note - Optional warning line for the panel, for example when the request fails.
 * @returns The panel element.
 * @see GET /api/chart in Program.cs
 */
export function ReadingsChart({
  chart,
  nowMs,
  note,
}: {
  chart: Chart | undefined;
  nowMs: number;
  note?: string | undefined;
}) {
  // Recomputed only when the chart data or the clock changes, not on every unrelated render.
  const data = useMemo(
    () => (chart ? fillBuckets(chart.points, nowMs, 10, chart.watermark) : []),
    [chart, nowMs],
  );
  // Start of the shaded band: the first provisional bucket (undefined if there is none).
  const firstProvisional = data.find((p) => p.provisional)?.bucketStart;
  // End of the shaded band: the newest bucket (undefined while `data` is empty).
  const last = data[data.length - 1]?.bucketStart;
  return (
    <Panel title="Readings by device time, 10 min (UTC)" note={note}>
      {!chart ? (
        <p className="text-muted">Waiting for data…</p>
      ) : (
        <>
          <div
            className="h-56"
            role="img"
            aria-label="Bar chart of readings per 5 seconds over the last 10 minutes; gaps are zero"
          >
            <ResponsiveContainer width="100%" height="100%">
              <BarChart
                data={data}
                margin={{ top: 4, right: 8, left: 0, bottom: 0 }}
                barCategoryGap={0}
              >
                <CartesianGrid stroke="#2a3650" vertical={false} />
                <XAxis
                  dataKey="bucketStart"
                  tickFormatter={clock}
                  stroke="#9fb0cc"
                  minTickGap={48}
                />
                <YAxis stroke="#9fb0cc" width={48} tickFormatter={formatInt} />
                <Tooltip
                  contentStyle={{
                    background: "#121a2b",
                    border: "1px solid #2a3650",
                  }}
                  labelFormatter={(l) => clock(String(l))}
                  formatter={(v) => [formatInt(Number(v)), "readings"]}
                />
                {/* The amber band is drawn only when there is a provisional bucket to start it. */}
                {firstProvisional && last && (
                  <ReferenceArea
                    x1={firstProvisional}
                    x2={last}
                    fill="#f5b942"
                    fillOpacity={0.12}
                  />
                )}
                <Bar dataKey="readings" isAnimationActive={false}>
                  {data.map((p) => (
                    <Cell
                      key={p.bucketStart}
                      fill={p.provisional ? "#f5b942" : "#38bdf8"}
                    />
                  ))}
                </Bar>
              </BarChart>
            </ResponsiveContainer>
          </div>
          <p className="mt-2 text-sm text-slate-300">
            <span className="text-sky-300">Blue: settled.</span>{" "}
            <span className="text-amber-300">Amber: provisional</span> (after
            the watermark
            {chart.watermark
              ? `, ${clock(chart.watermark)}`
              : ", not yet known"}
            ). A hole is a gap in device time and fills in when the backlog
            drains.
          </p>
        </>
      )}
    </Panel>
  );
}
