/**
 * The only file in the dashboard that talks to the backend.
 *
 * Flow: `App.tsx` calls the hooks below, each hook calls `fetch` on an `/api/*` path with no host, and
 * nginx (or the Vite dev proxy) forwards that path to one of the two API instances. The hooks return
 * plain data that `App.tsx` passes to the panels as props. No other file calls `fetch`.
 *
 * This file holds three things:
 * - the wire types, which mirror the C# records in `src/backend/EventDrivenDesign.Contracts`
 *   (`ApiModels.cs`, `ServiceHealth.cs`, `Readings.cs`);
 * - the error type and the two small request helpers (`get`, `post`);
 * - the React Query hooks for the five reads and the three commands.
 *
 * Routes used (see `src/backend/EventDrivenDesign.Api/Program.cs`):
 * `GET /api/overview`, `GET /api/reconciliation`, `GET /api/chart`, `GET /api/alarms`,
 * `POST /api/plant/pause`, `POST /api/plant/resume`, `POST /api/machines/{machineId}/faults`.
 *
 * @module
 */
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useCallback, useEffect, useRef, useState } from "react";
import { shouldRequestFull } from "./format";

/**
 * How healthy one source is, as the API judges it from the age of its last successful poll.
 *
 * @remarks
 * Wire types mirror src/backend/EventDrivenDesign.Contracts (camelCase, DateTimeOffset as ISO strings).
 * The union is discriminated by the `$type` property, like the C# `ServiceHealth` hierarchy.
 * `Healthy` carries no other field. `Degraded` carries a `reason`. `Down` carries `lastSeen`.
 * The API picks the case in `Proof.HealthFromAge`: age under 3 s is `Healthy`, under 10 s is
 * `Degraded`, anything older is `Down`. The page can also build a `Degraded` value itself, see
 * `mirrorHealth` in `format.ts`.
 *
 * @see ServiceHealth.cs in EventDrivenDesign.Contracts
 * @see Proof.HealthFromAge in EventDrivenDesign.Domain
 */
export type ServiceHealth =
  | { $type: "Healthy" }
  | {
      $type: "Degraded";
      /** Plain-text reason, for example "no update for 5 s". Shown after "Degraded: ". */
      reason: string;
    }
  | {
      $type: "Down";
      /**
       * When the source last answered, as an ISO-8601 UTC string. The Unix epoch
       * (1970-01-01) means the API has never heard from the source.
       */
      lastSeen: string;
    };

/**
 * One tile per shop: the shop's active gateway, which reads machines and buffers rows for sending.
 *
 * @see GatewayTile in ApiModels.cs
 */
export interface GatewayTile {
  /** Shop code, such as `BDY`. The panels show it as the row name. */
  shop: string;
  /** Health of the shop's gateway. `Down` with the epoch as `lastSeen` if it never answered. */
  health: ServiceHealth;
  /** State text the gateway reports. The API sends `"Unknown"` when it has never heard from the shop. */
  state: string;
  /** True while the gateway is paused: its machines produce no readings. */
  paused: boolean;
  /** Rows sent to the plant Kafka per second. The pipeline strip labels it "sent/s". */
  sentPerSecond: number;
  /** Count of readings waiting in the gateway's local buffer, not yet sent. */
  bufferedReadings: number;
  /** Count of alarms waiting in the gateway's local buffer, not yet sent. */
  bufferedAlarms: number;
  /** Age of the oldest row still unsent, in milliseconds (not seconds). 0 when nothing is waiting. */
  oldestUnsentAgeMs: number;
  /** Count of readings the gateway has generated, as it reports it. */
  generated: number;
  /**
   * Id of the shop's active gateway instance, such as `gateway-bdy-b`.
   * `null` when the shop has no active instance. See `instanceLabel` in `format.ts`.
   */
  instance: string | null;
}

/**
 * A Kafka cluster: the plant cluster or the central cluster.
 *
 * @see ClusterTile in ApiModels.cs
 */
export interface ClusterTile {
  /** Health of the cluster's last successful poll. */
  health: ServiceHealth;
  /** Records written so far, as the sum of the end offsets. The strip prints it as "records". */
  endOffsetTotal: number;
  /**
   * Device time of the newest record, as an ISO-8601 UTC string.
   * `null` when the API has not seen any record yet.
   */
  newestDeviceTime: string | null;
}

