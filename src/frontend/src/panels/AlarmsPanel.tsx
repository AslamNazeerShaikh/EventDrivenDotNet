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

const time = (iso: string) =>
  new Date(iso).toLocaleTimeString("en-GB", { hour12: false, timeZone: "UTC" });
const btn =
  "rounded border border-line bg-ink px-3 py-1.5 text-sm font-medium hover:border-sky-400 disabled:cursor-not-allowed disabled:opacity-50";

function errorText(e: unknown): string {
  return e instanceof ApiError
    ? `${e.code}: ${e.message}`
    : "The API did not answer.";
}

export function AlarmsPanel({
  alarms,
  paused,
  note,
}: {
  alarms: AlarmView[] | undefined;
  paused: boolean | undefined;
  note?: string | undefined;
}) {
  const [machineId, setMachineId] = useState("P1-BDY-W001");
  const [kind, setKind] = useState<AlarmKind>("Overheat");
  const inject = useInjectFault();
  const pause = usePause();
  const resume = useResume();
  const submit = (e: FormEvent) => {
    e.preventDefault();
    inject.mutate({ machineId: machineId.trim(), kind });
  };
  const failed = inject.error ?? pause.error ?? resume.error;
  return (
    <Panel title="Alarms" note={note}>
      <div className="max-h-44 overflow-auto">
        {!alarms ? (
          <p className="text-muted">Waiting for data…</p>
        ) : alarms.length === 0 ? (
          <p className="text-muted">No alarms at central yet.</p>
        ) : (
          <table className="w-full min-w-[460px] whitespace-nowrap text-sm tabular-nums">
            <caption className="sr-only">
              Alarms stored at central, newest first
            </caption>
            <thead className="sticky top-0 bg-panel text-left text-muted">
              <tr>
                <th scope="col" className="py-1 pr-2">
                  Stored (UTC)
                </th>
                <th scope="col" className="pr-2">
                  Machine
                </th>
                <th scope="col" className="pr-2">
                  Kind
                </th>
                <th scope="col" className="pr-2">
                  Severity
                </th>
                <th scope="col" className="text-right">
                  Latency
                </th>
              </tr>
            </thead>
            <tbody>
              {alarms.slice(0, 20).map((a) => (
                <tr
                  key={`${a.machineId}-${a.alarmSeq}`}
                  className="border-t border-line"
                >
                  <td className="py-0.5 pr-2">{time(a.storedAt)}</td>
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
            className="w-44 rounded border border-line bg-ink px-2 py-1.5 font-mono"
          />
        </label>
        <label className="text-sm">
          <span className="block text-muted">Kind</span>
          <select
            value={kind}
            onChange={(e) => setKind(e.target.value as AlarmKind)}
            className="rounded border border-line bg-ink px-2 py-1.5"
          >
            {ALARM_KINDS.map((k) => (
              <option key={k}>{k}</option>
            ))}
          </select>
        </label>
        <button type="submit" className={btn} disabled={inject.isPending}>
          Inject alarm
        </button>
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
        <span className="text-sm text-slate-300">
          Plant:{" "}
          {paused === undefined ? "unknown" : paused ? "paused" : "running"}
        </span>
      </form>
      <div aria-live="polite" className="mt-2 min-h-5 text-sm">
        {failed ? (
          <span role="alert" className="text-red-300">
            {errorText(failed)}
          </span>
        ) : inject.isSuccess ? (
          <span className="text-green-300">
            Alarm accepted for {inject.variables.machineId}.
          </span>
        ) : null}
      </div>
    </Panel>
  );
}
