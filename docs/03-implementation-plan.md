# EDD Plant Demo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Docker Compose demo that proves a six-hour central outage loses nothing and duplicates nothing: 500 simulated machines → 5 gateways → plant Kafka → MirrorMaker 2 → central Kafka → 2 store writers → PostgreSQL, with a control-room API, a dashboard and scripted drills.

**Architecture:** Three .NET 11 hosts (`Gateway` ×5, `Consumer` ×2, `Api` ×1; after the resilience tier, `Gateway` ×10 as active/standby pairs and `Api` ×2) over shared libraries (`Contracts`, `Domain`, `Messaging`, `Persistence`), two single-broker Kafka 4.4 clusters joined by MirrorMaker 2, PostgreSQL 18 as the one deduplication point, and a React dashboard behind nginx. Everything runs from `src/deployment/compose.yaml` (project `edd`).

**Tech Stack:** .NET 11 / C# 15, Confluent.Kafka, Microsoft.Data.Sqlite, Npgsql, xUnit.net v3 on Microsoft.Testing.Platform, AwesomeAssertions, Testcontainers, Bun, React 19, Vite 8, TypeScript 7, Tailwind 4, TanStack Query, Recharts, Docker Compose.

**Spec:** [doc 00, the build spec](00-edd-demo-game-plan.md) (sections cited as §n), [doc 02, the solution design](02-solution-design.md). Doc 02 wins where they differ. The cut line in §13 decides scope: build every **Must** task first; the resilience tier (R1–R4) and then Stretch 3 only after Task 16 passes.

## Global Constraints

- Names: only `EventDrivenDesign.*` projects and namespaces.
- Placement (§12): everything .NET under `src/backend/` (dotnet commands run there), everything web under `src/frontend/`, everything runtime under `src/deployment/`. Nothing new at the repo root.
- Versions, exact pins (§2): SDK `11.0.100`; `Confluent.Kafka` 2.16.0; `Microsoft.Data.Sqlite` 11.0.0; `Npgsql` 10.0.3; `xunit.v3` 4.0.3; `xunit.runner.visualstudio` 4.0.1; `Microsoft.NET.Test.Sdk` 18.11.1; `Testcontainers.Kafka` and `Testcontainers.PostgreSql` 4.16.0; `Microsoft.Extensions.TimeProvider.Testing` 11.0.0; `AwesomeAssertions` 9.6.0; Bun 1.4.4; React 19.3.1; Vite 8.3.8; TypeScript 7.0.2; Tailwind CSS 4.3.5; `@tanstack/react-query` 5.107.1; Recharts 3.10.1.
- Images (§2): `mcr.microsoft.com/dotnet/sdk:11.0`, `mcr.microsoft.com/dotnet/aspnet:11.0`, `apache/kafka:4.4.0`, `postgres:18.6-alpine`, `nginx:1.30-alpine-slim`, `oven/bun:1.4.4-alpine` (build stage only). Ad-hoc probes use `busybox` (Docker Official Image). No other images.
- Memory (§3): limits exactly as the §3 table; `memswap_limit` = `mem_limit` everywhere; Kafka heap `-Xms512m -Xmx512m`, MirrorMaker 2 heap `-Xms128m -Xmx384m`, .NET `DOTNET_gcServer=0`.
- Hard rules (§1): data travels only through Kafka; HTTP only for the control room; no code path writes a reading except through the `(machine_id, seq)` key.
- Wire JSON: camelCase properties, enums as strings, dictionary keys unchanged (signal names stay `snake_case`).
- Assertions: every test project references `AwesomeAssertions` and asserts in its `.Should()` style (`using AwesomeAssertions;`). The `Assert.*` snippets in this plan state the facts to assert; write them with `.Should()`. Not FluentAssertions 8: its licence needs payment for commercial use.
- Warnings are errors in every project. `TreatWarningsAsErrors` is set once in `src/backend/Directory.Build.props` and every project inherits it. No project, file or `#pragma` may override it, and no `NoWarn` goes in without a one-line comment saying why. The frontend's `tsc -b` runs with `"strict": true` and fails the build on any error.
- Clean Architecture dependency rule:
  - `Shared` (Result, Error) depends on nothing.
  - `Contracts` depends on nothing.
  - `Domain` depends only on `Contracts` and `Shared`. It is pure: no I/O, no clock, no framework.
  - The infrastructure projects (`Persistence`, `Messaging`, `Infrastructure`) may depend on `Domain`, `Contracts` and `Shared`.
  - The hosts (`Gateway`, `Consumer`, `Api`) compose everything. Nothing references a host.
  - DDD is applied where it buys something: one ubiquitous language across code, docs and wire, and a pure domain model. There are no repositories or aggregates for ceremony's sake (YAGNI).
- Result pattern: expected failures return `Result` or `Result<T>` carrying an `Error(string Code, string Message, ErrorKind Kind)` from `EventDrivenDesign.Shared`. Exceptions are for the unexpected only. HTTP endpoints turn a failed `Result` into an RFC 9457 ProblemDetails response through the one shared `ToHttpResult` helper: `Invalid` → 400, `NotFound` → 404, `Unavailable` → 503.
- Global exception handling: every host calls `AddGlobalExceptionHandling()` and `UseGlobalExceptionHandling()` from `EventDrivenDesign.Infrastructure`.
  - An unhandled request exception becomes a ProblemDetails 500 with a `traceId` and no stack trace or exception message.
  - A `JsonException` or `BadHttpRequestException` becomes a 400.
  - Background loops retry transient I/O (Kafka, PostgreSQL) with `Retry.Backoff`.
  - A failure that would leave memory ahead of disk, such as a failed buffer write, is fatal: the process exits non-zero and Compose (`restart: on-failure`) restarts it from disk.
- Patterns by name:
  - The gateway buffer is a transactional outbox. The reading, its sequence number and the machine state commit in one SQLite transaction, and the sender relays rows and deletes them only after the broker's acknowledgement.
  - The store writers are idempotent consumers. The unique key and the counters commit in the same database transaction as the insert, and offsets are stored only after that commit.
  - Idempotency at every hop is listed in doc 02 §4.6, and each row names its test.
- Formatting: C# is formatted with CSharpier 1.4.0 (local tool, `src/backend/dotnet-tools.json`). The pre-commit hook formats staged `.cs` files automatically, and the pre-push hook blocks unformatted code (`src/scripts/git/install-hooks.sh`). Run `src/scripts/build/format.sh` before committing if the hooks are not installed.
- Tests first: every task with logic starts with a failing test. Commits follow Conventional Commits and end with the `Co-Authored-By` trailer.

## Review Focus

1. **Outage memory.** During a central or plant outage nothing may be OOM-killed: librdkafka queues are capped at 16384 KB. Pinned by Task 6's config tests and checked under load in Task 16.
2. **Pattern subscriptions find MirrorMaker 2's topics.** The consumers subscribe by regex before `plant1.*` exists; they must start consuming within seconds of MirrorMaker 2 creating it. Pinned by Task 6 (`topic.metadata.refresh.interval.ms = 5000`) and verified in Task 11.
3. **The proof never says PASS early.** While anything is still buffered or lagging, the verdict is `WAIT`, even if the counts happen to match. Pinned by a Task 12 test.
4. **The API answers during `central-down`.** With central Kafka and PostgreSQL stopped, `/api/overview` must return 200 with those tiles `Down`, not a 500. Pinned by a Task 13 test.
5. **A gateway restarted while paused stays paused and keeps numbering.** Pinned by Task 4's reopen tests and Task 5's resume test, and exercised by the kill drill.
6. **One active gateway per shop.** Two instances never collect for the same shop at once, and a revoked instance stops before the next holder starts. Pinned by R3's lease tests.
7. **A lost lease session is not a revocation.** When the plant broker is unreachable, the active gateway keeps collecting to its disk (doc 02 §4.2, tier 1). Stopping it would turn a broker outage into a loss beyond the history depth. Pinned by R3's `A_lost_session_keeps_the_active_running`.

## Deliberately not built (scope cuts)

Not in the must tier, so no task builds them unless a resilience or Stretch task says so: the Playwright end-to-end tests and the full-pipeline integration test (§13 cut line); `Guard` (nothing needs it); per-host `.slnx` files; the simulator's wear drift (§5); signal 1 as a percentage of a disk budget (doc 02 §7 row 6); HTTP `Idempotency-Key` headers (every command is idempotent by design, doc 02 §4.6); the production-only fault-tolerance measures (doc 02 §4.3: three brokers per site, a PostgreSQL synchronous standby, transactional fencing, rolling upgrades).

## Parallel lanes

Tasks 1 and 2 run in parallel. After Task 1: Tasks 3, 4 and 6 in parallel, and Task 14 (dashboard) can start, since the API models are fixed in Task 1. Tasks 7 and 9 run in parallel. Everything else follows the numbering. In the resilience tier, R3 needs R1 and R2; R4 needs none of them. They share `compose.yaml` and `drill.sh`, so they still run one at a time.

---

## F0: Skeleton

### Task 1: Solution, contracts and contract tests

**Files:**
- Create: `src/backend/global.json`, `src/backend/Directory.Build.props`, `src/backend/Directory.Packages.props`, `src/backend/EventDrivenDesign.slnx`
- Create: `src/backend/EventDrivenDesign.Contracts/EventDrivenDesign.Contracts.csproj`, `Readings.cs`, `GatewayCommand.cs`, `Metrics.cs`, `ServiceHealth.cs`, `ApiModels.cs`, `TopicNames.cs`, `Json.cs`
- Create: `src/backend/tests/EventDrivenDesign.Tests/EventDrivenDesign.Tests.csproj`, `ContractShapeTests.cs`

**Interfaces:**
- Produces: every record in §4 exactly as written (`MachineReading`, `MachineAlarm`, `AlarmKind`, `Pause`, `Resume`, `InjectFault`, `GatewayCommand`, `GatewayMetrics`, `ConsumerMetrics`, `ProcessStats`, `ServiceHealth` + `Healthy`/`Degraded`/`Down`), plus:
  - `public static class TopicNames { Readings = "machine.readings"; Alarms = "machine.alarms"; CentralReadingsPattern = @"^plant[0-9]+\.machine\.readings$"; CentralAlarmsPattern = @"^plant[0-9]+\.machine\.alarms$"; }`
  - `public static class Json { public static JsonSerializerOptions Options { get; } }`: the options from `EventDrivenDesignJsonContext`, a source-generated context with `[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]` that registers every contract type.
  - API models in `ApiModels.cs` (the dashboard mirrors these in TypeScript):
    ```csharp
    public sealed record GatewayTile(string Shop, ServiceHealth Health, string State, bool Paused, double SentPerSecond, long BufferedReadings, long BufferedAlarms, long OldestUnsentAgeMs, long Generated);
    public sealed record ClusterTile(ServiceHealth Health, long EndOffsetTotal, DateTimeOffset? NewestDeviceTime);
    public sealed record ConsumerTile(string ConsumerId, ServiceHealth Health, double InsertedPerSecond, long Received, long Inserted);
    public sealed record DatabaseTile(ServiceHealth Health, long Stored, double? FreshnessSeconds);
    public sealed record Overview(DateTimeOffset At, IReadOnlyList<GatewayTile> Gateways, ClusterTile PlantKafka, double? MirrorLagSeconds, ClusterTile CentralKafka, long ConsumerLag, IReadOnlyList<ConsumerTile> Consumers, DatabaseTile Database, double LiveRate, double DrainRate, double? DrainEtaSeconds);
    public sealed record MachineProof(string MachineId, long Generated, long Stored, long Missing, long Gaps);
    public sealed record Reconciliation(long Generated, long Stored, long Missing, long Gaps, long DuplicatesAbsorbed, long? DuplicateRows, bool Paused, bool Drained, string Verdict, IReadOnlyList<MachineProof> Machines);
    public sealed record ChartPoint(DateTimeOffset BucketStart, long Readings, bool Provisional);
    public sealed record Chart(DateTimeOffset? Watermark, IReadOnlyList<ChartPoint> Points);
    public sealed record AlarmView(string MachineId, long AlarmSeq, AlarmKind Kind, string Severity, DateTimeOffset DeviceTime, DateTimeOffset StoredAt, double LatencySeconds);
    ```
  - `Verdict` is one of `"PASS"`, `"WAIT"`, `"FAIL"`.

- [ ] **Step 1: Create the build files.** Copy `global.json`, `Directory.Build.props` and the test `.csproj` shape from the last working skeleton (`git show 8ff0daa^:src/global.json`, `…:src/Directory.Build.props`, `…:src/tests/EventDrivenDesign.Tests/EventDrivenDesign.Tests.csproj`) into the new paths, with two changes: `GenerateDocumentationFile` is `false`, and `Directory.Packages.props` lists the Global Constraints pins. The solution lists every project the plan creates as each task adds it.

