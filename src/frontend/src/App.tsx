import { useAlarms, useChart, useOverview, useReconciliation } from "./api";
import { agoText } from "./format";
import { AlarmsPanel } from "./panels/AlarmsPanel";
import { BuffersLag } from "./panels/BuffersLag";
import { PipelineStrip } from "./panels/PipelineStrip";
import { ProofPanel } from "./panels/ProofPanel";
import { ReadingsChart } from "./panels/ReadingsChart";

// A panel keeps its last data when its query fails; this says so.
const stale = (q: { isError: boolean }) => (q.isError ? "Not updating, showing the last data received." : undefined);

export default function App() {
  const overview = useOverview();
  const proof = useReconciliation();
  const chart = useChart();
  const alarms = useAlarms();
  // Server time keeps "last seen" and the chart window in step with device times, and freezes with the data.
  const nowMs = overview.data ? Date.parse(overview.data.at) : Date.now();
  const paused = overview.data ? overview.data.gateways.length > 0 && overview.data.gateways.every((g) => g.paused) : undefined;

  return (
    <main className="mx-auto max-w-[1500px] space-y-4 p-4">
      <header className="flex flex-wrap items-baseline justify-between gap-2">
        <h1 className="text-2xl font-bold">Plant to central</h1>
        <p className="text-sm text-muted">Control room, updates every second</p>
      </header>
      {overview.isError && (
        <p role="alert" className="rounded border-2 border-red-500 bg-red-950 px-3 py-2 font-semibold text-red-200">
          API unreachable.{" "}
          {overview.data
            ? `Showing data from ${new Date(overview.data.at).toLocaleTimeString("en-GB", { hour12: false, timeZone: "UTC" })} UTC (${agoText(overview.data.at, Date.now())}). Retrying every second.`
            : "Retrying every second."}
        </p>
      )}
      <PipelineStrip overview={overview.data} nowMs={nowMs} />
      <div className="grid grid-cols-1 gap-4 min-[900px]:grid-cols-2">
        <BuffersLag overview={overview.data} />
        <ReadingsChart chart={chart.data} nowMs={nowMs} note={stale(chart)} />
        <ProofPanel proof={proof.data} note={stale(proof)} />
        <AlarmsPanel alarms={alarms.data} paused={paused} note={stale(alarms)} />
      </div>
    </main>
  );
}
