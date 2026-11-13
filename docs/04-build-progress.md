# Build Progress and Handover

> Updated after every step of the build, so another agent (opencode, or Claude after a context compaction) can take over at any point. Last update: 13 November 2026, 13:05 IST: Claude took over from opencode. R1–R4 merged into feat/plant-demo (fast-forward to `afb9bd9`); pre-demo hardening (H1) in progress.

## 1. Where things are

| # | What | Where |
| --- | --- | --- |
| 1 | Branch | `feat/plant-demo` (from `development`), pushed to `origin/feat/plant-demo` after every step |
| 2 | Spec | [doc 00](00-edd-demo-game-plan.md) build spec, [doc 02](02-solution-design.md) solution design (wins conflicts) |
| 3 | Plan | [doc 03](03-implementation-plan.md): tasks, exact values, tests |
| 4 | Detailed ledger | `.superpowers/sdd/03-implementation-plan/progress.md` (git-ignored, local): every dispatch, review, ruling, parked finding |
| 5 | Task briefs, reports, review diffs | Same folder: `task-N-brief.md`, `task-N-report.md` (TDD evidence), `review-<base>..<head>.diff`, `global-constraints.md` |

## 2. How each task runs

1. Extract the brief: the task's section of doc 03, verbatim.
2. A fresh builder works test-first: failing test, then minimal code, green, refactor, one commit. It writes its report with RED/GREEN evidence.
3. A fresh reviewer checks the diff against the brief and the spec, then for code quality.
4. Critical or Important findings go back to the same builder, followed by a scoped re-review, up to five rounds. Minor findings are logged for the final review.
5. The task is marked complete here and in the ledger.

After the last task: one whole-branch review on the most capable model, then the finish (PR to `development`).

## 3. Status

Execution order: 1, 2, 3, 4, 6, 5, 7, **7b**, 8, 9, 10, 11, 12, 13, 14, 15, 16, then the resilience tier R1–R4 (section 5), then Stretch 3.

