import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useCallback, useEffect, useRef, useState } from "react";
import { shouldRequestFull } from "./format";

// Wire types mirror src/backend/EventDrivenDesign.Contracts (camelCase, DateTimeOffset as ISO strings).
export type ServiceHealth =
  | { $type: "Healthy" }
  | { $type: "Degraded"; reason: string }
  | { $type: "Down"; lastSeen: string };

export interface GatewayTile {
  shop: string;
  health: ServiceHealth;
  state: string;
  paused: boolean;
  sentPerSecond: number;
  bufferedReadings: number;
  bufferedAlarms: number;
  oldestUnsentAgeMs: number;
  generated: number;
  instance: string | null;
}
export interface ClusterTile {
  health: ServiceHealth;
  endOffsetTotal: number;
  newestDeviceTime: string | null;
}
export interface ConsumerTile {
  consumerId: string;
  health: ServiceHealth;
  insertedPerSecond: number;
  received: number;
  inserted: number;
}
export interface DatabaseTile {
  health: ServiceHealth;
  stored: number;
  freshnessSeconds: number | null;
}
export interface Overview {
  at: string;
  gateways: GatewayTile[];
  plantKafka: ClusterTile;
  mirrorLagSeconds: number | null;
  centralKafka: ClusterTile;
  consumerLag: number;
  consumers: ConsumerTile[];
  database: DatabaseTile;
  liveRate: number;
  drainRate: number;
  drainEtaSeconds: number | null;
}

export interface MachineProof {
  machineId: string;
  generated: number;
  stored: number;
  missing: number;
  gaps: number;
}
export type Verdict = "PASS" | "WAIT" | "FAIL";
export interface Reconciliation {
  generated: number;
  stored: number;
  missing: number;
  gaps: number;
  duplicatesAbsorbed: number;
  duplicateRows: number | null;
  paused: boolean;
  drained: boolean;
  verdict: Verdict;
  machines: MachineProof[];
}

export interface ChartPoint {
  bucketStart: string;
  readings: number;
  provisional: boolean;
}
export interface Chart {
  watermark: string | null;
  points: ChartPoint[];
}

export const ALARM_KINDS = ["Overheat", "Vibration", "Overload", "EStop", "LowBattery", "OutOfBand", "DataGap"] as const;
export type AlarmKind = (typeof ALARM_KINDS)[number];
export interface AlarmView {
  machineId: string;
  alarmSeq: number;
  kind: AlarmKind;
  severity: string;
  deviceTime: string;
  storedAt: string;
  latencySeconds: number;
}

// RFC 9457 problem+json: title carries the error code.
export class ApiError extends Error {
  constructor(
    public readonly code: string,
    message: string,
  ) {
    super(message);
  }
}

async function failure(res: Response): Promise<ApiError> {
  try {
    const p = (await res.json()) as { title?: string; detail?: string };
    return new ApiError(p.title ?? `http.${res.status}`, p.detail ?? res.statusText);
  } catch {
    return new ApiError(`http.${res.status}`, res.statusText || "Request failed");
  }
}

async function get<T>(path: string): Promise<T> {
  const res = await fetch(path);
  if (!res.ok) throw await failure(res);
  return (await res.json()) as T;
}

async function post(path: string, body?: unknown): Promise<void> {
  const res = await fetch(path, {
    method: "POST",
    headers: body === undefined ? undefined : { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!res.ok) throw await failure(res);
}

const poll = { refetchInterval: 1000, refetchIntervalInBackground: true, retry: false } as const;

export const useOverview = () => useQuery({ queryKey: ["overview"], queryFn: () => get<Overview>("/api/overview"), ...poll });
// The polled call is the light one. The full call counts duplicate rows across the whole table, so see useFullCheck.
export const useReconciliation = () =>
  useQuery({ queryKey: ["reconciliation"], queryFn: () => get<Reconciliation>("/api/reconciliation"), ...poll });

// The full reconciliation (duplicate rows, per-machine table) on demand, and automatically at most every 10 s
// while the plant is paused and drained. A failed attempt counts too, so a slow scan is not hammered.
export function useFullCheck(light: Reconciliation | undefined) {
  const [result, setResult] = useState<{ data: Reconciliation; at: number } | undefined>();
  const [pending, setPending] = useState(false);
  const busy = useRef(false);
  const lastAttempt = useRef<number | null>(null);
  const check = useCallback(async () => {
    if (busy.current) return;
    busy.current = true;
    lastAttempt.current = Date.now();
    setPending(true);
    try {
      setResult({ data: await get<Reconciliation>("/api/reconciliation?full=true"), at: Date.now() });
    } catch {
      // keep the previous result; the light poll and the banner report API trouble
    } finally {
      busy.current = false;
      setPending(false);
    }
  }, []);
  useEffect(() => {
    if (shouldRequestFull(light, lastAttempt.current, Date.now())) void check();
  }, [light, check]);
  return { result, pending, check };
}
export const useChart = () => useQuery({ queryKey: ["chart"], queryFn: () => get<Chart>("/api/chart?minutes=10"), ...poll });
export const useAlarms = () => useQuery({ queryKey: ["alarms"], queryFn: () => get<AlarmView[]>("/api/alarms"), ...poll });

// Commands refresh everything straight away so the page does not wait for the next tick.
function useCommand<V>(fn: (v: V) => Promise<void>) {
  const qc = useQueryClient();
  return useMutation({ mutationFn: fn, onSettled: () => qc.invalidateQueries() });
}
export const usePause = () => useCommand<void>(() => pause());
export const useResume = () => useCommand<void>(() => resume());
export const useInjectFault = () => useCommand<{ machineId: string; kind: AlarmKind }>((v) => injectFault(v.machineId, v.kind));

export const pause = () => post("/api/plant/pause");
export const resume = () => post("/api/plant/resume");
export const injectFault = (machineId: string, kind: AlarmKind) =>
  post(`/api/machines/${encodeURIComponent(machineId)}/faults`, { kind });
