#!/usr/bin/env python3
"""gen01_plant_to_central.py: regenerate diagram 01, the detailed plant-to-central architecture.

Writes docs/diagrams/01-plant-to-central.html (the editable source the docs link
to). Every box, chip, arrow, label and the data-operations ledger is defined in
this file, so edit here, not in the HTML.

The layout follows the diagram-design skill (default style, faithful detail
level). The whole drawing sits on a 4 px grid and every connector is orthogonal.

Usage:
  python3 src/scripts/diagrams/gen01_plant_to_central.py [OUT.html]
  src/scripts/diagrams/check.sh --export 01-plant-to-central   # verify, then write the SVG the docs embed

OUT defaults to <repo>/docs/diagrams/01-plant-to-central.html.

Layout map (y grows downward; D shifts everything below the field layer):
  plant zone       machines (y 72), field networks (244), device interface (452),
                   gateways (216+D), plant-kafka (516+D)
  WAN strip        mm2 (724+D) with notes and a callout
  central zone     central-kafka (900+D), consumers (1080+D), central-db (1296+D)
  control room     dashboard (72) and the tall api column (240..1420+D) with its read arrows
  ledger           data operations table (1468+D), then the legend
"""
import subprocess
import sys
from html import escape
from pathlib import Path

_ROOT = Path(subprocess.run(["git", "-C", str(Path(__file__).resolve().parent), "rev-parse", "--show-toplevel"],
                            capture_output=True, text=True, check=True).stdout.strip())
OUT = Path(sys.argv[1]) if len(sys.argv) > 1 else _ROOT / "docs" / "diagrams" / "01-plant-to-central.html"
D = 424                      # everything from the gateways down sits below the field layer
W, H = 1840, 1860 + D

PAPER, INK, MUTED, SOFT, ACCENT, LINK = "#f5f5f5", "#2d3142", "#4f5d75", "#5a6580", "#bf4520", "#2e5aa8"
SANS, MONO, SERIF = "'Geist', sans-serif", "'Geist Mono', monospace", "'Instrument Serif', serif"

T = {  # node treatments: fill, stroke, width, dash, tag colour
    "focal":   ("rgba(191,69,32,0.08)", ACCENT, 1.2, None, "rgba(191,69,32,0.85)"),
    "service": ("#ffffff", INK, 1, None, "rgba(45,49,66,0.8)"),
    "store":   ("rgba(45,49,66,0.05)", MUTED, 1, None, "rgba(79,93,117,0.9)"),
    "input":   ("rgba(79,93,117,0.10)", SOFT, 1, None, "rgba(90,101,128,0.9)"),
    "prod":    ("rgba(45,49,66,0.03)", "rgba(45,49,66,0.55)", 1, "4,3", "rgba(45,49,66,0.8)"),
    "standby": ("#ffffff", INK, 1, "6,4", "rgba(45,49,66,0.8)"),
    "chip":    ("rgba(45,49,66,0.03)", "rgba(45,49,66,0.30)", 0.8, None, None),
}