/**
 * One store-writer consumer at central, which reads Kafka and inserts rows into the database.
 *
 * @see ConsumerTile in ApiModels.cs
 */
export interface ConsumerTile {
  /** Name of the consumer, shown by the strip when it is down. */
  consumerId: string;
  /** Health of the consumer's last successful poll. */
  health: ServiceHealth;
  /** Rows inserted per second. The strip labels it "inserted/s". */
  insertedPerSecond: number;
  /** Count of records the consumer has received. */
  received: number;
  /** Count of rows the consumer has inserted. */
  inserted: number;
}

/**
 * The central database.
 *
 * @see DatabaseTile in ApiModels.cs
 */
export interface DatabaseTile {
  /** Health of the database's last successful poll. */
  health: ServiceHealth;
  /** Count of readings stored, summed over all machines. The strip prints it as "rows". */
  stored: number;
  /**
   * Seconds between the API's clock and the newest device time stored, never below 0.
   * `null` when nothing is stored yet.
   */
  freshnessSeconds: number | null;
}

/**
 * The answer of `GET /api/overview`: one snapshot of the whole pipeline from plant to database.
 *
 * @see Overview in ApiModels.cs
 */
export interface Overview {
  /**
   * Server time when the snapshot was built, as an ISO-8601 UTC string. `App.tsx` uses it as the
   * page's "now", so it stays in step with device times and stops when the data stops.
   */
  at: string;
  /** One tile per shop. */
  gateways: GatewayTile[];
  /** The Kafka cluster in the plant. */
  plantKafka: ClusterTile;
  /**
   * Seconds that central's newest device time trails the plant's, never below 0.
   * `null` when the API cannot work it out, for example when central has no record yet.
   */
  mirrorLagSeconds: number | null;
  /** The Kafka cluster at central. */
  centralKafka: ClusterTile;
  /** Records at central that the consumers have not read yet. 0 when central is unknown. */
  consumerLag: number;
  /** The store-writer consumers. */
  consumers: ConsumerTile[];
  /** The central database. */
  database: DatabaseTile;
  /** Readings per second that the gateways send now, counting only gateways that are neither down nor paused. */
  liveRate: number;
  /** Rows per second that the consumers insert, counting only consumers that are not down. */
  drainRate: number;
  /**
   * Seconds until the backlog is empty at the current rates. 0 when there is no backlog.
   * `null` when unknown: the drain is not faster than the live rate, central is not healthy,
   * or every consumer is down.
   */
  drainEtaSeconds: number | null;
}

/**
 * The proof for one machine: what it generated against what central stored.
 *
 * @remarks
 * The C# record also sends an `over` field (rows numbered beyond what the machine generated).
 * This type does not declare it and the page does not show it, but the verdict counts it.
 *
 * @see MachineProof in ApiModels.cs
 */
export interface MachineProof {
  /** Machine id, such as `P1-BDY-W001`. */
  machineId: string;
  /** Count of readings the machine's gateway generated. */
  generated: number;
  /** Count of that machine's readings stored at central. */
  stored: number;
  /** Readings generated but not stored, never below 0. */
  missing: number;
  /** Sequence numbers below the highest stored one that have no stored row. */
  gaps: number;
}

/**
 * The verdict of the proof. `WAIT` until the plant is paused and drained; then `PASS` if nothing is
 * missing, gapped or over-numbered, otherwise `FAIL`.
 *
 * @see Proof.Reconcile in EventDrivenDesign.Domain
 */
export type Verdict = "PASS" | "WAIT" | "FAIL";

/**
 * The answer of `GET /api/reconciliation`: the proof that every generated reading was stored once.
 *
 * @remarks
 * The plain call is light and is polled every second. The call with `?full=true` also scans the
 * whole readings table for duplicate rows, see `useFullCheck`.
 * The C# record also sends an `over` total that this type does not declare.
 *
 * @see Reconciliation in ApiModels.cs
 */
