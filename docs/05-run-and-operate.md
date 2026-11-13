# Run and Operate Guide

How to build, start, stop, test and present the plant demo, where to look at every piece of data, and what each part of the dashboard means. Commands run from the repository root unless a step says otherwise.

## Contents

1. [What you need](#1-what-you-need)
2. [What runs](#2-what-runs)
3. [Build and test the code](#3-build-and-test-the-code)
4. [Start, stop and reset](#4-start-stop-and-reset)
5. [Scripts that do the work](#5-scripts-that-do-the-work)
6. [Where to see which data](#6-where-to-see-which-data)
7. [Manual tests](#7-manual-tests)
8. [Operating the demo](#8-operating-the-demo)
9. [The dashboard, panel by panel](#9-the-dashboard-panel-by-panel)
10. [Troubleshooting](#10-troubleshooting)

## 1. What you need

| # | Tool | Needed for | Check |
| --- | --- | --- | --- |
| 1 | Docker Desktop, memory set to 10 GB | Running the stack (the containers' limits add up to about 7.3 GB) | `docker info` answers |
| 2 | `bash`, `curl`, `python3` | The drill, proof and API scripts | already on macOS |
| 3 | .NET SDK 11.0.100 | Building and testing the backend (not for running the demo) | `dotnet --version` |
| 4 | Bun 1.4.4 | Building and testing the dashboard (not for running the demo) | `bun --version` |
| 5 | Playwright for Python (optional) | Screenshots and the rehearsal's dashboard capture | `python3 -c "import playwright"` |

## 2. What runs

Docker Compose project `edd` (`src/deployment/compose.yaml`) runs 21 containers on three networks: `plant`, `central` and `ops`.

| # | Container(s) | Role | Port on your Mac |
| --- | --- | --- | --- |
| 1 | `gateway-<shop>-a`, `gateway-<shop>-b` for `prs`, `bdy`, `pnt`, `mch`, `asm` (10) | Edge gateways. Per shop, one is active (collects, buffers in SQLite, sends) and one is the standby | none |
| 2 | `plant-kafka` | Plant Kafka cluster: `machine.readings`, `machine.alarms`, `gateway.lease.<shop>` | 19092 |
| 3 | `plant-topics` | One-shot: creates the plant topics, then exits 0 | none |
| 4 | `mm2-1`, `mm2-2` | MirrorMaker 2: copies plant topics to central as `plant1.*` | none |
| 5 | `central-kafka` | Central Kafka cluster | 29092 |
| 6 | `consumer-1`, `consumer-2` | Store writers: insert into PostgreSQL | none |
| 7 | `central-db` | PostgreSQL 18, database `edd`, user `edd`, password `edd` (demo only) | 55432 |
| 8 | `api-1`, `api-2` | Control-room API | none (reached through the dashboard) |
| 9 | `dashboard` | nginx: serves the dashboard and proxies `/api/` to both APIs | **3000** |

Machines are simulated inside the gateways: 500 machines, 100 per shop, one reading per machine per second. Machine IDs look like `P1-<SHOP>-<letter><nnn>`, for example `P1-BDY-W042` (a weld robot in Body) or `P1-MCH-C017` (a CNC machine in Machining). The shops are `PRS` (press), `BDY` (body), `PNT` (paint), `MCH` (machining) and `ASM` (assembly).

## 3. Build and test the code

You need this only to change the code; the demo runs from the Docker images.

| # | What | Command | Expected |
| --- | --- | --- | --- |
| 1 | Backend build (warnings are errors) | `src/scripts/build/test-backend.sh build` | `0 Warning(s)`, `0 Error(s)` |
| 2 | Backend unit tests | `src/scripts/build/test-backend.sh unit` | `total: 170 failed: 0` |
| 3 | Backend integration tests (Docker running) | `src/scripts/build/test-backend.sh integration` | `total: 19 failed: 0` |
| 4 | All of the above | `src/scripts/build/test-backend.sh all` | all three pass |
| 5 | Find a flaky test | `src/scripts/build/test-backend.sh unit --repeat 5` | the names of any test that failed in any run |
| 6 | Dashboard tests and strict build | `src/scripts/build/test-frontend.sh` | `47 pass`, then a clean `vite build` |
| 7 | Format the C# code | `src/scripts/build/format.sh` (check only: `--check`) | `C# is formatted` |
| 8 | Rebuild the Docker images after a code change | `cd src/deployment && docker compose build` | every image builds |

## 4. Start, stop and reset

### Start Docker

```sh
open -a Docker                           # then wait until this answers:
docker info > /dev/null && echo ready
```

### Start the stack

```sh
cd src/deployment
docker compose up -d --wait              # add --build after any code change
```

1. It takes about a minute. The first ever build takes several minutes.
2. Every service reports healthy or running, and `plant-topics` exits 0.
3. **If the plant was paused when it was stopped** (it was on 13 November), resume it:

   ```sh
   scripts/drill.sh resume
   ```

   You can also use **Resume** in the dashboard's Alarms panel.
4. Open <http://localhost:3000>. Within a minute: all stages green, five shops each naming their active gateway (`a` or `b`), about 500 readings per second.

### Check it is up

```sh
docker compose ps                        # 20 running, plant-topics exited (0)
../scripts/live/api.sh overview          # one line per area: gateways, clusters, rates, lag
```

### Stop the stack and keep the data

```sh
scripts/drill.sh pause                   # optional, but see the note below
docker compose down                      # removes containers, keeps every volume
osascript -e 'quit app "Docker"'         # optional: stop Docker itself to free the CPU
```

Pause first if the stack will be down for long. A gateway that starts paused does not backfill the stopped time. Without the pause, it backfills up to 10 minutes of history and records anything older as a `DataGap` alarm per machine. That is correct behaviour, but it fills the alarm panel.

### Reset to empty (deletes all data)

```sh
docker compose down -v                   # deletes the database, both Kafka logs and every gateway buffer
docker compose up -d --wait
```

Only do this when you want a cold start with zero rows. It cannot be undone.

### Storage: how big it gets and how to free space

Measured on 13 November 2026 at 16:35 IST, after about a day of running:

| # | What | Size | How it grows |
| --- | --- | --- | --- |
| 1 | PostgreSQL (`edd_central-db-data`) | 6.6 GB, 22.5 M rows | About 0.5 GB per hour while the plant runs (500 rows/s). Nothing deletes rows: every stored reading is kept |
| 2 | Central Kafka (`edd_central-kafka-data`) | 11.5 GB | Capped by retention (8 h for readings). Kafka deletes only whole 1 GB segment files, so the volume can sit above 8 hours' worth |
| 3 | Plant Kafka (`edd_plant-kafka-data`) | 5.4 GB | Capped the same way |
| 4 | Gateway buffers (10 volumes) | a few MB each | Rows are deleted once Kafka acknowledges them, so they stay near empty unless Kafka is down |
| 5 | Docker build cache | 24.8 GB, of which 21 GB is reclaimable | Old image-build layers; no data |
| 6 | Docker's whole disk | 45.8 GB used of 910 GB (5%) | Plenty of room: running all night adds a few GB |

The size is not a load problem. At 22.5 M rows the database used about 3% CPU and 149 MB of its 256 MB limit, had no slow queries, and kept up with 500 rows/s. The one place size shows is the dashboard's duplicate scan, which takes about 6–10 s at 16–22 M rows.

Check the sizes yourself:

```sh
docker system df                                   # images, volumes, build cache
docker system df -v | grep edd_                     # each volume's size
docker compose exec central-db psql -U edd -d edd -c "SELECT pg_size_pretty(pg_database_size('edd'));"
docker stats --no-stream edd-central-db-1           # CPU and memory of the database
```

Three ways to handle it:

| # | Option | Command | What you lose |
| --- | --- | --- | --- |
| 1 | Keep everything (the default) | none | nothing; the demo and the proof work at this size |
| 2 | Free disk without touching any data | `docker builder prune` | only the build cache (about 21 GB); the next image build is slower |
| 3 | Clean start with small numbers | `docker compose down -v`, `docker compose up -d --wait`, then `scripts/drill.sh resume` | **everything**: the database, both Kafka logs and every gateway buffer, together. Numbering restarts from 1 everywhere, so the proof stays consistent. Run `src/scripts/live/rehearse.sh 120` afterwards |

**Never delete only database rows** (for example `TRUNCATE readings`). The gateways keep numbering from where they are, and the proof compares what they generated with what is stored. It would then show millions of readings missing and FAIL. Reset all the stores together (option 3), or none.

## 5. Scripts that do the work

Full list with usage: [`src/scripts/README.md`](../src/scripts/README.md). Every script prints its help with `-h`.

| # | Script | What it does |
| --- | --- | --- |
| 1 | `src/scripts/live/rehearse.sh [SECONDS] [SHOT.png]` | **Runs the whole demo once** and prints a results table: steady state, central down, an alarm during the drain, the drain, kill a gateway, failover, kill a consumer, proof, and a dashboard screenshot. Resumes the plant at the end. About 7 minutes with `120` |
| 2 | `src/deployment/scripts/drill.sh <command>` | The failure drills and the proof (section 7). Prints each Docker command it runs |
| 3 | `src/scripts/live/api.sh <command>` | Talks to the API: `overview`, `proof [full]`, `alarms`, `pause`, `resume`, `fault <MACHINE> <KIND>`, `watch-proof <SECONDS>`, `raw <PATH>` |
| 4 | `src/scripts/build/test-backend.sh`, `test-frontend.sh`, `format.sh` | Build, test, format (section 3) |
| 5 | `src/scripts/diagrams/screenshot.py <URL> <OUT.png> --full` | Screenshot of the dashboard or any page |
| 6 | `src/scripts/git/commit-push.sh "<subject>" <paths>` | Commit the named paths, scan for private data, push |

## 6. Where to see which data

Run the `docker compose` commands from `src/deployment`.

### At a glance

| # | Data | Where |
| --- | --- | --- |
| 1 | Everything live | Dashboard <http://localhost:3000> (section 9) |
| 2 | Pipeline summary | `../scripts/live/api.sh overview` |
| 3 | The proof, per machine | `../scripts/live/api.sh proof full`, or `curl -s 'localhost:3000/api/reconciliation?full=true'` |
| 4 | Latest alarms at central | `../scripts/live/api.sh alarms` |
| 5 | Raw API JSON | `curl -s localhost:3000/api/overview`, `/api/chart?minutes=10`, `/api/alarms` |

### One gateway

Its counters, role (`active` or `standby`), last sequence number per machine, and buffer size:

```sh
docker run --rm --network edd_plant busybox wget -qO- http://gateway-bdy-a:8080/metrics
```

Its SQLite buffer. Copy all three files; the WAL holds the newest rows:

```sh
for f in buffer.db buffer.db-wal buffer.db-shm; do docker compose cp gateway-bdy-a:/data/$f /tmp/$f; done
sqlite3 /tmp/buffer.db 'SELECT lane, count(*) FROM outbox GROUP BY lane;'   # lane 0 = alarms, 1 = readings
sqlite3 /tmp/buffer.db 'SELECT * FROM machine_state LIMIT 5;'
```

### Kafka

```sh
# topics, partitions and settings (plant; use central-kafka for the plant1.* copies)
docker compose exec plant-kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 --describe --topic machine.readings

# read a few records with their key and CloudEvents headers
docker compose exec plant-kafka /opt/kafka/bin/kafka-console-consumer.sh --bootstrap-server localhost:9092 \
  --topic machine.alarms --from-beginning --max-messages 5 --property print.key=true --property print.headers=true

# who holds a shop's lease (the member with partition 0 is the active gateway)
docker compose exec plant-kafka /opt/kafka/bin/kafka-consumer-groups.sh --bootstrap-server localhost:9092 \
  --describe --group gateway-lease-bdy --members --verbose

# store-writer lag at central
docker compose exec central-kafka /opt/kafka/bin/kafka-consumer-groups.sh --bootstrap-server localhost:9092 \
  --describe --group central-readings
```

### PostgreSQL

```sh
docker compose exec central-db psql -U edd -d edd
```

From your Mac you can also use `psql -h localhost -p 55432 -U edd edd` (password `edd`).

```sql
SELECT count(*) FROM readings;                                        -- every stored reading
SELECT * FROM machine_progress ORDER BY machine_id LIMIT 10;          -- stored and max seq per machine
SELECT machine_id, seq, device_time, signals FROM readings
  WHERE machine_id = 'P1-BDY-W042' ORDER BY seq DESC LIMIT 5;         -- one machine's latest readings
SELECT * FROM alarms ORDER BY device_time DESC LIMIT 10;              -- latest alarms
SELECT * FROM ingest_totals;                                          -- received vs inserted per writer
SELECT topic, kafka_partition, kafka_offset, error FROM rejected_records LIMIT 10;   -- quarantined records
SELECT count(*) FROM (SELECT 1 FROM readings GROUP BY machine_id, seq HAVING count(*) > 1) d;  -- duplicates: 0
```

### Logs and resources

| # | What | Command |
| --- | --- | --- |
| 1 | Lease changes (who became active, takeovers) | `docker compose logs -f gateway-bdy-a gateway-bdy-b \| grep -iE 'active\|lease\|assigned'` |
| 2 | Any service | `docker compose logs -f --tail 50 <service>` |
| 3 | Container state | `docker compose ps` |
| 4 | CPU and memory per container | `docker stats --no-stream` |

## 7. Manual tests

Each drill runs from `src/deployment` and prints the Docker commands it runs. After any drill, `scripts/drill.sh proof` must print `verdict: PASS` with `missing: 0`, `gaps: 0`, `over: 0` and `duplicate rows: 0`. The proof pauses the plant, so run `scripts/drill.sh resume` afterwards.

| # | Test | Command | What you should see |
| --- | --- | --- | --- |
| 1 | Whole demo, automatically | `../scripts/live/rehearse.sh 120` | `REHEARSAL PASSED` and a 9-row table, all PASS |
| 2 | Central outage | `scripts/drill.sh central-down`, wait 2 min, then `scripts/drill.sh central-up` | Central tiles go Down; the plant keeps 500/s; mirror lag grows. After `central-up`: mirror lag clears in seconds; about 45 s later the store writers resume and the drain ETA counts down; the chart's hole fills in. The drain takes about as long as the outage |
| 3 | Alarm during the drain | once the drain ETA counts down: `../scripts/live/api.sh fault P1-MCH-C017 Vibration` | The alarm appears in the Alarms panel within seconds, ahead of the reading backlog |
| 4 | Kill a gateway | `scripts/drill.sh kill-gateway BDY` | BDY's standby takes the lease at once; the restarted one becomes the standby; sequence numbers continue |
| 5 | Gateway failover | `scripts/drill.sh failover BDY` | `gateway-bdy-… took over after N s` (N about 7–8), then `back as the standby` with no second switch |
| 6 | Kill a store writer | `scripts/drill.sh kill-consumer 1` | The group rebalances; Duplicates absorbed rises (redelivered rows dropped by the key) |
| 7 | WAN cut | `scripts/drill.sh wan-cut`, wait, `scripts/drill.sh wan-restore` | Mirror lag grows, then clears |
| 8 | Plant broker down | `scripts/drill.sh plant-down`, wait, `scripts/drill.sh plant-up` | Gateway buffers climb, then drain (alarms first); every shop keeps its active gateway |
| 9 | Lose a gateway's disk (**deletes a volume**) | `scripts/drill.sh lose-gateway-disk BDY` | The rebuilt instance continues numbering from the plant log, with 0 gaps |
| 10 | Pause and resume | `scripts/drill.sh pause`, then `resume` | Rate drops to 0 and tiles show `(paused)`, then back to 500/s |
| 11 | The proof | `scripts/drill.sh proof` | `generated` = `stored`, `missing 0`, `gaps 0`, `over 0`, `duplicate rows 0`, `verdict: PASS` |
| 12 | Proof stays settled | `../scripts/live/api.sh pause`, wait 40 s, `../scripts/live/api.sh watch-proof 60` | 60 of 60 samples PASS |

## 8. Operating the demo

### Before going live

1. Start Docker, start the stack, and resume the plant (section 4).
2. Run `src/scripts/live/rehearse.sh 120` once. It must end with `REHEARSAL PASSED`.
3. Open <http://localhost:3000> in the browser you will present from, and close other heavy tabs.
4. Keep a terminal open in `src/deployment`.

### The 12-minute script

The full version with talking points is in [doc 00 §14](00-edd-demo-game-plan.md#14-demo-script-and-talking-points).

| # | Step | Command | Point at |
| --- | --- | --- | --- |
| 1 | The design (2 min) | — | [Diagram 01](diagrams/01-plant-to-central.svg): two tiers of buffering, one deduplication point |
| 2 | Steady state | — | All green, 500/s, freshness about 1 s |
| 3 | Central down | `scripts/drill.sh central-down`, wait 2 min | The plant keeps writing; mirror lag grows; plant alarms stay live |
| 4 | Central back | `scripts/drill.sh central-up` | Mirror lag clears; after about 45 s the drain ETA counts down. **Then** inject the alarm: `../scripts/live/api.sh fault P1-MCH-C017 Vibration` |
| 5 | Crash a gateway | `scripts/drill.sh kill-gateway BDY` | BDY's tile switches to its standby; sequences continue |
| 6 | Fail over a gateway | `scripts/drill.sh failover BDY` | Takeover in about 8 s; no second switch |
| 7 | The proof | `scripts/drill.sh proof` | PASS: generated = stored, 0 gaps, 0 duplicate rows, N duplicates absorbed. On the dashboard, give the duplicate count about 10 s ("Checking duplicates…") |
| 8 | Optional | `wan-cut`/`wan-restore`, `plant-down`/`plant-up`, `kill-consumer 1` | — |

Afterwards: `scripts/drill.sh resume`.

## 9. The dashboard, panel by panel

<http://localhost:3000>. Everything refreshes every second. Times are UTC. A panel whose request fails keeps its last data and says "Not updating, showing the last data received.", or "No data yet. … Retrying every second." if it never had any. If the API itself cannot be reached, a red banner at the top says so.

### Health badges

| # | Badge | Meaning |
| --- | --- | --- |
| 1 | Healthy | Updated within the last 3 s |
| 2 | Degraded | No update for 3–10 s, or a stated reason, such as mirror lag above 10 s |
| 3 | Down | No update for more than 10 s; the panel shows when it was last seen |

### Panels

| # | Panel | What it shows | How to read it |
| --- | --- | --- | --- |
| 1 | **Plant to central** (top strip) | Seven stages: Machines → Gateways ×5 → Plant Kafka → MirrorMaker 2 → Central Kafka → Consumers ×2 → Database, each with a badge and its key number | Machines: readings per second, or Paused. Gateways: rows sent per second and the oldest buffered row. Kafka stages: records and the newest record's age. MirrorMaker 2: mirror lag. Consumers: rows inserted per second and lag. Database: rows stored and how fresh |
| 2 | **Buffers and lag** | One row per shop: `SHOP · a` or `· b` (its active gateway), a bar for buffered rows, then `rows · oldest age`. Below: mirror lag, consumer lag, drain ETA | The bars grow while central or the plant broker is down, then empty. `(paused)` while paused; `(last seen)` if the gateway stopped answering. The drain ETA counts down after an outage |
| 3 | **Readings by device time, 10 min** | Readings stored per 5 seconds of device time | Blue bars are settled; amber bars, after the watermark, are still filling. An outage shows as a hole that fills in as the backlog drains: readings are placed by when the machine produced them, not when they arrived |
| 4 | **Proof** | Generated, Stored, Missing, Gaps, Duplicate rows, Duplicates absorbed, and the verdict | **PASS**: every generated reading is stored once. **WAIT**: not settled yet, so pause the plant and let every buffer and lag drain. **FAIL**: a reading is missing, out of sequence or duplicated. Duplicate rows comes from a full scan, run on the **Check duplicates** button and automatically at most every 10 s once paused and drained; it shows "Checking duplicates…" while running. Duplicates absorbed counts redeliveries the key rejected. **Per-machine table** expands all 500 machines |
| 5 | **Alarms** | The latest alarms stored at central: time, machine, kind, severity, latency | Latency is the delay from the machine to central (0.0 s in steady state; higher during a drain). Below: **Machine id** + **Kind** + **Inject alarm** (kinds: Overheat, Vibration, Overload, EStop, LowBattery, OutOfBand), and **Pause** / **Resume** for all gateways. `Plant: running` or `paused` |

## 10. Troubleshooting

| # | Symptom | Cause | Fix |
| --- | --- | --- | --- |
| 1 | `localhost:3000` does not load | Stack not started, or the `dashboard` container is down | `docker compose ps`; `docker compose up -d --wait` |
| 2 | Red "API unreachable" banner | Both APIs down or still starting | `docker compose ps api-1 api-2`; wait 10 s; `docker compose up -d api-1 api-2` |
| 3 | Proof stays WAIT | The plant is running, or a buffer or lag is not empty yet | `scripts/drill.sh proof` pauses and waits up to 5 min. Check `../scripts/live/api.sh overview` for a non-zero buffer or lag |
| 4 | Rate stays 0 after a start | The plant was paused before it was stopped | `scripts/drill.sh resume` |
| 5 | Alarm panel full of `DataGap` after a start | The stack was stopped unpaused for more than 10 minutes, so each machine recorded its gap | Expected. Pause before stopping next time |
| 6 | Duplicate rows says "not checked" | The full scan is still running (about 6–10 s at 16–20 M rows) | Wait 10 s, or press **Check duplicates** |
| 7 | `failover` prints "did not take over" | A gateway is still starting, or busybox could not be pulled offline | Wait 30 s and run it again; `docker pull busybox` while online |
| 8 | A container keeps restarting | It failed its start-up (for example, a broker not ready) | `docker compose logs --tail 50 <service>`; services restart on failure by themselves |
| 9 | Fan loud, Docker VM busy | Each Kafka health check starts a Java process every 5 s (about 0.75 core for both brokers) | Harmless. Stop the stack when not in use (section 4) |
| 10 | Port 3000 in use | Another program holds it | Stop that program, or change `3000:80` in `compose.yaml` |
| 11 | The database looks overloaded | Usually only big: about 0.5 GB and 1.8 M rows per hour | `docker stats --no-stream edd-central-db-1` (a few % CPU is normal) and the consumer lag in `api.sh overview`. See "Storage" in section 4 |
| 12 | Docker disk filling up | The build cache (about 21 GB reclaimable) and data volumes growing | `docker system df`; `docker builder prune` frees the cache safely. Never delete only database rows (section 4) |