ICONS = {
    "robot": '<path d="M6 6a2 2 0 0 1 2 -2h8a2 2 0 0 1 2 2v4a2 2 0 0 1 -2 2h-8a2 2 0 0 1 -2 -2l0 -4" /> <path d="M12 2v2" /> <path d="M9 12v9" /> <path d="M15 12v9" /> <path d="M5 16l4 -2" /> <path d="M15 14l4 2" /> <path d="M9 18h6" /> <path d="M10 8v.01" /> <path d="M14 8v.01" />',
    "container": '<path d="M12 3l8 4.5l0 9l-8 4.5l-8 -4.5l0 -9l8 -4.5" /> <path d="M12 12l8 -4.5" /> <path d="M12 12l0 9" /> <path d="M12 12l-8 -4.5" /> <path d="M16 5.25l-8 4.5" />',
    "queue": '<path d="M12 4l-8 4l8 4l8 -4l-8 -4" /> <path d="M4 12l8 4l8 -4" /> <path d="M4 16l8 4l8 -4" />',
    "sync": '<path d="M20 11a8.1 8.1 0 0 0 -15.5 -2m-.5 -4v4h4" /> <path d="M4 13a8.1 8.1 0 0 0 15.5 2m.5 4v-4h-4" />',
    "database": '<path d="M4 6a8 3 0 1 0 16 0a8 3 0 1 0 -16 0" /> <path d="M4 6v6a8 3 0 0 0 16 0v-6" /> <path d="M4 12v6a8 3 0 0 0 16 0v-6" />',
    "monitoring": '<path d="M4 19l16 0" /> <path d="M4 15l4 -6l4 2l4 -5l4 4" />',
    "desktop": '<path d="M3 5a1 1 0 0 1 1 -1h16a1 1 0 0 1 1 1v10a1 1 0 0 1 -1 1h-16a1 1 0 0 1 -1 -1v-10" /> <path d="M7 20h10" /> <path d="M9 16v4" /> <path d="M15 16v4" />',
    "file": '<path d="M14 3v4a1 1 0 0 0 1 1h4" /> <path d="M17 21h-10a2 2 0 0 1 -2 -2v-14a2 2 0 0 1 2 -2h7l5 5v11a2 2 0 0 1 -2 2" />',
    "api": '<path d="M7 4a2 2 0 0 0 -2 2v3a2 3 0 0 1 -2 3a2 3 0 0 1 2 3v3a2 2 0 0 0 2 2" /> <path d="M17 4a2 2 0 0 1 2 2v3a2 3 0 0 0 2 3a2 3 0 0 0 -2 3v3a2 2 0 0 1 -2 2" />',
    # field media (wifi and bluetooth: Tabler Icons, MIT; ethernet and serial drawn to match)
    "ethernet": '<path d="M4 6h16v10h-4v3h-8v-3h-4z" /> <path d="M7.5 9v3" /> <path d="M10.5 9v3" /> <path d="M13.5 9v3" /> <path d="M16.5 9v3" />',
    "wifi": '<path d="M12 18l.01 0" /> <path d="M9.172 15.172a4 4 0 0 1 5.656 0" /> <path d="M6.343 12.343a8 8 0 0 1 11.314 0" /> <path d="M3.515 9.515c4.686 -4.687 12.284 -4.687 17 0" />',
    "bluetooth": '<path d="M7 8l10 8l-5 4l0 -16l5 4l-10 8" />',
    "serial": '<path d="M3 7h18l-2.5 9h-13z" /> <path d="M6.5 10h.01" /> <path d="M9.25 10h.01" /> <path d="M12 10h.01" /> <path d="M14.75 10h.01" /> <path d="M17.5 10h.01" /> <path d="M8 13h.01" /> <path d="M10.7 13h.01" /> <path d="M13.3 13h.01" /> <path d="M16 13h.01" />',
}
MEDIA = ("ethernet", "wifi", "bluetooth", "serial")

zones, arrows, labels, nodes, extras = [], [], [], [], []


def text(x, y, s, size=9, fill=MUTED, font=MONO, weight=None, anchor="start", italic=False, spacing=None):
    a = f' font-weight="{weight}"' if weight else ""
    a += ' font-style="italic"' if italic else ""
    a += f' letter-spacing="{spacing}"' if spacing else ""
    return (f'<text x="{x}" y="{y}" fill="{fill}" font-size="{size}"{a} '
            f'font-family="{font}" text-anchor="{anchor}">{escape(s)}</text>')


def icon(name, x, y, color=MUTED, scale=0.75):
    return (f'<g transform="translate({x},{y}) scale({scale})" color="{color}"><svg aria-hidden="true" width="24" height="24" '
            f'viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" '
            f'stroke-linejoin="round">{ICONS[name]}</svg></g>')


def tag(x, y, label, color):
    w = round(len(label) * 4.9 + 12)
    w += (-w) % 4
    return (f'<rect x="{x}" y="{y}" width="{w}" height="12" rx="2" fill="transparent" stroke="{color}" stroke-opacity="0.5" stroke-width="0.8"/>'
            + text(x + w / 2, y + 9, label, 7, color, MONO, anchor="middle", spacing="0.08em"))


def box(x, y, w, h, kind, out=nodes):
    fill, stroke, sw, dash, _ = T[kind]
    d = f' stroke-dasharray="{dash}"' if dash else ""
    out.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="6" fill="{PAPER}"/>')
    out.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="6" fill="{fill}" stroke="{stroke}" stroke-width="{sw}"{d}/>')


