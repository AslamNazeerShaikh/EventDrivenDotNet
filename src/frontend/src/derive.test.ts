/**
 * Unit tests for `derive.ts`, run by `bun test` next to `format.test.ts`.
 *
 * `derive.ts` holds the pure logic behind the redesigned page: the throughput sample buffer, the
 * status texts in the header, the alarm mix and the KPI strip. Like `format.test.ts`, these tests
 * need no browser, no React and no API: they call each function with set inputs and compare the
 * output. The fixtures below build `Overview`, `Reconciliation` and `AlarmView` values by hand, typed
 * by `api.ts`, which mirrors `ApiModels.cs` in `EventDrivenDesign.Contracts`.
 *
 * @module
 */
import { describe, expect, test } from "bun:test";
import type {
  AlarmView,
  ContainerView,
  GatewayTile,
  Overview,
  Reconciliation,
} from "./api";
import {
  addSample,
  alarmMix,
  apiState,
  containerStatus,
  containerTotals,
  formatBytes,
  groupByTier,
  portText,
  uptimeText,
  usagePercent,
  drainStatus,
  kpis,
  newestText,
  plantStatus,
  toneOfHealth,
  utcTime,
  type RateSample,
} from "./derive";

/** A healthy health value, shared by the fixtures. */
const healthy = { $type: "Healthy" } as const;
/** A down health value whose source was last seen at 11:59:00 UTC. */
const down = { $type: "Down", lastSeen: "2026-11-12T11:59:00Z" } as const;

/**
 * Builds a gateway tile that is healthy, running and sending 100 rows per second.
 *
 * @param shop - The shop code.
 * @param over - Fields to replace.
 * @returns The tile.
 */
const gw = (shop: string, over: Partial<GatewayTile> = {}): GatewayTile => ({
  shop,
  health: healthy,
  state: "Running",
  paused: false,
  sentPerSecond: 100,
  bufferedReadings: 0,
  bufferedAlarms: 0,
  oldestUnsentAgeMs: 0,
  generated: 1000,
  instance: `gateway-${shop.toLowerCase()}-a`,
  ...over,
});

/**
 * Builds an overview of a healthy pipeline with two shops and two store writers.
 *
 * @param over - Fields to replace.
 * @returns The overview.
 */
const overview = (over: Partial<Overview> = {}): Overview => ({
  at: "2026-11-12T12:00:00Z",
  gateways: [gw("BDY"), gw("PRS")],
  plantKafka: {
    health: healthy,
    endOffsetTotal: 5000,
    newestDeviceTime: "2026-11-12T11:59:59Z",
  },
  mirrorLagSeconds: 0.4,
  centralKafka: {
    health: healthy,
    endOffsetTotal: 4990,
    newestDeviceTime: "2026-11-12T11:59:59Z",
  },
  consumerLag: 120,
  consumers: [
    {
      consumerId: "store-writer-1",
      health: healthy,
      insertedPerSecond: 250,
      received: 2000,
      inserted: 2000,
    },
    {
      consumerId: "store-writer-2",
      health: healthy,
      insertedPerSecond: 248,
      received: 1990,
      inserted: 1990,
    },
  ],
  database: { health: healthy, stored: 1204500, freshnessSeconds: 0.84 },
  liveRate: 500,
  drainRate: 498,
  drainEtaSeconds: 0,
  ...over,
});

/**
 * Builds a light reconciliation that is paused, drained and PASS.
 *
 * @param over - Fields to replace.
 * @returns The reconciliation.
 */
const proof = (over: Partial<Reconciliation> = {}): Reconciliation => ({
  generated: 1000,
  stored: 1000,
  missing: 0,
  gaps: 0,
  duplicatesAbsorbed: 12,
  duplicateRows: null,
  paused: true,
  drained: true,
  verdict: "PASS",
  machines: [],
  ...over,
});

/**
 * Builds an alarm stored at central.
 *
 * @param over - Fields to replace.
 * @returns The alarm.
 */
const alarm = (over: Partial<AlarmView> = {}): AlarmView => ({
  machineId: "P1-MCH-C017",
  alarmSeq: 1,
  kind: "Vibration",
  severity: "warning",
  deviceTime: "2026-11-12T11:59:58Z",
  storedAt: "2026-11-12T11:59:59Z",
  latencySeconds: 0.5,
  ...over,
});