- [ ] **Step 2: Write the failing contract tests** in `ContractShapeTests.cs`:

```csharp
[Fact] public void Reading_uses_camelCase_and_keeps_signal_names()
{
    var r = new MachineReading("P1-BDY-W042", "WeldRobot", "BDY", 184223,
        DateTimeOffset.Parse("2026-11-12T10:15:03.120Z"), new Dictionary<string, double> { ["weld_current_ka"] = 11.8 });
    using var doc = JsonDocument.Parse(JsonSerializer.Serialize(r, Json.Options));
    Assert.Equal(["machineId", "machineType", "shop", "seq", "deviceTime", "signals"],
        doc.RootElement.EnumerateObject().Select(p => p.Name));
    Assert.Equal(11.8, doc.RootElement.GetProperty("signals").GetProperty("weld_current_ka").GetDouble());
    var back = JsonSerializer.Deserialize<MachineReading>(doc.RootElement, Json.Options)!;
    Assert.Equal((r.MachineId, r.Seq, r.DeviceTime, 11.8), (back.MachineId, back.Seq, back.DeviceTime, back.Signals["weld_current_ka"]));
}
[Fact] public void InjectFault_has_type_discriminator() =>
    Assert.Equal("""{"$type":"InjectFault","machineId":"P1-BDY-W042","kind":"Overheat"}""",
        JsonSerializer.Serialize<GatewayCommand>(new InjectFault("P1-BDY-W042", AlarmKind.Overheat), Json.Options));
[Fact] public void Pause_is_type_only() =>
    Assert.Equal("""{"$type":"Pause"}""", JsonSerializer.Serialize<GatewayCommand>(new Pause(), Json.Options));
[Fact] public void Command_round_trips() =>
    Assert.True(JsonSerializer.Deserialize<GatewayCommand>("""{"$type":"Resume"}""", Json.Options) is Resume);
[Fact] public void Unknown_command_type_throws() =>
    Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GatewayCommand>("""{"$type":"Reboot"}""", Json.Options));
[Fact] public void Degraded_health_has_type_and_reason() =>
    Assert.Equal("""{"$type":"Degraded","reason":"no update for 4 s"}""",
        JsonSerializer.Serialize<ServiceHealth>(new Degraded("no update for 4 s"), Json.Options));
[Fact] public void Alarm_kind_is_a_string() =>
    Assert.Contains("\"kind\":\"DataGap\"", JsonSerializer.Serialize(
        new MachineAlarm("P1-BDY-W042", 7, 184223, DateTimeOffset.UnixEpoch, AlarmKind.DataGap, "warning", null, null, null), Json.Options));
```

- [ ] **Step 3: Run and see them fail.** Run from `src/backend/`: `dotnet test --project tests/EventDrivenDesign.Tests/EventDrivenDesign.Tests.csproj`. Expected: build errors (types missing).

- [ ] **Step 4: Implement the contracts.** `GatewayCommand` is `public union GatewayCommand(Pause, Resume, InjectFault);` with a `[JsonConverter(typeof(GatewayCommandConverter))]` that writes a leading `$type` and the case's properties. Copy the pattern from `git show 8ff0daa^:src/backend/EventDrivenDesign.Contracts/ProducerCommand.cs` (it compiled and passed on this SDK). `ServiceHealth` is the closed hierarchy in §4 with `[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]`. If the C# 15 `union` keyword fails to compile, fall back to a `closed record class GatewayCommand` hierarchy like `ServiceHealth` and keep the same JSON.

- [ ] **Step 5: Run the tests.** Same command. Expected: 7 passed.

- [ ] **Step 6: Commit.** `git commit -m "feat(contracts): add plant contracts, API models and shape tests"`

### Task 2: Compose skeleton and database schema

**Files:**
- Create: `src/deployment/compose.yaml`, `src/deployment/postgres/01-schema.sql`

**Interfaces:**
- Produces: Compose project `edd` (`name: edd`), networks `plant`, `central`, `ops`; services `plant-kafka` (host 19092), `central-kafka` (host 29092), `central-db` (host 55432; user, password and database all `edd`); named volumes `plant-kafka-data`, `central-kafka-data`, `central-db-data`. Later tasks add services to this file.

- [ ] **Step 1: Write `01-schema.sql`** as the §7 schema, verbatim.
- [ ] **Step 2: Write `compose.yaml`.** Brokers use the §3 environment block (central: same with `central-kafka` names, `HOST://:29092`). Health checks: brokers `/opt/kafka/bin/kafka-broker-api-versions.sh --bootstrap-server localhost:9092 > /dev/null`; database `pg_isready -U edd -d edd`; all with `interval: 5s`, `retries: 30`. The database runs `postgres -c shared_buffers=64MB -c max_connections=20`, mounts its volume at `/var/lib/postgresql` and the schema file read-only at `/docker-entrypoint-initdb.d/01-schema.sql`. Limits per §3.
- [ ] **Step 3: Verify.** From `src/deployment/`: `docker compose up -d --wait`. Expected: exit 0, three services `healthy`. Then `docker compose exec central-db psql -U edd -d edd -c '\dt'`. Expected: `alarms`, `ingest_totals`, `machine_progress`, `readings`, `rejected_records`.
- [ ] **Step 4: Commit.** `git commit -m "build(deployment): add compose skeleton with brokers and schema"`

---

## F1: Machine simulator

### Task 3: Catalogue, signal model, faults and alarms

**Files:**
- Create: `src/backend/EventDrivenDesign.Domain/EventDrivenDesign.Domain.csproj` (references Contracts), `MachineCatalog.cs`, `Signals.cs`, `MachineSimulator.cs`
- Test: `src/backend/tests/EventDrivenDesign.Tests/SimulatorTests.cs`

**Interfaces:**
- Consumes: `AlarmKind` (Task 1).
- Produces:
  - `public enum MachineType { StampingPress, WeldRobot, PaintBooth, Cnc, Conveyor, Agv }` with type letters S, W, B, C, V, A.
  - `public sealed record MachineInfo(string MachineId, MachineType Type, string Shop, int Index);`
  - `public static class MachineCatalog { public static IReadOnlyList<string> Shops { get; } // PRS, BDY, PNT, MCH, ASM
    public static IReadOnlyList<MachineInfo> ForShop(string plant, string shop); }`
  - `public sealed record SignalSpec(string Name, double Min, double Max, AlarmKind? Kind = null, double? Above = null, double? Below = null);` and `public static class Signals { public static IReadOnlyList<SignalSpec> For(MachineType type); }`
  - `public sealed record AlarmEvent(AlarmKind Kind, string Severity, string? Signal, double? Value, double? Threshold);`
  - `public sealed record SimTick(IReadOnlyDictionary<string, double> Signals, IReadOnlyList<AlarmEvent> Alarms);`
  - `public sealed class MachineSimulator(MachineInfo machine, int seed) { public SimTick Next(); public bool InjectFault(AlarmKind kind); }`

Values the spec pins:
- Shop mix (§5): PRS 30 S, 50 V, 20 A; BDY 80 W, 10 V, 10 A; PNT 40 B, 50 V, 10 A; MCH 70 C, 20 V, 10 A; ASM 60 V, 40 A. The types are numbered in that order, 001 to 100 across the shop: `{plant}-{shop}-{letter}{n:000}`.
- Signals (§5 table): press `tonnage_t` 600–800 (Overload above 900), `strokes_per_min` 16–20, `die_temp_c` 40–60 (Overheat above 80); weld robot `weld_current_ka` 8–15 (OutOfBand above 16 / below 7), `weld_time_ms` 100–330, `motor_temp_c` 40–70 (Overheat above 85); paint booth `air_temp_c` 21–24 (OutOfBand above 25 / below 20), `humidity_pct` 40–60 (OutOfBand above 65 / below 35), `airflow_m_s` 0.3–0.5; CNC `spindle_rpm` 6000–12000, `vibration_mm_s` 0.5–2.8 (Vibration above 4.5), `load_pct` 30–80 (Overload above 95); conveyor `speed_m_min` 8–12, `motor_current_a` 10–20 (Overload above 25), `vibration_mm_s` 0.5–2.8 (Vibration above 4.5); AGV `battery_pct` 20–100 (LowBattery below 15), `velocity_m_s` 0–2, `e_stop` 0–0 (EStop above 0.5).
- Model: one `System.Random(seed)`; each signal is Ornstein-Uhlenbeck around the band middle (`x += 0.1·(mid − x) + σ·N(0,1)`, `σ = (Max − Min) / 20`, Box-Muller), clamped to `[Min, Max]`, so no alarm ever fires without a fault.
- Fault: `InjectFault(kind)` picks the first signal whose `Kind` matches (false if none). For the next 10 ticks that signal is `Above + max(0.1·(Max − Min), 0.5)`, or `Below − max(0.1·(Max − Min), 0.5)` when the spec only has a lower bound. The first faulted tick emits exactly one `AlarmEvent` (`Value` = the faulted value, `Threshold` = `Above ?? Below`). Severity: `EStop` and `Overheat` are `"critical"`, `Overload` is `"high"`, everything else is `"warning"`.

- [ ] **Step 1: Write the failing tests:**

```csharp
[Fact] public void Body_shop_has_the_spec_mix()
{
    var m = MachineCatalog.ForShop("P1", "BDY");
    Assert.Equal(100, m.Count);
    Assert.Equal("P1-BDY-W042", m[41].MachineId);
    Assert.Equal("P1-BDY-V081", m[80].MachineId);
    Assert.Equal("P1-BDY-A100", m[99].MachineId);
    Assert.Equal(80, m.Count(x => x.Type == MachineType.WeldRobot));
}
[Fact] public void Plant_has_500_unique_ids() =>
    Assert.Equal(500, MachineCatalog.Shops.SelectMany(s => MachineCatalog.ForShop("P1", s)).Select(m => m.MachineId).Distinct().Count());
[Fact] public void Same_seed_same_series() { /* two simulators, seed 2041, 100 ticks: every signal value equal */ }
[Fact] public void Different_seed_different_series() { /* seeds 2041 and 2042 differ on at least one value */ }
[Theory, MemberData(nameof(AllTypes))] public void Without_a_fault_values_stay_in_band_and_never_alarm(MachineType t)
{ /* 10,000 ticks: Min ≤ value ≤ Max for every signal, Alarms always empty */ }
[Fact] public void Overheat_fault_on_weld_robot()
{
    var sim = new MachineSimulator(MachineCatalog.ForShop("P1", "BDY")[41], 2041);
    Assert.True(sim.InjectFault(AlarmKind.Overheat));
    var ticks = Enumerable.Range(0, 11).Select(_ => sim.Next()).ToList();
    Assert.All(ticks.Take(10), t => Assert.Equal(88, t.Signals["motor_temp_c"], 6));
    Assert.InRange(ticks[10].Signals["motor_temp_c"], 40, 70);
    var alarm = Assert.Single(ticks.SelectMany(t => t.Alarms));
    Assert.Equal((AlarmKind.Overheat, "critical", "motor_temp_c", 85.0), (alarm.Kind, alarm.Severity, alarm.Signal, alarm.Threshold!.Value));
}
[Fact] public void EStop_does_not_apply_to_a_weld_robot() =>
    Assert.False(new MachineSimulator(MachineCatalog.ForShop("P1", "BDY")[0], 1).InjectFault(AlarmKind.EStop));
[Fact] public void EStop_on_agv_sets_e_stop_for_10_ticks() { /* ASM index 60: e_stop == 1 for 10 ticks, one alarm, kind EStop */ }
```

- [ ] **Step 2: Run them and see them fail.** From `src/backend/`: `dotnet test --project tests/EventDrivenDesign.Tests/EventDrivenDesign.Tests.csproj`. Expected: compile errors.
- [ ] **Step 3: Implement** the catalogue, `Signals.For` (the table above as data) and `MachineSimulator`.
- [ ] **Step 4: Run.** Expected: all pass.
- [ ] **Step 5: Commit.** `git commit -m "feat(domain): add machine catalogue and seeded signal simulator"`

---

## F2: Gateway buffer and collector

### Task 4: SQLite store-and-forward buffer

