# EventDrivenDotNet

Production-style event-driven reference in .NET 11 and Kafka 4.4 (KRaft): two producers publish domain events, two consumers sink them into SQLite (WAL), with a command API, delivery-proof endpoint, and React dashboard — all in one Docker image.
Append-only event log, idempotent consumers with DLQ isolation, and end-to-end tests (xUnit.net v3 + Playwright) demonstrating exactly-once-effect semantics.

```mermaid
flowchart LR
    Browser(["Browser<br/>React SPA · EXTERNAL"]) -->|HTTP :3000| Nginx
    subgraph Docker["docker · edd-demo · one image · supervisord PID 1"]
        direction LR
        subgraph Edge["edge tier"]
            direction TB
            Nginx["nginx :3000<br/>serves React build · proxies /api"] -->|proxy /api → :8080| API
            API["Command API :8080<br/>minimal API · SSE /api/stream<br/>start · stop · rate per producer"]
            SUP["supervisord · PID 1<br/>8 programs · restarts · unified logs"]
        end
        subgraph Prod["producers · .NET 11 · 100ms ticks · acks=all"]
            direction TB
            PA["producer-a :7101<br/>OrderPlaced · CloudEvents"]
            PB["producer-b :7102<br/>PaymentCaptured"]
        end
        subgraph Kafka["Kafka 4.4 KRaft · :9092 PLAINTEXT · :9093 controller<br/>heap 512M · Java 17+ · Confluent.Kafka 2.15.x"]
            direction TB
            TCMD["control.commands<br/>1 part · compacted"]
            TEVT["demo.events<br/>6 parts · key entity-id · 24h<br/>idempotent · lz4"]
            TACK["control.acks<br/>1 part · 1h"]
            TBRT["telemetry.heartbeats<br/>1 part · 1h"]
            TDLQ["demo.events.dlq<br/>1 part · 7d · poison only"]
        end
        subgraph Cons["consumers · group event-sinks · batch ≤500 / 50ms"]
            direction TB
            CA["consumer-a :7201<br/>CooperativeSticky"]
            CB["consumer-b :7202<br/>CooperativeSticky"]
        end
        subgraph Store["/data volume · SQLite WAL · 1 writer each"]
            direction TB
            DBA[("sink-a.db<br/>rows before offsets")]
            DBB[("sink-b.db<br/>INSERT OR IGNORE event_id")]
        end
        LOG["container stdout<br/>supervisord unifies all 8 · 10s HEALTHCHECK"]
        PRF["delivery proof<br/>Gen ≥ Acked = Bus ≥ Committed ≤ Persisted · gaps 0"]
        PUB["published: 3000 · 8080 · 9092 · 7101/02 · 7201/02<br/>internal only: 9093 controller"]
    end
    API -->|COMMANDS ×2| TCMD
    TCMD -->|COMMANDS · own group each| PA
    PA -->|EVENTS · OrderPlaced| TEVT
    PB -->|EVENTS · PaymentCaptured| TEVT
    TEVT -->|EVENTS| CA
    TEVT ==>|EVENTS| CB
    TACK -->|ACKS| API
    TBRT -.->|HEARTBEATS ×5| API
    CA -->|SQL BATCH| DBA
    CB -->|SQL BATCH| DBB
    CA -.->|POISON| TDLQ
    CB -.->|POISON| TDLQ
    API -.->|READ-ONLY| DBA
    API -.->|READ-ONLY| DBB
    SUP -.->|manages| LOG
    API -.->|proves| PRF
    Nginx ~~~ PA ~~~ TCMD ~~~ CA ~~~ DBA
    classDef ext fill:#0f172a,stroke:#94a3b8,color:#e2e8f0
    classDef edge fill:#082f3f,stroke:#22d3ee,color:#fff
    classDef api fill:#06302a,stroke:#34d399,color:#fff
    classDef svc fill:#06302a,stroke:#34d399,color:#fff
    classDef bus fill:#3a2408,stroke:#fb923c,color:#fff
    classDef dlq fill:#3d0f22,stroke:#fb7185,color:#fff
    classDef db fill:#2a1560,stroke:#a78bfa,color:#fff
    classDef ops fill:#0f172a,stroke:#64748b,color:#94a3b8
    classDef prf fill:#3a2a08,stroke:#fbbf24,color:#fff
    class Browser ext
    class Nginx edge
    class API api
    class SUP,LOG,PUB ops
    class TCMD,TEVT,TACK,TBRT bus
    class TDLQ dlq
    class PA,PB,CA,CB svc
    class DBA,DBB db
    class PRF prf
```

Full game plan (decisions, contracts, diagrams, build phases, demo script): [`docs/00-edd-demo-game-plan.md`](docs/00-edd-demo-game-plan.md).

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

| Area | Choice |
| --- | --- |
| Event medium | Apache Kafka 4.4, single-node KRaft |
| Runtime | .NET 11, C# 15, pure C# dispatch (no MediatR) |
| Sink | SQLite in WAL mode, one file per consumer |
| Dashboard | React + Vite + TypeScript behind nginx |
| Packaging | One image, `supervisord` as PID 1 |
| Tests | xUnit.net v3; Playwright for .NET for end-to-end |

Process: SDD then TDD — each phase is specified (analysis, pitfalls, pros/cons) before code; every implementation step is red-green-refactor. License: [AGPL-3.0](LICENSE.md).
