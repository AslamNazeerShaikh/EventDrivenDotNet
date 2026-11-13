/**
 * The Proof panel: did every generated reading arrive at central exactly once?
 *
 * Where it sits: `App.tsx` renders it with the light `Reconciliation` from `useReconciliation`
 * (polled every second) and the `full` object from `useFullCheck` (the on-demand duplicate scan),
 * both in `api.ts`. `proofView` in `format.ts` decides which numbers to show. The panel itself does not
 * call the API; its button calls `full.check`. The data comes from `GET /api/reconciliation`, with
 * `?full=true` for the scan. `Reconciliation` and `MachineProof` mirror `ApiModels.cs`, and the verdict
 * rules are in `Proof.Reconcile` in `EventDrivenDesign.Domain`.
 *
 * Exports: `ProofPanel`.
 *
 * @module
 */
import { useState } from "react";
import type { Reconciliation, Verdict } from "../api";
import { formatInt, proofView } from "../format";
import { Panel, Stat } from "./ui";

/**
 * How each verdict is drawn: an outline icon, the word, Tailwind colour classes, and a one-line hint.
 *
 * @remarks
 * PASS is green with a check mark, WAIT amber with a clock, FAIL red with a cross. `icon` is the SVG
 * path drawn on a 24 by 24 grid. The hints match the operator guide (docs/05-run-and-operate.md, section 9).
 */
const VERDICT: Record<
  Verdict,
  { icon: string; text: string; cls: string; hint: string }
> = {
  PASS: {
    icon: "M20 6 9 17l-5-5",
    text: "PASS",
    cls: "border-ok text-ok",
    hint: "Every generated reading is stored once.",
  },
  WAIT: {
    icon: "M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0ZM12 7v5l3 2",
    text: "WAIT",
    cls: "border-warn text-warn",
    hint: "Not settled: pause the plant and let every buffer and lag drain.",
  },
  FAIL: {
    icon: "M18 6 6 18M6 6l12 12",
    text: "FAIL",
    cls: "border-bad text-bad-fg",
    hint: "A reading is missing, out of sequence or duplicated.",
  },
};

/**
 * What is drawn for a verdict string that is not in `Verdict`, for example one sent by a newer API.
 */
const UNKNOWN = {
  icon: "M9.1 9a3 3 0 0 1 5.8 1c0 2-3 3-3 3M12 17h.01",
  text: "UNKNOWN",
  cls: "border-warn text-warn",
  hint: "The API sent a verdict this page does not know.",
};
/**
 * The state of the full duplicate check. It has the same shape as the value `useFullCheck` returns.
 */
interface FullCheck {
  /** The last successful full answer and the epoch-millisecond time it arrived, or `undefined` if none yet. */
  result: { data: Reconciliation; at: number } | undefined;
  /** True while a full scan is running. The button is disabled and says "Checking duplicates…". */
  pending: boolean;
  /** Starts a full scan now. It does nothing if one is already running. */
  check: () => void;
}

/**
 * Shows the counts, the verdict, the duplicate-check button and an expandable per-machine table.
 *
 * @remarks
 * The verdict and the counts always come from the light poll. Only "Duplicate rows" can come from
 * the full check, see `proofView`. The per-machine table is drawn only while its `<details>` is open,
 * so the rows of every machine are not drawn while it is closed. Machines are sorted with the
 * worst first (most missing plus gaps), then by machine id.
 *
 * @param props - The component props.
 * @param props.proof - The latest light reconciliation, or `undefined` before the first answer. Without it the panel shows "Waiting for data…".
 * @param props.full - The full duplicate check: its last result, whether it is running, and how to start it.
 * @param props.nowMs - The current time in epoch milliseconds, used for "checked N s ago".
 * @param props.note - Optional warning line for the panel, for example when the request fails.
 * @returns The panel element.
 * @see GET /api/reconciliation in Program.cs
 */
