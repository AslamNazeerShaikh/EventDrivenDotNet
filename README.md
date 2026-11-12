# EventDrivenDotNet

Event-driven proof in .NET 11 and Kafka 4.4 for a plant scenario: about 500 machines stream readings while the central server is down for hours, and nothing may be lost or duplicated. Five edge gateways buffer on disk, a plant Kafka cluster rides out the outage, MirrorMaker 2 copies to a central cluster, and PostgreSQL stores each reading exactly once through a unique `(machine_id, seq)` key. Everything runs on one laptop with Docker Compose, with failure drills and a dashboard that proves zero loss and zero duplicates per machine.

![Plant to central: every container, port and data store](docs/diagrams/01-plant-to-central.svg)

| # | Document | What it holds |
| --- | --- | --- |
| 1 | [`docs/00-edd-demo-game-plan.md`](docs/00-edd-demo-game-plan.md) | Build spec for the demo: containers, contracts, services, drills, tests, build plan |
| 2 | [`docs/01-second-round-design-exercise.md`](docs/01-second-round-design-exercise.md) | The exercise brief and what it asks |
| 3 | [`docs/02-solution-design.md`](docs/02-solution-design.md) | The solution design: architecture, the twelve aspects, sizing, trade-offs |
| 4 | [`docs/03-implementation-plan.md`](docs/03-implementation-plan.md) | The task-by-task build plan |
| 5 | [`docs/04-build-progress.md`](docs/04-build-progress.md) | What is built, what is left, and the test counts |

## Run the demo

### Prerequisites

1. Docker Desktop with its memory set to 10 GB (the containers' limits add up to about 5.1 GB).
2. `bash`, `curl` and `python3` for the drill and proof scripts.
3. The .NET SDK 11.0.100 and Bun 1.4.4 only to develop or run the tests.

### Start

From the repo root:

```sh
cd src/deployment
docker compose down -v                  # optional: a cold start from empty volumes
docker compose up -d --build --wait
```

Every service reports healthy or running, and `plant-topics` exits 0 after creating the topics. Open the control room at <http://localhost:3000> and give it a minute: five gateways healthy, about 500 readings per second.

### Drills

Run these from `src/deployment/` (the script works from anywhere). Each one prints the Docker command it runs, so the panel sees what is being broken.

| # | Command | What the panel sees |
| --- | --- | --- |
| 1 | `scripts/drill.sh central-down` | Central Kafka, the consumers and the database go Down. The plant keeps writing, and its alarms stay live. Mirror lag grows |
| 2 | `scripts/drill.sh central-up` | Mirror lag clears in seconds, then the database drains as its ETA counts down. The chart's gap fills in |
| 3 | `scripts/drill.sh kill-gateway BDY` | The gateway restarts and its sequence numbers continue |
| 4 | `scripts/drill.sh kill-consumer 1` | The consumer group rebalances and the redelivered batch is absorbed |
| 5 | `scripts/drill.sh pause` / `resume` | Stops or restarts all simulated machines |

To show the alarm lane, inject an alarm during the drain: `../scripts/live/api.sh fault P1-MCH-C017 Vibration`. It reaches central within seconds.

After `central-up`, the store writers resume in about 45 s and the backlog drains in about as long as the outage lasted.

### The proof

```sh
scripts/drill.sh proof
```

It pauses the plant, waits until every buffer and lag is drained, then prints the reconciliation and exits 0 on PASS. PASS means, for every machine, generated equals stored, with 0 gaps and 0 duplicate rows. The duplicates-absorbed count shows how many redeliveries the unique `(machine_id, seq)` key swallowed. While anything is still buffered or lagging, the verdict is WAIT, never PASS.

### Stop

```sh
docker compose down        # keeps the volumes; add -v to delete them
```

### More

1. [`src/scripts/README.md`](src/scripts/README.md): the developer and agent scripts (tests, API calls, screenshots).
2. [`docs/00-edd-demo-game-plan.md`](docs/00-edd-demo-game-plan.md) section 14 is the 12-minute demo script and section 10 lists every drill.
3. [`docs/01-second-round-design-exercise.md`](docs/01-second-round-design-exercise.md), [`docs/02-solution-design.md`](docs/02-solution-design.md), [`docs/03-implementation-plan.md`](docs/03-implementation-plan.md) and [`docs/04-build-progress.md`](docs/04-build-progress.md) hold the brief, the design, the plan and the progress.

## Approach

| # | Approach | Role in this repo |
| --- | --- | --- |
| 1 | SDD (Specification-Driven Development) | Planning, research, analysis — specs, contracts, pitfalls and pros/cons written before any code |
| 2 | DDD (Domain-Driven Design) | Structuring and design — bounded contexts, domain events/entities, code and dependency structure |
| 3 | TDD (Test-Driven Development) | Real implementation, Red → Green → Refactor, block by block (xUnit.net v3) |
| 4 | BDD (Behavior-Driven Development) | End-to-end scenarios in Given/When/Then form (Playwright for .NET) |
| 5 | FDD (Feature-Driven Development) | Workflow discipline — pros/cons, timeline, effort/capacity, delivery per feature slice |

### How They Work Together

- DDD guides the architecture and models the business problem.
- SDD defines the high-level specifications and contracts of what features must do.
- TDD verifies the low-level implementation block-by-block to ensure every piece works reliably.

## Stack

| # | Area | Choice |
| --- | --- | --- |
| 1 | Event medium | Apache Kafka 4.4 (KRaft): a plant cluster and a central cluster, joined by MirrorMaker 2 |
| 2 | Edge | Five .NET gateways, each with a SQLite store-and-forward buffer (`synchronous=FULL`) |
| 3 | Store | PostgreSQL 18, primary key `(machine_id, seq)`, `INSERT … ON CONFLICT DO NOTHING` |
| 4 | Runtime | .NET 11, C# 15, pure C# dispatch (no MediatR) |
| 5 | Dashboard | React, Vite, TypeScript and Tailwind, built with Bun and served by nginx |
| 6 | Packaging | Docker Compose; Docker Official Images or Verified Publisher images, plus `apache/kafka` |
| 7 | Tests | xUnit.net v3 with Testcontainers; Playwright for .NET for end-to-end |

Process: SDD then TDD — each phase is specified (analysis, pitfalls, pros/cons) before code; every implementation step is red-green-refactor. License: [AGPL-3.0](LICENSE.md).