def node(x, y, w, h, kind, tag_label, ic, name, subs, name_y=36, step=12):
    box(x, y, w, h, kind)
    nodes.append(tag(x + 12, y + 8, tag_label, T[kind][4]))
    if ic:
        nodes.append(icon(ic, x + w - 30, y + 6, ACCENT if kind == "focal" else MUTED))
    nodes.append(text(x + 12, y + name_y, name, 12, INK, SANS, 600))
    for i, s in enumerate(subs):
        nodes.append(text(x + 12, y + name_y + 14 + i * step, s, 9, MUTED))


def chip(x, y, w, h, name, lines, kind="chip", ic=None):
    box(x, y, w, h, kind)
    if ic:
        nodes.append(icon(ic, x + w - 26, y + 6, MUTED))
    nodes.append(text(x + 10, y + 17, name, 12, INK, SANS, 600))
    for i, s in enumerate(lines):
        nodes.append(text(x + 10, y + 31 + i * 12, s, 9, MUTED))


def mask_label(cx, cy, s):
    w = round(len(s) * 5.44 + 10)
    w += (-w) % 4
    labels.append(f'<rect x="{cx - w / 2}" y="{cy - 6}" width="{w}" height="12" rx="2" fill="{PAPER}"/>')
    labels.append(text(cx, cy + 3, s, 8, SOFT, MONO, anchor="middle", spacing="0.06em"))


def vline(x, y1, y2, label=None, color=MUTED, marker="arrow", dash=None, sw=1.2):
    d = f' stroke-dasharray="{dash}"' if dash else ""
    arrows.append(f'<line x1="{x}" y1="{y1}" x2="{x}" y2="{y2}" stroke="{color}" stroke-width="{sw}"{d} marker-end="url(#{marker})"/>')
    if label:  # beside the vertical segment, 8px gap
        w = round(len(label) * 5.44 + 10)
        w += (-w) % 4
        mask_label(x + 8 + w / 2, (y1 + y2) / 2, label)


def hline(y, x1, x2, label=None, color=MUTED, marker="arrow", dash=None, sw=1.2, below=False):
    d = f' stroke-dasharray="{dash}"' if dash else ""
    arrows.append(f'<line x1="{x1}" y1="{y}" x2="{x2}" y2="{y}" stroke="{color}" stroke-width="{sw}"{d} marker-end="url(#{marker})"/>')
    if label:  # above (or below) the horizontal segment, 8px gap
        mask_label((x1 + x2) / 2, y + 14 if below else y - 14, label)


def zone(x, y, w, h, label, dash=None):
    d = f' stroke-dasharray="{dash}"' if dash else ""
    stroke = "rgba(45,49,66,0.25)" if dash else "rgba(45,49,66,0.10)"
    zones.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="8" fill="rgba(45,49,66,0.02)" stroke="{stroke}" stroke-width="0.8"{d}/>')
    lw = round(len(label) * 5.32 + 16)
    lw += (-lw) % 4
    zones.append(f'<rect x="{x + 16}" y="{y + 4}" width="{lw}" height="12" rx="2" fill="{PAPER}"/>')
    zones.append(text(x + 24, y + 13, label, 7, SOFT, MONO, spacing="0.14em"))


# ---------------------------------------------------------------- columns
COLX = [64 + 264 * i for i in range(5)]          # five 240-wide columns (shops, gateways, broker chips)
COLC = [x + 120 for x in COLX]                   # 184, 448, 712, 976, 1240
MEDX = [64 + 332 * i for i in range(4)]          # four 300-wide columns (one per medium)
MEDC = [x + 150 for x in MEDX]                   # 214, 546, 878, 1210

# ---------------------------------------------------------------- zones
zone(40, 40, 1344, 628 + D, "PLANT 1 · ON-SITE · DOCKER NETWORK plant · DEMO: DEVICES, FIELD NETWORKS AND DEVICE INTERFACE ARE SIMULATED INSIDE EACH GATEWAY")
zone(40, 692 + D, 1344, 152, "WAN LINK · PLANT TO CENTRAL · DEMO: MM2 JOINS BOTH DOCKER NETWORKS", dash="4,3")
zone(40, 868 + D, 1344, 576, "CENTRAL DATA CENTRE · DOCKER NETWORK central")
zone(1432, 40, 368, 1404 + D, "CONTROL ROOM · DOCKER NETWORK ops")
zone(40, 1468 + D, 1760, 304, "DATA OPERATIONS · WHO WRITES AND READS EACH STORE, WHEN AND HOW")