export interface Reconciliation {
  /** Count of readings generated, summed over all machines. */
  generated: number;
  /** Count of readings stored at central, summed over all machines. */
  stored: number;
  /** Readings generated but not stored, summed over all machines. */
  missing: number;
  /** Gaps in the stored sequence numbers, summed over all machines. */
  gaps: number;
  /** Redeliveries that the database key rejected, so they were absorbed and stored once. */
  duplicatesAbsorbed: number;
  /**
   * Count of duplicate rows found by the full scan.
   * `null` when the scan was not requested or did not finish, shown as "not checked".
   */
  duplicateRows: number | null;
  /** True when every gateway instance is fresh and paused, and every shop has an active one. */
  paused: boolean;
  /** True when every buffer and lag is empty, so the numbers can be final. */
  drained: boolean;
  /** The server's verdict. The panel falls back to "UNKNOWN" if it sends a value not in `Verdict`. */
  verdict: Verdict;
  /** One row per machine, ordered by machine id. */
  machines: MachineProof[];
}

/**
 * One bar of the readings chart: the readings stored for one 5-second slice of device time.
 *
 * @see ChartPoint in ApiModels.cs
 */
export interface ChartPoint {
  /** Start of the 5-second slice, as an ISO-8601 UTC string. Device time, not arrival time. */
  bucketStart: string;
  /** Count of readings stored in the slice. */
  readings: number;
  /** True when the slice is after the watermark, so late readings can still change it. */
  provisional: boolean;
}

/**
 * The answer of `GET /api/chart?minutes=10`.
 *
 * @remarks
 * The API sends only the slices that have readings. `fillBuckets` in `format.ts` adds the empty ones.
 *
 * @see Chart in ApiModels.cs
 */
export interface Chart {
  /**
   * Device time up to which the stored data is settled, as an ISO-8601 UTC string.
   * `null` when the API has no value for it yet; then every slice is provisional.
   */
  watermark: string | null;
  /** Slices that have readings, oldest first. */
  points: ChartPoint[];
}

/**
 * The alarm kinds the inject form offers, in the same order as the C# `AlarmKind` enum.
 *
 * @remarks
 * `DataGap` is in the list because the API can send it, but it is raised by the gateways, not by a
 * sensor. The form offers every entry here.
 *
 * @see AlarmKind in Readings.cs
 */
export const ALARM_KINDS = [
  "Overheat",
  "Vibration",
  "Overload",
  "EStop",
  "LowBattery",
  "OutOfBand",
  "DataGap",
] as const;

/** One alarm kind: the union of the strings in `ALARM_KINDS`. */
export type AlarmKind = (typeof ALARM_KINDS)[number];

/**
 * One alarm stored at central, as `GET /api/alarms` lists it.
 *
 * @see AlarmView in ApiModels.cs
 */
export interface AlarmView {
  /** Machine that raised the alarm, such as `P1-MCH-C017`. */
  machineId: string;
  /** Number of the alarm for that machine. With `machineId` it is the alarm's unique key. */
  alarmSeq: number;
  /** What kind of alarm it is. */
  kind: AlarmKind;
  /** Severity text from the API, such as `critical`, `high` or `warning`. */
  severity: string;
  /** When the machine raised the alarm, as an ISO-8601 UTC string. */
  deviceTime: string;
  /** When central stored the alarm, as an ISO-8601 UTC string. The panel sorts and shows this. */
  storedAt: string;
  /** Seconds from `deviceTime` to `storedAt`, never below 0. */
  latencySeconds: number;
}

/**
 * An error answered by the API, built from its RFC 9457 `application/problem+json` body.
 *
 * @remarks
 * RFC 9457 problem+json: title carries the error code.
 * So `code` is the problem's `title` (for example `api.gateway.unavailable`) and `message` is its `detail`.
 *
 * @example
 * `new ApiError("api.gateway.unavailable", "gateway-bdy-a answered 500.")`
 */
export class ApiError extends Error {
  /**
   * @param code - Machine-readable error code, taken from the problem's `title`. When the body has none it is `http.<status>`.
   * @param message - Human-readable text, taken from the problem's `detail`.
   */
  constructor(
    public readonly code: string,
    message: string,
  ) {
    super(message);
  }
}

/**
 * Turns a failed response into an `ApiError`.
 *
 * @param res - A response whose `ok` is false.
 * @returns An `ApiError` from the problem+json body (`title` as code, `detail` as message).
 * If the body is not JSON, the code is `http.<status>` and the message is the status text,
 * or "Request failed" when there is none. It returns the error and does not throw it.
 */
