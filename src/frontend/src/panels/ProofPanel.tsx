import { useState } from "react";
import type { Reconciliation, Verdict } from "../api";
import { formatInt, proofView } from "../format";
import { Panel, Stat } from "./ui";

const VERDICT: Record<Verdict, { symbol: string; text: string; cls: string; hint: string }> = {
  PASS: { symbol: "✔", text: "PASS", cls: "border-green-500 text-green-300", hint: "Every generated reading is stored once." },
  WAIT: { symbol: "…", text: "WAIT", cls: "border-amber-500 text-amber-300", hint: "Not settled: pause the plant and let every buffer and lag drain." },
  FAIL: { symbol: "✖", text: "FAIL", cls: "border-red-500 text-red-300", hint: "A reading is missing, out of sequence or duplicated." },
};

const UNKNOWN = { symbol: "?", text: "UNKNOWN", cls: "border-amber-500 text-amber-300", hint: "The API sent a verdict this page does not know." };
interface FullCheck {
  result: { data: Reconciliation; at: number } | undefined;
  pending: boolean;
  check: () => void;
}

export function ProofPanel({ proof, full, nowMs, note }: { proof: Reconciliation | undefined; full: FullCheck; nowMs: number; note?: string | undefined }) {
  const [open, setOpen] = useState(false);
  if (!proof) return <Panel title="Proof" note={note}><p className="text-muted">Waiting for the API…</p></Panel>;
  const { verdict, duplicateRows, checkedAgoSeconds } = proofView(proof, full.result, nowMs);
  const v = VERDICT[verdict] ?? UNKNOWN;
  const machines = [...proof.machines].sort((a, b) => b.missing + b.gaps - (a.missing + a.gaps) || a.machineId.localeCompare(b.machineId));
  return (
    <Panel title="Proof" note={note}>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <dl className="grid grid-cols-2 gap-x-6 gap-y-2 min-[500px]:grid-cols-3">
          <Stat label="Generated" value={formatInt(proof.generated)} />
          <Stat label="Stored" value={formatInt(proof.stored)} />
          <Stat label="Missing" value={formatInt(proof.missing)} />
          <Stat label="Gaps" value={formatInt(proof.gaps)} />
          <Stat
            label="Duplicate rows"
            value={
              duplicateRows === null ? (
                "not checked"
              ) : (
                <>
                  {formatInt(duplicateRows)}
                  {checkedAgoSeconds !== null && <span className="block text-xs font-normal text-muted">checked {checkedAgoSeconds} s ago</span>}
                </>
              )
            }
          />
          <Stat label="Duplicates absorbed" value={formatInt(proof.duplicatesAbsorbed)} />
        </dl>
        <div role="status" aria-label={`Verdict ${v.text}`} className={`rounded-lg border-2 px-5 py-3 text-center ${v.cls}`}>
          <div className="text-3xl font-bold">
            <span aria-hidden>{v.symbol} </span>
            {v.text}
          </div>
        </div>
      </div>
      <p className="mt-2 text-sm text-slate-300">
        {v.hint} Plant {proof.paused ? "paused" : "running"}; {proof.drained ? "drained" : "not drained"}.
      </p>
      <button
        type="button"
        onClick={full.check}
        disabled={full.pending}
        className="mt-3 rounded border border-line bg-ink px-3 py-1.5 text-sm font-medium hover:border-sky-400 disabled:cursor-not-allowed disabled:opacity-50"
      >
        {full.pending ? "Checking duplicates…" : "Check duplicates"}
      </button>
      <p className="mt-1 text-xs text-muted">Scans every stored row. Runs by itself, at most every 10 s, once the plant is paused and drained.</p>
      <details className="mt-3" onToggle={(e) => setOpen(e.currentTarget.open)}>
        <summary className="cursor-pointer text-sm text-sky-300">Per-machine table ({formatInt(proof.machines.length)} machines)</summary>
        {open && (
          <div className="mt-2 max-h-64 overflow-auto">
            <table className="w-full text-sm tabular-nums">
              <thead className="sticky top-0 bg-panel text-left text-muted">
                <tr>
                  <th scope="col" className="py-1 pr-2">Machine</th>
                  <th scope="col" className="pr-2 text-right">Generated</th>
                  <th scope="col" className="pr-2 text-right">Stored</th>
                  <th scope="col" className="pr-2 text-right">Missing</th>
                  <th scope="col" className="text-right">Gaps</th>
                </tr>
              </thead>
              <tbody>
                {machines.map((m) => (
                  <tr key={m.machineId} className="border-t border-line">
                    <td className="py-0.5 pr-2 font-mono">{m.machineId}</td>
                    <td className="pr-2 text-right">{formatInt(m.generated)}</td>
                    <td className="pr-2 text-right">{formatInt(m.stored)}</td>
                    <td className="pr-2 text-right">{formatInt(m.missing)}</td>
                    <td className="text-right">{formatInt(m.gaps)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </details>
    </Panel>
  );
}
