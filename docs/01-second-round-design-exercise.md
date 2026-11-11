# Second-Round Design Exercise: Plant Outage Without Loss or Duplicates

> As of 11 November 2026
>
> The round-two brief from Anmeda Systems, kept verbatim, followed by what it asks for and how the EDD demo in this repo ([game plan](00-edd-demo-game-plan.md)) can serve as a working proof of the design.

---

## Contents

1. [The email, verbatim](#1-the-email-verbatim)
2. [What the email asks](#2-what-the-email-asks)
3. [How the EDD demo answers the exercise](#3-how-the-edd-demo-answers-the-exercise)

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

## 3. How the EDD demo answers the exercise

### 3.1 Verdict

The demo already contains most of what the exercise asks for. The main missing piece is an outage drill: a live, time-compressed version of "the central server is down for six hours". With that drill added, the demo becomes a small-scale working proof of the design presented to the panel.

Now is the cheapest time to make the changes. Only phase 0 of the [build plan](00-edd-demo-game-plan.md#12-build-plan) (skeleton, contracts, packaging) is built. The producer, outbox relay and consumer (phase 1) do not exist yet.

The producer outbox is not in the game plan, whose producers send straight to Kafka. It was added to the design later, and so far only its table exists in code ([`OutboxSchema`](../src/backend/EventDrivenDesign.Persistence/Schema.cs)). This document is where the outbox is now recorded.

The demo's own brief still holds: two producers, two consumers, one command API, one dashboard, and no service-to-service calls. Every change below keeps it. The presentation is the actual deliverable and the demo supports it, so build only what the drill needs.

### 3.2 How the pieces map

| # | Plant design | Demo component |
| --- | --- | --- |
| 1 | Machine | A simulated machine inside a producer, with its own `machine_id`, stream and sequence numbers |
| 2 | Edge gateway | A producer process: generator, SQLite outbox and relay |
| 3 | Self-developed Kafka-like broker | Apache Kafka 4.4, single node (Redpanda as the fallback) |
| 4 | Central consumers | consumer-a and consumer-b in group `event-sinks` |
| 5 | Central store | SQLite sink files, one per consumer |
| 6 | Monitoring | The Command API's snapshot plus the React dashboard |

### 3.3 Aspect by aspect

| # | Exercise aspect | Already in the demo's design | What to add |
| --- | --- | --- | --- |
| 1 | Data flow | producer → Kafka → consumer → SQLite ([game plan §6](00-edd-demo-game-plan.md#6-diagram-2-data-plane-event-flow)) | Present each producer as an edge gateway simulating many machines (section 3.2) |
| 2 | Local buffering | Producer SQLite outbox plus relay ([`OutboxSchema`](../src/backend/EventDrivenDesign.Persistence/Schema.cs)) | Nothing new: the outbox is the store-and-forward buffer. Show it filling up |
| 3 | Durability and recovery | The outbox is written in the same transaction as run state; the consumer commits rows before offsets | `synchronous = FULL` on the outbox (section 3.5, gap 1) |
| 4 | Kafka-like component | Apache Kafka, with Redpanda as a drop-in fallback, so the design already doesn't depend on one broker | Present Kafka as a stand-in and list the guarantees the in-house broker must provide (below) |
| 5 | Ack and retry | `acks=all` with delivery reports; a row is marked dispatched only after its ack | The relay retries pending rows until the broker is back. The Kafka client gives up on a message after `message.timeout.ms` (5 minutes by default), so its own retries cannot cover a six-hour outage; the outbox can |
| 6 | Duplicates | Idempotent producer; `INSERT OR IGNORE` on `event_id`; unique index on `(stream_id, seq)` | Nothing |
| 7 | Ordering | Per partition, by key (the game plan keys by order id) | Key = `machine_id`; the relay publishes in `outbox_id` order |
| 8 | Backlog | Lag bars on the dashboard that spike and drain | Optional: a relay drain-rate cap so the backlog doesn't starve live data |
| 9 | Scale | 6 partitions; the consumer group scales live | Simulate 500 machines, 250 per producer. At 1 message per second each, that is 500 events/s, about what the demo script already runs (2 × 200/s) |
| 10 | Failures | Drills: stop consumer-b, restart producer-a ([game plan §13](00-edd-demo-game-plan.md#13-interview-demo-script-and-talking-points)) | New drills: stop Kafka, and stop both consumers (section 3.4) |
| 11 | Monitoring | Heartbeat health, lag per partition, resource usage, the reconciliation chain | Outbox depth and the age of the oldest pending row, per producer |
| 12 | Assumptions and trade-offs | Decision record and rejected options ([game plan §3](00-edd-demo-game-plan.md#3-decision-record)) | Outage sizing: message rate, Kafka retention at least as long as the longest outage, disk size |

Guarantees the in-house broker must provide, which the demo shows with Kafka standing in:

1. Durable acknowledgement: acknowledge a write only once it would survive losing a broker node.
2. Keyed partitions: messages with the same key stay in order.
3. Offsets and consumer groups: consumers resume from their last committed position.
4. Idempotent produce: a retried send within a producer session is stored once.
5. Retention longer than the longest outage the design promises to survive.

### 3.4 The outage drill: six hours compressed to two minutes

| # | Step | What the panel sees |
| --- | --- | --- |
| 1 | Both producers run 500 simulated machines at 1 message per second each | Steady throughput |
| 2 | `docker exec edd supervisorctl stop kafka`: the central link is down | The broker tile goes Down; outbox depth climbs |
| 3 | Wait two minutes | About 60,000 events buffered at the "edge" (500/s × 120 s) |
| 4 | `docker exec edd supervisorctl start kafka` | The relay drains the backlog; lag spikes, then falls |
| 5 | Open Reconciliation once lag is 0 | Generated = persisted, `missing = 0` for every machine, no duplicate rows: both requirements proven live |
| 6 | Variant: stop both consumers instead of Kafka | Kafka holds the data (24 h retention on `demo.events`), lag grows, then drains on restart |

Step 2 is a network or broker outage, buffered at the edge. Step 6 is a central-server outage, buffered by the broker. Together they show the two-tier buffering a strong answer describes. During step 2, producer heartbeats also stop, because they travel on Kafka (section 3.5, gap 2).

### 3.5 Three gaps in the current plan

1. **Durability setting.** The only pragma in the code so far is `synchronous = NORMAL` ([`SinkSchema.Pragmas`](../src/backend/EventDrivenDesign.Persistence/Schema.cs)); the outbox has none yet. In WAL mode, `NORMAL` survives an application crash but can roll back the last commits after a power loss or OS crash.
   - The outbox: the "no data lost" claim needs `FULL`, because the outbox holds the only copy of unsent events.
   - The sink: under `NORMAL`, a power loss can drop rows whose Kafka offsets were already committed, and Kafka will not redeliver them. Use `FULL` there too, or don't claim the sink survives power loss.
2. **Visibility during the outage.** Heartbeats travel on Kafka (`telemetry.heartbeats`), so while Kafka is down, the API hears nothing from the producers and marks them Down. That is realistic, because a central site is blind during a link outage. But it hides the buffer the drill is meant to show. The fix: the API opens each producer's outbox file read-only and counts pending rows. That uses the same allowed exception the API already uses for the sink files (.NET services → DB files on disk), so the no-service-to-service rule holds. The outbox files must sit on the shared `/data` volume. A person can also curl the producer's own `/metrics` port.
3. **A duplicate source to name in the talk.** Kafka's idempotent producer removes duplicates only within one producer session. If the relay restarts during the outage, it resends rows whose acknowledgement was lost, under a new session, and those duplicates land in Kafka. The sink's primary key on `event_id` absorbs them. That is why deduplication must sit at the sink, not only at the producer.

### 3.6 Changes to fold into the plan

| # | Change | Build phase ([game plan §12](00-edd-demo-game-plan.md#12-build-plan)) |
| --- | --- | --- |
| 1 | Generator: a `Machines` setting (250 per producer); each machine gets its own `machine_id`, stream and sequence numbers; `machine_id` is the Kafka key and is carried in the envelope and the sink row | 1. Data plane |
| 2 | Outbox: `synchronous = FULL`; the relay publishes pending rows in `outbox_id` order and keeps retrying while the broker is down | 1. Data plane |
| 3 | API: read outbox depth and the oldest pending row's age from the outbox files, read-only | 3. Telemetry and proof |
| 4 | Dashboard: an outbox panel per producer | 4. Dashboard |
| 5 | Demo script: the outage drill and its variant from section 3.4, added to [game plan §13](00-edd-demo-game-plan.md#13-interview-demo-script-and-talking-points) | 5. Hardening and rehearsal |
| 6 | Optional: a relay drain-rate cap | 1. Data plane |

### 3.7 What to skip

| # | Skip | Why |
| --- | --- | --- |
| 1 | Building an in-house Kafka-like broker | A large build that adds no proof; the broker guarantees in section 3.3 cover it in the talk |
| 2 | A separate edge-gateway service | The producer process already plays the gateway |
| 3 | MQTT or OPC UA machine simulators | Protocol detail the exercise doesn't test; name the protocol as an assumption instead |

### 3.8 Open question

Will this demo be shown to the same Anmeda panel? If yes, rename the events from `OrderPlaced` and `PaymentCaptured` to machine events such as `MachineReading`, so the demo speaks the exercise's language. If the demo answers a different interview brief, keep the current names.