**Files:**
- Create: `src/backend/EventDrivenDesign.Persistence/EventDrivenDesign.Persistence.csproj` (Microsoft.Data.Sqlite, Npgsql; references Contracts), `GatewayBuffer.cs`
- Test: `src/backend/tests/EventDrivenDesign.Tests/GatewayBufferTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public enum Lane { Alarm = 0, Reading = 1 }
  public sealed record OutboxRow(long Id, Lane Lane, string MachineId, long Seq, long DeviceTimeMs, byte[] Payload); // Id = 0 when new
  public sealed record MachineState(string MachineId, long LastSeq, long LastAlarmSeq, long LastDeviceTimeMs);
  public sealed record TickBatch(IReadOnlyList<OutboxRow> Rows, IReadOnlyList<MachineState> States);
  public sealed record BufferStats(long Readings, long Alarms, long? OldestDeviceTimeMs, long FileBytes);
  public sealed class GatewayBuffer : IAsyncDisposable
  {
      public static GatewayBuffer Open(string path);
      public bool IsNew { get; }                                  // machine_state was empty at open
      public IReadOnlyDictionary<string, MachineState> LoadState();
      public bool LoadPaused();
      public Task WriteTickAsync(TickBatch tick, CancellationToken ct);       // one transaction
      public Task DeleteAsync(IReadOnlyCollection<long> ids, CancellationToken ct);
      public Task SetPausedAsync(bool paused, CancellationToken ct);
      public IReadOnlyList<OutboxRow> ReadOldest(int limit);      // ORDER BY lane, id; own read connection
      public BufferStats Stats();
  }
  ```
- Schema: doc 02 §4.2 verbatim, plus `CREATE TABLE gateway_state (id INTEGER PRIMARY KEY CHECK (id = 1), paused INTEGER NOT NULL)`. `PRAGMA auto_vacuum = INCREMENTAL` runs before the first `CREATE TABLE`.
- Writer: one long-lived connection on a dedicated `Thread`, fed by `Channel.CreateBounded<…>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.Wait })`; each operation completes a `TaskCompletionSource`. Deletes run `PRAGMA incremental_vacuum(256)` afterwards.

- [ ] **Step 1: Write the failing tests** (each opens a buffer in a fresh temp directory):
  - `New_file_uses_wal_full_and_incremental_vacuum`: `PRAGMA journal_mode` = `wal`, `synchronous` = `2`, `auto_vacuum` = `2`; `IsNew` is true.
  - `Alarms_come_first_then_readings_oldest_first`: write a tick of readings for seqs 1–3 and one alarm, then a tick for seqs 4–6. `ReadOldest(10)` returns the alarm, then seqs 1–6 in order.
  - `Reopen_keeps_rows_and_state`: write one tick (state `LastSeq` 7, `LastAlarmSeq` 2, `LastDeviceTimeMs` 1000), dispose, reopen: same rows, `LoadState()["P1-BDY-W042"]` equals that state, `IsNew` is false.
  - `Delete_removes_only_the_given_ids`.
  - `Paused_survives_reopen`.
  - `Stats_counts_lanes_and_oldest_device_time`: 3 readings and 1 alarm → `(3, 1, min device time, > 0)`.
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement `GatewayBuffer`.**
- [ ] **Step 4: Run.** Expected: 6 passed.
- [ ] **Step 5: Commit.** `git commit -m "feat(persistence): add SQLite store-and-forward gateway buffer"`

### Task 5: Collector: sequences, payloads, faults

**Files:**
- Create: `src/backend/EventDrivenDesign.Gateway/EventDrivenDesign.Gateway.csproj` (`Microsoft.NET.Sdk.Web`; references Contracts, Domain, Messaging, Persistence), `Collector.cs`
- Test: `src/backend/tests/EventDrivenDesign.Tests/CollectorTests.cs`

**Interfaces:**
- Consumes: `MachineCatalog`, `MachineSimulator` (Task 3), `TickBatch`, `OutboxRow`, `MachineState`, `Lane` (Task 4), `Json.Options` (Task 1).
- Produces: `public sealed class Collector(string plant, string shop, int seed, IReadOnlyDictionary<string, MachineState> resume)` with
  - `public TickBatch Tick(DateTimeOffset tickTime)`: one reading per machine. Device time is `tickTime + index × 10 ms`, and `seq = last + 1`. The payload is the `MachineReading` serialised with `Json.Options`. Each alarm adds an alarm-lane row whose `Seq` is the next `alarm_seq` and whose payload is a `MachineAlarm` with `ReadingSeq` = that reading's seq. The batch carries all 100 states. Applies queued faults first.
  - `public bool InjectFault(string machineId, AlarmKind kind)`: validates now (unknown machine or kind not applicable → false) and queues the fault for the next tick (thread-safe: called from HTTP).
  - `public IReadOnlyDictionary<string, long> LastSeqByMachine { get; }` and `public long Generated { get; }` (Σ last seq).
  - Machine seed = `seed + index`.

- [ ] **Step 1: Write the failing tests:**
  - `First_tick_numbers_every_machine_from_1`: shop BDY, empty state, tick `2026-11-12T10:00:00Z`: 100 reading rows, all `Seq` 1; row for index 41 has `DeviceTimeMs` = tick + 410 ms; 100 states with `LastSeq` 1.
  - `Resume_continues_numbering`: resume state `P1-BDY-W042` → `LastSeq` 41: its next row has `Seq` 42.
  - `Payload_is_a_machine_reading`: deserialise row 41 → `Shop` "BDY", `MachineType` "WeldRobot", 3 signals.
  - `Injected_fault_yields_one_alarm_row`: `InjectFault("P1-BDY-W042", Overheat)` is true; the next tick has one `Lane.Alarm` row, `Seq` 1, whose payload's `ReadingSeq` equals the machine's reading seq in that tick; its state has `LastAlarmSeq` 1.
  - `Invalid_faults_are_rejected`: unknown id → false; `EStop` on `P1-BDY-W042` → false.
  - `Generated_counts_all_readings`: after 3 ticks, `Generated` = 300.
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement `Collector`.**
- [ ] **Step 4: Run.** Expected: all pass.
- [ ] **Step 5: Commit.** `git commit -m "feat(gateway): add collector with persistent sequence numbering"`

---

## F3: Sender and gateway host

### Task 6: Kafka settings, CloudEvents headers, retry backoff, process stats

**Files:**
- Create: `src/backend/EventDrivenDesign.Messaging/EventDrivenDesign.Messaging.csproj` (Confluent.Kafka; references Contracts), `KafkaConfigs.cs`, `CloudEvents.cs`, `Retry.cs`, `ProcessSampler.cs`
- Test: `src/backend/tests/EventDrivenDesign.Tests/MessagingTests.cs`

**Interfaces:**
- Produces:
  - `public static class KafkaConfigs { ProducerConfig Producer(string bootstrap); ConsumerConfig StoreWriter(string bootstrap, string groupId); ConsumerConfig Tail(string bootstrap); AdminClientConfig Admin(string bootstrap); }`
  - `public static class CloudEvents { public static Headers For(bool isAlarm, string shop, string machineId, long seq, long deviceTimeMs); }`
  - `public sealed class ProcessSampler { public ProcessStats Sample(); }`: CPU % from the `TotalProcessorTime` delta since the last call, `Environment.WorkingSet`, `GC.GetTotalMemory(false)`.
  - `public static class Retry { public static TimeSpan Backoff(int attempt, Random rng); }` = `rng.NextDouble() × min(30 s, 0.1 s × 2^attempt)` (full jitter; doc 02 §4.5). The sender and the store writers both use it.
- `Tail`: unique `GroupId` (`"api-tail-" + Guid`), `EnableAutoCommit = false`, `QueuedMaxMessagesKbytes = 16384`.

- [ ] **Step 1: Write the failing tests:**
  - `Producer_settings_match_the_spec`: `Acks.All`, `EnableIdempotence` true, `LingerMs` 20, `CompressionType.Lz4`, `MessageTimeoutMs` 30000, `QueueBufferingMaxKbytes` 16384, `QueueBufferingMaxMessages` 20000.
  - `Store_writer_settings_match_the_spec`: `EnableAutoCommit` true, `EnableAutoOffsetStore` false, `AutoOffsetReset.Earliest`, `PartitionAssignmentStrategy.CooperativeSticky`, `QueuedMaxMessagesKbytes` 16384, `TopicMetadataRefreshIntervalMs` 5000.
  - `Reading_headers`: `For(false, "BDY", "P1-BDY-W042", 184223, <2026-11-12T10:15:03.120Z in ms>)` → `ce_specversion` `1.0`, `ce_id` `P1-BDY-W042-184223`, `ce_type` `com.eventdrivendesign.machine.reading.v1`, `ce_source` `/plant1/BDY/gateway`, `ce_time` `2026-11-12T10:15:03.120Z`.
  - `Alarm_headers`: `ce_id` `P1-BDY-W042-a7`, `ce_type` `com.eventdrivendesign.machine.alarm.v1`.
  - `Backoff_is_bounded_with_full_jitter`: for attempts 0–30 with `new Random(1)`, every value is ≥ 0 and ≤ `min(30 s, 0.1 s × 2^attempt)`; attempt 0 is ≤ 100 ms.
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement.** Header values are UTF-8 bytes; `ce_time` uses the format `yyyy-MM-dd'T'HH:mm:ss.fff'Z'` in UTC.
- [ ] **Step 4: Run.** Expected: all pass.
- [ ] **Step 5: Commit.** `git commit -m "feat(messaging): add Kafka settings and CloudEvents headers"`

### Task 7: Sender with delete-on-ack

**Files:**
- Create: `src/backend/EventDrivenDesign.Gateway/Sender.cs`
- Create: `src/backend/tests/EventDrivenDesign.IntegrationTests/EventDrivenDesign.IntegrationTests.csproj` (Testcontainers.Kafka, Testcontainers.PostgreSql; references Gateway, Persistence, Messaging), `SenderTests.cs`

**Interfaces:**
- Consumes: `GatewayBuffer` (Task 4), `KafkaConfigs.Producer`, `CloudEvents.For`, `Retry.Backoff` (Task 6), `TopicNames` (Task 1).
- Produces:
  - `public enum SenderState { Starting, Sending, CatchingUp, Retrying, Stopping }`
  - `public sealed class Sender(GatewayBuffer buffer, IProducer<string, byte[]> producer, string shop, TimeProvider time)` with `Task RunAsync(CancellationToken ct)`, `SenderState State`, `long Sent`, `long SendErrors`, `double SentPerSecond` (last 10 s).
- Loop (§5 pseudo-code): read `ReadOldest(batch)` (1,000 while `Sending`, 5,000 while `CatchingUp`); produce each row to its lane's topic with key `MachineId`, timestamp = device time, headers from `CloudEvents.For`; await all delivery reports; `DeleteAsync` the acknowledged ids; any failure → `Retrying` and `await Task.Delay(Retry.Backoff(n, rng))`. Otherwise `CatchingUp` when the oldest unsent row is over 5 s old, else `Sending`. Idle (no rows) → wait 50 ms. On cancellation: `Stopping`, flush for up to 10 s, delete what was acknowledged.

- [ ] **Step 1–2: Write the failing integration test** `Rows_stay_while_the_broker_is_paused_then_drain_exactly`:
  - Start `new KafkaBuilder("apache/kafka:4.4.0")`, then create `machine.readings` (12 partitions) and `machine.alarms` (3).
  - Fill a temp buffer with 3 collector ticks for BDY plus 1 injected alarm: 300 readings and 1 alarm.
  - `PauseAsync` the container, run the sender for 5 s, and assert the buffer still holds 301 rows.
  - `UnpauseAsync`, wait until the buffer is empty (timeout 90 s), then consume both topics from the beginning. Expected: exactly 300 readings with 300 distinct `ce_id`s, and exactly 1 alarm.
- [ ] **Step 3: Run it and see it fail.** Integration tests run from `src/backend/` with `dotnet test --project tests/EventDrivenDesign.IntegrationTests/EventDrivenDesign.IntegrationTests.csproj` (needs Docker).
- [ ] **Step 4: Implement `Sender`.**
- [ ] **Step 5: Run it.** Expected: pass. If Testcontainers fails on Kafka 4.4 (§16 risk 3), switch to a generic `ContainerBuilder` with the §3 broker environment.
- [ ] **Step 6: Commit.** `git commit -m "feat(gateway): add sender that deletes only acknowledged rows"`

### Task 7b: Shared kernel and host infrastructure (Result pattern, global exception handling)

Runs after Task 7 and before Task 8. Added on 12 November at the user's request.