# ---------------------------------------------------------------- plant: machines, with each shop's devices per medium
shops = [  # code, name, mix 1, mix 2, example id, devices on (RJ45 LAN, Wi-Fi, Bluetooth, serial)
    ("PRS", "Press shop", "30 stamping presses", "50 conveyors · 20 AGVs", "P1-PRS-S001", (30, 20, 18, 32)),
    ("BDY", "Body shop", "80 weld robots", "10 conveyors · 10 AGVs", "P1-BDY-W042", (74, 10, 4, 12)),
    ("PNT", "Paint shop", "40 paint booths", "50 conveyors · 10 AGVs", "P1-PNT-B007", (28, 10, 21, 41)),
    ("MCH", "Machining", "70 CNC machines", "20 conveyors · 10 AGVs", "P1-MCH-C017", (46, 10, 9, 35)),
    ("ASM", "Assembly", "60 conveyors", "40 AGVs", "P1-ASM-V031", (14, 40, 19, 27)),
]
assert all(sum(s[5]) == 100 for s in shops)
for (code, name, l1, l2, eg, mix), x, cx in zip(shops, COLX, COLC):
    node(x, 72, 240, 124, "input", "100 DEVICES", "robot", f"{code} · {name}",
         [l1, l2, f"PLC controllers · id e.g. {eg}"], name_y=40, step=14)
    for k, (m, n) in enumerate(zip(MEDIA, mix)):
        mx = x + 12 + 56 * k
        box(mx, 166, 48, 20, "chip")
        nodes.append(icon(m, mx + 5, 169, MUTED, 0.5))
        nodes.append(text(mx + 40, 180, str(n), 9, INK, MONO, anchor="end"))
    vline(cx, 196, 244, "100 DEVICES")

# ---------------------------------------------------------------- plant: field networks (one per medium, shared by all shops)
node(52, 244, 1320, 160, "prod", "FIELD NETWORKS · PRODUCTION", None, "Plant field networks · one network per medium, shared by all five shops",
     ["every device joins exactly one; each shop's split is on its chips above · controllers keep minutes of history for re-reads"])
media = [
    ("RJ45 Ethernet LAN", ["industrial switches · Cat6 · 1 Gbit/s", "OPC UA :4840 · Modbus TCP :502",
                           "presses · robots · CNC · booths · conveyors", "192 devices",
                           "PRS 30 · BDY 74 · PNT 28 · MCH 46 · ASM 14"]),
    ("Wi-Fi 6", ["access points · 802.11ax · WPA3-Enterprise", "MQTT over TLS :8883 · QoS 1",
                 "AGVs: they move, so no cable", "90 devices", "PRS 20 · BDY 10 · PNT 10 · MCH 10 · ASM 40"]),
    ("Bluetooth LE 5", ["BLE receivers · 10–30 m range", "GATT notifications every 1 s",
                        "battery vibration sensors on conveyors", "71 devices", "PRS 18 · BDY 4 · PNT 21 · MCH 9 · ASM 19"]),
    ("Serial · RS-485 / RS-232", ["multidrop buses · serial device servers", "Modbus RTU · 19,200 baud · polled",
                                  "conveyor drives · older robots, CNC", "147 devices", "PRS 32 · BDY 12 · PNT 41 · MCH 35 · ASM 27"]),
]
assert sum(int(m[1][3].split()[0]) for m in media) == 500
for (n, ls), m, x in zip(media, MEDIA, MEDX):
    chip(x, 304, 300, 88, n, ls, ic=m)
for cx, lab in zip(MEDC, ("OPC UA, MODBUS", "MQTT :8883", "BLE GATT", "MODBUS RTU")):
    vline(cx, 404, 452, lab)

# ---------------------------------------------------------------- plant: unified device interface
node(52, 452, 1320, 140, "prod", "DEVICE INTERFACE · PRODUCTION", "api", "Unified device interface · one listener per medium",
     ["normalises every frame to one reading: machine_id · device_time · signals · routes by id prefix (P1-BDY-… → gateway-bdy)",
      "forwards to the shop's gateway and retries until the gateway acks; holds nothing, so a gap is re-read from controller history"])
