/**
 * Unit tests for `format.ts`, run by `bun test` (47 tests in this file).
 *
 * `format.ts` holds the pure logic of the dashboard, so these tests need no browser, no React and
 * no API: they call each function with set inputs and compare the output. The current time is
 * always passed in as `NOW`, never read from the clock. `src/scripts/build/test-frontend.sh`
 * runs this file and then the strict build. The only import from `api.ts` is the `Reconciliation`
 * type, which mirrors `Reconciliation` in `ApiModels.cs` (`EventDrivenDesign.Contracts`).
 *
 * @module
 */
import type { Reconciliation } from "./api";
import { describe, expect, test } from "bun:test";
import {
  agoText,
  fillBuckets,
  formatEta,
  formatInt,
  formatSeconds,
  healthLabel,
  instanceLabel,
  isStale,
  mirrorHealth,
  panelNote,
  proofView,
  shouldRequestFull,
} from "./format";

/** The set "current time" for the tests: 2026-11-12T12:00:00Z, in epoch milliseconds. */
const NOW = Date.parse("2026-11-12T12:00:00Z");

// healthLabel: the text beside a health dot, including a down source that was never seen.
describe("healthLabel", () => {
  test("healthy", () =>
    expect(healthLabel({ $type: "Healthy" }, NOW)).toBe("Healthy"));
  test("degraded carries its reason", () =>
    expect(
      healthLabel({ $type: "Degraded", reason: "buffer climbing" }, NOW),
    ).toBe("Degraded: buffer climbing"));
  test("down says when it was last seen", () =>
    expect(
      healthLabel({ $type: "Down", lastSeen: "2026-11-12T11:59:30Z" }, NOW),
    ).toBe("Down, last seen 30 s ago"));
  test("down and never seen (Unix epoch)", () =>
    expect(
      healthLabel(
        { $type: "Down", lastSeen: "1970-01-01T00:00:00+00:00" },
        NOW,
      ),
    ).toBe("Down, never seen"));
});

// agoText: "N s ago" and friends; the epoch means never, and a clock ahead is clamped to 0 s.
describe("agoText", () => {
  test("epoch is never", () =>
    expect(agoText("1970-01-01T00:00:00Z", NOW)).toBe("never"));
  test("seconds", () =>
    expect(agoText("2026-11-12T11:59:57Z", NOW)).toBe("3 s ago"));
  test("minutes", () =>
    expect(agoText("2026-11-12T11:57:00Z", NOW)).toBe("3 min ago"));
  test("a clock slightly ahead is now, not negative", () =>
    expect(agoText("2026-11-12T12:00:02Z", NOW)).toBe("0 s ago"));
});

// instanceLabel: the last part of a gateway id; null is a dash.
describe("instanceLabel", () => {
  test("strips the gateway- and the shop", () =>
    expect(instanceLabel("gateway-bdy-b")).toBe("b"));
  test("null is a dash", () => expect(instanceLabel(null)).toBe("—"));
});

// isStale: only a Down tile is treated as history.
describe("isStale", () => {
  test("only Down is stale", () => {
    expect(isStale({ $type: "Down", lastSeen: "2026-11-12T11:59:30Z" })).toBe(
      true,
    );
    expect(isStale({ $type: "Healthy" })).toBe(false);
    expect(isStale({ $type: "Degraded", reason: "x" })).toBe(false);
  });
});

// formatSeconds: one decimal, a dash for null, negatives clamped to 0.
describe("formatSeconds", () => {
  test("null is a dash", () => expect(formatSeconds(null)).toBe("—"));
  test("one decimal", () => expect(formatSeconds(0.84)).toBe("0.8 s"));
  test("negative is clamped", () => expect(formatSeconds(-0.2)).toBe("0.0 s"));
});

// formatEta: seconds under a minute, then "M min SS s".
describe("formatEta", () => {
  test("null is a dash", () => expect(formatEta(null)).toBe("—"));
  test("under a minute", () => expect(formatEta(42.4)).toBe("42 s"));
  test("minutes and seconds", () => expect(formatEta(125)).toBe("2 min 05 s"));
  test("zero", () => expect(formatEta(0)).toBe("0 s"));
});

