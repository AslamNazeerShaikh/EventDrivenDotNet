/**
 * The containers panel: every container of the Compose project with its state, usage and sizes,
 * grouped by the tier it plays in the data flow, plant to control room.
 *
 * `App.tsx` passes it the result of `useContainers` in `api.ts` (`GET /api/containers`, every 5 s).
 * The figures are worked out by `containerTotals`, `groupByTier`, `containerStatus`, `uptimeText`,
 * `portText`, `formatBytes` and `usagePercent` in `derive.ts`. It holds no state and calls no API.
 *
 * @module
 */
import type { ContainersView } from "../api";
import {
  containerStatus,
  containerTotals,
  formatBytes,
  groupByTier,
  portText,
  uptimeText,
  usagePercent,
} from "../derive";
import { Dot, Panel, StatusText } from "./ui";

/** Tailwind classes of a right-aligned number column header. */
const num = "px-2 text-right align-bottom font-medium";

/** The number of table columns, so a tier header row spans them all. */
const COLUMNS = 12;

/**
 * A thin usage bar under a figure.
 *
 * @param props - The component props.
 * @param props.percent - The fill, 0–100 (see `usagePercent`).
 * @param props.label - The meter's accessible name.
 * @returns A `role="meter"` bar.
 */
const Bar = ({ percent, label }: { percent: number; label: string }) => (
  <div
    role="meter"
    aria-label={label}
    aria-valuemin={0}
    aria-valuemax={100}
    aria-valuenow={Math.round(percent)}
    className="mt-1 h-1 overflow-hidden rounded-full bg-raised"
  >
    <div
      className="h-full rounded-full bg-info"
      style={{ width: `${percent}%` }}
    />
  </div>
);

/** A pair of byte counts as "in / out", or "—" when neither is known. */
const pair = (a: number | null, b: number | null) =>
  a === null && b === null ? "—" : `${formatBytes(a)} / ${formatBytes(b)}`;

/**
 * Draws the summary line, the data-flow line and the container table.
 *
 * @param props - The component props.
 * @param props.view - The latest containers view, or `undefined` before the first answer.
 * @param props.note - Optional warning line when the latest request failed (see `note` in `App.tsx`).
 * @returns The panel.
 */
