# Task Card `WI-A5-26`: Track A5 throughput in B1

| Field | Value |
|---|---|
| Card ID | `WI-A5-26` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-5 authoring and visualization at scale |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-A5-16`, `WI-A5-23`, `WI-A5-25`; B1 harness implemented |
| Diagrams | B1 diagrams and [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) |

## Goal

Add B1-owned benchmarks for procedural expansion, serialization, bundle loading, random seek, and
visible-frame rendering at A5 scale.

## Context to read (and only this)

- B1 benchmark API, report schema, and baseline policy.
- A5 scale-budget section in the implementation plan.
- Procedural generator, matrix bundles, indexed reader, and replay renderer.

## Steps

1. Add B1 scenarios for 10k entities, 10k commands, and a long recording.
2. Measure expansion, serialization, bundle open, random seek, and visible render projection.
3. Record time, throughput, allocations, statistical summary, and hardware context.
4. Establish documented baselines/tolerances using B1's process.
5. Link A5-5 budgets to these benchmark IDs.

## Files likely to change

- B1 benchmark project/files
- A5 implementation plan
- Benchmark tests or smoke checks

## Acceptance criteria

- [x] All five A5 operations emit machine-readable B1 results.
  - `A5ThroughputBenchmark.Run` records `a5.expand`, `a5.serialize`, `a5.bundleOpen`,
    `a5.randomSeek`, and `a5.visibleProjection` spans plus `tick.wallMs`/`tick.allocBytes`
    through the manual `BenchmarkMetricRecorder`; `A5BenchmarkTests` asserts all seven
    statistics and a writer round-trip.
- [x] Scale fixtures meet the documented entity, command, and run-length sizes.
  - `A5ThroughputPlan.FullScale` pins 10,000 entities / 10,000 commands / 250 recording frames;
    asserted as constants.
- [x] Statistics, allocations, hardware, and thresholds use B1 infrastructure.
  - Seeded-bootstrap CIs via `SimulationBenchmarkRunner.BuildStatistics`, environment via
    `EnvironmentRecord.Capture`, baseline policy stays B1 CI-non-overlap (`--baseline`); no
    thresholds live outside B1.
- [x] A5 contains no standalone stopwatch gate.
  - The `ProceduralScenarioTests` 10k-expansion wall-clock assertion was removed; counts
    remain, throughput is owned by the `a5.*` metrics.
- [x] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~A5Benchmark"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

Do not loosen thresholds to make an initial run pass without following B1 baseline policy.

## On completion

- Set Status above to `Done` or `Blocked`.
- Tick A5-5 when all scale cards are complete.
- Append a result line to `.exchange`.