// formatInt: rounded, with comma separators.
describe("formatInt", () => {
  test("thousands separators", () =>
    expect(formatInt(1204500)).toBe("1,204,500"));
  test("small numbers", () => expect(formatInt(7)).toBe("7"));
  test("rounds", () => expect(formatInt(499.6)).toBe("500"));
});

// fillBuckets: the chart window is filled with 5-second buckets; empty ones are zero, and
// synthesised ones are provisional only after the watermark.
describe("fillBuckets", () => {
  /** End of the window: the same instant as `NOW`, in epoch milliseconds. */
  const end = Date.parse("2026-11-12T12:00:00Z");
  /**
   * The ISO-8601 UTC string for the moment `s` seconds before `end`.
   *
   * @param s - Seconds before the end of the window.
   * @returns For example `iso(30)` is `"2026-11-12T11:59:30.000Z"`.
   */
  const iso = (s: number) => new Date(end - s * 1000).toISOString();
  /** A watermark 20 seconds before the end: buckets older than this are settled. */
  const watermark = iso(20);

  test("covers the whole window in 5 s steps, oldest first", () => {
    const out = fillBuckets([], end, 1, null);
    expect(out.length).toBe(13);
    expect(out[0]!.bucketStart).toBe(iso(60));
    expect(out[12]!.bucketStart).toBe(iso(0));
  });

  test("missing buckets become zero, present ones keep their count", () => {
    const out = fillBuckets(
      [{ bucketStart: iso(30), readings: 2500, provisional: false }],
      end,
      1,
      watermark,
    );
    expect(out.find((p) => p.bucketStart === iso(30))!.readings).toBe(2500);
    expect(out.find((p) => p.bucketStart === iso(35))!.readings).toBe(0);
    expect(out.filter((p) => p.readings === 0).length).toBe(12);
  });

  test("synthesised buckets after the watermark are provisional, before it are not", () => {
    const out = fillBuckets([], end, 1, watermark);
    expect(out.find((p) => p.bucketStart === iso(30))!.provisional).toBe(false);
    expect(out.find((p) => p.bucketStart === iso(10))!.provisional).toBe(true);
  });

  test("an API timestamp with a +00:00 offset still lands on its bucket", () => {
    const out = fillBuckets(
      [
        {
          bucketStart: "2026-11-12T11:59:30+00:00",
          readings: 9,
          provisional: false,
        },
      ],
      end,
      1,
      null,
    );
    expect(out.find((p) => p.bucketStart === iso(30))!.readings).toBe(9);
  });
});

// mirrorHealth: no data and lag over 10 s are Degraded; 10 s exactly and negative lag are Healthy.
describe("mirrorHealth", () => {
  test("no data is degraded", () =>
    expect(mirrorHealth(null)).toEqual({
      $type: "Degraded",
      reason: "no data",
    }));
  test("small lag is healthy", () =>
    expect(mirrorHealth(0)).toEqual({ $type: "Healthy" }));
  test("exactly the limit is still healthy", () =>
    expect(mirrorHealth(10)).toEqual({ $type: "Healthy" }));
  test("over the limit is lagging", () =>
    expect(mirrorHealth(10.1)).toEqual({
      $type: "Degraded",
      reason: "lagging",
    }));
  test("a clock slightly ahead (negative lag) is healthy", () =>
    expect(mirrorHealth(-0.4)).toEqual({ $type: "Healthy" }));
});

// shouldRequestFull: only when paused and drained, and at most once per 10 s.
describe("shouldRequestFull", () => {
  /** A light reconciliation that is paused and drained, the only state that allows a scan. */
  const settled = { paused: true, drained: true };
  test("never before the light response arrives", () =>
    expect(shouldRequestFull(undefined, null, NOW)).toBe(false));
  test("not while running", () =>
    expect(
      shouldRequestFull({ paused: false, drained: false }, null, NOW),
    ).toBe(false));
  test("not while paused but still draining", () =>
    expect(shouldRequestFull({ paused: true, drained: false }, null, NOW)).toBe(
      false,
    ));
  test("the first time paused and drained", () =>
    expect(shouldRequestFull(settled, null, NOW)).toBe(true));
  test("not again within 10 s", () =>
    expect(shouldRequestFull(settled, NOW - 9_999, NOW)).toBe(false));
  test("again after 10 s", () =>
    expect(shouldRequestFull(settled, NOW - 10_000, NOW)).toBe(true));
});

