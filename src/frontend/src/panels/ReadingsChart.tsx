import { useMemo } from "react";
import { Bar, BarChart, CartesianGrid, Cell, ReferenceArea, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import type { Chart } from "../api";
import { fillBuckets, formatInt } from "../format";
import { Panel } from "./ui";

const clock = (iso: string) => new Date(iso).toLocaleTimeString("en-GB", { hour12: false, timeZone: "UTC" });

export function ReadingsChart({ chart, nowMs, note }: { chart: Chart | undefined; nowMs: number; note?: string | undefined }) {
  const data = useMemo(() => (chart ? fillBuckets(chart.points, nowMs, 10, chart.watermark) : []), [chart, nowMs]);
  const firstProvisional = data.find((p) => p.provisional)?.bucketStart;
  const last = data[data.length - 1]?.bucketStart;
  return (
    <Panel title="Readings by device time, 10 min (UTC)" note={note}>
      {!chart ? (
        <p className="text-muted">Waiting for data…</p>
      ) : (
        <>
          <div className="h-56" role="img" aria-label="Bar chart of readings per 5 seconds over the last 10 minutes; gaps are zero">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={data} margin={{ top: 4, right: 8, left: 0, bottom: 0 }} barCategoryGap={0}>
                <CartesianGrid stroke="#2a3650" vertical={false} />
                <XAxis dataKey="bucketStart" tickFormatter={clock} stroke="#9fb0cc" minTickGap={48} />
                <YAxis stroke="#9fb0cc" width={48} tickFormatter={formatInt} />
                <Tooltip
                  contentStyle={{ background: "#121a2b", border: "1px solid #2a3650" }}
                  labelFormatter={(l) => clock(String(l))}
                  formatter={(v) => [formatInt(Number(v)), "readings"]}
                />
                {firstProvisional && last && <ReferenceArea x1={firstProvisional} x2={last} fill="#f5b942" fillOpacity={0.12} />}
                <Bar dataKey="readings" isAnimationActive={false}>
                  {data.map((p) => (
                    <Cell key={p.bucketStart} fill={p.provisional ? "#f5b942" : "#38bdf8"} />
                  ))}
                </Bar>
              </BarChart>
            </ResponsiveContainer>
          </div>
          <p className="mt-2 text-sm text-slate-300">
            <span className="text-sky-300">Blue: settled.</span> <span className="text-amber-300">Amber: provisional</span> (after the
            watermark{chart.watermark ? `, ${clock(chart.watermark)}` : ", not yet known"}). A hole is a gap in device time and fills in
            when the backlog drains.
          </p>
        </>
      )}
    </Panel>
  );
}
