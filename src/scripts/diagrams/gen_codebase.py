#!/usr/bin/env python3
"""gen_codebase.py: regenerate diagrams 09 (backend codebase) and 10 (frontend codebase).

Writes docs/diagrams/09-backend-codebase.html and docs/diagrams/10-frontend-codebase.html,
the editable sources the README links to. Every card, line and arrow is defined here, so
edit here, not in the HTML. Card text is checked to fit its card (Geist Mono 9 px).

The layout follows the diagram-design skill (default style, faithful detail level, zoned):
a 4 px grid, orthogonal connectors only, at most two accent elements per diagram.

Usage:
  python3 src/scripts/diagrams/gen_codebase.py
  src/scripts/diagrams/check.sh --export 09-backend-codebase 10-frontend-codebase
"""
import subprocess
from html import escape
from pathlib import Path

ROOT = Path(subprocess.run(["git", "-C", str(Path(__file__).resolve().parent), "rev-parse", "--show-toplevel"],
                           capture_output=True, text=True, check=True).stdout.strip())
OUT = ROOT / "docs" / "diagrams"

PAPER, INK, MUTED, SOFT, ACCENT, LINK = "#f5f5f5", "#2d3142", "#4f5d75", "#5a6580", "#bf4520", "#2e5aa8"
SANS, MONO, SERIF = "'Geist', sans-serif", "'Geist Mono', monospace", "'Instrument Serif', serif"
CH = 5.45   # Geist Mono advance at 9 px
STEP = 13   # line pitch inside a card

T = {  # node treatments: fill, stroke, width, dash, tag colour
    "focal":   ("rgba(191,69,32,0.08)", ACCENT, 1.2, None, "rgba(191,69,32,0.85)"),
    "service": ("#ffffff", INK, 1, None, "rgba(45,49,66,0.8)"),
    "store":   ("rgba(45,49,66,0.05)", MUTED, 1, None, "rgba(79,93,117,0.9)"),
    "core":    ("rgba(79,93,117,0.08)", SOFT, 1, None, "rgba(90,101,128,0.9)"),
    "chip":    ("rgba(45,49,66,0.03)", "rgba(45,49,66,0.30)", 0.8, None, "rgba(45,49,66,0.7)"),
}


