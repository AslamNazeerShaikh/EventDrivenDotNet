/**
 * Small shared building blocks for the panels: the panel card, the health badge, and a labelled number.
 *
 * Where it sits: every file in `src/panels/` imports `Panel` from here, and `PipelineStrip`
 * imports `HealthBadge`; `BuffersLag` and `ProofPanel` use `Stat`. These are presentation only:
 * they hold no state and call no API. The health text comes from `healthLabel` in `format.ts`,
 * and the `ServiceHealth` type mirrors `ServiceHealth.cs` in `EventDrivenDesign.Contracts`.
 *
 * Exports: `Panel`, `toneOf`, `HealthBadge`, `Stat`.
 *
 * @module
 */
import type { ReactNode } from "react";
import type { ServiceHealth } from "../api";
import { healthLabel } from "../format";

/**
 * A titled card that every panel is drawn in.
 *
 * @remarks
 * The card is a `<section>` labelled by its title. The optional note is a `role="status"` paragraph
 * between the title and the content, so a screen reader announces it.
 *
 * @param props - The component props.
 * @param props.title - The heading text, shown in uppercase, and the section's accessible name.
 * @param props.children - The panel content.
 * @param props.note - Optional amber warning line, such as "Not updating, showing the last data received." Nothing is drawn when it is empty or missing.
 * @returns The card element.
 */
export function Panel({
  title,
  children,
  note,
}: {
  title: string;
  children: ReactNode;
  note?: string | undefined;
}) {
  return (
    <section
      className="rounded-lg border border-line bg-panel p-4"
      aria-label={title}
    >
      <h2 className="mb-3 text-sm font-semibold uppercase tracking-wide text-muted">
        {title}
      </h2>
      {note && (
        <p
          role="status"
          className="mb-2 rounded border border-amber-400 px-2 py-1 text-sm text-amber-300"
        >
          {note}
        </p>
      )}
      {children}
    </section>
  );
}

/**
 * Tailwind classes for each health case: the dot colour, the text colour and the border colour.
 *
 * @remarks
 * Green for Healthy, amber for Degraded, red for Down.
 */
const TONE = {
  Healthy: {
    dot: "bg-green-400",
    text: "text-green-300",
    border: "border-green-500",
  },
  Degraded: {
    dot: "bg-amber-400",
    text: "text-amber-300",
    border: "border-amber-500",
  },
  Down: { dot: "bg-red-400", text: "text-red-300", border: "border-red-500" },
} as const;

/**
 * Picks the colour classes for a health value.
 *
 * @remarks
 * An unknown $type from a newer API degrades to the amber tone instead of crashing the render.
 *
 * @param h - The health to colour.
 * @returns An object with `dot`, `text` and `border` Tailwind class strings.
 */
export const toneOf = (h: ServiceHealth) => TONE[h.$type] ?? TONE.Degraded;

/**
 * A coloured dot with the health text beside it.
 *
 * @remarks
 * Colour plus a text label: the label is the signal, the colour only helps.
 * The dot is `aria-hidden`, so assistive technology reads only the text.
 *
 * @param props - The component props.
 * @param props.health - The health to show.
 * @param props.nowMs - The current time in epoch milliseconds, used by the label for "last seen N s ago".
 * @returns The badge element.
 * @see healthLabel in `format.ts`
 */
export function HealthBadge({
  health,
  nowMs,
}: {
  health: ServiceHealth;
  nowMs: number;
}) {
  const t = toneOf(health);
  return (
    <span
      className={`inline-flex items-start gap-1.5 text-sm font-medium ${t.text}`}
    >
      <span
        aria-hidden
        className={`mt-1.5 h-2.5 w-2.5 shrink-0 rounded-full ${t.dot}`}
      />
      <span>{healthLabel(health, nowMs)}</span>
    </span>
  );
}

/**
 * One labelled figure inside a `<dl>`: a small label above a large value.
 *
 * @param props - The component props.
 * @param props.label - The caption, drawn as the `<dt>`.
 * @param props.value - The figure, drawn as the `<dd>`. Any node, so it can hold text or elements.
 * @param props.muted - When true the value is drawn in a grey colour.
 * @returns A `<div>` with a `<dt>` and a `<dd>`. It must be placed inside a `<dl>`.
 */
export const Stat = ({
  label,
  value,
  muted,
}: {
  label: string;
  value: ReactNode;
  muted?: boolean;
}) => (
  <div>
    <dt className="text-xs text-muted">{label}</dt>
    <dd
      className={`text-lg font-semibold tabular-nums ${muted ? "text-slate-400" : ""}`}
    >
      {value}
    </dd>
  </div>
);
