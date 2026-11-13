# Second-Round Design Exercise: Plant Outage Without Loss or Duplicates

> As of 11 November 2026, updated 12 November 2026
>
> The round-two brief from Anmeda Systems, kept verbatim, followed by what it asks for. The answer is [doc 02, the solution design](02-solution-design.md), and the live demo that proves it is specified in [doc 00, the game plan](00-edd-demo-game-plan.md).

---

## Contents

1. [The email, verbatim](#1-the-email-verbatim)
2. [What the email asks](#2-what-the-email-asks)
3. [How the exercise is answered](#3-how-the-exercise-is-answered)
4. [Toolchain and base images](#4-toolchain-and-base-images)

---

## 1. The email, verbatim

Decoded from the `.eml` file's plain-text part. The HTML part carries the same text, and the only attachment is the sender's signature logo (`S659.png`). Because this repository is public, email addresses, phone numbers, a tracking link and the confidentiality notice are marked `[redacted]`. Everything else is verbatim.

```text
From: Priyanka T <[redacted]>
To: [redacted]
CC: Ashok Srinivasa Rao <[redacted]>, Ambuj Kumar <[redacted]>, Anil Kumar S <[redacted]>, Usha Rani Palle <[redacted]>
Date: Wed, 11 Nov 2026 17:03:22 +0000
Subject: Second Round Discussion – Technical Design Exercise

Hi Aslam,
Thank you for the discussion.
As part of the next round of the interview process, we would like you to prepare and present a high-level solution design for the scenario discussed during the interview:
Scenario:
A system is connected to approximately 500 machines across an automobile manufacturing plant. Data is being generated continuously, and the central server becomes unavailable for six hours. The solution should ensure that no data is lost and duplicate data is not created after recovery.
Please prepare a high-level architecture/design covering the following aspects:

  *   Data flow from machines to the central system
  *   Local buffering / temporary storage during server or network unavailability
  *   Data durability and recovery mechanism
  *   Use of the proposed self-developed Kafka-like messaging/event streaming component
  *   Message acknowledgement and retry mechanism
  *   Duplicate prevention / idempotency
  *   Ordering of data/events
  *   Handling of backlog when connectivity is restored
  *   Scalability for 500 machines and future expansion
  *   Failure handling for edge nodes, messaging components, or communication links
  *   Monitoring and health-check considerations
  *   Key design assumptions and trade-offs

You may use an architecture diagram to explain the proposed solution.
The objective of the discussion is to understand your approach to system design, distributed architecture, reliability, scalability, and recovery mechanisms. Detailed implementation or source code is not required unless you feel it helps explain a specific design decision.
Our HR team will coordinate with you separately regarding the schedule for the second-round discussion.


Thanks,
Priyanka Thirukumar
HR & Admin
[image]
ANMEDA SYSTEMS PVT Ltd.

Mobile: [redacted] | [redacted]

www.anmedasystems.com<[redacted]>


Sent from Outlook for Android<https://aka.ms/AAb9ysg>
Thanks & Regards,
Priyanka T
Executive
ANMEDA SYSTEMS PVT LTD

(An ISO 9001:2015 Certified company)

M: [redacted] | E: [redacted]
W: www.anmedasystems.com<https://www.anmedasystems.com>
[ANMEDA Logo]           [https://upload.wikimedia.org/wikipedia/commons/c/ca/LinkedIn_logo_initials.png]  <https://www.linkedin.com/company/anmeda-systems-private-limited/>
[redacted]
```

---

## 2. What the email asks

### 2.1 The ask

Anmeda Systems' HR sent the email on 11 November 2026 and copied four colleagues, who are probably the round-two panel. It moves the interview to round two. The task is to prepare and present a high-level solution design for the scenario discussed in round one. A diagram is welcome. Source code is optional, only where it explains a design decision. HR will schedule the session separately, and the email sets no deadline.

### 2.2 The scenario in plain terms

- About 500 machines in an automobile plant send data continuously.
- The central server goes down for six hours.
- There are two hard requirements: no data is lost, and no duplicates appear after recovery.

The two requirements pull against each other, and handling that tension is the core of the exercise:

- No loss means the sender keeps resending until the receiver confirms receipt (at-least-once delivery).
- Resending creates duplicates, so the receiver must recognise and drop repeats (idempotency).
- Exactly-once delivery over a network is not achievable. The expected answer is at-least-once delivery plus deduplication, which gives effectively exactly-once results.

### 2.3 The twelve aspects to cover

| # | Aspect | What they're really asking | Core idea a strong answer gives |
| --- | --- | --- | --- |
| 1 | Data flow | The end-to-end path | Machine → edge gateway in the plant → broker → consumers → central store |
| 2 | Local buffering | Where six hours of data waits | A durable append-only log on disk at each edge gateway (store-and-forward), never memory alone |
| 3 | Durability and recovery | What survives a crash or power cut | Data is fsynced to disk before it counts as received; after a reboot, the gateway resumes from its last acknowledged position |
| 4 | Kafka-like component | How the in-house broker fits and what it must guarantee | A partitioned, replicated append-only log with offsets and consumer groups |
| 5 | Ack and retry | When data is safe and when to resend | Delete locally only after the broker acknowledges (after replication); retry with exponential backoff and jitter |
| 6 | Duplicates | How retries avoid double rows | Every message carries `(machine_id, sequence_no)`; the broker and the database drop IDs they have already seen (idempotent upsert) |
| 7 | Ordering | Whether order is guaranteed, and at what scope | Per machine, not plant-wide: partition key = `machine_id`, and the gateway sends in sequence |
| 8 | Backlog | Six hours of data arriving at once | Rate-limited drain with backpressure; live data keeps flowing while consumers catch up |
| 9 | Scale | 500 machines now, more later | Scale out by adding partitions, gateways and consumers; machines map to partitions by hash |
| 10 | Failures | An edge node, broker node or link dies | Broker replication; the gateway's disk survives restarts (or a redundant gateway takes over); a link failure means buffering |
| 11 | Monitoring | Knowing before data is lost | Buffer fill %, age of the oldest unsent message, consumer lag, ack latency, a heartbeat per machine |
| 12 | Assumptions and trade-offs | What was assumed and what was given up | Data rate, disk size, per-machine (not global) ordering, latency vs durability |

### 2.4 Gaps to state as assumptions

The email asks for assumptions explicitly, so naming these gaps is part of the answer.

| # | Not stated in the email | Why it matters |
| --- | --- | --- |
| 1 | Data rate and message size | They size the six-hour buffer (worked example below) |
| 2 | What exactly is down: only the central server, or the plant-to-central link too | It decides where buffering must live: in the plant, at the edge, or both |
| 3 | "Proposed self-developed Kafka-like component" | Most likely Anmeda's in-house broker. State the guarantees it must provide (durable append, replication, offsets, acks, keyed partitions) rather than assume Apache Kafka |
| 4 | Machine protocol (OPC UA, MQTT, Modbus) and whether the machines' controllers (PLCs) can buffer | PLCs usually hold little data, which is why edge gateways exist |
| 5 | An outage longer than the buffer | Name the limit, for example "sized for 24 h; beyond that, alert and apply a priority drop policy" |

Sizing example for row 1: 1 message per second × 1 KB × 500 machines × 21,600 seconds (six hours) = 10.8 million messages. That is about 10.8 GB in total, or about 22 MB per machine. Redo the sum with the real rate once it is known.

### 2.5 Catch-up time

The panel will likely ask how long recovery takes. While the backlog drains, live data keeps arriving at the normal rate. If the pipeline can process c times the normal rate, the backlog shrinks at (c − 1) times that rate, so a six-hour backlog clears in 6 h ÷ (c − 1).

| # | Processing capacity | Catch-up time for a six-hour backlog |
| --- | --- | --- |
| 1 | 2× normal | 6 h |
| 2 | 3× normal | 3 h |
| 3 | 4× normal | 2 h |
| 4 | 7× normal | 1 h |

---

## 3. How the exercise is answered

The full answer is [doc 02, the solution design](02-solution-design.md). The demo that proves it live is specified in [doc 00](00-edd-demo-game-plan.md).

### 3.1 Where each aspect is answered

| # | Aspect | Answered in doc 02 | What the demo shows ([doc 00](00-edd-demo-game-plan.md)) |
| --- | --- | --- | --- |
| 1 | Data flow | [§4.1](02-solution-design.md#41-data-flow) | Throughput at every stage, from the gateways to the database |
| 2 | Local buffering | [§4.2](02-solution-design.md#42-local-buffering) | Gateway buffers fill when the plant broker stops; mirror lag grows when central stops |
| 3 | Durability and recovery | [§4.3](02-solution-design.md#43-durability-and-recovery) | A killed gateway restarts and its sequences continue from disk |
| 4 | Kafka-like component | [§4.4](02-solution-design.md#44-role-of-the-kafka-like-component) | Plant and central clusters joined by MirrorMaker 2 |
| 5 | Ack and retry | [§4.5](02-solution-design.md#45-acknowledgement-and-retry) | Senders keep retrying through every drill, and rows leave only after acknowledgement |
| 6 | Duplicates | [§4.6](02-solution-design.md#46-duplicate-handling) | The duplicates-absorbed counter rises, while the table holds one row per reading |
| 7 | Ordering | [§4.7](02-solution-design.md#47-ordering) | 0 sequence gaps per machine after every drill |
| 8 | Backlog | [§4.8](02-solution-design.md#48-backlog-and-live-data) | A drain-time estimate, an alarm injected during catch-up, and the chart's gap filling in |
| 9 | Scale | [§4.9](02-solution-design.md#49-scale) | 500 machines at 500 readings per second, with drain rates measured live |
| 10 | Failures | [§4.10](02-solution-design.md#410-failure-scenarios) | Six scripted drills |
| 11 | Monitoring | [§4.11](02-solution-design.md#411-monitoring-and-alerting) | One dashboard covering every buffer, lag and alarm |
| 12 | Assumptions and trade-offs | [§1](02-solution-design.md#1-scenario-and-assumptions), [§4.12](02-solution-design.md#412-assumptions-and-trade-offs) | The demo's own limits, stated in [doc 02 §7](02-solution-design.md#7-live-proof-the-demo) |

### 3.2 What changed since the first analysis

The first version of this section, written on 11 November, proposed adapting the existing single-container demo. Research changed that plan:

| # | First analysis | Now | Why |
| --- | --- | --- | --- |
| 1 | One container under `supervisord` | Docker Compose: 14 containers on three networks | The drills can really stop "central" or cut the "WAN", and each container has its own memory limit |
| 2 | One Kafka broker | A plant cluster and a central cluster, joined by MirrorMaker 2 | A central outage becomes lag at the plant instead of errors at every gateway |
| 3 | Two producers emitting `OrderPlaced` and `PaymentCaptured` | Five shop gateways simulating 500 machines that emit readings and alarms | This also settles the open question about event names: the demo now speaks the exercise's language |
| 4 | SQLite sink files | PostgreSQL with primary key `(machine_id, seq)` | Two writers at once, and SQL queries for the proof |
| 5 | Producer outbox with `synchronous = NORMAL` | Gateway buffer with `synchronous = FULL` and a separate alarm lane | No loss on a power cut, and alarms are sent first |
| 6 | API reads the outbox files read-only | API reads each gateway's metrics endpoint | The gateways now run in separate containers |
| 7 | Deduplication by the idempotent producer plus the sink key | The sink key is the only deduplication point, covering five named duplicate sources | Producer idempotence lasts only one session |
| 8 | One outage drill: stop Kafka | Six drills, including central down and a WAN cut | Each failure the email names gets its own drill |

---

## 4. Toolchain and base images

The demo targets .NET 11 (`global.json` pins SDK 11.0.100) and runs as Docker Compose ([doc 00 §3](00-edd-demo-game-plan.md#3-containers-networks-and-memory)). These are the sources for the SDK and the container images, as checked on 12 November 2026.

| # | Link | What it is | What the check found |
| --- | --- | --- | --- |
| 1 | [Homebrew cask `dotnet-sdk`](https://formulae.brew.sh/cask/dotnet-sdk) | Microsoft's official, signed SDK installer (`.pkg`), installed to `/usr/local/share/dotnet` | 11.0.100, matching `global.json`. Needs the admin password; several SDKs can sit side by side |
| 2 | [Homebrew formula `dotnet`](https://formulae.brew.sh/formula/dotnet) | Homebrew's build of .NET from source | 10.0.403, with no .NET 11 yet. It clashes with the cask on `/opt/homebrew/bin/dotnet`, so run `brew unlink dotnet` before installing the cask |
| 3 | [MCR `dotnet/sdk` tags](https://mcr.microsoft.com/en-us/artifact/mar/dotnet/sdk/tags) | .NET SDK container images | `11.0` and `11.0.100` exist for the build stage, and `dotnet/aspnet:11.0` exists for the runtime stage. Docker Hub lists both under Microsoft as a Verified Publisher |
| 4 | [Docker Hub `apache/kafka` tags](https://hub.docker.com/r/apache/kafka/tags) | Apache Kafka images published by the Kafka project itself (Sponsored OSS badge) | `4.4.0` exists (pushed 22 October 2026, arm64 and amd64), unpacks Kafka to `/opt/kafka`, and includes MirrorMaker 2 |
| 5 | [Docker Hub: Docker Official Images](https://hub.docker.com/search?badges=official) | Images Docker curates | `postgres` (`18.6-alpine`) and `nginx` (`1.30-alpine-slim`) qualify. Kafka has none: `library/kafka` does not exist |
| 6 | [Docker Hub: Official Images and Verified Publishers](https://hub.docker.com/search?badges=official&badges=verified_publisher) | The same, plus images from verified vendors | Microsoft's .NET and Playwright images qualify. The verified Kafka images are either too old (`ubuntu/kafka` stops at 4.1) or an undocumented vendor rebuild (`cleanstart/kafka`), so the demo keeps `apache/kafka`. Bun has no badged image, but it is only used in a build stage |

Use the cask for local development: it is the only one with SDK 11.0.100, and it can hold .NET 10 alongside for other repositories.