class Canvas:
    def __init__(self):
        self.zones, self.arrows, self.labels, self.nodes, self.extras = [], [], [], [], []

    @staticmethod
    def text(x, y, s, size=9, fill=MUTED, font=MONO, weight=None, anchor="start", spacing=None):
        a = f' font-weight="{weight}"' if weight else ""
        a += f' letter-spacing="{spacing}"' if spacing else ""
        return (f'<text x="{x}" y="{y}" fill="{fill}" font-size="{size}"{a} '
                f'font-family="{font}" text-anchor="{anchor}">{escape(s)}</text>')

    def tag(self, x, y, label, color):
        w = round(len(label) * 4.9 + 12)
        w += (-w) % 4
        self.nodes.append(f'<rect x="{x}" y="{y}" width="{w}" height="12" rx="2" fill="transparent" stroke="{color}" '
                          f'stroke-opacity="0.5" stroke-width="0.8"/>')
        self.nodes.append(self.text(x + w / 2, y + 9, label, 7, color, MONO, anchor="middle", spacing="0.08em"))

    def box(self, x, y, w, h, kind):
        fill, stroke, sw, dash, _ = T[kind]
        d = f' stroke-dasharray="{dash}"' if dash else ""
        self.nodes.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="6" fill="{PAPER}"/>')
        self.nodes.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="6" fill="{fill}" stroke="{stroke}" '
                          f'stroke-width="{sw}"{d}/>')

    @staticmethod
    def card_height(lines):
        h = 64 + sum(STEP + (4 if s.startswith("§") else 0) for s in lines) + 4
        return h + (-h) % 4

    def card(self, x, y, w, h, kind, tag_label, name, sub, lines):
        """A node: tag, name (Geist 600), a mono sublabel, then lines. '§' starts a section header."""
        fit = int((w - 24) / CH)
        for s in [sub, *lines]:
            assert len(s.lstrip("§")) <= fit, f"{name}: {len(s)} > {fit} chars: {s}"
        assert len(name) * 6.8 <= w - 24, name
        self.box(x, y, w, h, kind)
        self.tag(x + 12, y + 8, tag_label, T[kind][4])
        self.nodes.append(self.text(x + 12, y + 36, name, 12, INK, SANS, 600))
        self.nodes.append(self.text(x + 12, y + 50, sub, 9, SOFT))
        ty = y + 64
        for s in lines:
            if s.startswith("§"):
                ty += 4
                self.nodes.append(self.text(x + 12, ty, s[1:], 7, ACCENT if kind == "focal" else SOFT, MONO,
                                            spacing="0.14em"))
            else:
                self.nodes.append(self.text(x + 12, ty, s, 9, INK if s and not s.startswith(" ") else MUTED))
            ty += STEP

    def zone(self, x, y, w, h, label):
        self.zones.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="8" fill="rgba(45,49,66,0.02)" '
                          f'stroke="rgba(45,49,66,0.12)" stroke-width="0.8"/>')
        lw = round(len(label) * 5.32 + 16)
        lw += (-lw) % 4
        self.zones.append(f'<rect x="{x + 16}" y="{y + 4}" width="{lw}" height="12" rx="2" fill="{PAPER}"/>')
        self.zones.append(self.text(x + 24, y + 13, label, 7, SOFT, MONO, spacing="0.14em"))

    def mask_label(self, cx, cy, s):
        w = round(len(s) * 5.44 + 10)
        w += (-w) % 4
        self.labels.append(f'<rect x="{cx - w / 2}" y="{cy - 6}" width="{w}" height="12" rx="2" fill="{PAPER}"/>')
        self.labels.append(self.text(cx, cy + 3, s, 8, SOFT, MONO, anchor="middle", spacing="0.06em"))

    def vline(self, x, y1, y2, label=None, color=MUTED, marker="arrow"):
        self.arrows.append(f'<line x1="{x}" y1="{y1}" x2="{x}" y2="{y2}" stroke="{color}" stroke-width="1.2" '
                           f'marker-end="url(#{marker})"/>')
        if label:  # beside the vertical segment, 8 px gap
            w = round(len(label) * 5.44 + 10)
            w += (-w) % 4
            self.mask_label(x + 8 + w / 2, (y1 + y2) / 2, label)

    def hline(self, y, x1, x2, label=None, color=MUTED, marker="arrow"):
        self.arrows.append(f'<line x1="{x1}" y1="{y}" x2="{x2}" y2="{y}" stroke="{color}" stroke-width="1.2" '
                           f'marker-end="url(#{marker})"/>')
        if label:  # above the horizontal segment, 8 px gap
            self.mask_label((x1 + x2) / 2, y - 14, label)

    def table(self, x, y, cols, header, rows):
        """A ledger: cols = [(x offset, font)], header labels, then rows of cells; 20 px rows."""
        for (dx, _), h in zip(cols, header):
            self.extras.append(self.text(x + dx, y, h, 7, SOFT, MONO, spacing="0.14em"))
        right = x + cols[-1][0] + 520
        self.extras.append(f'<line x1="{x}" y1="{y + 8}" x2="{right}" y2="{y + 8}" stroke="rgba(45,49,66,0.20)" '
                           f'stroke-width="0.8"/>')
        for i, r in enumerate(rows):
            ry = y + 26 + i * 20
            if i:
                self.extras.append(f'<line x1="{x}" y1="{ry - 14}" x2="{right}" y2="{ry - 14}" '
                                   f'stroke="rgba(45,49,66,0.08)" stroke-width="0.8"/>')
            for (dx, font), cell in zip(cols, [str(i + 1), *r]):
                if font == SANS:
                    self.extras.append(self.text(x + dx, ry, cell, 10, INK, SANS))
                else:
                    self.extras.append(self.text(x + dx, ry, cell, 9, MUTED if dx == 0 else INK))

    def legend(self, y, width, items):
        out = [f'<line x1="40" y1="{y}" x2="{width - 40}" y2="{y}" stroke="rgba(45,49,66,0.10)" stroke-width="0.8"/>',
               self.text(40, y + 20, "LEGEND", 8, MUTED, MONO, spacing="0.14em")]
        x = 40
        for kind, v, lab in items:
            iy = y + 28
            if kind == "box":
                fill, stroke, sw, dash, _ = T[v]
                out.append(f'<rect x="{x}" y="{iy}" width="14" height="10" rx="2" fill="{fill}" stroke="{stroke}" '
                           f'stroke-width="1"/>')
                lead = 20
            else:
                mk = "arrow-link" if v == LINK else "arrow"
                out.append(f'<line x1="{x}" y1="{iy + 5}" x2="{x + 24}" y2="{iy + 5}" stroke="{v}" stroke-width="1" '
                           f'marker-end="url(#{mk})"/>')
                lead = 32
            out.append(self.text(x + lead, iy + 8, lab, 8.5, MUTED, SANS))
            x += lead + round(len(lab) * 5.1) + 28
        self.extras.extend(out)

    def page(self, slug, eyebrow, title, desc, w, h):
        body = "\n".join(["<!-- Zones -->", *self.zones, "<!-- Connectors (behind boxes) -->", *self.arrows,
                          "<!-- Arrow labels -->", *self.labels, "<!-- Nodes -->", *self.nodes,
                          "<!-- Ledger and legend -->", *self.extras])
        html = f"""<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>{escape(title)}</title>
  <link href="https://fonts.googleapis.com/css2?family=Instrument+Serif:ital@0;1&family=Geist:wght@400;500;600&family=Geist+Mono:wght@400;500;600&display=swap" rel="stylesheet">
  <style>
    *, *::before, *::after {{ box-sizing: border-box; margin: 0; padding: 0; }}
    :root {{
      --color-paper:   #f5f5f5;
      --color-ink:     #2d3142;
      --color-muted:   #4f5d75;
      --color-accent:  #bf4520;
      --font-sans:     'Geist', system-ui, sans-serif;
      --font-serif:    'Instrument Serif', serif;
      --font-mono:     'Geist Mono', ui-monospace, monospace;
    }}
    body {{
      font-family: var(--font-sans);
      background: var(--color-paper);
      color: var(--color-ink);
      min-height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 3rem 2rem;
    }}
    .frame {{ max-width: {w}px; width: 100%; }}
    /* The SVG holds its readable width (min-width == the viewBox width); this
       wrapper lets a narrow screen scroll the diagram instead of the page. */
    .diagram-container {{ width: 100%; overflow-x: auto; }}
    .eyebrow {{
      font-family: var(--font-mono);
      font-size: 0.66rem;
      font-weight: 500;
      letter-spacing: 0.18em;
      text-transform: uppercase;
      color: var(--color-muted);
      margin-bottom: 0.5rem;
    }}
    h1 {{
      font-family: var(--font-serif);
      font-size: clamp(1.5rem, 2.4vw + 0.75rem, 2rem);
      font-weight: 400;
      letter-spacing: -0.02em;
      line-height: 1.15;
      color: var(--color-ink);
      margin-bottom: 1.5rem;
    }}
    svg {{ width: 100%; min-width: {w}px; display: block; }}
    /* Paper has no scrollbar: release the scroller in print. Must follow the svg rule. */
    @media print {{
      .diagram-container {{ overflow-x: visible; }}
      svg {{ min-width: 0; }}
    }}
  </style>
</head>
<body>
  <div class="frame">
    <p class="eyebrow">{escape(eyebrow)}</p>
    <h1>{escape(title)}</h1>

    <div class="diagram-container">
      <svg viewBox="0 0 {w} {h}" xmlns="http://www.w3.org/2000/svg" role="img" aria-labelledby="{slug}-title {slug}-desc">
        <title id="{slug}-title">{escape(title)}</title>
        <desc id="{slug}-desc">{escape(desc)}</desc>
        <defs>
          <marker id="arrow" markerWidth="8" markerHeight="6" refX="7" refY="3" orient="auto"><polygon points="0 0, 8 3, 0 6" fill="{MUTED}"/></marker>
          <marker id="arrow-accent" markerWidth="8" markerHeight="6" refX="7" refY="3" orient="auto"><polygon points="0 0, 8 3, 0 6" fill="{ACCENT}"/></marker>
          <marker id="arrow-link" markerWidth="8" markerHeight="6" refX="7" refY="3" orient="auto"><polygon points="0 0, 8 3, 0 6" fill="{LINK}"/></marker>
        </defs>

        <rect width="100%" height="100%" fill="{PAPER}"/>
{body}
      </svg>
    </div>
  </div>
</body>
</html>
"""
        path = OUT / f"{slug}.html"
        path.write_text(html)
        print("wrote", path, len(html), "bytes")