**Files:**
- Create: `src/backend/EventDrivenDesign.Shared/EventDrivenDesign.Shared.csproj` (no references), `Result.cs`
- Create: `src/backend/EventDrivenDesign.Infrastructure/EventDrivenDesign.Infrastructure.csproj` (`<FrameworkReference Include="Microsoft.AspNetCore.App" />`; references Shared and Contracts), `GlobalExceptionHandler.cs`, `HostingExtensions.cs`, `ResultHttpExtensions.cs`
- Move: `ProcessSampler.cs` from `EventDrivenDesign.Messaging` to `EventDrivenDesign.Infrastructure` (namespace `EventDrivenDesign.Infrastructure`), with its test
- Test: `src/backend/tests/EventDrivenDesign.Tests/ResultTests.cs`, `GlobalExceptionHandlerTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public enum ErrorKind { Invalid, NotFound, Unavailable }
  public sealed record Error(string Code, string Message, ErrorKind Kind = ErrorKind.Invalid);
  public readonly record struct Result      // IsSuccess, Error? Error; Success(); Failure(Error); implicit from Error
  public readonly record struct Result<T>   // IsSuccess, Value (throws InvalidOperationException on failure), Error? Error;
                                            // Success(T); Failure(Error); implicit from T and from Error;
                                            // TOut Match<TOut>(Func<T, TOut> ok, Func<Error, TOut> fail)
  public static class HostingExtensions
  {
      public static IServiceCollection AddGlobalExceptionHandling(this IServiceCollection services); // AddProblemDetails + AddExceptionHandler<GlobalExceptionHandler>
      public static WebApplication UseGlobalExceptionHandling(this WebApplication app);              // UseExceptionHandler + UseStatusCodePages
  }
  public static class ResultHttpExtensions
  {
      public static IResult ToHttpResult(this Result result, int successStatus = StatusCodes.Status202Accepted);
      public static IResult ToHttpResult<T>(this Result<T> result);                                  // success → 200 with the value
  }
  ```
- A failed result becomes `Results.Problem(statusCode: <kind's status>, title: error.Code, detail: error.Message)`. `GlobalExceptionHandler : IExceptionHandler` logs the exception at Error and writes ProblemDetails through `IProblemDetailsService`:
  - `JsonException` and `BadHttpRequestException` (including an inner `JsonException`) → 400, title `"Malformed request"`.
  - Anything else → 500, title `"Unexpected error"`, with `traceId` in the extensions and no exception message or stack.

- [ ] **Step 1: Write the failing tests:**
  - `Success_has_no_error` and `Failure_carries_its_error`.
  - `Value_of_a_failure_throws`.
  - `Match_takes_the_right_branch`.
  - `Implicit_conversions` (from `Error`, and from `T`).
  - `Failed_result_maps_to_problem_by_kind`: execute the `IResult` against a `DefaultHttpContext` with a `MemoryStream` body and services from `new ServiceCollection().AddProblemDetails().AddLogging().BuildServiceProvider()`. `Invalid` → 400, `NotFound` → 404, `Unavailable` → 503; the body's `title` is the error code.
  - `Unhandled_exception_becomes_problem_500_without_leaking`: a minimal `WebApplication` on `http://127.0.0.1:0` with the two extensions and one endpoint that throws `InvalidOperationException("secret detail")`. A real `HttpClient` GET returns 500, content type `application/problem+json`, a `traceId` in the body, and no `"secret detail"` anywhere in it.
  - `Malformed_json_becomes_400`: same host, a `POST` endpoint bound to `GatewayCommand` posted the body `[]` → 400 `application/problem+json`. This pins Task 1 minor M1 and needs the converter's `ValueKind` guard: a non-object body throws `JsonException`, not `InvalidOperationException`. Make that one-line fix in `GatewayCommand.cs` as part of this task.
- [ ] **Step 2: Run them and see them fail.** **Step 3: Implement**, add both projects to the `.slnx`, and move `ProcessSampler` (Messaging no longer has it). **Step 4: Run the full unit suite.** Expected: all pass, 0 warnings.
- [ ] **Step 5: Commit.** `git commit -m "feat(infrastructure): add Result pattern and global exception handling"`

### Task 8: Gateway host, image and five gateways in Compose

**Files:**
- Create: `src/backend/EventDrivenDesign.Gateway/Program.cs`, `CollectorService.cs`
- Create: `src/deployment/Dockerfile`, `src/deployment/Dockerfile.dockerignore` (`**/bin`, `**/obj`, `**/node_modules`, `**/dist`), `src/deployment/kafka/create-topics.sh`
- Modify: `src/deployment/compose.yaml` (add `plant-topics` and `gateway-prs` … `gateway-asm`)

**Interfaces:**
- Consumes: Tasks 4–7.
- Settings (§5): `Gateway__Shop`, `Gateway__Plant` (`P1`), `Gateway__Seed` (PRS 1000, BDY 2000, PNT 3000, MCH 4000, ASM 5000), `Gateway__FaultsPerMinute` (0.5), `Kafka__BootstrapServers` (`plant-kafka:9092`), `Buffer__Path` (`/data/buffer.db`), `ASPNETCORE_HTTP_PORTS` (8080).
- Endpoints (§5): `GET /metrics` → `GatewayMetrics`; `POST /control` → `GatewayCommand`; `GET /healthz` → 200.
- `public sealed class GatewayControl(Collector collector, GatewayBuffer buffer) { public Task<Result> ApplyAsync(GatewayCommand command, CancellationToken ct); }`, answered by an exhaustive `switch` (§15). It is test-driven first (ledger Ruling 3):
  - `Pause` and `Resume` persist through `SetPausedAsync`. They are idempotent: sending one twice succeeds both times and leaves a single persisted state.
  - `InjectFault` succeeds, with the fault reaching the collector.
  - An unknown machine fails with `Error("gateway.machine.unknown", …, ErrorKind.NotFound)`.
  - A kind that doesn't fit the machine type fails with `Error("gateway.fault.not_applicable", …, ErrorKind.Invalid)`.
  - `/control` returns `(await control.ApplyAsync(cmd, ct)).ToHttpResult()`: 202, 400 or 404 as ProblemDetails.
- The host calls `AddGlobalExceptionHandling()` / `UseGlobalExceptionHandling()` and uses `ProcessSampler` from Infrastructure (one shared instance, called only from `/metrics`).
- A failed `WriteTickAsync` is fatal: let it escape `CollectorService`, so the host stops with a non-zero exit code and Compose's `restart: on-failure` restarts the gateway from its persisted state. Never keep ticking with in-memory sequence numbers that disk does not have. Sender failures stay inside the sender's retry loop.
- `CollectorService`: `PeriodicTimer(TimeSpan.FromSeconds(1), TimeProvider.System)`; skip while paused; random faults at `FaultsPerMinute` (per tick, the probability is `FaultsPerMinute / 60`, applied to a random machine with a random applicable kind); `WriteTickAsync(collector.Tick(now))`.
- Dockerfile (context `src/`): a `build` stage on `sdk:11.0` restores `backend/EventDrivenDesign.slnx` and publishes each host to `/out/<host>`. Each host has a target on `aspnet:11.0` with `ENV DOTNET_gcServer=0`, the entrypoint `dotnet EventDrivenDesign.<Host>.dll`, and the target name `gateway`. Later tasks add `consumer`, `api` and `dashboard`.
- `create-topics.sh`: `kafka-topics.sh --create --if-not-exists` for `machine.readings` (12 partitions) and `machine.alarms` (3), replication factor 1, `retention.ms=10800000`.
- Compose: the five gateways come from one YAML anchor (`x-gateway: &gateway`), overriding the shop, the seed and the volume `gateway-<shop>-data:/data`. They depend on `plant-topics` completing successfully. Limit 256 MB each. `restart: on-failure`.

- [ ] **Step 0: Test-drive `GatewayControl`** in `src/backend/tests/EventDrivenDesign.Tests/GatewayControlTests.cs`. Use a real collector and a temp-file buffer. The cases:
  - `Inject_valid_fault_succeeds_and_reaches_the_collector`: the next tick carries one alarm row.
  - `Unknown_machine_is_not_found`.
  - `Inapplicable_fault_is_invalid`.
  - `Pause_twice_is_idempotent`: both calls succeed, and `LoadPaused()` is true.
  - `Resume_after_pause_unpauses`.

  RED, then GREEN.
- [ ] **Step 1: Write `Program.cs` and `CollectorService.cs`; build.** From `src/backend/`: `dotnet build EventDrivenDesign.slnx`. Expected: 0 errors.
- [ ] **Step 2: Write the Dockerfile, the topics script and the Compose services.**
- [ ] **Step 3: Verify the flow.** From `src/deployment/`: `docker compose up -d --build --wait`. Then run `docker compose exec plant-kafka /opt/kafka/bin/kafka-get-offsets.sh --bootstrap-server localhost:9092 --topic machine.readings` twice, 10 s apart. Expected: the summed offsets grow by about 5,000 (±10%).
- [ ] **Step 4: Verify metrics and a restart.**
  - Read the metrics: `docker run --rm --network edd_plant busybox wget -qO- http://gateway-bdy:8080/metrics`. Expected: JSON with `"machines":100` and a `lastSeqByMachine` map.
  - Then `docker compose kill gateway-bdy && docker compose start gateway-bdy`, wait 5 s, and read the metrics again. Expected: `P1-BDY-W042`'s last seq is higher than before the kill. The sequence never restarts at 1.
- [ ] **Step 5: Commit.** `git commit -m "feat(gateway): add gateway host, image and five shop gateways"`

---

## F4: Store writers and PostgreSQL

### Task 9: Central store with one deduplication point

