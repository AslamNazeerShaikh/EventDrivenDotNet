/**
 * The Alarms panel: the latest alarms stored at central, plus the controls to inject an alarm and to pause or resume the plant.
 *
 * Where it sits: `App.tsx` renders it with the list from `useAlarms` and a `paused` flag worked out
 * from the overview. The three buttons use the command hooks in `api.ts` (`useInjectFault`,
 * `usePause`, `useResume`), which call `POST /api/machines/{machineId}/faults`,
 * `POST /api/plant/pause` and `POST /api/plant/resume`. The list comes from `GET /api/alarms`;
 * `AlarmView` and `AlarmKind` mirror `ApiModels.cs` and `Readings.cs` in `EventDrivenDesign.Contracts`.
 *
 * Exports: `AlarmsPanel`.
 *
 * @module
 */
import { useState, type FormEvent } from "react";
import {
  ALARM_KINDS,
  ApiError,
  useInjectFault,
  usePause,
  useResume,
  type AlarmKind,
  type AlarmView,
} from "../api";
import { formatSeconds } from "../format";
import { Panel } from "./ui";

/**
 * Formats an ISO timestamp as a 24-hour clock time in UTC.
 *
 * @param iso - An ISO-8601 timestamp.
 * @returns For example `"11:59:30"`.
 */
const time = (iso: string) =>
  new Date(iso).toLocaleTimeString("en-GB", { hour12: false, timeZone: "UTC" });
/** Tailwind classes shared by the Inject, Pause and Resume buttons. Disabled buttons are dimmed. */
const btn =
  "rounded-md border border-line bg-raised px-3 py-1.5 text-sm font-medium transition-colors duration-150 hover:border-line-strong hover:bg-line disabled:cursor-not-allowed disabled:opacity-50";
/** Tailwind classes shared by the machine id input and the kind select. */
const field =
  "rounded-md border border-line bg-ink px-2 py-1.5 transition-colors duration-150 hover:border-line-strong";

/**
 * Turns an error from a command into one line of text.
 *
 * @param e - The error of a failed mutation.
 * @returns `"<code>: <message>"` for an `ApiError`, otherwise "The API did not answer."
 * (for example when the network request failed before any answer).
 */
function errorText(e: unknown): string {
  return e instanceof ApiError
    ? `${e.code}: ${e.message}`
    : "The API did not answer.";
}

/**
 * Shows the alarm table, the inject-alarm form, the Pause and Resume buttons, and the result of the last command.
 *
 * @remarks
 * The table shows the first 20 alarms of the list, which the API sends newest stored first.
 * The form starts with machine `P1-BDY-W001` and kind `Overheat`. Whether the machine accepts the
 * kind is decided by the API, not here; a rejection is shown as an error line under the form.
 * Pressing a button sends the command and then refreshes every query (see `useCommand` in `api.ts`).
 *
 * @param props - The component props.
 * @param props.alarms - The alarms, or `undefined` before the first answer ("Waiting for data…"). An empty array shows "No alarms at central yet."
 * @param props.paused - True when every gateway is paused, false when not, `undefined` when unknown. Shown as "Plant: paused", "running" or "unknown".
 * @param props.note - Optional warning line for the panel, for example when the request fails.
 * @returns The panel element.
 * @see GET /api/alarms in Program.cs
 */