export function ContainersPanel({
  view,
  note,
}: {
  view: ContainersView | undefined;
  note?: string | undefined;
}) {
  const title = "Containers";
  if (!view)
    return (
      <Panel title={title} note={note}>
        <p className="text-muted">Waiting for the API…</p>
      </Panel>
    );
  if (!view.available)
    return (
      <Panel title={title} note={note}>
        <p role="status" className="text-warn">
          Container stats unavailable: {view.reason ?? "no reason given"}.
        </p>
      </Panel>
    );

  const t = containerTotals(view.containers);
  const groups = groupByTier(view.containers);
  const memPct = usagePercent(t.memoryUsedBytes, t.memoryLimitBytes);

  return (
    <Panel title={title} note={note}>
      <dl className="flex flex-wrap gap-x-8 gap-y-2 text-sm">
        <div>
          <dt className="text-xs text-muted">Running</dt>
          <dd className="text-lg font-semibold">
            {t.running} of {t.total}
          </dd>
        </div>
        <div>
          <dt className="text-xs text-muted">CPU, all containers</dt>
          <dd className="text-lg font-semibold">{t.cpuPercent.toFixed(1)} %</dd>
        </div>
        <div className="min-w-48">
          <dt className="text-xs text-muted">Memory used of limit</dt>
          <dd className="text-lg font-semibold">
            {formatBytes(t.memoryUsedBytes)}{" "}
            <span className="text-sm font-normal text-muted">
              of {formatBytes(t.memoryLimitBytes)}
            </span>
            <Bar percent={memPct} label="Memory used of limit" />
          </dd>
        </div>
        <div>
          <dt className="text-xs text-muted">Disk</dt>
          <dd className="text-lg font-semibold">
            {formatBytes(t.diskBytes)}{" "}
            <span className="text-sm font-normal text-muted">
              volumes {formatBytes(t.volumeBytes)}, writable layers{" "}
              {formatBytes(t.writableBytes)}, images {formatBytes(t.imageBytes)}{" "}
              (shared layers counted per image)
            </span>
          </dd>
        </div>
      </dl>

      <ol
        aria-label="Data flow"
        className="mt-3 flex flex-wrap items-center gap-x-2 gap-y-1 text-sm"
      >
        {groups.map((g, i) => (
          <li key={g.tier} className="flex items-center gap-2">
            {i > 0 && (
              <span aria-hidden className="text-muted">
                →
              </span>
            )}
            <span className="inline-flex items-center gap-1.5 rounded-md border border-line bg-raised px-2 py-0.5">
              <Dot tone={g.tone} />
              {g.tier}
              <span className="text-muted">{g.containers.length}</span>
              <span className="sr-only">
                {g.tone === "ok" ? "all fine" : "needs attention"}
              </span>
            </span>
          </li>
        ))}
      </ol>

      <div className="mt-3 overflow-x-auto">
        <table className="w-full text-sm whitespace-nowrap">
          <caption className="sr-only">
            Containers by tier, with state, usage and sizes
          </caption>
          <thead className="text-left align-bottom text-xs text-muted">
            <tr>
              <th scope="col" className="py-1.5 pr-2 font-medium">
                Container
              </th>
              <th scope="col" className="px-2 font-medium">
                State
              </th>
              <th scope="col" className="px-2 font-medium">
                Uptime
              </th>
              <th scope="col" className={num}>
                CPU
              </th>
              <th scope="col" className={num}>
                Memory used / limit
              </th>
              <th scope="col" className={num}>
                Network in / out
              </th>
              <th scope="col" className={num}>
                Block read / write
              </th>
              <th scope="col" className={num}>
                Writable
              </th>
              <th scope="col" className="px-2 font-medium">
                Volumes
              </th>
              <th scope="col" className="px-2 font-medium">
                Image
              </th>
              <th scope="col" className={num}>
                Image size
              </th>
              <th scope="col" className="pl-2 font-medium">
                Ports
              </th>
            </tr>
          </thead>
          {groups.map((g) => (
            <tbody key={g.tier}>
              <tr className="border-t border-line">
                <th
                  scope="colgroup"
                  colSpan={COLUMNS}
                  className="pt-3 pb-1 text-left text-xs font-semibold text-muted"
                >
                  {g.order}. {g.tier} ({g.containers.length})
                </th>
              </tr>
              {g.containers.map((c) => {
                const cpu = c.cpuPercent;
                return (
                  <tr key={c.name} className="border-t border-line align-top">
                    <td className="py-1.5 pr-2 font-mono">{c.name}</td>
                    <td className="px-2 py-1.5">
                      <StatusText status={containerStatus(c)} />
                    </td>
                    <td className="px-2 py-1.5">{uptimeText(c)}</td>
                    <td className="px-2 py-1.5 text-right">
                      {cpu === null ? "—" : `${cpu.toFixed(1)} %`}
                      {cpu !== null && (
                        <Bar
                          percent={usagePercent(cpu, 100)}
                          label={`${c.name} CPU`}
                        />
                      )}
                    </td>
                    <td className="px-2 py-1.5 text-right">
                      {pair(c.memoryUsedBytes, c.memoryLimitBytes)}
                      {c.memoryUsedBytes !== null && (
                        <Bar
                          percent={usagePercent(
                            c.memoryUsedBytes,
                            c.memoryLimitBytes,
                          )}
                          label={`${c.name} memory`}
                        />
                      )}
                    </td>
                    <td className="px-2 py-1.5 text-right">
                      {pair(c.netRxBytes, c.netTxBytes)}
                    </td>
                    <td className="px-2 py-1.5 text-right">
                      {pair(c.blockReadBytes, c.blockWriteBytes)}
                    </td>
                    <td className="px-2 py-1.5 text-right">
                      {formatBytes(c.writableBytes)}
                    </td>
                    <td className="px-2 py-1.5">
                      {c.volumes.length === 0
                        ? "—"
                        : c.volumes.map((v) => (
                            <div key={v.name}>
                              <span className="font-mono">
                                {/* An unnamed volume (from an image's VOLUME line) is a 64-character hash. */}
                                {/^[0-9a-f]{64}$/.test(v.name)
                                  ? `${v.name.slice(0, 12)}… (anonymous)`
                                  : v.name}
                              </span>{" "}
                              <span className="text-muted">
                                {formatBytes(v.bytes)}
                              </span>
                            </div>
                          ))}
                    </td>
                    <td className="px-2 py-1.5 font-mono">
                      {/* A container whose image tag moved to a newer build reports the bare image id. */}
                      {c.image.startsWith("sha256:")
                        ? `${c.image.slice(7, 19)}… (untagged)`
                        : c.image}
                    </td>
                    <td className="px-2 py-1.5 text-right">
                      {formatBytes(c.imageBytes)}
                    </td>
                    <td className="py-1.5 pl-2 font-mono">
                      {c.ports.length === 0
                        ? "—"
                        : c.ports.map(portText).join(", ")}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          ))}
        </table>
      </div>
    </Panel>
  );
}