**Files:**
- Create: `src/backend/EventDrivenDesign.Persistence/CentralStore.cs`
- Test: `src/backend/tests/EventDrivenDesign.IntegrationTests/CentralStoreTests.cs`; link the schema into the test output: `<None Include="../../../deployment/postgres/01-schema.sql" Link="01-schema.sql" CopyToOutputDirectory="PreserveNewest" />`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record RejectedRecord(string Topic, int Partition, long Offset, byte[] Raw, string Error);
  public sealed class CentralStore(NpgsqlDataSource db)
  {
      public Task<int> InsertReadingsAsync(IReadOnlyList<MachineReading> batch, IReadOnlyList<RejectedRecord> rejected, string writerId, CancellationToken ct);
      public Task<int> InsertAlarmsAsync(IReadOnlyList<MachineAlarm> batch, string writerId, CancellationToken ct);
  }
  ```
- `InsertReadingsAsync` runs in one transaction:
  1. The §7 CTE, verbatim. Pass `signals` as `text[]` of JSON strings and keep the `::jsonb[]` cast.
  2. `ingest_totals` for `('readings', writerId)`: `received += batch.Count`, `inserted += n`.
  3. `rejected_records … ON CONFLICT DO NOTHING`.

  It returns `n`, the number of rows inserted. `InsertAlarmsAsync` does the same for `alarms` (`ON CONFLICT (machine_id, alarm_seq) DO NOTHING`) and the stream `'alarms'`.

- [ ] **Step 1: Write the failing tests.** Each starts `new PostgreSqlBuilder("postgres:18.6-alpine")` and applies `01-schema.sql`.
  - `Every_reading_twice_is_stored_once`:
    - Inserting 100 readings for 10 machines × seqs 1–10 returns 100.
    - Inserting the same 100 again returns 0.
    - `count(*)` is 100.
    - `ingest_totals` shows received 200 and inserted 100, so duplicates absorbed = 100.
    - Every `machine_progress` row shows `stored` 10 and `max_seq` 10.
  - `Duplicates_inside_one_batch_are_absorbed`: one batch with the same reading twice → returns 1.
  - `Out_of_order_seqs_still_add_up`: seq 3 first, then 1 and 2 → `stored` 3, `max_seq` 3, `max_device_time` = seq 3's time.
  - `Alarms_are_stored_once`: the same alarm twice → 1 row.
  - `Rejected_records_are_kept_once`.
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement `CentralStore`.**
- [ ] **Step 4: Run.** Expected: all pass.
- [ ] **Step 5: Commit.** `git commit -m "feat(persistence): add central store with one unique-key insert"`

### Task 10: Consumer host with capped drain

**Files:**
- Create: `src/backend/EventDrivenDesign.Consumer/EventDrivenDesign.Consumer.csproj` (`Microsoft.NET.Sdk.Web`; references Contracts, Messaging, Persistence), `Program.cs`, `BatchLoop.cs`, `RateCap.cs`
- Test: `src/backend/tests/EventDrivenDesign.Tests/RateCapTests.cs`
- Modify: `src/deployment/Dockerfile` (target `consumer`), `src/deployment/compose.yaml` (`consumer-1`, `consumer-2`)

**Interfaces:**
- Consumes: `KafkaConfigs.StoreWriter` (Task 6), `CentralStore` (Task 9), `TopicNames` (Task 1).
- Produces: `public sealed class RateCap(int rowsPerSecond) { public TimeSpan DelayAfter(int rows, TimeSpan elapsed); }`, which returns `max(0, rows / rate − elapsed)`. When `rowsPerSecond` is 0 it returns zero.
- Two loops (§7), each with its own consumer and group:
  - **Readings:** group `central-readings`, pattern `TopicNames.CentralReadingsPattern`, batches of up to 5,000 records or 200 ms. It is capped by `Consumer__MaxRowsPerSecond` (default 1000).
  - **Alarms:** group `central-alarms`, batches of up to 100 records or 50 ms. It is never capped.
- Per batch:
  1. Parse each record with `Json.Options`. A record that fails becomes a `RejectedRecord`.
  2. Run the insert.
  3. Only after it succeeds, call `StoreOffset` on the last record of each partition. A database error retries the same batch with `Retry.Backoff`, without storing offsets.
  4. Apply the cap delay.
  5. The partitions-revoked handler calls `Commit()`.
- Settings: `Consumer__Id`, `Consumer__MaxRowsPerSecond`, `Kafka__BootstrapServers` (`central-kafka:9092`), `ConnectionStrings__Central` (`Host=central-db;Username=edd;Password=edd;Database=edd`).
- `GET /metrics` → `ConsumerMetrics`; `GET /healthz`. The host calls `AddGlobalExceptionHandling()` / `UseGlobalExceptionHandling()` and uses `ProcessSampler` from Infrastructure. Compose: `restart: on-failure`.

- [ ] **Step 1: Write the failing tests:** `RateCap(1000).DelayAfter(5000, 1 s)` = 4 s; `RateCap(1000).DelayAfter(500, 1 s)` = 0; `RateCap(0).DelayAfter(1_000_000, 0)` = 0.
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement `RateCap`, `BatchLoop` and `Program`; add the image target and both consumers to Compose** (256 MB each, network `central`).
- [ ] **Step 4: Run the unit tests and build.** Expected: pass; 0 build errors.
- [ ] **Step 5: Commit.** `git commit -m "feat(consumer): add store writers with stored offsets and drain cap"`

---

## F5: MirrorMaker 2 and the full pipeline

### Task 11: MirrorMaker 2 and an end-to-end check

**Files:**
- Create: `src/deployment/kafka/mm2.properties` (§6, verbatim)
- Modify: `src/deployment/compose.yaml` (add `mm2`)

**Interfaces:**
- `mm2`: image `apache/kafka:4.4.0`, command `/opt/kafka/bin/connect-mirror-maker.sh /etc/mm2/mm2.properties`, the properties file mounted read-only, `KAFKA_HEAP_OPTS: -Xms128m -Xmx384m`, networks `plant` and `central`, 768 MB, depends on both brokers healthy and `plant-topics` completed.

- [ ] **Step 1: Add the service and start the stack.** `docker compose up -d --build --wait`.
- [ ] **Step 2: Verify the mirror.** Run `docker compose exec central-kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 --describe --topic plant1.machine.readings`. Expected: 12 partitions within about 60 s of start.
- [ ] **Step 3: Verify the database fills.** Run `docker compose exec central-db psql -U edd -d edd -c "select count(*), count(distinct machine_id) from readings"` twice, 10 s apart. Expected: 500 machines, and the count grows by about 5,000.
- [ ] **Step 4: Verify there are no gaps.**
  - Stop the gateways with `docker compose stop gateway-prs gateway-bdy gateway-pnt gateway-mch gateway-asm` and wait 30 s.
  - Then run `select count(*) from machine_progress where stored <> max_seq`. Expected: 0.
  - Start the gateways again.
- [ ] **Step 5: Commit.** `git commit -m "feat(deployment): mirror plant topics to central with MirrorMaker 2"`

---

## F6: Control-room API

### Task 12: Proof, drain-time, watermark and health maths

**Files:**
- Create: `src/backend/EventDrivenDesign.Domain/Proof.cs`
- Test: `src/backend/tests/EventDrivenDesign.Tests/ProofTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record ProgressRow(string MachineId, long Stored, long MaxSeq, DateTimeOffset MaxDeviceTime);
  public static class Proof
  {
      public static Reconciliation Reconcile(IReadOnlyDictionary<string, long> generated, IReadOnlyList<ProgressRow> progress,
                                             long duplicatesAbsorbed, long? duplicateRows, bool allPaused, bool drained);
      public static double? DrainEtaSeconds(long backlog, double drainRate, double liveRate);
      public static DateTimeOffset? Watermark(IReadOnlyList<ProgressRow> progress);
      public static ServiceHealth HealthFromAge(TimeSpan age, DateTimeOffset lastSeen);
  }
  ```
- Rules (§8):
  - **Missing** = generated − stored, per machine. A machine with no progress row counts as stored 0.
  - **Gaps** = `MaxSeq − Stored`.
  - **Verdict:** `PASS` when `allPaused && drained && Σ missing == 0 && Σ gaps == 0`. `WAIT` when not (`allPaused && drained`). Otherwise `FAIL`.
  - **ETA** = backlog ÷ (drain − live). It is `null` when drain ≤ live, and 0 when the backlog is 0.
  - **Watermark** = the oldest `MaxDeviceTime` among rows with `Stored == MaxSeq`, or `null`.
  - **Health:** under 3 s `Healthy`; under 10 s `Degraded("no update for N s")`; otherwise `Down(lastSeen)`.

- [ ] **Step 1: Write the failing tests:**
  - `Matching_counts_when_paused_and_drained_pass`: A 10/10/10, B 5/5/5 → verdict `PASS`, missing 0, gaps 0.
  - `Matching_counts_still_wait_while_in_flight`: same data, `drained = false` → `WAIT` (Review Focus 3).
  - `A_hole_fails_once_drained`: generated B 5, progress B stored 4, max 5 → missing 1, gaps 1, `FAIL`.
  - `Machine_with_nothing_stored_is_all_missing`: generated C 3, no row → missing 3, gaps 0.
  - `Drain_eta`: `(60000, 2000, 500)` = 40.0; `(100, 500, 500)` = null; `(0, 0, 500)` = 0.
  - `Watermark_ignores_machines_with_gaps`: A complete at 10:05, B complete at 10:03, C with a gap at 10:01 → 10:03.
  - `Health_by_age`: 2 s → `Healthy`; 5 s → `Degraded`; 12 s → `Down`.
- [ ] **Step 2: Run them and see them fail.** **Step 3: Implement.** **Step 4: Run.** Expected: pass.
- [ ] **Step 5: Commit.** `git commit -m "feat(domain): add proof, drain-time and watermark maths"`

### Task 13: API host

**Files:**
- Create: `src/backend/EventDrivenDesign.Persistence/CentralQueries.cs`
- Create: `src/backend/EventDrivenDesign.Api/EventDrivenDesign.Api.csproj` (`Microsoft.NET.Sdk.Web`; references Contracts, Domain, Messaging, Persistence), `Program.cs`, `Snapshot.cs`, `Pollers.cs`, `KafkaWatcher.cs`, `OverviewBuilder.cs`
- Test: `src/backend/tests/EventDrivenDesign.IntegrationTests/CentralQueriesTests.cs`, `src/backend/tests/EventDrivenDesign.Tests/OverviewBuilderTests.cs`
- Modify: `src/deployment/Dockerfile` (target `api`), `src/deployment/compose.yaml` (`api` on `plant`, `central`, `ops`)

**Interfaces:**
- Consumes: `Proof` (Task 12), `KafkaConfigs.Admin`/`Tail` (Task 6), API models (Task 1).
- Produces:
  - `public sealed class CentralQueries(NpgsqlDataSource db)` with these methods:
    - `ProgressAsync()` returns `IReadOnlyList<ProgressRow>`.
    - `TotalsAsync(string stream)` returns `(long Received, long Inserted)`.
    - `ChartAsync(int minutes)` returns `IReadOnlyList<(DateTimeOffset Bucket, long Readings)>`. It uses `date_bin('5 seconds', device_time, timestamptz '2000-01-01')` over the last N minutes by `device_time`.
    - `RecentAlarmsAsync(int limit)` returns `IReadOnlyList<AlarmView>`, with latency = `stored_at − device_time`.
    - `DuplicateRowsAsync()` returns `count(*) − count(distinct (machine_id, seq))`.
  - `public sealed class Snapshot`: thread-safe latest values from each source, each with its last-success time. It holds the gateway metrics by shop, the consumer metrics by id, and the plant end-offset total. It also holds the newest device time seen at the plant and at central, the central end offsets, the committed offsets of `central-readings`, the progress rows and the totals.
  - `public static class OverviewBuilder { public static Overview Build(Snapshot s, DateTimeOffset now); }` is pure:
    - Health comes from `Proof.HealthFromAge`.
    - Mirror lag = plant newest − central newest device time.
    - Consumer lag = Σ(end − committed).
    - Live rate = Σ gateway `SentPerSecond` while not paused, and drain rate = Σ consumer `InsertedPerSecond`. These two feed `DrainEtaSeconds` with the consumer lag as the backlog.
- Sources (§8):
  - **Gateways:** `GET http://gateway-<shop>:8080/metrics` every 1 s, with an 800 ms timeout.
  - **Consumers:** `GET http://consumer-<n>:8080/metrics` every 1 s.
  - **Offsets:** an `IAdminClient` per cluster every 2 s, `ListOffsetsAsync` (latest), plus the central group's committed offsets.
  - **Tails:** two tail consumers assigned to every partition at `Offset.End`, tracking the newest record timestamp.
  - **PostgreSQL:** every 2 s.
- Endpoints (§8):
  - `GET /api/overview`
  - `GET /api/reconciliation?full=true` (`full` adds `DuplicateRows`). `drained` means every gateway buffer is 0, the consumer lag is 0, and the mirror lag is ≤ 2 s.
  - `GET /api/chart?minutes=10`: a point is provisional after the watermark.
  - `GET /api/alarms`: central alarms, the last 50.
  - `POST /api/plant/pause` and `POST /api/plant/resume`: fan out to the five gateways.
  - `POST /api/machines/{machineId}/faults` with body `{ "kind": "Overheat" }`: routed to `gateway-<shop>` by the id's second segment. A `public static Result<string> GatewayFor(string machineId)` (unit-tested) returns `Error("api.machine.bad_id", …, Invalid)` for an id without a known shop segment. An unreachable gateway → `Error("api.gateway.unavailable", …, Unavailable)` (503). A 400 or 404 ProblemDetails from the gateway is passed through. The host calls `AddGlobalExceptionHandling()` / `UseGlobalExceptionHandling()`.
  - `GET /healthz`
- Settings: `Api__Gateways` (`gateway-prs:8080,…`), `Api__Consumers` (`consumer-1:8080,consumer-2:8080`), `Kafka__Plant` (`plant-kafka:9092`), `Kafka__Central` (`central-kafka:9092`), `ConnectionStrings__Central`.

- [ ] **Step 1: Write the failing tests:**
  - `Central_down_still_builds_an_overview` (unit; Review Focus 4): a `Snapshot` whose database, central cluster and consumers were last seen 30 s ago → `Database.Health`, `CentralKafka.Health` and every consumer tile are `Down`; no exception.
  - `Chart_buckets_by_device_time` (Postgres container): store 12 readings across 3 five-second windows via `CentralStore` → 3 buckets with the right counts.
  - `Progress_and_duplicate_rows`: after the Task 9 double insert, `ProgressAsync` returns 10 rows and `DuplicateRowsAsync` returns 0.
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement the queries, snapshot, pollers, Kafka watcher, builder and endpoints; add the image target and Compose service** (256 MB).
- [ ] **Step 4: Run the tests.** Expected: pass.
- [ ] **Step 5: Verify live.** `docker compose up -d --build --wait`, then `docker run --rm --network edd_ops busybox wget -qO- http://api:8080/api/overview`. Expected: 5 gateways `Healthy`, live rate about 500.
- [ ] **Step 6: Commit.** `git commit -m "feat(api): add control-room API with proof and drain estimate"`

---

## F7: Dashboard

### Task 14: React dashboard behind nginx

**Files:**
- Create: `src/frontend/package.json`, `bun.lock`, `index.html`, `vite.config.ts`, `tsconfig.json`, `src/main.tsx`, `src/App.tsx`, `src/api.ts`, `src/index.css`, `src/panels/PipelineStrip.tsx`, `src/panels/BuffersLag.tsx`, `src/panels/ReadingsChart.tsx`, `src/panels/ProofPanel.tsx`, `src/panels/AlarmsPanel.tsx`
- Create: `src/deployment/nginx/default.conf`
- Modify: `src/deployment/Dockerfile` (stages `web` on `oven/bun:1.4.4-alpine`, target `dashboard` on `nginx:1.30-alpine-slim`), `src/deployment/compose.yaml` (`dashboard`, port 3000, network `ops`, 64 MB)

