import type { ReactNode } from "react";
import type { ServiceHealth } from "../api";
import { healthLabel } from "../format";

export function Panel({ title, children, note }: { title: string; children: ReactNode; note?: string | undefined }) {
  return (
    <section className="rounded-lg border border-line bg-panel p-4" aria-label={title}>
      <h2 className="mb-3 text-sm font-semibold uppercase tracking-wide text-muted">{title}</h2>
      {note && (
        <p role="status" className="mb-2 rounded border border-amber-400 px-2 py-1 text-sm text-amber-300">
          {note}
        </p>
      )}
      {children}
    </section>
  );
}

const TONE = {
  Healthy: { dot: "bg-green-400", text: "text-green-300", border: "border-green-500" },
  Degraded: { dot: "bg-amber-400", text: "text-amber-300", border: "border-amber-500" },
  Down: { dot: "bg-red-400", text: "text-red-300", border: "border-red-500" },
} as const;

// An unknown $type from a newer API degrades to the amber tone instead of crashing the render.
export const toneOf = (h: ServiceHealth) => TONE[h.$type] ?? TONE.Degraded;

// Colour plus a text label: the label is the signal, the colour only helps.
export function HealthBadge({ health, nowMs }: { health: ServiceHealth; nowMs: number }) {
  const t = toneOf(health);
  return (
    <span className={`inline-flex items-start gap-1.5 text-sm font-medium ${t.text}`}>
      <span aria-hidden className={`mt-1.5 h-2.5 w-2.5 shrink-0 rounded-full ${t.dot}`} />
      <span>{healthLabel(health, nowMs)}</span>
    </span>
  );
}

export const Stat = ({ label, value, muted }: { label: string; value: ReactNode; muted?: boolean }) => (
  <div>
    <dt className="text-xs text-muted">{label}</dt>
    <dd className={`text-lg font-semibold tabular-nums ${muted ? "text-slate-400" : ""}`}>{value}</dd>
  </div>
);