W = 1840
X3 = [40, 488, 936]          # three 424-wide columns in the left area (ends at 1360)
CW = 424
RX, RW = 1480, 320           # the right-hand column


def row(c, y, cards):
    """Cards side by side at y, all as tall as the tallest; returns that height."""
    h = max(Canvas.card_height(lines) for *_, lines in cards)
    for x, w, kind, tag_label, name, sub, lines in cards:
        c.card(x, y, w, h, kind, tag_label, name, sub, lines)
    return h


# ============================================================ 09 backend
def backend():
    c = Canvas()
    gateway = [
        "§START POINT",
        "Program.cs: WebApplication; Gateway__Shop, __Instance, __Seed",
        "DI singletons: GatewayBuffer, Collector, IProducer, Sender,",
        "  GatewayControl, ProcessSampler, GatewayLease",
        "§HOSTED SERVICE",
        "GatewayLease : BackgroundService, group gateway-lease-<shop>",
        "  Assigned / Revoked / Lost → LeaseTerm → StartTerm, StopTerm",
        "  standby: PlantLogReader.ReconcileBufferAsync every 10 s",
        "§TERM (whileActive, only on the lease holder)",
        "1 fence the old sender  2 ReconcileBufferAsync  3 Resume",
        "4 MarkActive  5 CollectorService: Backfill, then Tick each 1 s",
        "6 Sender.RunAsync: ReadOldest (alarms lane first) → Produce",
        "  → DeleteAsync once the broker acks (outbox pattern)",
        "§ENDPOINTS",
        "GET  /healthz",
        "GET  /metrics → GatewayMetrics (role, lastSeqByMachine)",
        "POST /control  GatewayCommand → GatewayControl.ApplyAsync",
        "§EVENTS PRODUCED",
        "MachineReading → machine.readings (key machine_id)",
        "MachineAlarm   → machine.alarms · CloudEvents ce_* headers",
        "§CLASSES",
        "Collector: Tick, Backfill, Resume, InjectFault",
        "Sender: RunAsync, State (SenderState) · CollectorService",
        "LeaseTerm: Assigned, Revoked, Lost → LeaseDecision",
        "PlantLogReader: NewestAsync, ReconcileBufferAsync",
        "§REFERENCES",
        "Contracts Domain Shared Messaging Persistence Infrastructure",
    ]
    consumer = [
        "§START POINT",
        "Program.cs: NpgsqlDataSource, CentralStore, ConsumerStats",
        "ConsumerLoops.AddStoreWriterLoops → 2 × LoopService",
        "§LOOPS (hosted, one consumer each)",
        "readings: group central-readings",
        "  topic regex ^plant[0-9]+\\.machine\\.readings$",
        "alarms: group central-alarms",
        "  topic regex ^plant[0-9]+\\.machine\\.alarms$",
        "§HANDLER: BatchLoop<T>.Run",
        "1 Consume a batch (cooperative-sticky)",
        "2 RecordValidation.Problem → bad records quarantined",
        "3 CentralStore.InsertReadingsAsync / InsertAlarmsAsync",
        "  one transaction: rows, progress, totals, rejects",
        "4 StoreOffset per partition after the commit",
        "  CommitOnRevoke on rebalance",
        "RateCap / RateMeter: the drain cap leaves room for live data",
        "§ENDPOINTS",
        "GET /healthz",
        "GET /metrics → ConsumerMetrics (rates, lag, rejected)",
        "§CLASSES",
        "BatchLoop<T>, ConsumerStats, LoopService, RateCap,",
        "  RateMeter, RecordValidation, ConsumerLoops",
        "§REFERENCES",
        "Contracts Messaging Persistence Infrastructure",
    ]
    api = [
        "§START POINT",
        "Program.cs: Snapshot, Pollers, KafkaWatcher, CentralQueries",
        "Pollers : BackgroundService, every 1 s: gateway and",
        "  consumer /metrics, database progress → Snapshot",
        "KafkaWatcher: PollOffsetsAsync (lag), RunTail (tails)",
        "Snapshot: Stamped<T> samples, SettledSince",
        "OverviewBuilder (pure): Build, Reconcile, BuildChart",
        "§ROUTES (minimal APIs)",
        "GET  /api/overview → Overview",
        "GET  /api/reconciliation[?full=true] → Reconciliation",
        "GET  /api/chart?minutes=10 → Chart",
        "GET  /api/alarms → AlarmView[]",
        "POST /api/plant/pause, /api/plant/resume",
        "  FanOut → POST /control on all 10 gateways",
        "POST /api/machines/{machineId}/faults (FaultRequest)",
        "  ApiRoutes.GatewayFor → the shop's active gateway",
        "§ERRORS",
        "Db<T>: 2 s cap (60 s for the full scan) → 503",
        "  api.database.unavailable",
        "Result → 202, or ProblemDetails 400 / 404 / 503",
        "GlobalExceptionHandler → 500 ProblemDetails",
        "§REFERENCES",
        "Contracts Domain Shared Messaging Persistence Infrastructure",
    ]
    y_a = 48
    h_a = row(c, y_a, [
        (X3[0], CW, "service", "HOST · EXE", "Gateway", "EventDrivenDesign.Gateway · gateway-<shop>-a|b ×10", gateway),
        (X3[1], CW, "service", "HOST · EXE", "Consumer", "EventDrivenDesign.Consumer · consumer-1, -2", consumer),
        (X3[2], CW, "service", "HOST · EXE", "Api", "EventDrivenDesign.Api · api-1, api-2 behind nginx", api),
    ])
    c.zone(24, 24, 1352, h_a + 40, "ENTRY POINTS · THREE HOSTS · PROGRAM.CS STARTS EACH ONE")
    za_bottom = 24 + h_a + 40

    infra = [
        "HostingExtensions:",
        "  AddGlobalExceptionHandling, UseGlobalExceptionHandling",
        "GlobalExceptionHandler : IExceptionHandler",
        "  any unhandled exception → 500 ProblemDetails",
        "ResultHttpExtensions.ToHttpResult: Result → 202 or",
        "  ProblemDetails: title = Error.Code, detail = Message,",
        "  status by ErrorKind (400 / 404 / 503)",
        "ProcessSampler.Sample → ProcessStats (CPU %, memory)",
        "§REFERENCES",
        "Contracts Shared",
    ]
    messaging = [
        "KafkaConfigs.Producer: acks=all, idempotence on,",
        "  lz4, linger 20 ms",
        "KafkaConfigs.StoreWriter: cooperative-sticky,",
        "  offsets stored only after the insert commits",
        "KafkaConfigs.Lease: static member, session 6 s,",
        "  cooperative-sticky, reconnect backoff ≤ 1 s",
        "KafkaConfigs.Tail, Admin: assign-only readers, metadata",
        "CloudEvents.For: ce_specversion, ce_id, ce_source,",
        "  ce_type, ce_time headers on every record",
        "Retry.Backoff: full jitter, 0.1 s × 2^n, capped at 30 s",
        "§REFERENCES",
        "Contracts · package Confluent.Kafka",
    ]
    persistence = [
        "§GATEWAYBUFFER: THE OUTBOX (SQLITE)",
        "WAL, synchronous=FULL, one writer thread",
        "WriteTickAsync · ReadOldest · DeleteAsync · Stats",
        "ReconcileAsync · SetPausedAsync · LoadPaused · LoadState",
        "records OutboxRow, MachineState, LogPosition,",
        "  TickBatch, BufferStats · enum Lane { Alarm, Reading }",
        "§CENTRALSTORE: THE ONE DEDUPLICATION POINT",
        "InsertReadingsAsync, InsertAlarmsAsync:",
        "  unnest(...) INSERT ON CONFLICT (machine_id, seq)",
        "  DO NOTHING; progress and totals in the same tx",
        "§CENTRALQUERIES (READ SIDE)",
        "TotalsAsync, ProgressAsync, ChartAsync,",
        "  RecentAlarmsAsync, DuplicateRowsAsync",
        "§REFERENCES",
        "Contracts Domain · Microsoft.Data.Sqlite, Npgsql",
    ]
    y_b = za_bottom + 88
    h_b = row(c, y_b, [
        (X3[0], CW, "service", "ADAPTER", "Infrastructure", "EventDrivenDesign.Infrastructure · ASP.NET Core", infra),
        (X3[1], CW, "service", "ADAPTER", "Messaging", "EventDrivenDesign.Messaging · Kafka settings", messaging),
        (X3[2], CW, "focal", "ADAPTER", "Persistence", "EventDrivenDesign.Persistence · SQLite and SQL", persistence),
    ])
    c.zone(24, y_b - 24, 1352, h_b + 40, "ADAPTERS · I/O AND HTTP PLUMBING THE HOSTS SHARE")
    c.vline(700, za_bottom, y_b - 24, "REFERENCES")
    zb_bottom = y_b + h_b + 16

    contracts = [
        "Readings.cs: record MachineReading, MachineAlarm,",
        "  enum AlarmKind",
        "GatewayCommand.cs: union GatewayCommand",
        "  (Pause | Resume | InjectFault) + JSON converter",
        "ServiceHealth.cs: closed record ServiceHealth",
        "  Healthy | Degraded(Reason) | Down(LastSeen)",
        "Metrics.cs: GatewayMetrics, ConsumerMetrics, ProcessStats",
        "ApiModels.cs: Overview, GatewayTile, ClusterTile,",
        "  ConsumerTile, DatabaseTile, Reconciliation,",
        "  MachineProof, Chart, ChartPoint, AlarmView",
        "TopicNames: Readings, Alarms, Lease(shop), patterns",
        "Json: source-generated EventDrivenDesignJsonContext",
    ]
    domain = [
        "MachineCatalog.ForShop → MachineInfo × 100 per shop",
        "  5 shops, 500 machines · enum MachineType",
        "Signals.For(type) → SignalSpec[] with thresholds",
        "MachineSimulator.Next → SimTick(Signals, Alarms)",
        "  InjectFault · record AlarmEvent",
        "Proof.Reconcile: per machine generated, stored,",
        "  missing, gaps, over → PASS / WAIT / FAIL",
        "  PASS only when paused, drained and all zero",
        "Proof.DrainEtaSeconds, Watermark, HealthFromAge",
        "  record ProgressRow",
        "§REFERENCES",
        "Contracts",
    ]
    shared = [
        "readonly record struct Result, Result<T>",
        "  Success() · Failure(Error) · Match(ok, fail)",
        "record Error(Code, Message, Kind)",
        "enum ErrorKind:",
        "  Invalid → 400 · NotFound → 404 · Unavailable → 503",
        "Expected failures are values, not exceptions;",
        "  Infrastructure turns them into ProblemDetails",
        "§REFERENCES",
        "none",
    ]
    y_c = zb_bottom + 88
    h_c = row(c, y_c, [
        (X3[0], CW, "core", "CORE · NO I/O", "Contracts", "EventDrivenDesign.Contracts · no references", contracts),
        (X3[1], CW, "core", "CORE · NO I/O", "Domain", "EventDrivenDesign.Domain · pure rules", domain),
        (X3[2], CW, "core", "CORE · NO I/O", "Shared", "EventDrivenDesign.Shared · the Result pattern", shared),
    ])
    c.zone(24, y_c - 24, 1352, h_c + 40, "CORE · RECORDS, RULES AND RESULTS, NO I/O")
    c.vline(700, zb_bottom, y_c - 24, "REFERENCES")
    zc_bottom = y_c + h_c + 16

    # right-hand column: what lives outside the process
    kafka = [
        "§PLANT-KAFKA",
        "machine.readings   12 p · 8 h",
        "machine.alarms      3 p · kept",
        "gateway.lease.<shop> 1 p",
        "  Sender produces · GatewayLease",
        "  joins · PlantLogReader scans",
        "§MM2-1, MM2-2 → CENTRAL-KAFKA",
        "plant1.machine.readings, alarms",
        "  BatchLoop consumes",
        "  KafkaWatcher reads offsets",
    ]
    sqlite = [
        "one buffer.db per gateway volume",
        "outbox(id, lane, machine_id, seq,",
        "  device_time_ms, payload)",
        "  ix (lane, id), (machine_id, lane, seq)",
        "machine_state · gateway_state",
    ]
    postgres = [
        "readings PK (machine_id, seq)",
        "alarms PK (machine_id, alarm_seq)",
        "machine_progress · ingest_totals",
        "rejected_records PK (topic,",
        "  kafka_partition, kafka_offset)",
    ]
    hk = Canvas.card_height(kafka)
    c.card(RX, y_a, RW, hk, "store", "BROKER", "Kafka 4.4 (KRaft)", "two clusters joined by MirrorMaker 2", kafka)
    c.hline(y_a + 60, 1376, RX, "KAFKA CLIENTS")
    hs, hp = Canvas.card_height(sqlite), Canvas.card_height(postgres)
    c.card(RX, y_b, RW, hs, "store", "STORE", "SQLite buffer.db", "GatewayBuffer owns it", sqlite)
    y_p = y_b + hs + 24
    c.card(RX, y_p, RW, hp, "focal", "STORE", "PostgreSQL 18", "central-db · CentralStore, CentralQueries", postgres)
    assert y_p + 36 < y_b + h_b, "the PostgreSQL arrow must leave the Persistence card"
    c.hline(y_b + 36, 1360, RX, "SQLITE")
    c.hline(y_p + 36, 1360, RX, "NPGSQL")
    c.zone(1464, 24, 352, zc_bottom - 24, "OUTSIDE THE PROCESS")

    # bottom band: idempotency ledger and the test projects
    y_d = zc_bottom + 48
    idem = [
        ("History re-read after a restart", "skip samples at or before last_device_time_ms", "Collector.Backfill"),
        ("Pause, resume or a fault sent twice", "a persisted flag; a faulted signal is ignored",
         "GatewayControl.ApplyAsync, MachineSimulator"),
        ("Producer retry inside one session", "idempotent producer, acks=all", "KafkaConfigs.Producer"),
        ("Resend after a gateway restart", "PK (machine_id, seq), ON CONFLICT DO NOTHING",
         "CentralStore.InsertReadingsAsync"),
        ("MirrorMaker 2 copies again", "the same key", "CentralStore"),
        ("Store writer dies before its offset", "the same key; counters in the same transaction",
         "BatchLoop<T>.Run + CentralStore"),
        ("Malformed record delivered twice", "rejected_records keyed by (topic, partition, offset)",
         "RecordValidation + CentralStore"),
        ("Two gateways after a lost session", "new term: fence, reconcile from the log, resume",
         "GatewayLease + PlantLogReader"),
    ]
    c.table(40, y_d + 32, [(0, MONO), (24, SANS), (336, SANS), (712, MONO)],
            ["#", "REPEAT THAT CAN HAPPEN", "WHAT MAKES IT HARMLESS", "WHERE IN THE CODE"], idem)
    h_d = 32 + 26 + len(idem) * 20
    c.zone(24, y_d, 1352, h_d, "IDEMPOTENCY · EVERY REPEAT AND THE CODE THAT ABSORBS IT")
    tests = [
        "EventDrivenDesign.Tests: 170 unit",
        "  AwesomeAssertions, FakeTimeProvider",
        "  Collector, Sender, Proof, LeaseTerm,",
        "  BatchLoop, OverviewBuilder, Result",
        "IntegrationTests: 19, Testcontainers",
        "  Kafka + PostgreSQL: CentralStore,",
        "  Sender, PlantLogReader, GatewayLease",
    ]
    ht = Canvas.card_height(tests)
    c.card(RX, y_d + 24, RW, ht, "chip", "XUNIT V3", "Tests", "warnings are errors in every project", tests)
    c.zone(1464, y_d, 352, max(h_d, ht + 40), "TESTS")

    y_l = y_d + max(h_d, ht + 40) + 24
    c.legend(y_l, W, [("box", "service", "Host or adapter project"), ("box", "core", "Core project (no I/O)"),
                      ("box", "store", "Store or broker outside the process"),
                      ("box", "focal", "Focal: where duplicates die"), ("box", "chip", "Test projects"),
                      ("line", MUTED, "Project reference or I/O")])
    c.page("09-backend-codebase", "Codebase · backend · .NET 11, C# 15, Clean Architecture",
           "Backend codebase: hosts, adapters, core and where each repeat dies",
           "Nine .NET projects in three layers: three hosts (Gateway, Consumer, Api) start at Program.cs and "
           "reference the adapters (Infrastructure, Messaging, Persistence), which reference the core (Contracts, "
           "Domain, Shared); each card lists its files, classes, records, methods, routes, hosted services and "
           "events, the right column shows Kafka, SQLite and PostgreSQL, and the bottom band maps every idempotency "
           "point to its code next to the test projects.", W, y_l + 60)