/**
 * Shorthand for a throughput sample.
 *
 * @param t - Time in epoch milliseconds.
 * @param live - Live rate.
 * @param drain - Drain rate.
 * @returns The sample.
 */
const s = (t: number, live = 500, drain = 498): RateSample => ({
  t,
  live,
  drain,
});

// addSample: the client-side buffer behind the live throughput chart.
describe("addSample", () => {
  test("appends to an empty buffer", () =>
    expect(addSample([], s(1000), 60_000)).toEqual([s(1000)]));
  test("appends a newer sample at the end", () =>
    expect(addSample([s(1000)], s(2000), 60_000)).toEqual([s(1000), s(2000)]));
  test("drops samples older than the window before the new one", () =>
    expect(addSample([s(0), s(1000), s(2000)], s(3000), 2000)).toEqual([
      s(1000),
      s(2000),
      s(3000),
    ]));
  test("ignores a sample at the same time and returns the same array", () => {
    const buf = [s(1000)];
    expect(addSample(buf, s(1000, 1, 1), 60_000)).toBe(buf);
  });
  test("ignores a sample older than the newest one", () => {
    const buf = [s(1000), s(2000)];
    expect(addSample(buf, s(1500), 60_000)).toBe(buf);
  });
});

// toneOfHealth: the colour meaning of a health value.
describe("toneOfHealth", () => {
  test("healthy is ok", () => expect(toneOfHealth(healthy)).toBe("ok"));
  test("degraded is warn", () =>
    expect(toneOfHealth({ $type: "Degraded", reason: "x" })).toBe("warn"));
  test("down is bad", () => expect(toneOfHealth(down)).toBe("bad"));
  test("an unknown case is warn", () =>
    expect(toneOfHealth({ $type: "Odd" } as never)).toBe("warn"));
});

// apiState: the connection pill in the header.
describe("apiState", () => {
  test("an error is unreachable", () =>
    expect(apiState(true, true)).toEqual({
      text: "API unreachable",
      tone: "bad",
    }));
  test("no data yet is connecting", () =>
    expect(apiState(false, false)).toEqual({
      text: "Connecting",
      tone: "idle",
    }));
  test("data and no error is connected", () =>
    expect(apiState(false, true)).toEqual({
      text: "API connected",
      tone: "ok",
    }));
});

// plantStatus: the plant pill in the header.
describe("plantStatus", () => {
  test("running", () =>
    expect(plantStatus(false)).toEqual({ text: "Running", tone: "ok" }));
  test("paused", () =>
    expect(plantStatus(true)).toEqual({ text: "Paused", tone: "info" }));
  test("unknown", () =>
    expect(plantStatus(undefined)).toEqual({ text: "Unknown", tone: "idle" }));
});

// drainStatus: what the drain ETA means, in words.
describe("drainStatus", () => {
  test("zero is no backlog", () =>
    expect(drainStatus(0)).toEqual({ text: "No backlog", tone: "ok" }));
  test("a positive ETA is draining", () =>
    expect(drainStatus(42)).toEqual({
      text: "Draining backlog",
      tone: "info",
    }));
  test("null is unknown", () =>
    expect(drainStatus(null)).toEqual({
      text: "Unknown at current rates",
      tone: "idle",
    }));
});

// alarmMix: counts by kind and severity over the alarms the page has.
describe("alarmMix", () => {
  test("an empty list has zero counts for every known kind and severity", () => {
    const m = alarmMix([]);
    expect(m.total).toBe(0);
    expect(m.byKind.map((k) => k.name)).toEqual([
      "Overheat",
      "Vibration",
      "Overload",
      "EStop",
      "LowBattery",
      "OutOfBand",
      "DataGap",
    ]);
    expect(m.byKind.every((k) => k.count === 0)).toBe(true);
    expect(m.bySeverity).toEqual([
      { name: "critical", count: 0 },
      { name: "high", count: 0 },
      { name: "warning", count: 0 },
    ]);
    expect(m.latestLatency).toBeNull();
    expect(m.maxLatency).toBeNull();
  });
  test("counts kinds and severities", () => {
    const m = alarmMix([
      alarm({ alarmSeq: 3, kind: "Overheat", severity: "critical" }),
      alarm({ alarmSeq: 2 }),
      alarm({ alarmSeq: 1 }),
    ]);
    expect(m.total).toBe(3);
    expect(m.byKind.find((k) => k.name === "Vibration")?.count).toBe(2);
    expect(m.byKind.find((k) => k.name === "Overheat")?.count).toBe(1);
    expect(m.bySeverity).toEqual([
      { name: "critical", count: 1 },
      { name: "high", count: 0 },
      { name: "warning", count: 2 },
    ]);
  });
  test("an unknown kind or severity is added after the known ones", () => {
    const m = alarmMix([alarm({ kind: "Melt" as never, severity: "info" })]);
    expect(m.byKind.at(-1)).toEqual({ name: "Melt", count: 1 });
    expect(m.bySeverity.at(-1)).toEqual({ name: "info", count: 1 });
  });
  test("latest latency is the first alarm's, max is the largest", () => {
    const m = alarmMix([
      alarm({ latencySeconds: 0.2 }),
      alarm({ alarmSeq: 2, latencySeconds: 7.5 }),
      alarm({ alarmSeq: 3, latencySeconds: 1 }),
    ]);
    expect(m.latestLatency).toBe(0.2);
    expect(m.maxLatency).toBe(7.5);
  });
});

