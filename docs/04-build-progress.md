# Build Progress and Handover

> Updated after every step of the build, so another agent (opencode, or Claude after a context compaction) can take over at any point. Last update: 13 November 2026, Task 14 (dashboard) in fix round 1. The branch is pushed to `origin/feat/plant-demo` after every step.

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
| 14 | Dashboard (+ the alarm-latency clamp as its own commit) | **Fix round 1 running** | `225c837`, `626ed6d` | Needs fixes: MM2 tile health; on-demand duplicate check; live banner age; error boundary |
| 15 | `drill.sh` | To do | — | — |
| 16 | Cold-start rehearsal, README | To do | — | — |
| R1–R4 | Resilience tier (section 5) | Designed; **not yet written into docs 00/02/03** | — | — |

Tests on the branch: 143 unit, 13 integration and 23 frontend (`bun test`) tests passing, 0 warnings (after Task 13). One flaky candidate is logged for the final review (a sender unit test with a 31 s fake-clock advance).

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

## 5. Pending: resilience tier (user requirements, 12 November, evening)

The user asked, in their own words:
- "If one of the producer gets down the another will take its place, keep ready for back plans and fallback always"
- "make zero downtime solution"
- "make zero fault tolerant solution"

The design decided, to be written into doc 02 (a new "Fault tolerance and zero downtime" subsection in §4.3, plus §4.10 rows 5 and 6), doc 00 (§3 containers and memory, §10 drills, §13 cut line) and doc 03 (new tasks R1–R4 replacing Stretch 1 and 2), then built:

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
3. **Right now:** Task 14 is in fix round 1 (`626ed6d`): MM2 health tile, duplicate check only on demand or when paused and drained, a banner age that counts, and an error boundary. If the result is lost, look for a commit after `626ed6d` touching `src/frontend/`. If it exists, re-review `626ed6d..HEAD` against the four findings in the ledger; otherwise re-dispatch them. Then Task 15 (`drill.sh`, `task-15-brief.md`).
4. Then dispatch Task 7b, then 8 to 16 in order, using each task's brief.
   - Generate a brief with `bash ~/.claude/plugins/cache/superpowers-dev/superpowers/7.0.2/skills/subagent-driven-development/scripts/task-brief "$PWD/docs/03-implementation-plan.md" N`, or copy the task's section of doc 03.
   - Refresh `global-constraints.md` from doc 03's Global Constraints whenever doc 03 changes.
5. After Task 16: write R1–R4 into docs 02, 00 and 03 (section 5 has the content), commit, then build R1 to R4 the same way.
6. Keep the rules: test-first, one commit per task in Conventional Commits with the `Co-Authored-By` trailer, and review before the next task. Since 13 November the user wants every step committed **and pushed** to `origin/feat/plant-demo`, together with a progress update; scan for private data before each push.
7. Update this file after every step and commit it (`docs: update build progress`).