listeners = [
    ("Ethernet listener", ["OPC UA subscriptions · Modbus TCP polls", "1 s sampling per device"]),
    ("MQTT listener", ["subscribes plant1/+/+/readings · TLS", "QoS 1 · persistent session"]),
    ("BLE listener", ["GATT notify per sensor MAC", "reconnects on its own"]),
    ("Serial poller", ["one Modbus RTU master per bus", "1 s poll · CRC checked"]),
]
for (n, ls), m, x in zip(listeners, MEDIA, MEDX):
    chip(x, 524, 300, 56, n, ls, ic=m)

# ---------------------------------------------------------------- plant: gateways (active/standby pairs)
for (code, *_), x, cx in zip(shops, COLX, COLC):
    s = code.lower()
    vline(cx, 592, 640, "FORWARD + ACK")
    node(x, 216 + D, 240, 100, "service", "EDGE GATEWAY · ACTIVE", "container", f"gateway-{s}-a",
         ["EventDrivenDesign.Gateway · .NET 11", "collector → buffer → sender · backfill"])
    nodes.append(text(x + 12, 292 + D, f"vol gateway-{s}-a-data → /data", 9, MUTED))
    node(x, 328 + D, 240, 100, "standby", "STANDBY", "container", f"gateway-{s}-b",
         [f"lease: gateway.lease.{s} · 1 partition", "static member · session 6 s"])
    nodes.append(text(x + 12, 414 + D, f"vol gateway-{s}-b-data → /data", 9, MUTED))
    vline(cx, 440 + D, 516 + D, "ACKS=ALL :9092")

# ---------------------------------------------------------------- plant: broker (focal 1)
node(52, 516 + D, 1320, 124, "focal", "BROKER · KRAFT", "queue", "plant-kafka · Apache Kafka 4.4",
     ["apache/kafka:4.4.0 · broker + controller (KRaft) · 1 GB, heap 512 MB · vol plant-kafka-data · topics made at start by plant-topics (one-shot)"])
pk = [
    ("machine.readings", ["12 partitions · key machine_id", "JSON value · CloudEvents ce_* headers"]),
    ("machine.alarms", ["3 partitions · key machine_id", "own topic, so alarms never queue"]),
    ("Listeners", [":9092 plant net · :19092 host", ":9093 KRaft controller"]),
    ("Durability", ["prod: 3 brokers · RF 3 · min ISR 2", "demo: 1 broker · RF 1"]),
    ("Retention", ["prod: 7 days ≈ 302 GB per broker", "demo: 3 h · producers lz4"]),
]
for (n, ls), x in zip(pk, COLX):
    chip(x, 576 + D, 240, 56, n, ls)

# ---------------------------------------------------------------- WAN: MirrorMaker 2
vline(712, 640 + D, 724 + D, "FETCH :9092")
node(552, 724 + D, 320, 96, "service", "MIRROR", "sync", "mm2 · MirrorMaker 2",
     ["apache/kafka:4.4.0 · connect-mirror-maker.sh · 768 MB", "at-least-once · renames to plant1.machine.*",
      "offset-syncs on target · errors.tolerance=none"])
vline(712, 820 + D, 900 + D, "ACKS=ALL :9092")
extras.append(text(64, 760 + D, "WAN 100 Mbit/s: a 6-hour backlog", 9, MUTED))
extras.append(text(64, 774 + D, "(10.8 GB) copies in about 15 minutes.", 9, MUTED))
extras.append(text(64, 798 + D, "Drill wan-cut: docker network disconnect", 9, MUTED))
extras.append(text(64, 812 + D, "edd_plant edd-mm2-1", 9, MUTED))
# callout 1: plant broker rides out the outage
extras.append(text(912, 752 + D, "Central or WAN down? Plant Kafka keeps", 14, INK, SERIF, italic=True))
extras.append(text(912, 770 + D, "taking every write for up to 7 days.", 14, INK, SERIF, italic=True))
extras.append(f'<path d="M 1148,{738 + D} Q 1176,{704 + D} 1188,{644 + D}" fill="none" stroke="rgba(45,49,66,0.40)" stroke-width="1" stroke-dasharray="4,3"/>')
extras.append(f'<circle cx="1188" cy="{642 + D}" r="2" fill="{INK}"/>')

# ---------------------------------------------------------------- central: broker
node(52, 900 + D, 1320, 124, "service", "BROKER · KRAFT", "queue", "central-kafka · Apache Kafka 4.4",
     ["apache/kafka:4.4.0 · broker + controller (KRaft) · 1 GB, heap 512 MB · vol central-kafka-data · topics created by MirrorMaker 2"])
