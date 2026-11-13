/**
 * The root component of the dashboard: polls the API and lays out the panels.
 *
 * Flow: `main.tsx` renders `App`. `App` calls the hooks in `api.ts` (all polling once a second),
 * and passes each result to a panel in `src/panels/` as props. The panels do not call the API
 * themselves, except `AlarmsPanel`, which uses the command hooks (pause, resume, inject fault).
 * Each panel sits in its own `ErrorBoundary`, so a render failure in one leaves the others on screen.
 *
 * The default export is the `App` component. It also holds one private helper, `note`.
 * It relates to the whole read side of the API: `/api/overview`, `/api/reconciliation`,
 * `/api/chart` and `/api/alarms` (see `Program.cs` in `EventDrivenDesign.Api`).
 *
 * @module
 */
import {
  ApiError,
  useAlarms,
  useChart,
  useFullCheck,
  useOverview,
  useReconciliation,
} from "./api";
import { ErrorBoundary } from "./ErrorBoundary";
import { useNow } from "./useNow";
import { agoText, panelNote } from "./format";
import { AlarmsPanel } from "./panels/AlarmsPanel";
import { BuffersLag } from "./panels/BuffersLag";
import { PipelineStrip } from "./panels/PipelineStrip";
import { ProofPanel } from "./panels/ProofPanel";
import { ReadingsChart } from "./panels/ReadingsChart";

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
 * The whole page: a title, an error banner when the API is unreachable, the pipeline strip,
 * and a grid of four panels.
 *
 * @remarks
 * It takes no props. The grid has one column, and two columns from 900 px wide.
 * `overview.dataUpdatedAt` and the other `dataUpdatedAt` values are the time of the last successful
 * fetch, so each boundary retries its panel when fresh data arrives.
 *
 * @returns The page's `<main>` element.
 */
export default function App() {
  // The four polled reads, each refreshed every second (see `poll` in api.ts).
  const overview = useOverview();
  const proof = useReconciliation();
  const chart = useChart();
  const alarms = useAlarms();
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

  return (
    <main className="mx-auto max-w-[1500px] space-y-4 p-4">
      <header className="flex flex-wrap items-baseline justify-between gap-2">
        <h1 className="text-2xl font-bold">Plant to central</h1>
        <p className="text-sm text-muted">Control room, updates every second</p>
      </header>
      {/* Shown while the latest overview request has failed. With old data it gives that data's time (UTC) and its age by the local clock. */}
      {overview.isError && (
        <p
          role="alert"
          className="rounded border-2 border-red-500 bg-red-950 px-3 py-2 font-semibold text-red-200"
        >
          API unreachable.{" "}
          {overview.data
            ? `Showing data from ${new Date(overview.data.at).toLocaleTimeString("en-GB", { hour12: false, timeZone: "UTC" })} UTC (${agoText(overview.data.at, clock)}). Retrying every second.`
            : "Retrying every second."}
        </p>
      )}
      <ErrorBoundary label="Pipeline" resetKey={overview.dataUpdatedAt}>
        <PipelineStrip overview={overview.data} nowMs={nowMs} />
      </ErrorBoundary>
      <div className="grid grid-cols-1 gap-4 min-[900px]:grid-cols-2">
        <ErrorBoundary
          label="Buffers and lag"
          resetKey={overview.dataUpdatedAt}
        >
          <BuffersLag overview={overview.data} />
        </ErrorBoundary>
        <ErrorBoundary label="Chart" resetKey={chart.dataUpdatedAt}>
          <ReadingsChart chart={chart.data} nowMs={nowMs} note={note(chart)} />
        </ErrorBoundary>
        <ErrorBoundary label="Proof" resetKey={proof.dataUpdatedAt}>
          <ProofPanel
            proof={proof.data}
            full={full}
            nowMs={clock}
            note={note(proof)}
          />
        </ErrorBoundary>
        <ErrorBoundary label="Alarms" resetKey={alarms.dataUpdatedAt}>
          <AlarmsPanel
            alarms={alarms.data}
            paused={paused}
            note={note(alarms)}
          />
        </ErrorBoundary>
      </div>
    </main>
  );
}
