import { describe, expect, test } from "bun:test";
import { agoText, fillBuckets, formatEta, formatInt, formatSeconds, healthLabel, isStale } from "./format";

const NOW = Date.parse("2026-11-12T12:00:00Z");

describe("healthLabel", () => {
  test("healthy", () => expect(healthLabel({ $type: "Healthy" }, NOW)).toBe("Healthy"));
  test("degraded carries its reason", () =>
    expect(healthLabel({ $type: "Degraded", reason: "buffer climbing" }, NOW)).toBe("Degraded: buffer climbing"));
  test("down says when it was last seen", () =>
    expect(healthLabel({ $type: "Down", lastSeen: "2026-11-12T11:59:30Z" }, NOW)).toBe("Down, last seen 30 s ago"));
  test("down and never seen (Unix epoch)", () =>
    expect(healthLabel({ $type: "Down", lastSeen: "1970-01-01T00:00:00+00:00" }, NOW)).toBe("Down, never seen"));
});

describe("agoText", () => {
  test("epoch is never", () => expect(agoText("1970-01-01T00:00:00Z", NOW)).toBe("never"));
  test("seconds", () => expect(agoText("2026-11-12T11:59:57Z", NOW)).toBe("3 s ago"));
  test("minutes", () => expect(agoText("2026-11-12T11:57:00Z", NOW)).toBe("3 min ago"));
  test("a clock slightly ahead is now, not negative", () => expect(agoText("2026-11-12T12:00:02Z", NOW)).toBe("0 s ago"));
});

describe("isStale", () => {
  test("only Down is stale", () => {
    expect(isStale({ $type: "Down", lastSeen: "2026-11-12T11:59:30Z" })).toBe(true);
    expect(isStale({ $type: "Healthy" })).toBe(false);
    expect(isStale({ $type: "Degraded", reason: "x" })).toBe(false);
  });
});

describe("formatSeconds", () => {
  test("null is a dash", () => expect(formatSeconds(null)).toBe("—"));
  test("one decimal", () => expect(formatSeconds(0.84)).toBe("0.8 s"));
  test("negative is clamped", () => expect(formatSeconds(-0.2)).toBe("0.0 s"));
});

describe("formatEta", () => {
  test("null is a dash", () => expect(formatEta(null)).toBe("—"));
  test("under a minute", () => expect(formatEta(42.4)).toBe("42 s"));
  test("minutes and seconds", () => expect(formatEta(125)).toBe("2 min 05 s"));
  test("zero", () => expect(formatEta(0)).toBe("0 s"));
});

describe("formatInt", () => {
  test("thousands separators", () => expect(formatInt(1204500)).toBe("1,204,500"));
  test("small numbers", () => expect(formatInt(7)).toBe("7"));
  test("rounds", () => expect(formatInt(499.6)).toBe("500"));
});

describe("fillBuckets", () => {
  const end = Date.parse("2026-11-12T12:00:00Z");
  const iso = (s: number) => new Date(end - s * 1000).toISOString();
  const watermark = iso(20);

  test("covers the whole window in 5 s steps, oldest first", () => {
    const out = fillBuckets([], end, 1, null);
    expect(out.length).toBe(13);
    expect(out[0]!.bucketStart).toBe(iso(60));
    expect(out[12]!.bucketStart).toBe(iso(0));
  });

  test("missing buckets become zero, present ones keep their count", () => {
    const out = fillBuckets([{ bucketStart: iso(30), readings: 2500, provisional: false }], end, 1, watermark);
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
    const out = fillBuckets([{ bucketStart: "2026-11-12T11:59:30+00:00", readings: 9, provisional: false }], end, 1, null);
    expect(out.find((p) => p.bucketStart === iso(30))!.readings).toBe(9);
  });
});