ck = [
    ("plant1.machine.readings", ["12 partitions · copied by mm2", "group central-readings reads it"]),
    ("plant1.machine.alarms", ["3 partitions · copied by mm2", "group central-alarms reads it"]),
    ("MirrorMaker 2 state", ["mm2-offsets · mm2-configs", "mm2-status · offset-syncs"]),
    ("Listeners", [":9092 central net · :29092 host", ":9093 KRaft controller"]),
    ("Durability · retention", ["prod: 3 brokers · RF 3 · 3 days", "demo: 1 broker · 3 h"]),
]
for (n, ls), x in zip(ck, COLX):
    chip(x, 960 + D, 240, 56, n, ls)

# ---------------------------------------------------------------- central: consumers (store writers)
for i, (x, cx) in enumerate(((208, 448), (736, 976)), start=1):
    vline(cx, 1024 + D, 1080 + D, "POLL BATCHES")
    node(x, 1080 + D, 480, 160, "service", "STORE WRITER", "container", f"consumer-{i}",
         ["EventDrivenDesign.Consumer · .NET 11 · Confluent.Kafka + Npgsql · 256 MB",
          "offsets stored after the DB commit, sent every 5 s · :8080 /metrics"])
    chip(x + 12, 1152 + D, 220, 76, "Readings loop", ["group central-readings", r"^plant[0-9]+\.machine\.readings$",
                                                      "≤5,000 records or 200 ms", "cap 1,000 rows/s"])
    chip(x + 248, 1152 + D, 220, 76, "Alarms loop", ["group central-alarms", r"^plant[0-9]+\.machine\.alarms$",
                                                     "≤100 records or 50 ms", "never capped"])
    vline(cx, 1240 + D, 1296 + D, "1 TX PER BATCH")

# ---------------------------------------------------------------- central: database (focal 2)
node(52, 1296 + D, 1320, 124, "focal", "DATABASE", "database", "central-db · PostgreSQL 18",
     ["postgres:18.6-alpine · :5432 (host 55432) · 256 MB, shared_buffers 64 MB · synchronous_commit=on · vol central-db-data → /var/lib/postgresql"])
tables = [
    ("readings", ["PK (machine_id, seq)", "index on device_time"]),
    ("alarms", ["PK (machine_id, alarm_seq)", "kind · severity · value"]),
    ("machine_progress", ["PK machine_id", "stored · max_seq · max time"]),
    ("ingest_totals", ["PK (stream, writer_id)", "received · inserted"]),
    ("rejected_records", ["PK (topic, partition, offset)", "raw bytes · parse error"]),
]
for (n, ls), x in zip(tables, COLX):
    chip(x, 1356 + D, 240, 56, n, ls, kind="store")

# ---------------------------------------------------------------- control room
node(1456, 72, 320, 112, "service", "WEB", "desktop", "dashboard",
     ["nginx:1.30-alpine-slim · :3000 · 64 MB", "React 19 · Vite 8 · TypeScript 7 · Tailwind 4",
      "Recharts 3 · TanStack Query, polls every 1 s", "nginx proxies /api/* to api:8080",
      "browser opens http://localhost:3000"], step=12)
vline(1616, 184, 240, "HTTP /API", color=LINK, marker="arrow-link")
node(1456, 240, 320, 1180 + D, "service", "API", "monitoring", "api · control-room API",
     ["EventDrivenDesign.Api · .NET 11", "ASP.NET Core minimal API · :8080", "256 MB · joins plant, central and ops"])
chip(1468, 356 + D, 296, 76, "Gateways ×10 (5 pairs) · HTTP", ["GET gateway-*:8080/metrics · 1 s", "POST gateway-*:8080/control",
                                                     "Pause · Resume · InjectFault"])
chip(1468, 540 + D, 296, 76, "Plant Kafka · AdminClient", ["end offsets per partition · 2 s", "tail: newest device time",
                                                            "tail: the last 50 alarms"])
chip(1468, 708 + D, 296, 88, "Computes", ["health: poll age <3 s ok, <10 s degraded", "drain ETA = backlog ÷ (drain − live)",
                                          "watermark: oldest gap-free device time", "proof: generated = stored, 0 gaps"])
chip(1468, 924 + D, 296, 76, "Central Kafka · AdminClient", ["end and committed offsets · 2 s", "tail: newest mirrored device time",
                                                              "mirror lag = plant − central time"])
