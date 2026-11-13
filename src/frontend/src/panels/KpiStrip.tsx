/**
 * The KPI strip: eight compact cards with the figures an operator checks first.
 *
 * Where it sits: `App.tsx` renders it under the header with the latest `Overview` from `useOverview`
 * and the light `Reconciliation` from `useReconciliation`, both in `api.ts`. What each card says is
 * worked out by `kpis` in `derive.ts`; this file only draws it. It does not call the API. The data
 * comes from `GET /api/overview` and `GET /api/reconciliation`.
 *
 * Exports: `KpiStrip`.
 *
 * @module
 */
import type { Overview, Reconciliation } from "../api";
import { kpis, type Tone } from "../derive";
import { Dot } from "./ui";

/**
 * Colour of a card's secondary line. Only a warning or a fault colours the words; otherwise the
 * dot alone carries the colour, so a healthy strip stays quiet.
 */
const SUB_TEXT: Record<Tone, string> = {
  ok: "text-muted",
  info: "text-muted",
  idle: "text-muted",
  warn: "text-warn",
  bad: "text-bad-fg",
};

/**
 * Draws the eight KPI cards as one description list: two per row on phones, four from `sm`, eight from `xl`.
 *
 * @remarks
 * Each card has a label, a large figure with its unit, and a secondary line with a status dot.
 * The meaning is always in the words; the dot only helps.
 *
 * @param props - The component props.
 * @param props.overview - The latest overview, or `undefined` before the first answer.
 * @param props.proof - The latest light reconciliation, or `undefined` before the first answer.
 * @returns The strip element.
 * @see kpis in `derive.ts`
 */
export function KpiStrip({
  overview,
  proof,
}: {
  overview: Overview | undefined;
  proof: Reconciliation | undefined;
}) {
  return (
    <section aria-label="Key figures">
      <dl className="grid grid-cols-2 gap-3 sm:grid-cols-4 xl:grid-cols-8">
        {kpis(overview, proof).map((k) => (
          <div
            key={k.label}
            className="min-w-0 rounded-lg border border-line bg-panel px-3 py-2.5"
          >
            <dt className="text-xs text-muted">{k.label}</dt>
            <dd className="mt-1 text-xl leading-7 font-semibold">
              {k.value}
              {k.unit && (
                <span className="ml-1 text-xs font-normal text-muted">
                  {k.unit}
                </span>
              )}
            </dd>
            <dd
              className={`mt-0.5 flex items-start gap-1.5 text-xs ${SUB_TEXT[k.tone]}`}
            >
              {/* The padding lines the dot up with the first line of text. */}
              <span className="flex pt-1">
                <Dot tone={k.tone} />
              </span>
              <span>{k.sub}</span>
            </dd>
          </div>
        ))}
      </dl>
    </section>
  );
}