**Interfaces:**
- Consumes: the Task 1 API models over HTTP (`src/api.ts` mirrors them as TypeScript types; `ServiceHealth` arrives as `{ "$type": "Healthy" | "Degraded" | "Down", … }`).
- `api.ts`: `useOverview()`, `useReconciliation()`, `useChart()` and `useAlarms()` (TanStack Query, `refetchInterval: 1000`, relative `/api/...` URLs); `pause()`, `resume()`, `injectFault(machineId, kind)`.
- Panels (§9): pipeline strip (seven stages, health with a text label, rate, buffer or lag, freshness); buffers and lag (five gateway bars, mirror lag, consumer lag, ETA or "—"); chart (Recharts bars per 5 s by device time, the region after the watermark shaded "provisional"); proof (generated, stored, missing, gaps, duplicate rows, duplicates absorbed, verdict); alarms (central alarms with latency, an inject-alarm form with a machine id and kind, pause and resume buttons). One column under 900 px.
- nginx: `location /api/ { proxy_pass http://api:8080; }`, SPA fallback to `index.html`.
- `tsconfig.json`: `"types": ["vite/client"]` (TypeScript 7 defaults to none).

- [ ] **Step 1: Scaffold with the exact versions.** In `src/frontend/`: `bun add react@19.3.1 react-dom@19.3.1 @tanstack/react-query@5.107.1 recharts@3.10.1 react-is@19.3.1` and `bun add -d vite@8.3.8 typescript@7.0.2 tailwindcss@4.3.5 @tailwindcss/vite@4.3.5 @vitejs/plugin-react @types/react @types/react-dom`. Scripts: `"build": "tsc -b && bunx --bun vite build"`, `"dev": "bunx --bun vite"` (dev proxy `/api` → `http://localhost:3000`).
- [ ] **Step 2: Write `api.ts` and the panels.**
- [ ] **Step 3: Verify the build.** `bun run build`. Expected: the type check passes and `dist/` is written.
- [ ] **Step 4: Verify live.** `docker compose up -d --build --wait`, open `http://localhost:3000`. Expected: green tiles, about 500 readings per second, the chart drawing, the proof showing `WAIT`.
- [ ] **Step 5: Commit.** `git commit -m "feat(frontend): add control-room dashboard"`

---

## F8: Drills and the proof

### Task 15: `drill.sh`

**Files:**
- Create: `src/deployment/scripts/drill.sh` (executable; run from `src/deployment/`)

**Interfaces:**
- Commands (§10): `central-down`, `central-up`, `kill-gateway <SHOP>`, `kill-consumer <N>`, `proof`, `pause`, `resume` (`curl -fsS -X POST http://localhost:3000/api/plant/pause` or `…/resume`); plus `wan-cut`, `wan-restore`, `plant-down` and `plant-up` as one line each (unrehearsed until Stretch 3).
  - `central-down` = `docker compose kill mm2 consumer-1 consumer-2` then `docker compose stop central-db central-kafka`.
  - `central-up` = `docker compose start central-kafka central-db consumer-1 consumer-2 mm2`.
- `proof`:
  1. `curl -fsS -X POST http://localhost:3000/api/plant/pause`.
  2. Poll `/api/reconciliation?full=true` every 2 s, for up to 5 minutes, until `drained` is true.
  3. Print generated, stored, missing, gaps, duplicate rows, duplicates absorbed and the verdict. Parse the JSON with `python3 -c`.
  4. Exit 0 only on `PASS`. Leave the plant paused; `resume` is a separate command.
- Every command echoes the Docker command before running it, so the panel sees what is broken.

- [ ] **Step 1: Write the script.**
- [ ] **Step 2: Run the main story.** `drill.sh central-down`, wait 60 s, `drill.sh central-up`, wait for the drain, then `drill.sh proof`. Expected: `PASS`, duplicates absorbed > 0, duplicate rows 0.
- [ ] **Step 3: Run the crash drills.** `drill.sh resume`; `drill.sh kill-gateway BDY`; `drill.sh kill-consumer 1`; then `drill.sh proof`. Expected: `PASS`.
- [ ] **Step 4: Commit.** `git commit -m "feat(deployment): add failure drills and the proof command"`

---

## F9: Rehearsal

### Task 16: Cold start, memory check and README

**Files:**
- Modify: `README.md` (a "Run the demo" section: prerequisites, `docker compose up -d --build --wait`, `http://localhost:3000`, the drill commands)

- [ ] **Step 1: Cold start.** `docker compose down -v && docker compose up -d --build --wait`. Expected: every service healthy or running; `plant-topics` exited 0.
- [ ] **Step 2: Rehearse §14 steps 1–6** with `docker stats --no-stream` taken during `central-down` and during the drain. Expected: every container below its limit; no exit 137.
- [ ] **Step 3: Write the README section.**
- [ ] **Step 4: Commit.** `git commit -m "docs: add demo run instructions"`

---

## Resilience tier (only after Task 16: R1 → R2 → R3; R4 at any point)

The user asked for a standby that takes over when a gateway fails, for zero downtime and for fault tolerance. Doc 02 §4.3, "Fault tolerance and zero downtime", states the target: no single point of failure, zero data loss, failover in seconds, and component downtime that never becomes data downtime. R1 and R2 grow out of the old Stretch 1 and 2. R3 needs both. R4 needs none of them. Every task ends with a live check: `drill.sh proof` reaches PASS with 0 gaps. The production-only measures in doc 02 §4.3 are not built.

### Task R1: Reconcile from the plant log (drill 8)

Replaces Stretch 1. Doc 02 §4.3 (recovery step 2); §5 ("Rebuild on a new file"); §10 drill 8.

**Files:**
- Create: `src/backend/EventDrivenDesign.Gateway/PlantLogReader.cs`
- Modify: `src/backend/EventDrivenDesign.Persistence/GatewayBuffer.cs` (`LogPosition`, `ReconcileAsync`), `src/backend/EventDrivenDesign.Gateway/Program.cs`, `src/deployment/scripts/drill.sh` (`lose-gateway-disk`)
- Test: `src/backend/tests/EventDrivenDesign.Tests/GatewayBufferTests.cs`, `src/backend/tests/EventDrivenDesign.IntegrationTests/PlantLogReaderTests.cs`

**Interfaces:**
- Consumes: `GatewayBuffer`, `MachineState` (Task 4); `KafkaConfigs.Tail`, `KafkaConfigs.Admin`, `KafkaConfigs.Producer` (tests), `Retry.Backoff` (Task 6); `TopicNames`, `MachineReading`, `MachineAlarm`, `Json.Options` (Task 1); `MachineCatalog` (Task 3).
- Produces:
  ```csharp
  // EventDrivenDesign.Persistence, in GatewayBuffer.cs next to MachineState
  public sealed record LogPosition(long LastSeq, long LastAlarmSeq, long LastDeviceTimeMs);

  // GatewayBuffer: one transaction on the writer thread
  public Task ReconcileAsync(IReadOnlyDictionary<string, LogPosition> log, CancellationToken ct);

  // EventDrivenDesign.Gateway
  public static class PlantLogReader
  {
      // The newest reading and alarm per machine in the plant topics. A machine with no reading is absent.
      public static Task<IReadOnlyDictionary<string, LogPosition>> NewestAsync(
          string bootstrap, IReadOnlyCollection<string> machines, CancellationToken ct);

      // NewestAsync until the broker answers (Retry.Backoff between attempts), then buffer.ReconcileAsync.
      public static Task ReconcileBufferAsync(
          GatewayBuffer buffer, string bootstrap, IReadOnlyCollection<string> machines, ILogger log, CancellationToken ct);
  }
  ```
- `NewestAsync`:
  - It reads `TopicNames.Readings` and `TopicNames.Alarms`. Partitions come from `IAdminClient.GetMetadata`, and low and high watermarks from `QueryWatermarkOffsets`, each with a 10 s timeout, so an unreachable broker throws `KafkaException`.
  - Each topic is scanned backwards in lock-step rounds. Round r reads the next 1,000 × 2^r records below where the previous round started, on every partition not yet at its low watermark. The scan stops when every machine in `machines` has been seen on that topic, or every partition has reached its low watermark.
  - Per machine: the highest `Seq` seen in readings, with that reading's device time in ms, and the highest `AlarmSeq` seen in alarms (0 when none). Payloads are parsed with `Json.Options`; a record that does not parse is skipped.
  - The consumer comes from `KafkaConfigs.Tail(bootstrap)` and uses `Assign` with explicit offsets, so it never joins a group or commits.
- `ReconcileAsync`, for each machine in `log`, in one transaction:
  - Delete its reading rows with `seq <= LastSeq` and its alarm rows with `seq <= LastAlarmSeq`: the log already holds those numbers.
  - Upsert `machine_state` so that `last_seq`, `last_alarm_seq` and `last_device_time_ms` each become the larger of the local and the log value. A machine with no local row takes the log's.
  - The lane counters behind `Stats()` follow the deletes.
- `Program`: after `builder.Build()` and before `app.Run()`, when `buffer.IsNew`, call `await PlantLogReader.ReconcileBufferAsync(buffer, bootstrap, <the shop's machine ids>, app.Logger, CancellationToken.None)`. The `Collector` singleton is resolved later, so it starts from the rebuilt state, and nothing is numbered until the log answers (doc 02 §4.3). R3 moves this call into every lease assignment.
- `drill.sh lose-gateway-disk <SHOP>` (§10 drill 8) = `docker compose rm -sf gateway-<shop>`, `docker volume rm edd_gateway-<shop>-data`, `docker compose up -d gateway-<shop>`. It checks the shop the way `kill-gateway` does.

- [ ] **Step 1: Write the failing unit tests** in `GatewayBufferTests.cs`:
  - `Reconcile_drops_rows_the_log_already_holds`: reading rows with seqs 48–52 for `P1-BDY-W042`, state `LastSeq` 52 with seq 52's device time. Reconcile with `LogPosition(50, 0, <seq 50's time>)`. Expected: `ReadOldest(10)` returns seqs 51 and 52 only; the state keeps `LastSeq` 52 and seq 52's time; `Stats().Readings` is 2.
  - `Reconcile_takes_the_log_when_it_is_ahead`: rows 51–52, state `(52, 0, 5_200)`. Log `LogPosition(200, 3, 9_000)`. Expected: no rows left; state `(200, 3, 9000)`.
  - `Reconcile_keeps_alarms_the_log_lacks`: alarm rows 1–2, `LastAlarmSeq` 2. Log alarm seq 1. Expected: alarm row 2 remains; `LastAlarmSeq` stays 2.
  - `Reconcile_on_a_new_file_writes_the_log_state`: a new file, and a log for two machines. Expected: `LoadState()` holds both positions; `Stats()` counts 0 rows.
- [ ] **Step 2: Write the failing integration tests** in `PlantLogReaderTests.cs`. Start Kafka and create the two plant topics as `SenderTests` does. Produce with `KafkaConfigs.Producer`: key = machine id, value = the contract serialised with `Json.Options`.
  - `Newest_position_comes_from_the_log`: readings with seqs 1–50 (device time T0 + seq seconds) and alarms 1–3 for `P1-BDY-W042`. `NewestAsync(bootstrap, ["P1-BDY-W042", "P1-BDY-W001"], ct)`. Expected: W042 → `LogPosition(50, 3, <T0 + 50 s in ms>)`; no entry for W001.
  - `Doubling_scan_reaches_past_other_traffic`: W042 seqs 1–50, then 30,000 readings of other machines (about 2,500 per partition, more than the first round of 1,000). Expected: W042 still has `LastSeq` 50.
- [ ] **Step 3: Run them and see them fail.** From `src/backend/`: `dotnet test --project tests/EventDrivenDesign.Tests/EventDrivenDesign.Tests.csproj` and `dotnet test --project tests/EventDrivenDesign.IntegrationTests/EventDrivenDesign.IntegrationTests.csproj`. Expected: compile errors (`LogPosition`, `ReconcileAsync` and `PlantLogReader` do not exist).
- [ ] **Step 4: Implement** `LogPosition`, `ReconcileAsync`, `PlantLogReader`, the `Program` call and the drill.
- [ ] **Step 5: Run every suite.** `src/scripts/build/test-backend.sh all`. Expected: all pass, 0 warnings.
- [ ] **Step 6: Verify live.** From `src/deployment/`:
  - `docker compose up -d --build --wait`, wait 30 s, and note `lastSeqByMachine["P1-BDY-W042"]` from `docker run --rm --network edd_plant busybox wget -qO- http://gateway-bdy:8080/metrics`.
  - `scripts/drill.sh lose-gateway-disk BDY`, wait 30 s, and read it again. Expected: higher than before; numbering never restarted at 1.
  - `scripts/drill.sh proof`. Expected: PASS, 0 missing, 0 gaps. Then `scripts/drill.sh resume`.
- [ ] **Step 7: Commit.** `git commit -m "feat(gateway): reconcile buffer state from the plant log"`

### Task R2: Backfill and `DataGap`

Replaces Stretch 2. Doc 02 §4.3 (recovery step 3); §5 ("Backfill on restart").