chip(1468, 1122 + D, 296, 76, "Consumers ×2 · HTTP", ["GET consumer-*:8080/metrics · 1 s", "received · inserted · rejected",
                                                       "assigned partitions · rates"])
chip(1468, 1320 + D, 296, 76, "PostgreSQL · Npgsql", ["reads only, every 2 s", "proof · chart buckets · alarms",
                                                       "never writes a reading"], kind="store")
hline(392 + D, 1456, 1360, "GET /METRICS", color=LINK, marker="arrow-link")
hline(416 + D, 1456, 1360, "POST /CONTROL", color=LINK, marker="arrow-link", below=True)
hline(578 + D, 1456, 1372, "ADMIN + TAIL", dash="5,4", sw=1)
hline(962 + D, 1456, 1372, "ADMIN + TAIL", dash="5,4", sw=1)
hline(1160 + D, 1456, 1216, "GET /METRICS", color=LINK, marker="arrow-link")
hline(1358 + D, 1456, 1372, "SQL READS", dash="5,4", sw=1)

# ---------------------------------------------------------------- data operations ledger
cols = [(64, "#"), (92, "STORE"), (268, "OBJECT"), (468, "WRITTEN BY"), (708, "WHEN · HOW"), (1132, "READ BY"), (1332, "WHEN · WHY")]
rows = [
    ("buffer.db (SQLite)", "outbox · insert", "gateway writer thread (only writer)", "every 1 s tick: INSERT the tick's readings and alarms in 1 tx, fsync", "gateway sender", "SELECT … ORDER BY lane, id LIMIT 1,000 (5,000 when catching up)"),
    ("", "outbox · delete", "gateway writer thread", "after each delivery report: DELETE the acknowledged ids, never before", "—", "—"),
    ("", "machine_state", "gateway writer thread", "same tx as the tick: last_seq, last_alarm_seq, last_device_time_ms", "every lease assignment", "reconcile with plant Kafka, then backfill; resume numbering"),
    ("plant-kafka", "machine.readings · .alarms", "5 active gateways (producers)", "continuous: acks=all, idempotent, key = machine_id, lz4", "mm2 · api tail", "mm2 copies from its saved offset; the api reads the newest device time"),
    ("central-kafka", "plant1.machine.*", "mm2", "continuous, at-least-once, with the plant1. prefix", "consumers · api tail", "2 consumer groups poll batches; the api reads the newest mirrored time"),
    ("", "mm2-offsets · offset-syncs", "mm2", "after each produce acknowledgement", "mm2 at restart", "resume the copy; anything re-copied is absorbed by the DB key"),
    ("", "__consumer_offsets", "consumers", "stored after the DB commit, sent every 5 s", "consumers at restart", "resume; anything redelivered is absorbed by the DB key"),
    ("central-db (PostgreSQL)", "readings", "consumer readings loop", "per batch ≤5,000: INSERT … SELECT unnest(…) ON CONFLICT DO NOTHING", "api", "every 2 s: readings per 5 s bucket, by device_time"),
    ("", "machine_progress", "same tx, as a CTE", "stored += inserted · max_seq · max_device_time", "api", "every 2 s: missing = generated − stored; gaps; watermark"),
    ("", "ingest_totals", "same tx", "received += batch size · inserted += rows inserted", "api", "every 2 s: duplicates absorbed = received − inserted"),
    ("", "alarms", "consumer alarms loop", "per batch ≤100: INSERT … ON CONFLICT (machine_id, alarm_seq) DO NOTHING", "api", "every 2 s: recent alarms, latency = stored_at − device_time"),
    ("", "rejected_records", "readings loop, same tx", "a record that fails to parse, with topic, partition, offset", "api", "rejected count on the dashboard"),
]
L0 = 1468 + D
for x, h in cols:
    extras.append(text(x, L0 + 40, h, 7, SOFT, MONO, 500, spacing="0.14em"))
extras.append(f'<line x1="64" y1="{L0 + 48}" x2="1776" y2="{L0 + 48}" stroke="rgba(45,49,66,0.20)" stroke-width="0.8"/>')
for i, r in enumerate(rows):
    y = L0 + 66 + i * 20
    if r[0] and i:
        extras.append(f'<line x1="64" y1="{y - 14}" x2="1776" y2="{y - 14}" stroke="rgba(45,49,66,0.10)" stroke-width="0.8"/>')
    extras.append(text(64, y, str(i + 1), 9, SOFT))
    if r[0]:
        extras.append(text(92, y, r[0], 11, INK, SANS, 600))
    for (x, _), cell in zip(cols[2:], r[1:]):
        extras.append(text(x, y, cell, 9, INK if x == 268 else MUTED))