| # | Task | Status | Commits | Review |
| --- | --- | --- | --- | --- |
| 1 | Contracts and shape tests | Done | `dc39237` | Clean |
| 2 | Compose skeleton and schema | Done | `d7595e8` | Clean |
| 3 | Machine simulator | Done | `b5b98a1` | Clean |
| 4 | SQLite outbox buffer | Done | `e27e9c3`, `1fb8dea` | Clean after 1 fix round |
| 6 | Kafka settings, CloudEvents, backoff | Done | `7102f51` | Clean |
| 5 | Collector (+ simulator re-inject fix) | Done | `de16055`, `f0fe090` | Clean |
| 7 | Sender, with a Kafka Testcontainers test | Done | `272ae74`, `ba00a3b` | Clean after 1 fix round |
| 7b | Shared (Result) + Infrastructure (global exception handling) | Done | `5a2ef94`, `5d4b4dc` | Clean after 1 fix round |
| 8 | Gateway host, image, 5 gateways | Done (live: ~500 readings/s, seqs continue across kill) | `ea3908d` | Clean |
| 9 | Central store (PostgreSQL) | Done (every reading twice → stored once, counters exact) | `041e123` | Clean |
| 10 | Consumer host, drain cap | Done (malformed and incomplete records quarantined, never stuck) | `31fa8a7`, `7107eed`, `3c37261` | Clean after 1 fix round |
| 11 | MirrorMaker 2, end-to-end check | Done (live: 500 machines → PostgreSQL, 0 gaps, 0 duplicate rows, redelivery absorbed; regex fixed to POSIX `[0-9]+`) | `7566fd9`, `18de8cb` | Clean after 1 fix round |
| 12 | Proof, ETA, watermark maths | Done (PASS only when paused, drained, 0 missing, 0 gaps) | `df8f0d6` | Clean |
| 13 | Control-room API | Done (live: proof WAIT → PASS with 0/0/0; overview answers with central down; ETA "—" during outage) | `d067b02`, `58ef5ac` | Clean after 1 fix round |
| 10b | Fix: central alarms never stored (found live in Task 13). Root cause: .NET dedupes `AddHostedService` by type, so the alarms loop was dropped | Done (live: 62 alarms stored, `central-alarms` group exists) | `3678d05` | Clean |
| 14 | Dashboard (+ the alarm-latency clamp as its own commit) | Done (live: green tiles, 500/s, outage hole, MM2 tile turns Degraded when lagging, banner on API loss, pause → PASS, Resume → WAIT at once) | `225c837`, `626ed6d`, `15686b1`, `44ef7e6` | Clean after 2 fix rounds |
| 14b | Fix: honest panel notes during an outage | Done. A page loaded during an outage now says 'No data yet. The central database did not answer. Retrying every second.'; a long-running page keeps its last data (live screenshots). screenshot.py gains --run/--after | `817d4b9`, `18b3fb8`, `2ff9d5a` | Clean after 1 fix round |
| 13b | Fix: the proof verdict flickers PASS/WAIT once paused and drained (found live in Task 15) | Done (root cause: the proof compared the DB sample with the latest offsets poll; now with the start of the stable run. Live: 60/60 PASS, WAIT 0.6 s after Resume) | `bbb47ca` | Clean |
| 15 | `drill.sh` | Done (live: central down 60 s → up → PASS 914,200 = 914,200, 0/0/0, 117,742 duplicates absorbed; crash drills → PASS) | `f3e0975` | Clean |
| 16 | Cold-start rehearsal, README | Done. Rehearsal PASS 184,700 = 184,700, 12,502 duplicates absorbed, cold start 12 s; README run section | `e8a272e`, `9289603` | Clean after 1 fix round |
| Rfix | Resilience docs fix round (spec review findings 1–7, rulings 13–14) | Done (opencode) | `cad43ae` | Clean after re-review (muse-spark) |
| R1 build | Reconcile buffer state from the plant log | Done (opencode). Live drill 8 PASS before R3 | `8236c6f` | Clean, 4 minors parked (muse-spark) |
| R2 build | Backfill missed seconds from machine history; DataGap beyond 600 s | Done (opencode) | `5d1aa1f` | Clean, 5 minors parked (muse-spark) |
| R3 build | Active/standby gateway pairs on a Kafka lease: 10 gateways, fencing, standby reconcile, proof `Over` → FAIL | Done (opencode). Isolated-stack failover verified | `12edadc` | Clean, 11 minors parked (deepseek); Opus re-review running |
| R4 build | Two APIs behind nginx, two MirrorMaker 2 workers, `restart: on-failure`, diagram 08 | Done (opencode). Kill-API, kill-MM2, central-down proof PASS 3,561,600 = 3,561,600 | `afb9bd9` | Clean, 6 minors parked (deepseek); final review (deepseek): merge-ready with conditions |
| H1 | Pre-demo hardening: R1–R4 fast-forwarded into `feat/plant-demo`; README and doc 00 §14 for pairs and failover; duplicate-row scan gets 30 s; `drill.sh proof` prints `over` and tolerates a starting instance; full drill rehearsal on the 21-container stack | **In progress** (Claude) | — | — |

Tests on the branch: 170 unit (3 runs green), 19 integration and 47 frontend (`bun test`) tests passing, 0 warnings, C# formatted.

## 4. Rulings so far

