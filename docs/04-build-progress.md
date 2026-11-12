# Build Progress and Handover

> Updated after every step of the build, so another agent (opencode) can take over at any point. Last update: 12 November 2026, Task 7 in progress.

## 1. Where things are

| # | What | Where |
| --- | --- | --- |
| 1 | Branch | `feat/plant-demo` (from `development`; nothing pushed) |
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

Execution order (rulings 1 and 7): 1, 2, 3, 4, 6, 5, 7, **7b**, 8, 9, 10, 11, 12, 13, 14, 15, 16, then Stretch 1–3.

| # | Task | Status | Commits | Review |
| --- | --- | --- | --- | --- |
| 1 | Contracts and shape tests | Done | `dc39237` | Clean |
| 2 | Compose skeleton and schema | Done | `d7595e8` | Clean |
| 3 | Machine simulator | Done | `b5b98a1` | Clean |
| 4 | SQLite outbox buffer | Done | `e27e9c3`, `1fb8dea` | Clean after 1 fix round |
| 6 | Kafka settings, CloudEvents, backoff | Done | `7102f51` | Clean |
| 5 | Collector (+ simulator re-inject fix) | Done | `de16055`, `f0fe090` | Clean |
| 7 | Sender (Kafka integration test) | **In progress** | — | — |
| 7b | Shared (Result) + Infrastructure (global exception handling) | Next | — | — |
| 8 | Gateway host, image, 5 gateways | To do | — | — |
| 9 | Central store (PostgreSQL) | To do | — | — |
| 10 | Consumer host, drain cap | To do | — | — |
| 11 | MirrorMaker 2, end-to-end check | To do | — | — |
| 12 | Proof, ETA, watermark maths | To do | — | — |
| 13 | Control-room API | To do | — | — |
| 14 | Dashboard | To do | — | — |
| 15 | `drill.sh` | To do | — | — |
| 16 | Cold-start rehearsal, README | To do | — | — |

Tests on the branch: 48 unit tests passing, 0 warnings (after Task 5).

## 4. Rulings so far

| # | Ruling | Why |
| --- | --- | --- |
| 1 | Task 6 runs before Task 5 | Task 5's project references Messaging, which Task 6 creates |
| 2 | No parallel builders | One working tree; parallel commits would conflict |
| 3 | Task 8 test-drives `GatewayControl` first | Strict TDD: the plan had no failing test for `/control` |
| 4 | Task 14 test-drives pure formatters with `bun test` first | Strict TDD for the dashboard, no new dependency |
| 5 | Builders and reviewers on Sonnet; final review on Opus | Cost, with the most capable model kept for the final review |
| 6 | `TreatWarningsAsErrors` stays in `Directory.Build.props` only | Already inherited by every project, with no overrides; written into the constraints |
| 7 | New Task 7b: `Shared` (Result/Error) and `Infrastructure` (global exception handler, Result → ProblemDetails, process sampler) | User asked for a Result pattern and global exception handling; doc 00 §12 already listed both projects |
| 8 | Idempotency shown as a hop-by-hop table in doc 02 §4.6, each row with its test; no HTTP `Idempotency-Key` | Every command is idempotent by design |
| 9 | A failed buffer write is fatal: exit non-zero, Compose restarts from disk | Memory must never run ahead of disk (doc 02 §4.2 rule 2) |

## 5. Handover: how to continue

1. `git switch feat/plant-demo` and `git log --oneline` shows the commits above.
2. Read this file, then the ledger in `.superpowers/sdd/03-implementation-plan/progress.md`. Trust the ledger and git over memory.
3. If a task shows **In progress** but has no review entry, check for its commit:
   - If the commit exists, review it against `task-N-brief.md`.
   - If not, re-run the task from its brief.
4. For the next task, give a fresh builder `task-N-brief.md` and `global-constraints.md`. Generate a brief with: `bash ~/.claude/plugins/cache/superpowers-dev/superpowers/7.0.2/skills/subagent-driven-development/scripts/task-brief "$PWD/docs/03-implementation-plan.md" N`, or copy the task's section of doc 03.
5. Keep the rules: test-first, one commit per task in Conventional Commits with the `Co-Authored-By` trailer, review before the next task, no push without the user's say-so.
6. Update this file after every step.