# ---------------------------------------------------------------- legend
LY = 1804 + D
legend = [f'<line x1="40" y1="{LY}" x2="1800" y2="{LY}" stroke="rgba(45,49,66,0.10)" stroke-width="0.8"/>',
          text(40, LY + 20, "LEGEND", 8, MUTED, MONO, spacing="0.14em")]
items = [("box", "focal", "Focal: plant Kafka and the central DB"), ("box", "service", "Container"),
         ("box", "store", "Durable store or file"), ("box", "input", "Devices (input)"),
         ("box", "prod", "Production only: simulated in the demo"), ("box", "standby", "Standby"),
         ("box", "chip", "Inside a container"),
         ("line", MUTED, "Data flow"), ("line", LINK, "HTTP call"), ("dash", MUTED, "Passive read"),
         ("icon", "ethernet", "RJ45 LAN"), ("icon", "wifi", "Wi-Fi"), ("icon", "bluetooth", "Bluetooth"), ("icon", "serial", "Serial")]
x = 40
for kind, v, lab in items:
    y = LY + 28
    if kind == "box":
        fill, stroke, sw, dash, _ = T[v]
        dsh = f' stroke-dasharray="{dash}"' if dash else ""
        legend.append(f'<rect x="{x}" y="{y}" width="14" height="10" rx="2" fill="{fill}" stroke="{stroke}" stroke-width="1"{dsh}/>')
        lead = 20
    elif kind == "icon":
        legend.append(icon(v, x, y - 1, MUTED, 0.5))
        lead = 18
    else:
        mk = "arrow-link" if v == LINK else "arrow"
        dsh = ' stroke-dasharray="5,4"' if kind == "dash" else ""
        legend.append(f'<line x1="{x}" y1="{y + 5}" x2="{x + 24}" y2="{y + 5}" stroke="{v}" stroke-width="1"{dsh} marker-end="url(#{mk})"/>')
        lead = 32
    legend.append(text(x + lead, y + 8, lab, 8.5, MUTED, SANS))
    x += lead + round(len(lab) * 5.1) + 28

TITLE = "Plant to central: every container, port and data store"
DESC = ("Each shop's 100 devices talk over RJ45 Ethernet, Wi-Fi, Bluetooth LE or serial to one plant-wide network per "
        "medium; a unified device interface normalises their frames and forwards them to five shop gateways, which buffer "
        "in SQLite and produce to plant Kafka; MirrorMaker 2 copies the plant topics over the WAN into central Kafka; two "
        "store-writer consumers insert into PostgreSQL under a unique (machine_id, seq) key; a control-room API and "
        "dashboard watch every stage; and a ledger lists who writes and reads each store, when and how.")

body = "\n".join(["<!-- Zones -->", *zones, "<!-- Connectors (behind boxes) -->", *arrows, "<!-- Arrow labels -->", *labels,
                  "<!-- Nodes -->", *nodes, "<!-- Notes, callout, ledger -->", *extras, "<!-- Legend -->", *legend])

html = f"""<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>{TITLE}</title>
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
    .frame {{ max-width: {W}px; width: 100%; }}
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
    svg {{ width: 100%; min-width: {W}px; display: block; }}
    /* Paper has no scrollbar: release the scroller in print. Must follow the svg rule. */
    @media print {{
      .diagram-container {{ overflow-x: visible; }}
      svg {{ min-width: 0; }}
    }}
  </style>
</head>
<body>
  <div class="frame">
    <p class="eyebrow">Architecture · detailed · as built for the demo, with production values</p>
    <h1>{TITLE}</h1>

    <div class="diagram-container">
      <svg viewBox="0 0 {W} {H}" xmlns="http://www.w3.org/2000/svg" role="img" aria-labelledby="01-plant-to-central-title 01-plant-to-central-desc">
        <title id="01-plant-to-central-title">{TITLE}</title>
        <desc id="01-plant-to-central-desc">{escape(DESC)}</desc>
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
OUT.write_text(html)
print("wrote", OUT, len(html), "bytes")