| # | Ruling | Why |
| --- | --- | --- |
| 1 | Task 6 runs before Task 5 | Task 5's project references Messaging, which Task 6 creates |
| 2 | No parallel builders | One working tree; parallel commits would conflict |
| 3 | Task 8 test-drives `GatewayControl` first | Strict TDD: the plan had no failing test for `/control` |
| 4 | Task 14 test-drives pure formatters with `bun test` first | Strict TDD for the dashboard, no new dependency |
| 5 | Builders and reviewers on Sonnet; final review on Opus | Cost, with the most capable model kept for the final review |
| 6 | `TreatWarningsAsErrors` stays in `Directory.Build.props` only | Already inherited by every project, with no overrides; written into the constraints |
| 7 | New Task 7b: `Shared` (Result/Error) and `Infrastructure` (global exception handler, Result → ProblemDetails, process sampler) | User asked for a Result pattern and global exception handling |
| 8 | Idempotency shown as a hop-by-hop table in doc 02 §4.6, each row with its test; no HTTP `Idempotency-Key` | Every command is idempotent by design |
| 9 | A failed buffer write is fatal: exit non-zero, Compose restarts from disk | Memory must never run ahead of disk (doc 02 §4.2 rule 2) |
| 10 | The resilience tier (section 5) comes after Task 16 | A working, rehearsed demo first is the main fallback; failover builds on it |
| 11 | Doc 00 gives the measured drain: writers resume about 45 s after `central-up`; a 2.5-minute outage drains in about 2 min 15 s | The Task 16 rehearsal measured it; the earlier ~40 s was an estimate |
| 12 | Task 14b re-scoped: long-running pages already keep their last data; fix only the false "last data" note on a page opened during an outage | The rehearsal screenshot was a fresh page load |
| 13 | Resilience fix round: fence the lease (any assignment after a lost session reconciles first), drain the sender on revocation, `drained` needs every gateway buffer empty, standbys reconcile every 10 s | The spec review found two writers after a one-sided session loss and a simulator that cannot replay a seq |
| 14 | Lease assignor `cooperative-sticky`: the current owner keeps the lease, a repaired gateway rejoins as the standby | Matches doc 02 §4.10 row 6; one takeover per failover instead of two |
| 15 | Opencode's stacked branches (`feat/plant-demo-rfix-docs`, `-r1` … `-r4`, one commit each, linear on `8b94fc0`) are fast-forwarded into `feat/plant-demo`; the side branches stay as they are | No conflicts, no history rewrite; `feat/plant-demo` stays the one branch to push |
| 16 | The duplicate-row scan (`full=true`) gets 30 s instead of the 2 s query cap; `drill.sh proof` polls the light reconciliation until drained, then makes one full call | At 3.5 M rows the scan took over 2 s, so the proof printed "duplicate rows: None" while doc 00 §14 promises 0 on screen. The dashboard asks for it only when paused and drained, at most every 10 s |
| 17 | The orphaned pre-R3 volumes `edd_gateway-<shop>-data` are kept, not deleted | Standing rule: never delete Docker volumes. They are small and unused |
| 18 | `drill.sh lose-gateway-disk` is rehearsed even though it deletes one gateway volume | It is the approved drill 8; it deletes only that gateway's buffer, which is what it proves the system survives |

## 5. Superseded: resilience tier (built as R1–R4, see section 3)

The user asked, in their own words:
- "If one of the producer gets down the another will take its place, keep ready for back plans and fallback always"
- "make zero downtime solution"
- "make zero fault tolerant solution"

**Superseded:** this was the first design. Docs 02 and 03 now hold the source of truth, after the spec review (rulings 13 and 14). The design decided, to be written into doc 02 (a new "Fault tolerance and zero downtime" subsection in §4.3, plus §4.10 rows 5 and 6), doc 00 (§3 containers and memory, §10 drills, §13 cut line) and doc 03 (new tasks R1–R4 replacing Stretch 1 and 2), then built:

**Framing for the panel:** absolute zero is not possible across a network. The target is no single point of failure, zero data loss, failover in seconds, and component downtime that never becomes data downtime, because missed seconds are backfilled from machine history.

| # | Task | What | Test |
| --- | --- | --- | --- |
| R1 | Reconcile from the plant log (was Stretch 1) | `PlantLogReader.NewestAsync(bootstrap, machines, ct)` returns `LogPosition(LastSeq, LastAlarmSeq, LastDeviceTimeMs)` per machine, scanning each partition of both plant topics backwards in doubling chunks. `GatewayBuffer.ReconcileAsync(log)` runs in one transaction: it deletes local rows whose seq is at or below the log's per machine, and sets state = max(local, log) | Kafka container: seqs 1–50 → `LastSeq` 50. Temp buffer: local rows 48–52 with log at 50 → rows 51–52 remain, state 52 |
| R2 | Backfill and `DataGap` (was Stretch 2) | `Collector.Backfill(now, depthSeconds)` yields 60-second chunks from each machine's last device time; past the depth (600 s), one `DataGap` alarm per machine | Gap 120 s → 120 readings per machine; gap 900 s, depth 600 → 600 readings plus a `DataGap` |
| R3 | Active/standby gateway pairs with a Kafka lease | Topic `gateway.lease.<shop>` with 1 partition; consumer group `gateway-lease-<shop>`; static membership `group.instance.id` = container name; `session.timeout.ms` 6000 and `heartbeat.interval.ms` 2000. On assignment: reconcile (R1), backfill (R2), then start the collector and sender. On revocation: stop both at once and leave unacked rows. `GatewayMetrics` gains `Role` (`active`/`standby`). Compose runs `gateway-<shop>-a` and `-b` (10 gateways, +1.28 GB). The API polls all 10; the dashboard shows each shop's active instance. Drill: `drill.sh failover <SHOP>` kills the active one | Kafka container: two leases → exactly one active; dispose it → the other is active within 15 s. Drill → proof PASS, 0 gaps |
| R4 | No single point of failure elsewhere in the demo | API ×2 behind nginx `upstream` (passive health check). MirrorMaker 2 ×2 in dedicated mode with internal REST. Consumers are already ×2 in one group. `restart: on-failure` on every .NET service | Kill one API → the dashboard stays up. Kill one mm2 → mirroring continues |