export function ProofPanel({
  proof,
  full,
  nowMs,
  note,
}: {
  proof: Reconciliation | undefined;
  full: FullCheck;
  nowMs: number;
  note?: string | undefined;
}) {
  // Whether the per-machine table is open. It is set by the <details> toggle event below.
  const [open, setOpen] = useState(false);
  if (!proof)
    return (
      <Panel title="Proof" note={note}>
        <p className="text-muted">Waiting for data…</p>
      </Panel>
    );
  // Verdict and counts from the light poll; duplicate rows from the full check when it still applies.
  const { verdict, duplicateRows, checkedAgoSeconds } = proofView(
    proof,
    full.result,
    nowMs,
  );
  // The fallback covers a verdict string this page does not know.
  const v = VERDICT[verdict] ?? UNKNOWN;
  // Worst machines first, then by id. The array is copied so the cached data is not reordered.
  const machines = [...proof.machines].sort(
    (a, b) =>
      b.missing + b.gaps - (a.missing + a.gaps) ||
      a.machineId.localeCompare(b.machineId),
  );
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
            // "not checked" until a full scan has run; the age line appears only when the scan result is in use.
            value={
              duplicateRows === null ? (
                "not checked"
              ) : (
                <>
                  {formatInt(duplicateRows)}
                  {checkedAgoSeconds !== null && (
                    <span className="block text-xs font-normal text-muted">
                      checked {checkedAgoSeconds} s ago
                    </span>
                  )}
                </>
              )
            }
          />
          <Stat
            label="Duplicates absorbed"
            value={formatInt(proof.duplicatesAbsorbed)}
          />
        </dl>
        <div
          role="status"
          aria-live="polite"
          aria-label={`Verdict ${v.text}`}
          className={`rounded-lg border px-5 py-3 ${v.cls}`}
        >
          <div className="flex items-center gap-2 text-2xl font-semibold">
            <svg
              aria-hidden
              viewBox="0 0 24 24"
              className="h-6 w-6"
              fill="none"
              stroke="currentColor"
              strokeWidth={2}
              strokeLinecap="round"
              strokeLinejoin="round"
            >
              <path d={v.icon} />
            </svg>
            {v.text}
          </div>
        </div>
      </div>
      <p className="mt-3 text-sm text-fg/80">
        {v.hint} Plant {proof.paused ? "paused" : "running"};{" "}
        {proof.drained ? "drained" : "not drained"}.
      </p>
      <button
        type="button"
        onClick={full.check}
        disabled={full.pending}
        className="mt-3 rounded-md border border-line bg-raised px-3 py-1.5 text-sm font-medium transition-colors duration-150 hover:border-line-strong hover:bg-line disabled:cursor-not-allowed disabled:opacity-50"
      >
        {full.pending ? "Checking duplicates…" : "Check duplicates"}
      </button>
      <p className="mt-1 text-xs text-muted">
        Scans every stored row. Runs by itself, at most every 10 s, once the
        plant is paused and drained.
      </p>
      <details className="mt-3" onToggle={(e) => setOpen(e.currentTarget.open)}>
        <summary className="cursor-pointer text-sm text-info">
          Per-machine table ({formatInt(proof.machines.length)} machines)
        </summary>
        {/* The rows are drawn only while the details are open. */}
        {open && (
          <div className="mt-2 max-h-64 overflow-auto">
            <table className="w-full text-sm tabular-nums">
              <thead className="sticky top-0 bg-panel text-left text-muted">
                <tr>
                  <th scope="col" className="py-1 pr-2">
                    Machine
                  </th>
                  <th scope="col" className="pr-2 text-right">
                    Generated
                  </th>
                  <th scope="col" className="pr-2 text-right">
                    Stored
                  </th>
                  <th scope="col" className="pr-2 text-right">
                    Missing
                  </th>
                  <th scope="col" className="text-right">
                    Gaps
                  </th>
                </tr>
              </thead>
              <tbody>
                {machines.map((m) => (
                  <tr key={m.machineId} className="border-t border-line">
                    <td className="py-0.5 pr-2 font-mono">{m.machineId}</td>
                    <td className="pr-2 text-right">
                      {formatInt(m.generated)}
                    </td>
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
