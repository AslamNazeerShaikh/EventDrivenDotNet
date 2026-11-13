/**
 * The Alarm mix panel: how the alarms the page already has split by severity and by kind, and their latency.
 *
 * Where it sits: `App.tsx` renders it next to the Alarms panel with the same list from `useAlarms`
 * in `api.ts` (`GET /api/alarms`, at most 50 alarms, newest stored first). The counting is done by
 * `alarmMix` in `derive.ts`; this file only draws it. It does not call the API. `AlarmView` mirrors
 * `ApiModels.cs` in `EventDrivenDesign.Contracts`.
 *
 * Exports: `AlarmMixPanel`.
 *
 * @module
 */
import type { AlarmView } from "../api";
import { alarmMix, type Count, type Tone } from "../derive";
import { formatInt, formatSeconds } from "../format";
import { Dot, Panel, Stat } from "./ui";

/** Colour meaning of each severity the API sends. Any other severity is neutral. */
const SEVERITY_TONE: Record<string, Tone> = {
  critical: "bad",
  high: "warn",
  warning: "idle",
};

/**
 * One group of bars: a name, a bar scaled to the largest count of the group, and the count.
 *
 * @remarks
 * The count is printed as text; the bar is decorative, so assistive technology skips it.
 *
 * @param props - The component props.
 * @param props.title - The group heading, such as "By kind".
 * @param props.rows - The counts, in the order to draw them.
 * @param props.tones - Optional colour meaning per name, drawn as a dot before it.
 * @returns The group element.
 */
function Bars({
  title,
  rows,
  tones,
}: {
  title: string;
  rows: Count[];
  tones?: Record<string, Tone>;
}) {
  // Scale of the bars; the 1 keeps the width calculation from dividing by zero.
  const max = Math.max(1, ...rows.map((r) => r.count));
  return (
    <div>
      <h3 className="mb-1.5 text-xs font-medium text-muted">{title}</h3>
      <ul className="space-y-1.5">
        {rows.map((r) => (
          <li
            key={r.name}
            className="grid grid-cols-[7rem_1fr_2.5rem] items-center gap-2 text-sm"
          >
            <span className="flex items-center gap-1.5">
              {tones && <Dot tone={tones[r.name] ?? "idle"} />}
              {r.name}
            </span>
            <span
              aria-hidden
              className="h-2 overflow-hidden rounded-full bg-raised"
            >
              <span
                className="block h-full rounded-full bg-muted"
                style={{ width: `${(r.count / max) * 100}%` }}
              />
            </span>
            <span className="text-right">{formatInt(r.count)}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}

/**
 * Shows the alarm count, the latest and largest latency, then bars by severity and by kind.
 *
 * @remarks
 * The rows keep a set order (see `alarmMix`), so they do not reshuffle as counts change.
 *
 * @param props - The component props.
 * @param props.alarms - The alarms, or `undefined` before the first answer ("Waiting for data…").
 * @param props.note - Optional warning line for the panel, for example when the request fails.
 * @returns The panel element.
 * @see GET /api/alarms in Program.cs
 */
export function AlarmMixPanel({
  alarms,
  note,
}: {
  alarms: AlarmView[] | undefined;
  note?: string | undefined;
}) {
  if (!alarms)
    return (
      <Panel title="Alarm mix" note={note}>
        <p className="text-muted">Waiting for data…</p>
      </Panel>
    );
  const m = alarmMix(alarms);
  return (
    <Panel title="Alarm mix" note={note}>
      <dl className="mb-4 grid grid-cols-3 gap-3">
        <Stat label="Alarms counted" value={formatInt(m.total)} />
        <Stat label="Latest latency" value={formatSeconds(m.latestLatency)} />
        <Stat label="Max latency" value={formatSeconds(m.maxLatency)} />
      </dl>
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-1">
        <Bars title="By severity" rows={m.bySeverity} tones={SEVERITY_TONE} />
        <Bars title="By kind" rows={m.byKind} />
      </div>
      <p className="mt-3 text-xs text-muted">
        Counted over the latest alarms stored at central, up to 50. The Alarms
        panel lists the newest 20 of them.
      </p>
    </Panel>
  );
}
