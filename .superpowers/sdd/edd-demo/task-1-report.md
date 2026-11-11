# Task 1 Report: Skeleton + Contracts + single-container packaging (Phase 0)

## Status: DONE_WITH_CONCERNS

## What was created

| # | Path | Contents |
|---|------|----------|
| 1 | `Directory.Build.props` | `net10.0` (SDK 10.0.403 is installed; retarget note inside), Nullable, TreatWarningsAsErrors, GenerateDocumentationFile, ImplicitUsings |
| 2 | `Directory.Packages.props` | Central management, pinned: Confluent.Kafka 2.15.1, Microsoft.Data.Sqlite 10.0.15, xunit.v3 4.0.3, Microsoft.NET.Test.Sdk 18.11.1, xunit.runner.visualstudio 4.0.1 |
| 3 | `global.json` | SDK pin 10.0.403 + `"test": {"runner": "Microsoft.Testing.Platform"}` (required: .NET 10 `dotnet test` rejects MTP projects otherwise) |
| 4 | `edd-demo.slnx` | Contracts, Messaging, Edd.Tests |
| 5 | `src/backend/Edd.Contracts/` | `Result<T>`, `ProducerCommand` (Start/Stop/SetRate, `$type` discriminator), `ServiceHealth` (Healthy/Degraded/Down), `CommandAck`, `Heartbeat` (15 spec fields), `TopicNames` (5 topics), source-gen `EddJsonContext`. Abstract records + `// shortcut:` swap-to-union/closed notes (SDK 10 has no `union`/`closed` keywords) |
| 6 | `src/backend/Edd.Messaging/` | `IMessageHandler<T>`, `KafkaOptions` + `HeartbeatOptions`, `RateMath` (rate/10 + remainder), `ResourceSampler` (spec section 8 CPU% formula) |
| 7 | `tests/Edd.Tests/` | `ContractShapeTests.cs` (topics, command/health/ack/heartbeat JSON round-trips, per-variant dispatch), `ResultTests.cs` (Ok/Fail semantics, rate maths, CPU formula) |
| 8 | `src/frontend/` | bun + Vite + React 19 + TS + Tailwind v4, mobile-first tile grid, single relative-path helper `src/lib/api.ts` |
| 9 | `Dockerfile` | 4 stages (sdk:11.0 build, oven/bun:1 web, kafka 4.4.0, temurin 21-jre, aspnet:11.0 runtime), restore-before-copy cached layers, supervisord + nginx, 8-program config, single-node KRaft properties, topic-init for the 5 topics, NO volume, http only, EXPOSE 3000 8080 9092 |
| 10 | `deploy/` | `supervisord.conf`, `nginx.conf`, `kafka.properties`, `scripts/start-kafka.sh`, `scripts/init-topics.sh` |

## Test evidence (TDD red-green)

| # | Command | Result |
|---|---------|--------|
| 1 | `dotnet test tests/Edd.Tests/` (types missing) | FAIL as required: `CS0234 Edd.Contracts does not exist`, `CS0246 ProducerCommand/ServiceHealth not found` |
| 2 | Same after `using Xunit;` added | Still FAIL (production types missing) — genuine red state preserved |
| 3 | After Contracts+Messaging implemented | 21/22; 1 fail: record equality on `Heartbeat.Assigned int[]` (reference compare) — fixed test to compare array by value |
| 4 | `dotnet test` (full suite, pristine) | **Passed: 22/22, 0 failed** |
| 5 | `bash -n` on both deploy scripts | OK |
| 6 | `bun install && bun run build` | OK (17 modules, dist emitted) |
| 7 | `docker build -t edd-demo .` | **NOT RUN — no Docker daemon on this machine** (see concerns) |

Deviations from plan worth knowing:

| # | Deviation | Reason |
|---|-----------|--------|
| 1 | `global.json` added (not in plan file list) | .NET 10 SDK refuses `dotnet test` on MTP/xunit.v3 projects without MTP runner mode |
| 2 | Test project sets `GenerateDocumentationFile=false` | Otherwise CS1591-as-error fires on public test classes; production libs keep 100% XML docs |
| 3 | Rate/CPU tests live in `ResultTests.cs`, dispatch test keeps a `_ => throw` arm | xunit file list fixed at 2 files; SDK 10 compiler proves abstract-record switch non-exhaustive (CS8509), so the default arm is mandatory until the `union` keyword lands |
| 4 | `@types/react{,-dom}` in frontend devDeps | React 19 ships no bundled types |
| 5 | Dockerfile builds (not publishes) .NET + copies no `/app` service DLLs yet | Api/Producer/Consumer projects land in Tasks 2-4; `docker build` layer order is final |

## Concerns

1. **No Docker on this machine — `docker build` unverified.** Image/tag names (`mcr.microsoft.com/dotnet/sdk:11.0`, `aspnet:11.0`, `apache/kafka:4.4.0`, `eclipse-temurin:21-jre`, `oven/bun:1`) were checked against Docker Hub/NuGet registries but never pulled. First `docker build` may surface issues (e.g. kafka path not `/opt/kafka`, apt package names on the .NET 11 base image).
2. **Supervisord will fail at container runtime until Tasks 2-4 land** (`/app/api|producer|consumer` DLLs don't exist yet). Expected per plan; image build is the Task 1 bar.
3. **Spec still says `src/Edd.*` + `web/` + `net11.0` + minimal API** in sections 9-10; binding overrides (backend/frontend layout, controllers, net10.0-now/11.0-images) live only in the plan + this report. A later reader of the spec alone will be confused.