async function failure(res: Response): Promise<ApiError> {
  try {
    const p = (await res.json()) as { title?: string; detail?: string };
    // A JSON body that lacks title or detail falls back to the status.
    return new ApiError(
      p.title ?? `http.${res.status}`,
      p.detail ?? res.statusText,
    );
  } catch {
    // The body was empty or not JSON.
    return new ApiError(
      `http.${res.status}`,
      res.statusText || "Request failed",
    );
  }
}

/**
 * Sends a GET request and reads the JSON body.
 *
 * @typeParam T - The type the caller expects the body to have. It is a cast, not a check.
 * @param path - Path to fetch, such as `/api/overview`.
 * @returns The parsed body.
 * @throws {ApiError} When the response status is not 2xx.
 * A network failure makes `fetch` reject, and a body that is not JSON makes `res.json()` reject. Those pass through unchanged.
 */
async function get<T>(path: string): Promise<T> {
  const res = await fetch(path);
  if (!res.ok) throw await failure(res);
  return (await res.json()) as T;
}

/**
 * Sends a POST request. The answer body is ignored.
 *
 * @param path - Path to post to, such as `/api/plant/pause`.
 * @param body - Optional value to send as JSON. Without it no body and no `Content-Type` header are sent.
 * @returns Nothing, once the response status is 2xx.
 * @throws {ApiError} When the response status is not 2xx.
 * A network failure makes `fetch` reject and passes through unchanged.
 */
async function post(path: string, body?: unknown): Promise<void> {
  const res = await fetch(path, {
    method: "POST",
    headers:
      body === undefined ? undefined : { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!res.ok) throw await failure(res);
}

/**
 * React Query options shared by the four polled reads.
 *
 * @remarks
 * `refetchInterval` is in milliseconds: every query asks again each second.
 * `refetchIntervalInBackground` keeps polling when the browser tab is hidden.
 * `retry: false` turns off React Query's own retries, because the next tick is the retry.
 * A failed poll keeps the last good `data` and sets `isError`, which the panels turn into a note.
 */
const poll = {
  refetchInterval: 1000,
  refetchIntervalInBackground: true,
  retry: false,
} as const;

/**
 * Polls the whole-pipeline snapshot every second.
 *
 * @returns The React Query result; `data` is an `Overview` once the first call succeeds.
 * @see GET /api/overview in Program.cs
 */
export const useOverview = () =>
  useQuery({
    queryKey: ["overview"],
    queryFn: () => get<Overview>("/api/overview"),
    ...poll,
  });

/**
 * Polls the light reconciliation every second.
 *
 * @remarks
 * The polled call is the light one. The full call counts duplicate rows across the whole table, so see useFullCheck.
 *
 * @returns The React Query result; `data` is a `Reconciliation` whose `duplicateRows` is `null`.
 * @see GET /api/reconciliation in Program.cs
 */
export const useReconciliation = () =>
  useQuery({
    queryKey: ["reconciliation"],
    queryFn: () => get<Reconciliation>("/api/reconciliation"),
    ...poll,
  });

/**
 * Runs the full reconciliation: on demand, and by itself while the plant is paused and drained.
 *
 * @remarks
 * The full reconciliation (duplicate rows, per-machine table) on demand, and automatically at most every 10 s
 * while the plant is paused and drained. A failed attempt counts too, so a slow scan is not hammered.
 * The scan reads every stored row, so it is not polled like the other reads.
 * The rule for when to start one is `shouldRequestFull` in `format.ts`.
 *
 * @param light - The latest light reconciliation, or `undefined` before the first answer.
 * The hook checks it for "paused and drained" whenever it changes.
 * @returns `result`, the last successful full answer with the time it arrived (epoch milliseconds),
 * or `undefined` before the first success; `pending`, true while a scan runs; and `check`, which starts
 * a scan now and does nothing if one is already running.
 * @see GET /api/reconciliation?full=true in Program.cs
 */
export function useFullCheck(light: Reconciliation | undefined) {
  const [result, setResult] = useState<
    { data: Reconciliation; at: number } | undefined
  >();
  const [pending, setPending] = useState(false);
  // A ref, not state: it must block a second scan at once, without waiting for a re-render.
  const busy = useRef(false);
  // Epoch milliseconds of the last start, successful or not. null until the first attempt.
  const lastAttempt = useRef<number | null>(null);
  const check = useCallback(async () => {
    if (busy.current) return;
    busy.current = true;
    lastAttempt.current = Date.now();
    setPending(true);
    try {
      setResult({
        data: await get<Reconciliation>("/api/reconciliation?full=true"),
        at: Date.now(),
      });
    } catch {
      // keep the previous result; the light poll and the banner report API trouble
    } finally {
      busy.current = false;
      setPending(false);
    }
  }, []);
  useEffect(() => {
    // Starts a scan only if the plant is paused and drained and the last attempt is old enough.
    if (shouldRequestFull(light, lastAttempt.current, Date.now())) void check();
  }, [light, check]);
  return { result, pending, check };
}

/**
 * Polls the readings chart every second, for a window of the last 10 minutes.
 *
 * @returns The React Query result; `data` is a `Chart` once the first call succeeds.
 * @see GET /api/chart in Program.cs. It accepts `minutes` and clamps it to 1 to 180.
 */
export const useChart = () =>
  useQuery({
    queryKey: ["chart"],
    queryFn: () => get<Chart>("/api/chart?minutes=10"),
    ...poll,
  });

/**
 * Polls the latest alarms every second.
 *
 * @returns The React Query result; `data` is an array of `AlarmView`, newest stored first,
 * at most 50 long. The alarms panel shows the first 20.
 * @see GET /api/alarms in Program.cs
 */
export const useAlarms = () =>
  useQuery({
    queryKey: ["alarms"],
    queryFn: () => get<AlarmView[]>("/api/alarms"),
    ...poll,
  });

/**
 * Wraps a command in a React Query mutation that refreshes every query when it ends.
 *
 * @remarks
 * Commands refresh everything straight away so the page does not wait for the next tick.
 * `onSettled` runs after success and after failure, so the page refreshes either way.
 *
 * @typeParam V - The type of the value passed to `mutate`.
 * @param fn - The request to send. It rejects with `ApiError` when the API answers with an error.
 * @returns The React Query mutation result.
 */
function useCommand<V>(fn: (v: V) => Promise<void>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSettled: () => qc.invalidateQueries(),
  });
}

