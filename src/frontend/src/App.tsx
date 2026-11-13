/**
 * The root component of the dashboard: polls the API and lays out the panels.
 *
 * Flow: `main.tsx` renders `App`. `App` calls the hooks in `api.ts` (all polling once a second),
 * and passes each result to a panel in `src/panels/` as props. The panels do not call the API
 * themselves, except `AlarmsPanel`, which uses the command hooks (pause, resume, inject fault).
 * Each panel sits in its own `ErrorBoundary`, so a render failure in one leaves the others on screen.
 * `App` also keeps the live throughput samples, one per overview answer, with `addSample` in `derive.ts`.
 *
 * The default export is the `App` component. It also holds two private helpers, `note` and `Slot`.
 * It relates to the whole read side of the API: `/api/overview`, `/api/reconciliation`,
 * `/api/chart`, `/api/alarms` and `/api/containers` (see `Program.cs` in `EventDrivenDesign.Api`).
 *
 * @module
 */
import { useEffect, useState, type ReactNode } from "react";
import {
  ApiError,
  useAlarms,
  useChart,
  useContainers,
  useFullCheck,
  useOverview,
  useReconciliation,
} from "./api";
import {
  addSample,
  apiState,
  plantStatus,
  utcTime,
  type RateSample,
} from "./derive";
import { ErrorBoundary } from "./ErrorBoundary";
import { useNow } from "./useNow";
import { agoText, panelNote } from "./format";
import { AlarmMixPanel } from "./panels/AlarmMixPanel";
import { AlarmsPanel } from "./panels/AlarmsPanel";
import { BuffersLag } from "./panels/BuffersLag";
import { CentralSide } from "./panels/CentralSide";
import { ContainersPanel } from "./panels/ContainersPanel";
import { GatewaysTable } from "./panels/GatewaysTable";
import { KpiStrip } from "./panels/KpiStrip";
import { PipelineStrip } from "./panels/PipelineStrip";
import { ProofPanel } from "./panels/ProofPanel";
import { ReadingsChart } from "./panels/ReadingsChart";
import { Throughput } from "./panels/Throughput";
import { StatusText } from "./panels/ui";

/**
 * Turns a query's state into the note a panel shows when its request failed.
 *
 * @param q - A React Query result, or any object with these three fields.
 * @param q.isError - True when the latest request failed.
 * @param q.data - The last good data, or `undefined` if there never was any.
 * @param q.error - The error. Only an `ApiError` contributes its message as the reason.
 * @returns The note text, or `undefined` when the query is fine. See `panelNote` in `format.ts`.
 */
const note = (q: { isError: boolean; data: unknown; error: unknown }) =>
  panelNote(
    q.isError,
    q.data !== undefined,
    q.error instanceof ApiError ? q.error.message : undefined,
  );

/**
 * One cell of the panel grid: its column span, and an error boundary around the panel inside.
 *
 * @remarks
 * `min-w-0` lets the cell shrink below its content's width, so a wide table scrolls inside its
 * panel instead of widening the page.
 *
 * @param props - The component props.
 * @param props.span - Tailwind column-span classes, such as `"xl:col-span-4"`.
 * @param props.label - The boundary's label, used in its fallback text.
 * @param props.resetKey - The boundary's reset key: the `dataUpdatedAt` of the panel's query.
 * @param props.children - The panel.
 * @returns The grid cell.
 */
function Slot({
  span,
  label,
  resetKey,
  children,
}: {
  span: string;
  label: string;
  resetKey: unknown;
  children: ReactNode;
}) {
  return (
    <div className={`min-w-0 ${span}`}>
      <ErrorBoundary label={label} resetKey={resetKey}>
        {children}
      </ErrorBoundary>
    </div>
  );
}

/**
 * The whole page: a sticky header with the plant and API status, an error banner when the API is
 * unreachable, the KPI strip, the pipeline strip, the full-width containers panel, and three rows of panels.
 *
 * @remarks
 * It takes no props. The page uses the full width. The panel grid has one column on phones, two
 * from `md`, and twelve from `xl`, where each row holds three panels (the last row two):
 * Buffers and lag, Readings, Live throughput; Proof, Gateways, Kafka writers and database;
 * Alarms, Alarm mix. `overview.dataUpdatedAt` and the other `dataUpdatedAt` values are the time of
 * the last successful fetch, so each boundary retries its panel when fresh data arrives.
 *
 * @returns The header and the page's `<main>` element.
 */