// kpis: the eight cards of the KPI strip.
describe("kpis", () => {
  /**
   * Finds one card by its label.
   *
   * @param list - The cards.
   * @param label - The label to find.
   * @returns The card. The test fails if it is missing.
   */
  const card = (list: ReturnType<typeof kpis>, label: string) => {
    const k = list.find((x) => x.label === label);
    if (!k) throw new Error(`no card ${label}`);
    return k;
  };
  test("eight cards in a set order", () =>
    expect(kpis(overview(), proof()).map((k) => k.label)).toEqual([
      "Live rate",
      "Drain rate",
      "Rows stored",
      "Mirror lag",
      "Consumer lag",
      "Drain ETA",
      "Duplicates absorbed",
      "Proof",
    ]));
  test("a healthy pipeline", () => {
    const list = kpis(overview(), proof());
    expect(card(list, "Live rate")).toEqual({
      label: "Live rate",
      value: "500",
      unit: "readings/s",
      sub: "2 of 2 gateways live",
      tone: "ok",
    });
    expect(card(list, "Drain rate")).toMatchObject({
      value: "498",
      unit: "rows/s",
      sub: "2 of 2 writers up",
      tone: "ok",
    });
    expect(card(list, "Rows stored")).toMatchObject({
      value: "1,204,500",
      sub: "fresh 0.8 s",
      tone: "ok",
    });
    expect(card(list, "Mirror lag")).toMatchObject({
      value: "0.4 s",
      sub: "within the 10 s limit",
      tone: "ok",
    });
    expect(card(list, "Consumer lag")).toMatchObject({
      value: "120",
      unit: "records",
      tone: "idle",
    });
    expect(card(list, "Drain ETA")).toMatchObject({
      value: "0 s",
      sub: "No backlog",
      tone: "ok",
    });
    expect(card(list, "Duplicates absorbed")).toMatchObject({
      value: "12",
      tone: "idle",
    });
    expect(card(list, "Proof")).toMatchObject({
      value: "PASS",
      sub: "plant paused, drained",
      tone: "ok",
    });
  });
  test("before any data every value is a dash with no unit", () => {
    const list = kpis(undefined, undefined);
    expect(list).toHaveLength(8);
    for (const k of list) {
      expect(k.value).toBe("—");
      expect(k.unit).toBe("");
      expect(k.tone).toBe("idle");
    }
    expect(card(list, "Live rate").sub).toBe("waiting for the API");
    expect(card(list, "Proof").sub).toBe("waiting for data");
  });
  test("a down or paused gateway is not live", () => {
    const o = overview({
      gateways: [
        gw("BDY", { health: down }),
        gw("PRS", { paused: true }),
        gw("ASM"),
      ],
    });
    expect(card(kpis(o, undefined), "Live rate")).toMatchObject({
      sub: "1 of 3 gateways live",
      tone: "warn",
    });
  });
  test("every gateway paused says the plant is paused", () => {
    const o = overview({
      gateways: [gw("BDY", { paused: true }), gw("PRS", { paused: true })],
    });
    expect(card(kpis(o, undefined), "Live rate")).toMatchObject({
      sub: "plant paused",
      tone: "info",
    });
  });
  test("store writers: some down is warn, all down is bad", () => {
    const one = overview();
    one.consumers[0]!.health = down;
    expect(card(kpis(one, undefined), "Drain rate")).toMatchObject({
      sub: "1 of 2 writers up",
      tone: "warn",
    });
    const all = overview();
    for (const c of all.consumers) c.health = down;
    expect(card(kpis(all, undefined), "Drain rate")).toMatchObject({
      sub: "0 of 2 writers up",
      tone: "bad",
    });
  });
  test("mirror lag above the limit or unknown is warn", () => {
    expect(
      card(kpis(overview({ mirrorLagSeconds: 12 }), undefined), "Mirror lag"),
    ).toMatchObject({ value: "12.0 s", sub: "lagging", tone: "warn" });
    expect(
      card(kpis(overview({ mirrorLagSeconds: null }), undefined), "Mirror lag"),
    ).toMatchObject({ value: "—", sub: "no data", tone: "warn" });
  });
  test("database health colours rows stored", () => {
    const o = overview({
      database: { health: down, stored: 7, freshnessSeconds: null },
    });
    expect(card(kpis(o, undefined), "Rows stored")).toMatchObject({
      value: "7",
      sub: "fresh —",
      tone: "bad",
    });
  });
  test("drain ETA while draining", () =>
    expect(
      card(kpis(overview({ drainEtaSeconds: 125 }), undefined), "Drain ETA"),
    ).toMatchObject({
      value: "2 min 05 s",
      sub: "Draining backlog",
      tone: "info",
    }));
  test("verdicts map to tones; an unknown verdict is UNKNOWN", () => {
    const v = (verdict: string, over: Partial<Reconciliation> = {}) =>
      card(
        kpis(undefined, proof({ verdict: verdict as never, ...over })),
        "Proof",
      );
    expect(v("WAIT", { paused: false, drained: false })).toMatchObject({
      value: "WAIT",
      sub: "plant running, not drained",
      tone: "warn",
    });
    expect(v("FAIL")).toMatchObject({ value: "FAIL", tone: "bad" });
    expect(v("MAYBE")).toMatchObject({ value: "UNKNOWN", tone: "warn" });
  });
});