/**
 * Mutation that pauses the plant. Call `mutate()` with no argument.
 *
 * @see POST /api/plant/pause in Program.cs, which sends a pause command to the gateways.
 */
export const usePause = () => useCommand<void>(() => pause());

/**
 * Mutation that resumes the plant. Call `mutate()` with no argument.
 *
 * @see POST /api/plant/resume in Program.cs, which sends a resume command to the gateways.
 */
export const useResume = () => useCommand<void>(() => resume());

/**
 * Mutation that injects a fault into one machine. Call `mutate({ machineId, kind })`.
 *
 * @see POST /api/machines/{machineId}/faults in Program.cs
 */
export const useInjectFault = () =>
  useCommand<{ machineId: string; kind: AlarmKind }>((v) =>
    injectFault(v.machineId, v.kind),
  );

/**
 * Asks the API to pause the plant.
 *
 * @returns Nothing, once the API accepts.
 * @throws {ApiError} When the API answers with an error status.
 * @see POST /api/plant/pause in Program.cs
 */
export const pause = () => post("/api/plant/pause");

/**
 * Asks the API to resume the plant.
 *
 * @returns Nothing, once the API accepts.
 * @throws {ApiError} When the API answers with an error status.
 * @see POST /api/plant/resume in Program.cs
 */
export const resume = () => post("/api/plant/resume");

/**
 * Asks the API to make one machine raise an alarm of the given kind.
 *
 * @param machineId - Id of the machine, such as `P1-MCH-C017`. It is URL-encoded into the path.
 * @param kind - The alarm kind, sent as `{ "kind": ... }` in the request body.
 * @returns Nothing, once the API accepts.
 * @throws {ApiError} When the API rejects the request. For example, it rejects a malformed or
 * unknown machine id, or a kind that does not apply to the machine's type.
 * @see POST /api/machines/{machineId}/faults in Program.cs
 */
export const injectFault = (machineId: string, kind: AlarmKind) =>
  post(`/api/machines/${encodeURIComponent(machineId)}/faults`, { kind });
