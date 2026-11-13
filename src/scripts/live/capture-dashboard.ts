/**
 * capture-dashboard.ts: a full-page PNG of the running control-room dashboard.
 *
 * Adapted from the PlaywrightDotNet project's tools/screenshots/capture.ts. It uses the same mechanism:
 * Playwright's bundled Chromium, driven over the DevTools protocol on an ephemeral port, with the page
 * height measured so the file fits the page. The login, route list and theme toggle are dropped: the
 * dashboard is one dark page with no sign-in.
 *
 * Run (the stack must be up, see docs/05-run-and-operate.md section 4):
 *   bun src/scripts/live/capture-dashboard.ts
 *
 * Env:
 *   DASHBOARD_URL  default http://localhost:4000
 *   SHOT_OUT       default docs/screenshots/dashboard.png
 *   SHOT_WIDTH     viewport width in CSS px, default 2560 (a full-screen browser on a 2560x1440 display)
 *   SHOT_SETTLE_MS wait after load so every 1 s poll has data, default 8000
 *   PLAYWRIGHT_BROWSERS_PATH  default ~/Library/Caches/ms-playwright (macOS), ~/.cache/ms-playwright (Linux)
 *
 * Needs a bundled Chromium: `bunx playwright install chromium` once.
 * Exit code: 0 when the PNG is written; non-zero otherwise.
 */
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync } from "node:fs";
import { homedir, tmpdir } from "node:os";
import { dirname, join } from "node:path";

const URL = process.env["DASHBOARD_URL"] ?? "http://localhost:4000";
const OUT = process.env["SHOT_OUT"] ?? "docs/screenshots/dashboard.png";
const WIDTH = Number(process.env["SHOT_WIDTH"] ?? 2560);
const SETTLE_MS = Number(process.env["SHOT_SETTLE_MS"] ?? 8000);

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

/** The highest installed bundled Chromium revision. */
function bundledChromium(): string {
  const root =
    process.env["PLAYWRIGHT_BROWSERS_PATH"] ??
    (process.platform === "darwin"
      ? join(homedir(), "Library", "Caches", "ms-playwright")
      : join(homedir(), ".cache", "ms-playwright"));
  const revisions = (existsSync(root) ? readdirSync(root) : [])
    .map((d) => /^chromium-(\d+)$/.exec(d)?.[1])
    .filter((v): v is string => v !== undefined)
    .map(Number)
    .sort((a, b) => b - a);
  for (const rev of revisions) {
    const dir = join(root, `chromium-${rev}`);
    const bin =
      process.platform === "darwin"
        ? join(dir, `chrome-mac-${process.arch === "arm64" ? "arm64" : "x64"}`, "Google Chrome for Testing.app",
            "Contents", "MacOS", "Google Chrome for Testing")
        : join(dir, "chrome-linux", "chrome");
    if (existsSync(bin)) return bin;
  }
  throw new Error(`No bundled Chromium under ${root}; run \`bunx playwright install chromium\`.`);
}

const profileDir = mkdtempSync(join(tmpdir(), "dashboard-shot-"));
const browser = Bun.spawn(
  [bundledChromium(), "--headless", "--remote-debugging-port=0", `--user-data-dir=${profileDir}`, "about:blank"],
  { stdout: "ignore", stderr: "ignore" },
);

// Port 0: Chromium picks a free port and writes it to DevToolsActivePort in our own throwaway profile.
let cdp: number | null = null;
for (let i = 0; i < 40 && cdp === null; i++) {
  try {
    const port = Number(readFileSync(join(profileDir, "DevToolsActivePort"), "utf8").split("\n")[0]?.trim());
    if (Number.isInteger(port) && port > 0) cdp = port;
  } catch {
    // Not written yet.
  }
  if (cdp === null) await sleep(500);
}
if (cdp === null) {
  browser.kill();
  rmSync(profileDir, { recursive: true, force: true });
  throw new Error("Bundled Chromium did not report a DevTools port within 20 s");
}

const tab = await fetch(`http://127.0.0.1:${cdp}/json/new?about:blank`, { method: "PUT" }).then((r) => r.json());
const ws = new WebSocket(tab.webSocketDebuggerUrl);
await new Promise<void>((resolve) => ws.addEventListener("open", () => resolve(), { once: true }));
let id = 0;
const pending = new Map<number, (v: any) => void>();
ws.addEventListener("message", (e) => {
  const msg = JSON.parse(String(e.data));
  if (msg.id !== undefined && pending.has(msg.id)) {
    pending.get(msg.id)!(msg);
    pending.delete(msg.id);
  }
});
const send = (method: string, params: any = {}): Promise<any> => {
  const cur = ++id;
  ws.send(JSON.stringify({ id: cur, method, params }));
  return new Promise((resolve) => pending.set(cur, resolve));
};
const evalJs = async (expression: string) =>
  (await send("Runtime.evaluate", { expression, returnByValue: true })).result?.result?.value;

try {
  await send("Page.enable");
  await send("Runtime.enable");
  // A tall viewport first, so the whole page lays out and every panel renders its data.
  await send("Emulation.setDeviceMetricsOverride", { width: WIDTH, height: 3000, deviceScaleFactor: 1, mobile: false });
  await send("Page.navigate", { url: URL });
  await sleep(SETTLE_MS);
  // The content's own bottom: the document's scroll height never drops below the tall viewport above.
  const bottom = Number(await evalJs("Math.ceil(document.getElementById('root')?.getBoundingClientRect().bottom ?? 0)"));
  const height = Math.min(Math.max((bottom || 1056) + 24, 800), 8000);
  await send("Emulation.setDeviceMetricsOverride", { width: WIDTH, height, deviceScaleFactor: 1, mobile: false });
  await sleep(1500);
  const shot = await send("Page.captureScreenshot", { format: "png", captureBeyondViewport: false });
  mkdirSync(dirname(OUT), { recursive: true });
  await Bun.write(OUT, Buffer.from(shot.result.data, "base64"));
  console.log(`saved ${OUT} (${WIDTH}x${height})`);
} finally {
  ws.close();
  browser.kill();
  await browser.exited.catch(() => {});
  rmSync(profileDir, { recursive: true, force: true });
}