// utcTime: a 24-hour UTC clock time for the header and the throughput axis.
describe("utcTime", () => {
  test("from an ISO string", () =>
    expect(utcTime("2026-11-12T11:59:30Z")).toBe("11:59:30"));
  test("from epoch milliseconds", () =>
    expect(utcTime(Date.parse("2026-11-12T23:05:09+00:00"))).toBe("23:05:09"));
});

// newestText: the age of a cluster's newest record.
describe("newestText", () => {
  const now = Date.parse("2026-11-12T12:00:00Z");
  test("null is no data", () => expect(newestText(null, now)).toBe("no data"));
  test("a time is its age", () =>
    expect(newestText("2026-11-12T11:59:57Z", now)).toBe("3 s ago"));
});

/** A running, healthy container; each test overrides the fields it is about. */
const box = (o: Partial<ContainerView> = {}): ContainerView => ({
  name: "edd-api-1-1",
  service: "api-1",
  tier: "Control room",
  tierOrder: 7,
  state: "running",
  health: "healthy",
  status: "Up 5 minutes (healthy)",
  image: "edd-api",
  imageBytes: 1000,
  ports: [],
  cpuPercent: 10,
  memoryUsedBytes: 100,
  memoryLimitBytes: 400,
  netRxBytes: 1,
  netTxBytes: 2,
  blockReadBytes: 3,
  blockWriteBytes: 4,
  writableBytes: 50,
  volumes: [],
  startedAt: null,
  ...o,
});

// formatBytes: sizes in B, KB, MB and GB, one decimal place above bytes.
describe("formatBytes", () => {
  test("null is a dash", () => expect(formatBytes(null)).toBe("—"));
  test("bytes", () => expect(formatBytes(512)).toBe("512 B"));
  test("kilobytes", () => expect(formatBytes(1536)).toBe("1.5 KB"));
  test("megabytes", () => expect(formatBytes(5 * 1024 ** 2)).toBe("5.0 MB"));
  test("gigabytes", () => expect(formatBytes(2.25 * 1024 ** 3)).toBe("2.3 GB"));
});

// usagePercent: the width of a usage bar, clamped to 0–100.
describe("usagePercent", () => {
  test("a share", () => expect(usagePercent(25, 100)).toBe(25));
  test("over the limit is 100", () => expect(usagePercent(150, 100)).toBe(100));
  test("no limit is 0", () => expect(usagePercent(5, null)).toBe(0));
  test("no value is 0", () => expect(usagePercent(null, 100)).toBe(0));
});