**Files:**
- Modify: `src/backend/EventDrivenDesign.Gateway/Collector.cs`, `CollectorService.cs`, `Program.cs`
- Test: `src/backend/tests/EventDrivenDesign.Tests/CollectorTests.cs`, `CollectorServiceTests.cs`

**Interfaces:**
- Consumes: `Collector` (Task 5), `CollectorService` (Task 8), `MachineState`, `TickBatch` (Task 4), `MachineAlarm`, `AlarmKind.DataGap` (Task 1).
- Produces:
  ```csharp
  // Collector
  public IEnumerable<TickBatch> Backfill(DateTimeOffset now, int depthSeconds);

  // CollectorService: one new parameter before time
  public CollectorService(Collector collector, GatewayBuffer buffer, IReadOnlyList<MachineInfo> machines,
      double faultsPerMinute, int historyDepthSeconds, TimeProvider time);
  ```
- `Backfill`:
  - Only machines with a resume state are backfilled. A machine's last tick is its `LastDeviceTimeMs` − index × 10 ms. Its missed ticks are the last tick + k seconds, for k = 1 to n, where n is the number of whole seconds from the last tick to `now`.
  - Each missed tick yields one reading built exactly as `Tick` builds it: device time = tick + index × 10 ms, `seq = last + 1`, the simulator's next values. Queued faults stay queued for the next live tick.
  - When n is larger than `depthSeconds`, only the newest `depthSeconds` ticks are backfilled. The older ones become one `DataGap` alarm on the alarm lane, with: the next `alarm_seq`; `ReadingSeq` = the machine's last seq before the backfill; `DeviceTime` = the first missing tick + index × 10 ms; severity `"warning"`; no signal; `Value` = the missing seconds; `Threshold` = `depthSeconds`. Missing seconds take no sequence numbers, so the gap shows in device time, never in sequence.
  - It yields one `TickBatch` per 60 seconds of device time, from the oldest backfilled tick. Each batch carries its rows and the state after it of every machine it touches. The `DataGap` rows go in the first batch.
  - `LastSeqByMachine` and `Generated` include the backfilled readings.
- `CollectorService`: before its first timer wait, unless paused, it writes every batch of `Backfill(time.GetUtcNow(), historyDepthSeconds)` with `WriteTickAsync`. It repeats with a fresh `now` until a pass yields nothing, so the seconds spent backfilling are covered too. A paused gateway never backfills (§5). A failed write stays fatal.
- `Program` passes `Gateway:HistoryDepthSeconds` (default 600, §5).

- [ ] **Step 1: Write the failing tests.** In `CollectorTests.cs`, every BDY machine resumes at `LastSeq` 10 with its last tick at T0 (device time T0 + index × 10 ms):
  - `Backfill_fills_a_120_second_gap`: `Backfill(T0 + 120 s, 600)`. Expected: 2 batches; 120 readings per machine with seqs 11–130 and device times T0 + 1 s … T0 + 120 s (plus the offset), 1 s apart; no alarm rows; the last batch's states have `LastSeq` 130.
  - `Backfill_past_the_depth_records_one_DataGap`: `Backfill(T0 + 900 s, 600)`. Expected: 600 readings per machine with seqs 11–610 and device times T0 + 301 s … T0 + 900 s; one `DataGap` alarm per machine with `Value` 300, `Threshold` 600, `ReadingSeq` 10 and device time T0 + 1 s (plus the offset).
  - `Backfill_skips_machines_never_numbered`: no resume state. Expected: no batches.
  - `Backfill_within_a_second_yields_nothing`: `now` = T0 + 900 ms. Expected: no batches.

  In `CollectorServiceTests.cs` (the helper passes `historyDepthSeconds` 600):
  - `Start_backfills_before_the_first_tick`: write one collector tick at T − 120 s (100 readings), then start the service at T without advancing the clock and wait 500 ms. Expected: 12,100 readings in the buffer, and every state at `LastSeq` 121.
  - `A_paused_gateway_never_backfills`: the same, paused. Expected: 100 readings.
- [ ] **Step 2: Run them and see them fail.** From `src/backend/`: `dotnet test --project tests/EventDrivenDesign.Tests/EventDrivenDesign.Tests.csproj`. Expected: compile errors (`Backfill` and the new constructor do not exist).
- [ ] **Step 3: Implement** `Backfill`, the service's start-up backfill and the `Program` setting.
- [ ] **Step 4: Run every suite.** `src/scripts/build/test-backend.sh all`. Expected: all pass, 0 warnings.
- [ ] **Step 5: Verify live.** From `src/deployment/`:
  - `docker compose down -v && docker compose up -d --build --wait`, then wait 60 s.
  - `docker compose kill gateway-bdy`, wait 30 s, `docker compose start gateway-bdy`, wait 30 s.
  - `docker compose exec central-db psql -U edd -d edd -c "select max(step) from (select device_time - lag(device_time) over (order by seq) as step from readings where machine_id = 'P1-BDY-W042') s"`. Expected: under 2 s. Without the backfill it would be about 30 s.
  - `scripts/drill.sh proof`. Expected: PASS, 0 missing, 0 gaps. Then `scripts/drill.sh resume`.
- [ ] **Step 6: Commit.** `git commit -m "feat(gateway): backfill missed seconds from machine history"`

### Task R3: Active/standby gateway pairs with a Kafka lease

Needs R1 and R2. Doc 02 §4.3 ("Fault tolerance and zero downtime") and §4.10 rows 5–6; §3, §4 (topic 5, `GatewayMetrics.Role`), §5 (`Gateway__Instance`), §8, §10 drill 9.

**Files:**
- Create: `src/backend/EventDrivenDesign.Gateway/GatewayLease.cs`
- Delete: `src/backend/EventDrivenDesign.Gateway/SenderService.cs` (the lease runs the sender now)
- Modify (contracts and settings): `src/backend/EventDrivenDesign.Contracts/TopicNames.cs`, `Metrics.cs`, `ApiModels.cs`; `src/backend/EventDrivenDesign.Messaging/KafkaConfigs.cs`
- Modify (gateway): `src/backend/EventDrivenDesign.Gateway/Collector.cs` (`Resume`), `Program.cs`
- Modify (API): `src/backend/EventDrivenDesign.Api/Snapshot.cs`, `Pollers.cs`, `ApiRoutes.cs`, `OverviewBuilder.cs`, `Program.cs`
- Modify (dashboard): `src/frontend/src/api.ts`, `src/frontend/src/format.ts`, `src/frontend/src/panels/BuffersLag.tsx`
- Modify (runtime): `src/deployment/kafka/create-topics.sh`, `src/deployment/compose.yaml`, `src/deployment/scripts/drill.sh`
- Modify (diagram): `src/scripts/diagrams/gen01_plant_to_central.py`, then regenerate `docs/diagrams/01-plant-to-central.html` and `.svg`; the diagram's alt text in `docs/02-solution-design.md` §3
- Test: `src/backend/tests/EventDrivenDesign.IntegrationTests/GatewayLeaseTests.cs`; `src/backend/tests/EventDrivenDesign.Tests/MessagingTests.cs`, `TopicNamesTests.cs`, `CollectorTests.cs`, `OverviewBuilderTests.cs`; `src/frontend/src/format.test.ts`

**Interfaces:**
- Consumes: `PlantLogReader.ReconcileBufferAsync` (R1); `CollectorService` with its backfill (R2); `Sender` (Task 7); `Snapshot`, `Pollers`, `OverviewBuilder`, `ApiRoutes` (Task 13).
- Produces:
  ```csharp
  // TopicNames
  public static string Lease(string shop);   // "gateway.lease.bdy": one partition, never written to

  // KafkaConfigs
  public static ConsumerConfig Lease(string bootstrap, string shop, string instanceId);

  // Contracts: one new last parameter each
  public sealed record GatewayMetrics(/* … as in Task 1 … */ ProcessStats Process, string Role);  // "active" or "standby"
  public sealed record GatewayTile(/* … as in Task 1 … */ long Generated, string? Instance);      // the shop's active instance

  // Collector: sets numbering and last device time from the state; machines missing from it keep theirs.
  // Call it only while no CollectorService runs.
  public void Resume(IReadOnlyDictionary<string, MachineState> state);

  // EventDrivenDesign.Gateway
  public sealed class GatewayLease(string bootstrap, string shop, string instanceId,
      Func<CancellationToken, Task> whileActive, ILogger log) : BackgroundService
  {
      public bool IsActive { get; }   // from the assignment until a revocation; unchanged by a lost session
  }

  // Pollers
  public static string ShopOf(string endpoint);                                  // "gateway-bdy-a:8080" → "BDY"
  // Snapshot
  public Snapshot(IEnumerable<string> gateways, IEnumerable<string> consumers);  // gateway instances: "gateway-bdy-a", …
  public IReadOnlyList<string> Instances { get; }
  public void SetGateway(string instance, GatewayMetrics m, DateTimeOffset at);
  public Stamped<GatewayMetrics>? Gateway(string shop);                          // the shop's active instance, or null
  // ApiRoutes
  public static Result<string> GatewayFor(string machineId, Snapshot s);         // "<active instance>:8080"
  ```