# ============================================================ 10 frontend
def frontend():
    c = Canvas()
    toolchain = [
        "package.json scripts:",
        "  dev   bunx --bun vite",
        "  build tsc -b && bunx --bun vite build",
        "  test  bun test",
        "React 19.3.1 · TypeScript 7.0.2 (tsc -b)",
        "Vite 8.3.8 + @vitejs/plugin-react",
        "Tailwind 4.3.5 via @tailwindcss/vite",
        "@tanstack/react-query 5.107.1 · recharts 3.10.1",
    ]
    image = [
        "deployment/Dockerfile, two stages:",
        "web: oven/bun:1.4.4-alpine",
        "  bun install --frozen-lockfile, bun run build",
        "  → dist/ (index.html + hashed assets)",
        "dashboard: nginx:1.30-alpine-slim",
        "  serves dist/ · port 3000 → 80",
        "  nginx/default.conf routes /api/ (right)",
    ]
    dev = [
        "vite.config.ts: plugins react(), tailwindcss()",
        "  dev proxy /api → http://localhost:3000",
        "  (the running dashboard, then the APIs)",
        "format.test.ts: 47 bun tests of format.ts",
        "  pure functions only: no DOM, no network",
        "src/scripts/build/test-frontend.sh:",
        "  bun test, then the strict build",
    ]
    y_a = 48
    h_a = row(c, y_a, [
        (X3[0], CW, "chip", "TOOLCHAIN", "Bun 1.4.4 and Vite", "src/frontend/package.json · bun.lock", toolchain),
        (X3[1], CW, "chip", "IMAGE", "Build and serve", "the dashboard container", image),
        (X3[2], CW, "chip", "DEV · TESTS", "Local loop", "vite.config.ts · format.test.ts", dev),
    ])
    c.zone(24, 24, 1352, h_a + 40, "BUILD AND SERVE")
    za_bottom = 24 + h_a + 40

    index = [
        "<div id=\"root\">",
        "<title>Plant to central</title>",
        "<script type=\"module\"",
        "  src=\"/src/main.tsx\">",
    ]
    main = [
        "createRoot(#root).render(",
        "  <StrictMode>",
        "   <QueryClientProvider client={new QueryClient()}>",
        "    <ErrorBoundary label=\"The dashboard\">",
        "     <App />",
    ]
    app = [
        "queries: useOverview, useReconciliation,",
        "  useChart, useAlarms · useFullCheck(proof)",
        "useNow() clock · nowMs = overview.at (server time)",
        "banner role=alert when the API is unreachable",
        "each panel in <ErrorBoundary label resetKey>",
        "  resetKey = query.dataUpdatedAt",
        "note(q) → panelNote(isError, hasData, reason)",
        "grid: 1 column, 2 from 900 px",
    ]
    y_b = za_bottom + 88
    h_b = row(c, y_b, [
        (X3[0], CW - 64, "service", "START POINT", "index.html", "Vite entry, served by nginx", index),
        (X3[1] - 40, CW + 40, "service", "BOOTSTRAP", "main.tsx", "providers and the root boundary", main),
        (X3[2], CW, "service", "SHELL", "App.tsx", "default export App(): the page", app),
    ])
    c.hline(y_b + 40, X3[0] + CW - 64, X3[1] - 40)
    c.hline(y_b + 40, X3[1] + CW, X3[2])
    c.zone(24, y_b - 24, 1352, h_b + 40, "START POINT · INDEX.HTML → MAIN.TSX → APP.TSX")
    c.vline(700, za_bottom, y_b - 24, "BUILDS")
    zb_bottom = y_b + h_b + 16

    pw = 312
    px = [40, 376, 712, 1048]
    strip = ["props { overview, nowMs }", "Machines → Gateways ×5 → Plant Kafka", "  → MirrorMaker 2 → Central Kafka",
             "  → Consumers ×2 → Database", "HealthBadge per stage · agoText, isStale"]
    buffers = ["props { overview }", "5 shop tiles: instanceLabel", "  (a | b), buffered rows,",
               "  oldest age, paused", "mirror and consumer lag"]
    chart = ["props { chart, nowMs, note? }", "recharts bar chart, 10 min", "  by device time",
             "fillBuckets: empty minutes", "provisional past watermark"]
    proof = ["props { proof, full, nowMs, note? }", "verdict PASS / WAIT / FAIL", "generated, stored, missing,",
             "  gaps, duplicates absorbed", "Check duplicates: full check"]
    alarms = ["props { alarms, paused, note? }", "latest alarms, kind, latency", "commands: usePause, useResume,",
              "  useInjectFault(machineId,", "  kind) · ALARM_KINDS"]
    ui = ["Panel(title, note, children)", "HealthBadge(health)", "Stat(label, value)", "toneOf(health) → colours"]
    boundary = ["class ErrorBoundary extends", "  Component<Props>", "props { label, resetKey,", "  children }",
                "a crash shows one panel's", "  message; others keep going"]
    now = ["useNow(intervalMs = 1000)", "  → Date.now() each tick", "drives 'ago' text and the",
           "  stale check between polls"]
    y_c = zb_bottom + 88
    h_c1 = row(c, y_c, [
        (px[0], pw, "service", "PANEL", "PipelineStrip", "panels/PipelineStrip.tsx", strip),
        (px[1], pw, "service", "PANEL", "BuffersLag", "panels/BuffersLag.tsx", buffers),
        (px[2], pw, "service", "PANEL", "ReadingsChart", "panels/ReadingsChart.tsx", chart),
        (px[3], pw, "focal", "PANEL", "ProofPanel", "panels/ProofPanel.tsx", proof),
    ])
    y_c2 = y_c + h_c1 + 24
    h_c2 = row(c, y_c2, [
        (px[0], pw, "service", "PANEL", "AlarmsPanel", "panels/AlarmsPanel.tsx", alarms),
        (px[1], pw, "core", "UI KIT", "ui.tsx", "panels/ui.tsx", ui),
        (px[2], pw, "core", "BOUNDARY", "ErrorBoundary", "ErrorBoundary.tsx", boundary),
        (px[3], pw, "core", "HOOK", "useNow", "useNow.ts", now),
    ])
    c.zone(24, y_c - 24, 1352, h_c1 + h_c2 + 64, "COMPONENTS · SRC/PANELS · EACH PANEL GETS DATA AS PROPS")
    c.vline(700, zb_bottom, y_c - 24, "PROPS")
    zc_bottom = y_c2 + h_c2 + 16

    fmt = [
        "agoText, healthLabel, instanceLabel, isStale",
        "formatSeconds, formatEta, formatInt",
        "fillBuckets(points, nowMs, minutes)",
        "mirrorHealth (MIRROR_LAG_LIMIT_S)",
        "shouldRequestFull(light, lastAttempt, now)",
        "  paused + drained, at most every 10 s",
        "proofView(light, full, nowMs)",
        "panelNote(isError, hasData, reason)",
        "  'No data yet. … Retrying every second.'",
    ]
    api = [
        "§TYPES (MIRROR THE C# CONTRACTS)",
        "Overview, GatewayTile, ClusterTile, ConsumerTile, DatabaseTile,",
        "ServiceHealth { $type: Healthy | Degraded | Down }, Verdict,",
        "Reconciliation, MachineProof, Chart, ChartPoint, AlarmView",
        "§TRANSPORT",
        "get<T>(path) and post(path, body) over fetch; a non-2xx answer",
        "  throws ApiError(code = ProblemDetails.title, message = detail)",
        "§QUERIES (REACT QUERY: POLL EVERY 1 S, RETRY OFF, KEEP LAST DATA)",
        "useOverview → /api/overview · useReconciliation → /api/reconciliation",
        "useChart → /api/chart?minutes=10 · useAlarms → /api/alarms",
        "useFullCheck(light) → /api/reconciliation?full=true, on demand",
        "§COMMANDS (MUTATIONS, THEN INVALIDATE EVERY QUERY)",
        "usePause → pause() → POST /api/plant/pause",
        "useResume → resume() → POST /api/plant/resume",
        "useInjectFault → POST /api/machines/{machineId}/faults",
    ]
    y_d = zc_bottom + 88
    h_d = row(c, y_d, [
        (40, CW, "core", "PURE LOGIC", "format.ts", "no React, no fetch: unit-tested", fmt),
        (488, 872, "focal", "DATA LAYER", "api.ts", "the only file that talks to the backend", api),
    ])
    c.zone(24, y_d - 24, 1352, h_d + 40, "DATA AND LOGIC · PANELS IMPORT FORMAT.TS, APP IMPORTS THE HOOKS")
    c.vline(700, zc_bottom, y_d - 24, "IMPORTS")
    zd_bottom = y_d + h_d + 16

    nginx = [
        "location /  try_files → index.html",
        "location /api/ → upstream api:",
        "  api-1:8080, api-2:8080, resolve",
        "  ip_hash: one viewer, one API",
        "  max_fails=1 fail_timeout=5s",
        "  next upstream on error, timeout",
        "  read timeout 10 s",
        "location = /api/reconciliation",
        "  read timeout 65 s (full scan)",
    ]
    routes = [
        "GET  /api/overview",
        "GET  /api/reconciliation",
        "GET  /api/reconciliation?full=true",
        "GET  /api/chart?minutes=10",
        "GET  /api/alarms",
        "POST /api/plant/pause",
        "POST /api/plant/resume",
        "POST /api/machines/{id}/faults",
        "errors: ProblemDetails (title,",
        "  detail) → ApiError in api.ts",
    ]
    hn, hr = Canvas.card_height(nginx), Canvas.card_height(routes)
    c.card(RX, y_d, RW, hn, "store", "PROXY", "nginx", "nginx/default.conf in the dashboard", nginx)
    c.hline(y_d + 40, 1360, RX, "FETCH /API")
    y_r = y_d + hn + 48
    c.card(RX, y_r, RW, hr, "store", "BACKEND", "Control-room API", "two instances, same routes", routes)
    c.vline(RX + 160, y_d + hn, y_r, "PROXY", LINK, "arrow-link")
    c.zone(1464, y_d - 24, 352, max(h_d, hn + 48 + hr) + 40, "OUTSIDE THE BROWSER")
    bottom = max(zd_bottom, y_r + hr + 16)

    y_l = bottom + 24
    c.legend(y_l, W, [("box", "service", "Page or component"), ("box", "core", "Hook, helper or pure logic"),
                      ("box", "chip", "Tooling"), ("box", "store", "Outside the browser"),
                      ("box", "focal", "Focal: the data boundary and the proof"),
                      ("line", MUTED, "Import, render or build"), ("line", LINK, "HTTP")])
    c.page("10-frontend-codebase", "Codebase · frontend · React 19, TypeScript, Vite, Tailwind, React Query",
           "Frontend codebase: from index.html to the control-room API",
           "The dashboard builds with Bun and Vite into an nginx image; index.html loads main.tsx, which wraps App in "
           "React Query and an error boundary; App polls four queries through api.ts and passes the data as props to "
           "five panels, which format it with the pure functions in format.ts; api.ts is the only file that calls the "
           "backend, through nginx to two API instances.", W, y_l + 60)


backend()
frontend()