export default function App() {
  // The four polled reads, each refreshed every second (see `poll` in api.ts).
  const overview = useOverview();
  const proof = useReconciliation();
  const chart = useChart();
  const alarms = useAlarms();
  // The project's containers, polled every 5 s (the API's own refresh rate).
  const containers = useContainers();
  // The full duplicate check; it starts itself from the light proof once the plant is paused and drained.
  const full = useFullCheck(proof.data);
  // Local clock that ticks every second, so "N s ago" keeps counting when no data arrives.
  const clock = useNow();
  // Server time keeps "last seen" and the chart window in step with device times, and freezes with the data.
  // Before the first answer there is no server time, so the device clock stands in.
  const nowMs = overview.data ? Date.parse(overview.data.at) : Date.now();
  // True only when there is at least one gateway and all are paused; undefined until the overview arrives.
  const paused = overview.data
    ? overview.data.gateways.length > 0 &&
      overview.data.gateways.every((g) => g.paused)
    : undefined;
  // Live and drain rate samples for the throughput chart, one per new overview answer.
  const [samples, setSamples] = useState<readonly RateSample[]>([]);
  useEffect(() => {
    const o = overview.data;
    if (o)
      setSamples((prev) =>
        addSample(prev, {
          t: Date.parse(o.at),
          live: o.liveRate,
          drain: o.drainRate,
        }),
      );
  }, [overview.data]);

  return (
    <>
      <header className="sticky top-0 z-20 border-b border-line bg-ink">
        <div className="flex flex-wrap items-center justify-between gap-x-6 gap-y-2 px-4 py-3 sm:px-6">
          <div>
            <h1 className="text-lg leading-tight font-semibold">
              Plant to central
            </h1>
            <p className="text-sm text-muted">
              Control room, updates every second
            </p>
          </div>
          <dl className="flex flex-wrap items-center gap-x-6 gap-y-1 text-sm">
            <div className="flex items-center gap-2">
              <dt className="text-muted">Plant</dt>
              <dd>
                <StatusText status={plantStatus(paused)} />
              </dd>
            </div>
            <div className="flex items-center gap-2">
              <dt className="text-muted">API</dt>
              <dd>
                <StatusText
                  status={apiState(
                    overview.isError,
                    overview.data !== undefined,
                  )}
                />
              </dd>
            </div>
            <div className="flex items-center gap-2">
              <dt className="text-muted">Updated</dt>
              <dd>
                {overview.data ? (
                  <>
                    <span className="font-mono">
                      {utcTime(overview.data.at)} UTC
                    </span>{" "}
                    <span className="text-muted">
                      ({agoText(overview.data.at, clock)})
                    </span>
                  </>
                ) : (
                  "—"
                )}
              </dd>
            </div>
          </dl>
        </div>
        {/* Shown while the latest overview request has failed. With old data it gives that data's time (UTC) and its age by the local clock. */}
        {overview.isError && (
          <p
            role="alert"
            className="border-t border-bad bg-bad/15 px-4 py-2 font-semibold text-fg sm:px-6"
          >
            API unreachable.{" "}
            {overview.data
              ? `Showing data from ${new Date(overview.data.at).toLocaleTimeString("en-GB", { hour12: false, timeZone: "UTC" })} UTC (${agoText(overview.data.at, clock)}). Retrying every second.`
              : "Retrying every second."}
          </p>
        )}
      </header>
      <main className="space-y-3 px-4 py-4 sm:px-6">
        <ErrorBoundary
          label="Key figures"
          resetKey={`${overview.dataUpdatedAt}-${proof.dataUpdatedAt}`}
        >
          <KpiStrip overview={overview.data} proof={proof.data} />
        </ErrorBoundary>
        <ErrorBoundary label="Pipeline" resetKey={overview.dataUpdatedAt}>
          <PipelineStrip overview={overview.data} nowMs={nowMs} />
        </ErrorBoundary>
        <Slot span="" label="Containers" resetKey={containers.dataUpdatedAt}>
          <ContainersPanel view={containers.data} note={note(containers)} />
        </Slot>
        <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-12">
          <Slot
            span="xl:col-span-4"
            label="Buffers and lag"
            resetKey={overview.dataUpdatedAt}
          >
            <BuffersLag overview={overview.data} />
          </Slot>
          <Slot
            span="xl:col-span-4"
            label="Chart"
            resetKey={chart.dataUpdatedAt}
          >
            <ReadingsChart
              chart={chart.data}
              nowMs={nowMs}
              note={note(chart)}
            />
          </Slot>
          <Slot
            span="md:col-span-2 xl:col-span-4"
            label="Live throughput"
            resetKey={overview.dataUpdatedAt}
          >
            <Throughput samples={samples} overview={overview.data} />
          </Slot>
          <Slot
            span="xl:col-span-4"
            label="Proof"
            resetKey={proof.dataUpdatedAt}
          >
            <ProofPanel
              proof={proof.data}
              full={full}
              nowMs={clock}
              note={note(proof)}
            />
          </Slot>
          <Slot
            span="xl:col-span-5"
            label="Gateways"
            resetKey={overview.dataUpdatedAt}
          >
            <GatewaysTable overview={overview.data} nowMs={nowMs} />
          </Slot>
          <Slot
            span="md:col-span-2 xl:col-span-3"
            label="Kafka, writers and database"
            resetKey={overview.dataUpdatedAt}
          >
            <CentralSide overview={overview.data} nowMs={nowMs} />
          </Slot>
          <Slot
            span="md:col-span-2 xl:col-span-8"
            label="Alarms"
            resetKey={alarms.dataUpdatedAt}
          >
            <AlarmsPanel
              alarms={alarms.data}
              paused={paused}
              note={note(alarms)}
            />
          </Slot>
          <Slot
            span="md:col-span-2 xl:col-span-4"
            label="Alarm mix"
            resetKey={alarms.dataUpdatedAt}
          >
            <AlarmMixPanel alarms={alarms.data} note={note(alarms)} />
          </Slot>
        </div>
      </main>
    </>
  );
}