- `KafkaConfigs.Lease`: `GroupId` `gateway-lease-<shop>` (lower case); `GroupInstanceId` = the instance; `SessionTimeoutMs` 6000; `HeartbeatIntervalMs` 2000; `PartitionAssignmentStrategy.Range`; `EnableAutoCommit` false; `AutoOffsetReset.Latest`.
  - It uses the classic group protocol (librdkafka's default). The newer consumer protocol (KIP-848) sets session timeouts on the broker, with a 45 s minimum. 6,000 ms is the broker's default `group.min.session.timeout.ms`, the lowest it accepts.
  - Range is an eager protocol: every rebalance revokes first and then hands each member its full assignment, even an empty one, so an instance always learns whether it holds the lease. Range sorts static members by `group.instance.id`, so `-a` holds the lease whenever it is up.
- `GatewayLease` (the lease partition is partition 0 of `TopicNames.Lease(shop)`):
  1. It subscribes and calls `Consume(500 ms)` in a loop on a long-running thread. Nothing is ever produced; the loop only drives heartbeats and rebalances.
  2. Assigned, with the lease partition, while no term runs: `IsActive = true`, log it, and start a term, `whileActive(token)`, on the thread pool.
  3. Assigned, with the lease partition, while a term runs (it survived a lost session): carry on.
  4. Assigned without the lease partition while a term runs: stop the term, as in rule 5.
  5. Revoked, with the lease partition: cancel the term's token and wait, inside the handler, until `whileActive` returns, so the next holder is assigned only after this one has stopped. Then `IsActive = false`. Unacknowledged rows stay on disk. A revoke without the lease partition changes nothing.
  6. Lost, with the lease partition (the session expired because the plant broker was unreachable for 6 s): keep the term running. Its disk buffer rides out the broker outage (doc 02 §4.2, tier 1), and the standby cannot be granted the lease meanwhile. The next assignment decides (rules 3 and 4).
  7. A faulted term is fatal: the consume loop rethrows its exception from `ExecuteAsync`, so the host stops with a non-zero exit code and Compose restarts it, as a faulted `CollectorService` does today (Task 8).
  8. On host stop: stop the term, then `Close()` the consumer. A static member sends no LeaveGroup, so the standby is granted the lease after the 6 s session timeout.
- Gateway host (`Program`):
  - New required setting `Gateway__Instance`: the Compose service name, e.g. `gateway-bdy-a`. It is the lease's `group.instance.id` and the `GatewayId` in `/metrics`.
  - `GatewayLease` is the only hosted service. `CollectorService` is no longer registered, and `SenderService` is deleted. One term (`whileActive`):
    1. `PlantLogReader.ReconcileBufferAsync(buffer, …)` (R1), on every term, not only on a new file, because the partner may have numbered further. R1's start-up call is removed.
    2. `collector.Resume(buffer.LoadState())`.
    3. Start a new `CollectorService` (it backfills first, R2) and `sender.RunAsync(token)`. When the token is cancelled, stop both; when either faults, the term faults.
  - `/metrics`: `Role` = `lease.IsActive ? "active" : "standby"`.
- API:
  - `Api__Gateways` lists the ten endpoints, `gateway-prs-a:8080,gateway-prs-b:8080,…,gateway-asm-b:8080` (also the default in `Program`); `Program` passes their host names to `Snapshot`. `Shops` stays the five shops, derived from the instances.
  - `Snapshot.Gateway(shop)` is the shop's active instance: of its instances whose last metrics say `Role` `"active"`, the most recently polled. `SettledSince(shop)` follows that instance. `OverviewBuilder` already reads `Gateway(shop)`, so the tile, the live rate, the proof and the drained check use the active instance, and a standby's buffer and stale numbering never count.
  - `GatewayTile.Instance` = the active instance's `GatewayId`, or null when the shop has none.
  - Pause and resume fan out to every entry in `Snapshot.Instances`, so a standby that takes over keeps the paused state. The error lists the instances not reached.
  - `ApiRoutes.GatewayFor(machineId, s)`: a bad id → `api.machine.bad_id` (Invalid); no active instance for the shop → `api.gateway.unavailable` (Unavailable, 503); otherwise `"<instance>:8080"`.
- Dashboard: `api.ts` adds `instance: string | null` to the gateway tile. `format.ts` adds `instanceLabel(instance: string | null): string` (`"gateway-bdy-b"` → `"b"`, null → `"—"`), and `BuffersLag` shows it next to each shop (`BDY · b`).
- Runtime:
  - `create-topics.sh` also creates `gateway.lease.prs`, `.bdy`, `.pnt`, `.mch` and `.asm`, with 1 partition each and `--if-not-exists`.
  - Compose: ten gateways from the `x-gateway` anchor, `gateway-<shop>-a` and `gateway-<shop>-b`. Each has `Gateway__Instance` = its service name, the shop's existing seed (both read the same machines), and its own volume `gateway-<shop>-<a|b>-data`; 256 MB each and `restart: on-failure` (§3). The old `gateway-<shop>` services and volumes go.
  - `drill.sh`: a helper reads the shop's active instance from `/api/overview` (`curl -fsS --max-time 5`, parsed with `python3 -c`) and fails when there is none. `kill-gateway` and `lose-gateway-disk` act on it; its volume is `edd_<instance>-data`. New `failover <SHOP>` (§10 drill 9): kill the active instance; poll `/api/overview` every 1 s, for up to 30 s, until the other instance is the shop's active one and `Healthy`; print `<instance> took over after N s`; then `docker compose start` the killed one. It exits non-zero when nobody took over.
- Diagram 01: each shop's gateway becomes a pair, `gateway-<shop>-a` (active) and `gateway-<shop>-b` (standby, dashed), with their volumes and the note `lease: gateway.lease.<shop> · 1 partition · static member · session 6 s`. The `Gateways ×5` chip becomes `Gateways ×10 (5 pairs)`. The `machine_state` ledger row reads "every lease assignment: reconcile with plant Kafka, then backfill".

- [ ] **Step 1: Write the failing unit tests:**
  - `MessagingTests.Lease_settings_match_the_spec`: `KafkaConfigs.Lease("k:9092", "BDY", "gateway-bdy-a")` has the values above.
  - `TopicNamesTests.Lease_topic_is_per_shop`: `TopicNames.Lease("BDY")` is `"gateway.lease.bdy"`.
  - `CollectorTests.Resume_restarts_numbering_from_the_given_state`: three ticks, then `Resume` with `P1-BDY-W042` at `LastSeq` 200. Expected: the next tick gives it seq 201; the other machines continue at 4.
  - In `OverviewBuilderTests.cs`:
    - `Tile_and_proof_use_the_active_instance`: `gateway-bdy-a` is standby with `lastSeqByMachine` 3 and 50 buffered rows; `gateway-bdy-b` is active, paused and drained at 5; the other shops are active, paused and drained, and every count matches. Expected: BDY's tile has `Instance` `"gateway-bdy-b"` and generated 5; the verdict is PASS.
    - `A_shop_without_an_active_instance_waits`: both BDY instances are standby. Expected: BDY's tile is `Down` with `Instance` null; the verdict is WAIT.
    - `ShopOf_strips_the_instance`: `Pollers.ShopOf("gateway-bdy-a:8080")` is `"BDY"`.
    - `Fault_goes_to_the_active_instance` (→ `"gateway-bdy-b:8080"`) and `Fault_without_an_active_instance_is_unavailable` (→ `ErrorKind.Unavailable`). They replace the two `GatewayFor` tests.
    - The existing tests switch to instance names and `Role` `"active"` through their helpers. Their assertions stay as they are.
  - `format.test.ts`: `instanceLabel("gateway-bdy-b")` is `"b"`; `instanceLabel(null)` is `"—"`.
- [ ] **Step 2: Write the failing integration tests** in `GatewayLeaseTests.cs`. Each starts Kafka as `SenderTests` does and creates `gateway.lease.tst` with 1 partition. Two leases for shop `TST`, `tst-a` and `tst-b`, get a `whileActive` that counts its starts and waits on its token.
  - `Exactly_one_instance_holds_the_lease`: start `tst-b`, then `tst-a`. Expected: within 15 s exactly one is active, and it is `tst-a`; sampled every 100 ms for the next 5 s, never both.
  - `The_standby_takes_over_within_15_seconds`: stop the active one. Expected: its term's token is cancelled at once, and the other is active within 15 s.
  - `A_lost_session_keeps_the_active_running`: `PauseAsync` the container for 10 s. Expected: `tst-a`'s token is never cancelled and it stays active. After `UnpauseAsync`, within 30 s exactly one instance is active, it is `tst-a`, and its `whileActive` started once.
- [ ] **Step 3: Run them and see them fail.** From `src/backend/`, both test projects; from `src/frontend/`, `bun test`. Expected: compile errors and a missing export.
- [ ] **Step 4: Implement** the contracts, the settings, `GatewayLease`, `Collector.Resume`, the gateway `Program`, the API changes and the dashboard label. Delete `SenderService.cs`.
- [ ] **Step 5: Run every suite.** `src/scripts/build/test-backend.sh all` and `src/scripts/build/test-frontend.sh`. Expected: all pass, 0 warnings.
- [ ] **Step 6: Write the runtime changes:** the topics script, the ten gateways in Compose, `Api__Gateways`, and the drills.
- [ ] **Step 7: Redraw diagram 01.** Edit the generator, run `python3 src/scripts/diagrams/gen01_plant_to_central.py`, then `src/scripts/diagrams/check.sh --export 01-plant-to-central`. Expected: both checks pass and the SVG is written. Update the diagram's alt text in doc 02 §3 to say five active/standby gateway pairs.
- [ ] **Step 8: Verify live.** From `src/deployment/`:
  - `docker compose up -d --build --wait --remove-orphans`, then remove the old volumes: `docker volume rm edd_gateway-prs-data edd_gateway-bdy-data edd_gateway-pnt-data edd_gateway-mch-data edd_gateway-asm-data`.
  - `curl -fsS http://localhost:3000/api/overview`. Expected: five gateway tiles `Healthy`, each with instance `gateway-<shop>-a`; a live rate of about 500, because standbys send nothing.
  - `scripts/drill.sh failover BDY`. Expected: `gateway-bdy-b took over after N s`, with N ≤ 15; then `gateway-bdy-a` returns and takes the lease back.
  - `scripts/drill.sh proof`. Expected: PASS, 0 missing, 0 gaps. Then `scripts/drill.sh resume`.
  - `docker stats --no-stream`. Expected: every container below its limit.
- [ ] **Step 9: Commit.** `git commit -m "feat(gateway): run active/standby gateway pairs with a Kafka lease"`

### Task R4: No single point of failure elsewhere in the demo

Needs none of R1–R3. Doc 02 §4.3 (rows 3, 5, 7 and 8 of its table); §3, §6, §9, §10 drills 1–3.

**Files:**
- Modify: `src/deployment/compose.yaml` (`api-1` and `api-2` from an `x-api` anchor; `mm2-1` and `mm2-2` from an `x-mm2` anchor; the dashboard's `depends_on`), `src/deployment/nginx/default.conf`, `src/deployment/kafka/mm2.properties`, `src/deployment/scripts/drill.sh`

**Interfaces:**
- Two APIs: `api-1` and `api-2`, identical to today's `api` (networks plant, central and ops; 256 MB each; `restart: on-failure`). Each polls every source itself and shares nothing with the other, so either can answer alone. The dashboard's `depends_on` names both.
- nginx (open-source nginx 1.27.3 and later resolves upstream names at run time with `resolve`, so nginx still starts without the APIs). `resolver` moves to the top of `default.conf`, which is the `http` context:
  ```nginx
  resolver 127.0.0.11 valid=5s;   # Docker's embedded DNS

  upstream api {
      zone api 64k;
      ip_hash;                    # one viewer reads one API's snapshot, so the numbers do not jump between two
      server api-1:8080 resolve max_fails=1 fail_timeout=5s;
      server api-2:8080 resolve max_fails=1 fail_timeout=5s;
  }

  server {
      listen 80;
      root /usr/share/nginx/html;

      location /api/ {
          proxy_pass http://api;
          proxy_next_upstream error timeout non_idempotent;   # every command is idempotent (doc 02 §4.6)
          proxy_connect_timeout 1s;
          proxy_read_timeout 10s;
      }

      location / {
          try_files $uri /index.html;
      }
  }
  ```
  This is a passive health check: a refused or timed-out connection marks the server failed for 5 s and sends the request to the other one. An HTTP 503 from a running API (central database down) is an answer, not a failure: it is not retried, so a central outage never marks both APIs failed. If `nginx -t` rejects `ip_hash` together with `resolve`, use `hash $remote_addr;` instead.
- Two MirrorMaker 2 workers: `mm2-1` and `mm2-2` run the same `mm2.properties` in dedicated mode. For the flow `plant1->central` they form one Connect group through the flow's internal topics on central, share its tasks, and take over each other's. Each sets `hostname` to its service name, so the REST address it advertises resolves on the Docker networks. 768 MB each, the same heap (§3). Append to `mm2.properties` (§6):
  ```properties
  # R4: two dedicated workers. They forward task configs to the leader over internal REST (KIP-710)
  dedicated.mode.enable.internal.rest = true
  # hand a dead worker's tasks to the survivor after 10 s, not Connect's default 5 minutes
  plant1->central.scheduled.rebalance.max.delay.ms = 10000
  ```
- `restart: on-failure`: already on every .NET service through the gateway and consumer anchors and on `api`. The new services keep it, and Step 3 checks it.
- `drill.sh` (§10 drills 1–3): `central-down` kills `mm2-1 mm2-2 consumer-1 consumer-2`; `central-up` starts `central-kafka central-db consumer-1 consumer-2 mm2-1 mm2-2`; `wan-cut` and `wan-restore` disconnect and connect `edd-mm2-1-1` and `edd-mm2-2-1`.

- [ ] **Step 1: See both single points fail (RED).** On the current stack, from `src/deployment/`:
  - `docker compose kill api`, then `curl -s -o /dev/null -w '%{http_code}\n' http://localhost:3000/api/overview`. Expected: 502. Then `docker compose start api`.
  - `docker compose kill mm2`, then run `docker compose exec central-kafka /opt/kafka/bin/kafka-get-offsets.sh --bootstrap-server localhost:9092 --topic plant1.machine.readings` twice, 10 s apart. Expected: no partition grows. Then `docker compose start mm2`.
- [ ] **Step 2: Write** the Compose, nginx, MirrorMaker 2 and drill changes.
- [ ] **Step 3: Check the configuration.** `docker compose config --format json | python3 -c 'import json,sys; s=json.load(sys.stdin)["services"]; print([n for n,v in s.items() if "build" in v and v.get("restart") != "on-failure"])'`. Expected: `[]`. Then `docker compose up -d --build --wait --remove-orphans` and `docker compose exec dashboard nginx -t`. Expected: `syntax is ok` and `test is successful`.
- [ ] **Step 4: Verify live (GREEN):**
  - `docker compose kill api-1`; then, once a second for 15 s, `curl -fsS --max-time 2 -o /dev/null http://localhost:3000/api/overview`. Expected: every call succeeds. `docker compose start api-1`, and repeat with `api-2`.
  - `docker compose kill mm2-1`; 30 s and 40 s later, run the `kafka-get-offsets.sh` command above. Expected: every one of the 12 partitions grew between the two reads, because the dead worker's task moved to `mm2-2`; the dashboard's mirror lag is back under 2 s. Then `docker compose start mm2-1`.
  - `scripts/drill.sh central-down`, wait 60 s, `scripts/drill.sh central-up`, then `scripts/drill.sh proof`. Expected: PASS, 0 missing, 0 gaps. Then `scripts/drill.sh resume`.
  - `docker stats --no-stream`. Expected: every container below its limit.
- [ ] **Step 5: Commit.** `git commit -m "feat(deployment): run two APIs and two MirrorMaker 2 workers"`

---

## Stretch (only after the resilience tier)

### Stretch 3: WAN-cut and plant-down drills, plant-side alarm list

- [ ] Rehearse `wan-cut`/`wan-restore` and `plant-down`/`plant-up` and fix what breaks. Add a plant alarms tail to `KafkaWatcher`, the plant column in `/api/alarms`, and the plant list in `AlarmsPanel`. Commit `feat: add WAN and plant broker drills`.