// groupByTier: tiers in data-flow order, containers by name inside each.
describe("groupByTier", () => {
  test("orders tiers and names", () => {
    const groups = groupByTier([
      box({ name: "b", tier: "Control room", tierOrder: 7 }),
      box({ name: "z", tier: "Edge gateways", tierOrder: 1 }),
      box({ name: "a", tier: "Control room", tierOrder: 7 }),
    ]);
    expect(
      groups.map((g) => [g.tier, g.containers.map((c) => c.name)]),
    ).toEqual([
      ["Edge gateways", ["z"]],
      ["Control room", ["a", "b"]],
    ]);
  });
  test("a tier is green when all its containers are fine", () =>
    expect(groupByTier([box()])[0]?.tone).toBe("ok"));
  test("a finished one-shot job does not turn its tier amber", () =>
    expect(
      groupByTier([
        box(),
        box({ state: "exited", health: null, status: "Exited (0) 1 hour ago" }),
      ])[0]?.tone,
    ).toBe("ok"));
  test("an unhealthy container turns its tier amber", () =>
    expect(groupByTier([box(), box({ health: "unhealthy" })])[0]?.tone).toBe(
      "warn",
    ));
});

// containerStatus: the state and health text with its colour.
describe("containerStatus", () => {
  test("running and healthy", () =>
    expect(containerStatus(box())).toEqual({
      text: "running, healthy",
      tone: "ok",
    }));
  test("running without a health check", () =>
    expect(containerStatus(box({ health: null }))).toEqual({
      text: "running",
      tone: "ok",
    }));
  test("starting", () =>
    expect(containerStatus(box({ health: "starting" }))).toEqual({
      text: "running, starting",
      tone: "warn",
    }));
  test("unhealthy", () =>
    expect(containerStatus(box({ health: "unhealthy" }))).toEqual({
      text: "running, unhealthy",
      tone: "bad",
    }));
  test("exited cleanly", () =>
    expect(
      containerStatus(
        box({
          state: "exited",
          health: null,
          status: "Exited (0) 2 hours ago",
        }),
      ),
    ).toEqual({
      text: "exited 0",
      tone: "idle",
    }));
  test("exited with an error", () =>
    expect(
      containerStatus(
        box({
          state: "exited",
          health: null,
          status: "Exited (137) 1 minute ago",
        }),
      ),
    ).toEqual({
      text: "exited 137",
      tone: "bad",
    }));
  test("restarting", () =>
    expect(
      containerStatus(
        box({
          state: "restarting",
          health: null,
          status: "Restarting (1) 2 seconds ago",
        }),
      ),
    ).toEqual({
      text: "restarting",
      tone: "warn",
    }));
});

// uptimeText: how long a running container has been up, from Docker's status text.
describe("uptimeText", () => {
  test("running", () => expect(uptimeText(box())).toBe("5 minutes"));
  test("not running", () =>
    expect(
      uptimeText(box({ state: "exited", status: "Exited (0) 2 hours ago" })),
    ).toBe("—"));
});

// portText: host→container/protocol, or container/protocol when not published.
describe("portText", () => {
  test("published", () =>
    expect(portText({ private: 8080, public: 3000, type: "tcp" })).toBe(
      "3000→8080/tcp",
    ));
  test("not published", () =>
    expect(portText({ private: 9092, public: null, type: "tcp" })).toBe(
      "9092/tcp",
    ));
});

// containerTotals: the summary line.
describe("containerTotals", () => {
  test("sums usage and counts disk once per volume and image", () => {
    const vol = { name: "v", destination: "/d", bytes: 300 };
    expect(
      containerTotals([
        box({ volumes: [vol] }),
        box({ volumes: [vol], cpuPercent: 5 }),
        box({
          state: "exited",
          cpuPercent: null,
          memoryUsedBytes: null,
          memoryLimitBytes: null,
          image: "other",
          imageBytes: 7,
        }),
      ]),
    ).toEqual({
      running: 2,
      total: 3,
      cpuPercent: 15,
      memoryUsedBytes: 200,
      memoryLimitBytes: 800,
      writableBytes: 150,
      volumeBytes: 300,
      imageBytes: 1007,
      diskBytes: 1457,
    });
  });
});
