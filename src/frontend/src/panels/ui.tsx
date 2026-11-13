/**
 * Small shared building blocks for the panels: the panel card, the status colours, the health badge,
 * a status text, and a labelled number.
 *
 * Where it sits: every file in `src/panels/` imports `Panel` from here. `PipelineStrip`,
 * `GatewaysTable` and `CentralSide` use `HealthBadge`; `BuffersLag`, `ProofPanel`, `Throughput` and
 * `AlarmMixPanel` use `Stat`; `App.tsx` and `Throughput` use `StatusText`; `KpiStrip` and
 * `AlarmMixPanel` use `Dot`. These are presentation only: they hold no state and call no API. The health text comes
 * from `healthLabel` in `format.ts`, the colour meaning from `toneOfHealth` in `derive.ts`, and the
 * `ServiceHealth` type mirrors `ServiceHealth.cs` in `EventDrivenDesign.Contracts`.
 *
 * Exports: `Panel`, `toneOf`, `Dot`, `StatusText`, `HealthBadge`, `Stat`.
 *
 * @module
 */
import type { ReactNode } from "react";
import type { ServiceHealth } from "../api";
import { toneOfHealth, type Status, type Tone } from "../derive";
import { healthLabel } from "../format";

/**
 * A titled card that every panel is drawn in.
 *
 * @remarks
 * The card is a `<section>` labelled by its title. It fills the height of its grid cell, so the
 * cards of one row line up. The optional note is a `role="status"` paragraph between the title and
 * the content, so a screen reader announces it.
 *
 * @param props - The component props.
 * @param props.title - The heading text and the section's accessible name.
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
      className="h-full rounded-lg border border-line bg-panel p-4"
      aria-label={title}
    >
      <h2 className="mb-3 text-sm font-semibold text-fg">{title}</h2>
      {note && (
        <p
          role="status"
          className="mb-3 rounded-md border border-warn px-2 py-1 text-sm text-warn"
        >
          {note}
        </p>
      )}
      {children}
    </section>
  );
}

/** Text colour class for each tone. The red is the lighter `bad-fg`, so small text stays readable. */
const TONE_TEXT: Record<Tone, string> = {
  ok: "text-ok",
  warn: "text-warn",
  bad: "text-bad-fg",
  info: "text-info",
  idle: "text-muted",
};

/** Dot colour class for each tone. */
const TONE_DOT: Record<Tone, string> = {
  ok: "bg-ok",
  warn: "bg-warn",
  bad: "bg-bad",
  info: "bg-info",
  idle: "bg-line-strong",
};

/**
 * Picks the colour classes for a health value.
 *
 * @remarks
 * Green for Healthy, amber for Degraded, red for Down. An unknown $type from a newer API gets the
 * amber tone instead of crashing the render (see `toneOfHealth`).
 *
 * @param h - The health to colour.
 * @returns An object with `dot` and `text` Tailwind class strings.
 */
export const toneOf = (h: ServiceHealth) => {
  const t = toneOfHealth(h);
  return { dot: TONE_DOT[t], text: TONE_TEXT[t] };
};

/**
 * A small round status dot. It is decorative: the text next to it carries the meaning.
 *
 * @param props - The component props.
 * @param props.tone - The colour meaning.
 * @returns An `aria-hidden` span.
 */
export const Dot = ({ tone }: { tone: Tone }) => (
  <span
    aria-hidden
    className={`inline-block h-2 w-2 shrink-0 rounded-full ${TONE_DOT[tone]}`}
  />
);

/**
 * A status dot followed by its text, in the status colour.
 *
 * @param props - The component props.
 * @param props.status - The text and its tone, for example from `plantStatus` in `derive.ts`.
 * @returns The status element.
 */
export const StatusText = ({ status }: { status: Status }) => (
  <span
    className={`inline-flex items-center gap-1.5 font-medium ${TONE_TEXT[status.tone]}`}
  >
    <Dot tone={status.tone} />
    {status.text}
  </span>
);

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
        className={`mt-1.5 h-2 w-2 shrink-0 rounded-full ${t.dot}`}
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
 * @param props.muted - When true the value is drawn in the secondary grey.
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
      className={`text-lg font-semibold tabular-nums ${muted ? "text-muted" : ""}`}
    >
      {value}
    </dd>
  </div>
);
