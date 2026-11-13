/**
 * The live throughput panel: a streaming area chart of the live rate and the drain rate.
 *
 * Where it sits: `App.tsx` keeps the samples, one per overview poll, with `addSample` in `derive.ts`,
 * and renders this panel with them and the latest `Overview`. The current values and the backlog
 * status are printed as text above the chart, so the panel reads without the colours. It uses
 * Recharts for drawing and does not call the API. The rates come from `liveRate` and `drainRate`
 * in `GET /api/overview` (`Overview` in `ApiModels.cs`).
 *
 * Exports: `Throughput`.
 *
 * @module
 */
import {
  Area,
  AreaChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import type { Overview } from "../api";
import {
  drainStatus,
  THROUGHPUT_WINDOW_MS,
  utcTime,
  type RateSample,
} from "../derive";
import { formatInt } from "../format";
import { Panel, Stat, StatusText } from "./ui";

/**
 * Shows the current live and drain rates, the backlog status, and the rates over the last minutes.
 *
 * @remarks
 * The live rate is the accent line with a light fill; the drain rate is a dashed grey line, so the
 * two differ by more than colour. The chart starts once two samples are in. Samples are taken on
 * this page, so the chart starts empty on each page load and pauses while the API is unreachable.
 *
 * @param props - The component props.
 * @param props.samples - The samples, oldest first, at most `THROUGHPUT_WINDOW_MS` long.
 * @param props.overview - The latest overview, or `undefined` before the first answer. Without it the panel shows "Waiting for the API…".
 * @returns The panel element.
 * @see addSample in `derive.ts`
 */
export function Throughput({
  samples,
  overview,
}: {
  samples: readonly RateSample[];
  overview: Overview | undefined;
}) {
  const title = `Live throughput, ${THROUGHPUT_WINDOW_MS / 60_000} min (UTC)`;
  if (!overview)
    return (
      <Panel title={title}>
        <p className="text-muted">Waiting for the API…</p>
      </Panel>
    );
  return (
    <Panel title={title}>
      <dl className="mb-3 grid grid-cols-3 gap-3">
        <Stat
          label="Live rate, readings/s"
          value={formatInt(overview.liveRate)}
        />
        <Stat
          label="Drain rate, rows/s"
          value={formatInt(overview.drainRate)}
        />
        <Stat
          label="Backlog"
          value={
            <span className="text-sm">
              <StatusText status={drainStatus(overview.drainEtaSeconds)} />
            </span>
          }
        />
      </dl>
      {samples.length < 2 ? (
        <p className="text-muted">Collecting samples…</p>
      ) : (
        <div
          className="h-44"
          role="img"
          aria-label="Area chart of the live rate and the drain rate over the last few minutes"
        >
          <ResponsiveContainer width="100%" height="100%">
            <AreaChart
              data={[...samples]}
              margin={{ top: 4, right: 8, left: 0, bottom: 0 }}
            >
              <CartesianGrid stroke="var(--color-line)" vertical={false} />
              <XAxis
                dataKey="t"
                type="number"
                scale="time"
                domain={["dataMin", "dataMax"]}
                tickFormatter={(t: number) => utcTime(t)}
                stroke="var(--color-muted)"
                tick={{ fontSize: 12 }}
                minTickGap={48}
              />
              <YAxis
                stroke="var(--color-muted)"
                tick={{ fontSize: 12 }}
                width={48}
                tickFormatter={formatInt}
              />
              <Tooltip
                contentStyle={{
                  background: "var(--color-panel)",
                  border: "1px solid var(--color-line)",
                  borderRadius: 6,
                }}
                labelFormatter={(l) => utcTime(Number(l))}
                formatter={(v, name) => [formatInt(Number(v)), name]}
              />
              <Area
                dataKey="live"
                name="Live rate"
                type="monotone"
                stroke="var(--color-info)"
                strokeWidth={2}
                fill="var(--color-info)"
                fillOpacity={0.12}
                isAnimationActive={false}
              />
              <Area
                dataKey="drain"
                name="Drain rate"
                type="monotone"
                stroke="var(--color-muted)"
                strokeWidth={2}
                strokeDasharray="5 4"
                fill="none"
                isAnimationActive={false}
              />
            </AreaChart>
          </ResponsiveContainer>
        </div>
      )}
      <p className="mt-2 text-sm text-muted">
        <span className="text-info">Solid blue: live rate.</span> Dashed grey:
        drain rate. Sampled once a second on this page.
      </p>
    </Panel>
  );
}