Production only, documented and not built (laptop memory): 3 brokers per site with RF 3 and min ISR 2; a PostgreSQL synchronous standby with automatic failover (Patroni); `transactional.id` zombie fencing on the gateways, with `isolation.level=read_committed` in MirrorMaker 2; rolling upgrades of each gateway pair (standby first, then switch over).

### Must fix before the demo

- ~~Alarm latency shows slightly negative~~: fixed in `225c837` (clamped at 0, with a test).
- ~~`drill.sh proof`: a malformed poll body aborted under `set -e`; curl had no time limit~~: fixed in `1f69cb0` (a bad body counts as not drained; `--max-time 5` on the poll, 10 on the POSTs). Live proof PASS 305,300 = 305,300.

## 6. Fallbacks (plan B for every risk)

| # | Risk | Fallback |
| --- | --- | --- |
| 1 | Kafka 4.4 misbehaves | Pin `apache/kafka:4.3.1` (doc 00 §16) |
| 2 | Testcontainers cannot start Kafka 4.4 | Generic `ContainerBuilder` with the §3 broker environment (not needed so far: Task 7 passed) |
| 3 | A gateway process crashes | `restart: on-failure` restarts it from its disk in seconds; after R3, its standby takes over |
| 4 | The resilience tier is not finished in time | Demo the must tier: crash-and-restart from disk, plus the doc 02 failover design on screen |
| 5 | Dashboard build trouble | `drill.sh proof` prints the verdict in the terminal; `/api/*` JSON in the browser |
| 6 | The live demo fails on the day | A screen recording of a full rehearsal, made in Task 16 |
| 7 | Laptop memory pressure | Stop the standby gateways (R3) first; every limit is in doc 00 §3 |
| 8 | Claude Code limit or compaction | This file plus the ledger; opencode continues from section 7 |

## 7. Handover: how to continue

1. `git switch feat/plant-demo` and `git log --oneline` shows the commits above.
2. Read this file, then the ledger in `.superpowers/sdd/03-implementation-plan/progress.md`. Trust the ledger and git over memory.
3. **Right now:** H1, pre-demo hardening (Claude, from 12:30 IST). Done: R1–R4 fast-forwarded into feat/plant-demo; 170 unit ×3, 19 integration and 47 frontend tests green; stack rebuilt (21 containers, each shop's active instance named on its tile). In progress: README, doc 00 §14 (failover step) and doc nits from opencode's final review; duplicate-row scan 30 s; drill.sh proof prints over; full drill rehearsal (central-down/up, kill-gateway, failover, lose-gateway-disk, kill-consumer, proof after each); Opus re-review of R1–R3 code. Then: fix what the re-review and rehearsal find, final rehearsal from 17:00 IST. The demo is at 19:00 IST.
4. Continue in the order in section 3. **Use the scripts in [`src/scripts/`](../src/scripts/README.md)** instead of retyping commands:
   - `sdd/sdd.sh brief N`, `review BASE HEAD` and `log "…"` for the build loop;
   - `build/test-backend.sh` and `build/test-frontend.sh` for tests;
   - `live/api.sh` against the running stack;
   - `progress/progress.py` for this file;
   - `git/commit-push.sh "<subject>" [paths]` to commit, scan for private data and push.
5. R1–R4 are specified in doc 03 (written in `d175d4d`, then fixed after the spec review in `resilience-spec-review.md`). Build them the same way, R1 → R2 → R3, and R4.
6. Keep the rules: test-first, one commit per task in Conventional Commits with the `Co-Authored-By` trailer, and review before the next task. Since 13 November the user wants every step committed **and pushed** to `origin/feat/plant-demo`, together with a progress update; scan for private data before each push.
7. Update this file after every step and commit it (`docs: update build progress`).