export function AlarmsPanel({
  alarms,
  paused,
  note,
}: {
  alarms: AlarmView[] | undefined;
  paused: boolean | undefined;
  note?: string | undefined;
}) {
  // Form fields. The defaults are a machine and a kind that the API accepts.
  const [machineId, setMachineId] = useState("P1-BDY-W001");
  const [kind, setKind] = useState<AlarmKind>("Overheat");
  // One mutation per command, so each keeps its own pending and error state.
  const inject = useInjectFault();
  const pause = usePause();
  const resume = useResume();
  /**
   * Handles the form's submit: sends the inject command for the typed machine and chosen kind.
   *
   * @param e - The form's submit event. Its default page reload is cancelled.
   */
  const submit = (e: FormEvent) => {
    e.preventDefault();
    // The id is trimmed so a stray space does not make the API reject it.
    inject.mutate({ machineId: machineId.trim(), kind });
  };
  // The first error among the three commands, if any. It stays until that command is run again.
  const failed = inject.error ?? pause.error ?? resume.error;
  return (
    <Panel title="Alarms" note={note}>
      <div className="max-h-56 overflow-auto">
        {/* Three states: no data yet, no alarms, or the table. */}
        {!alarms ? (
          <p className="text-muted">Waiting for data…</p>
        ) : alarms.length === 0 ? (
          <p className="text-muted">No alarms at central yet.</p>
        ) : (
          <table className="w-full min-w-[460px] whitespace-nowrap text-sm">
            <caption className="sr-only">
              Alarms stored at central, newest first
            </caption>
            <thead className="sticky top-0 bg-panel text-left text-xs text-muted">
              <tr>
                <th scope="col" className="py-1.5 pr-2 font-medium">
                  Stored (UTC)
                </th>
                <th scope="col" className="pr-2 font-medium">
                  Machine
                </th>
                <th scope="col" className="pr-2 font-medium">
                  Kind
                </th>
                <th scope="col" className="pr-2 font-medium">
                  Severity
                </th>
                <th scope="col" className="text-right font-medium">
                  Latency
                </th>
              </tr>
            </thead>
            <tbody>
              {/* Only the 20 newest rows are drawn, although the API sends up to 50. */}
              {alarms.slice(0, 20).map((a) => (
                <tr
                  // An alarm is unique by machine and sequence number.
                  key={`${a.machineId}-${a.alarmSeq}`}
                  className="border-t border-line"
                >
                  <td className="py-1 pr-2 font-mono">{time(a.storedAt)}</td>
                  <td className="pr-2 font-mono">{a.machineId}</td>
                  <td className="pr-2">{a.kind}</td>
                  <td className="pr-2">{a.severity}</td>
                  <td className="text-right">
                    {formatSeconds(a.latencySeconds)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      <form
        onSubmit={submit}
        className="mt-4 flex flex-wrap items-end gap-3 border-t border-line pt-3"
      >
        <label className="text-sm">
          <span className="block text-muted">Machine id</span>
          <input
            value={machineId}
            onChange={(e) => setMachineId(e.target.value)}
            required
            className={`w-44 font-mono ${field}`}
          />
        </label>
        <label className="text-sm">
          <span className="block text-muted">Kind</span>
          <select
            value={kind}
            onChange={(e) => setKind(e.target.value as AlarmKind)}
            className={field}
          >
            {ALARM_KINDS.map((k) => (
              <option key={k}>{k}</option>
            ))}
          </select>
        </label>
        <button type="submit" className={btn} disabled={inject.isPending}>
          Inject alarm
        </button>
        {/* A thin divider between the inject controls and the pause controls; hidden on narrow screens. */}
        <span
          className="mx-1 hidden h-6 w-px bg-line min-[500px]:block"
          aria-hidden
        />
        <button
          type="button"
          className={btn}
          disabled={pause.isPending}
          onClick={() => pause.mutate()}
        >
          Pause
        </button>
        <button
          type="button"
          className={btn}
          disabled={resume.isPending}
          onClick={() => resume.mutate()}
        >
          Resume
        </button>
        <span className="text-sm text-fg/80">
          Plant:{" "}
          {paused === undefined ? "unknown" : paused ? "paused" : "running"}
        </span>
      </form>
      {/* Result line. It is a polite live region so a screen reader announces it; the error is an alert. */}
      <div aria-live="polite" className="mt-2 min-h-5 text-sm">
        {failed ? (
          <span role="alert" className="text-bad-fg">
            {errorText(failed)}
          </span>
        ) : inject.isSuccess ? (
          <span className="text-ok">
            Alarm accepted for {inject.variables.machineId}.
          </span>
        ) : null}
      </div>
    </Panel>
  );
}