// proofView: the verdict is always the live poll's; duplicate rows come from the full check only while it applies.
describe("proofView", () => {
  /**
   * Builds a paused, drained, PASS reconciliation with no machines, with some fields overridden.
   *
   * @param o - The fields to change.
   * @returns A complete `Reconciliation`. `duplicateRows` is `null` unless overridden.
   */
  const rec = (o: Partial<Reconciliation>): Reconciliation => ({
    generated: 10,
    stored: 10,
    missing: 0,
    gaps: 0,
    duplicatesAbsorbed: 0,
    duplicateRows: null,
    paused: true,
    drained: true,
    verdict: "PASS",
    machines: [],
    ...o,
  });
  /**
   * Builds a full-check result: a reconciliation with 0 duplicate rows plus the time it arrived.
   *
   * @param o - The fields of the reconciliation to change.
   * @param at - Epoch milliseconds when the result arrived. Defaults to 4 s before `NOW`.
   * @returns An object shaped like `useFullCheck`'s `result`.
   */
  const full = (o: Partial<Reconciliation>, at = NOW - 4_000) => ({
    data: rec({ duplicateRows: 0, ...o }),
    at,
  });

  test("both settled: the full result's duplicate rows with their age", () =>
    expect(proofView(rec({}), full({}), NOW)).toEqual({
      verdict: "PASS",
      duplicateRows: 0,
      checkedAgoSeconds: 4,
    }));
  test("full says PASS but the live poll says WAIT: WAIT", () =>
    expect(
      proofView(
        rec({ verdict: "WAIT", paused: false, drained: false }),
        full({}),
        NOW,
      ).verdict,
    ).toBe("WAIT"));
  test("plant no longer paused and drained: not checked, no age", () =>
    expect(
      proofView(
        rec({ verdict: "WAIT", paused: false, drained: false }),
        full({}),
        NOW,
      ),
    ).toEqual({
      verdict: "WAIT",
      duplicateRows: null,
      checkedAgoSeconds: null,
    }));
  test("paused but not yet drained: not checked", () =>
    expect(
      proofView(rec({ verdict: "WAIT", drained: false }), full({}), NOW)
        .duplicateRows,
    ).toBeNull());
  test("no full check yet: not checked", () =>
    expect(proofView(rec({}), undefined, NOW)).toEqual({
      verdict: "PASS",
      duplicateRows: null,
      checkedAgoSeconds: null,
    }));
  test("a live FAIL is not hidden by an older full PASS", () =>
    expect(
      proofView(rec({ verdict: "FAIL" }), full({ verdict: "PASS" }), NOW)
        .verdict,
    ).toBe("FAIL"));
});

// panelNote: no note without an error; the wording depends on whether old data exists and on the reason.
describe("panelNote", () => {
  test("no error, no note", () =>
    expect(panelNote(false, true, "x")).toBeUndefined());
  test("error with data keeps the last-data note", () =>
    expect(panelNote(true, true, "x")).toBe(
      "Not updating, showing the last data received.",
    ));
  test("error before any data names the reason", () =>
    expect(panelNote(true, false, "The central database did not answer.")).toBe(
      "No data yet. The central database did not answer. Retrying every second.",
    ));
  test("error before any data, reason without a full stop", () =>
    expect(panelNote(true, false, "Bad Gateway")).toBe(
      "No data yet. Bad Gateway. Retrying every second.",
    ));
  test("error before any data, no reason", () =>
    expect(panelNote(true, false, undefined)).toBe(
      "No data yet. The API did not answer. Retrying every second.",
    ));
});
