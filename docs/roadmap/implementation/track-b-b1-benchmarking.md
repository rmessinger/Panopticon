# Implementation plan: Track B · B1 benchmarking

> Audience: human + agent implementation plan for B1 benchmark harness work.

Source of truth: [B1 benchmarking deep dive](../deep-dives/b1-benchmarking.md) and
[Architecture Diagrams](../deep-dives/b1-benchmarking.md#architecture-diagrams).

## Scope

B1 adds a custom macro benchmark harness over `SimulationHost`. BenchmarkDotNet remains the micro
benchmark tool for isolated data structures. The harness measures cost and memory for offline runs;
it does not reduce simulation detail.

## Card sequence

| Card | Sub-phase | Output | Status |
|---|---|---|---|
| [`WI-B1-01`](../agent/cards/WI-B1-01-benchmark-audit-contract.md) | B1-1 | Hook audit and report schema | Done |
| [`WI-B1-02`](../agent/cards/WI-B1-02-macro-harness-skeleton.md) | B1-2 | Benchmark project, CLI, empty-host scenario | Ready |
| [`WI-B1-03`](../agent/cards/WI-B1-03-tick-system-metrics.md) | B1-3 | Tick, allocation, GC, memory, and system samples | Draft |
| [`WI-B1-04`](../agent/cards/WI-B1-04-report-writers.md) | B1-4 | JSON records and CSV summary | Draft |
| [`WI-B1-05`](../agent/cards/WI-B1-05-statistics-regression.md) | B1-5 | Median bootstrap CI and regression comparer | Draft |
| [`WI-B1-06`](../agent/cards/WI-B1-06-reference-scenarios.md) | B1-6 | Seeded reference scenarios and 10k-unit anchor | Draft |
| [`WI-B1-07`](../agent/cards/WI-B1-07-consumer-measurements.md) | B1-7 | B4a, B4b, and Western Front adapters | Draft |

## Acceptance by sub-phase

| Sub-phase | Acceptance criteria |
|---|---|
| B1-1 | The deep dive lists every harness hook with file:line references and fixes the report schema. |
| B1-2 | A CLI can run a `SimulationHost` benchmark plan and emit a skeleton report. |
| B1-3 | Measured frames record wall time, allocations, GC counts, peak memory, and optional system spans. |
| B1-4 | JSON is compatible with A2 distribution reports and CSV is derived from JSON. |
| B1-5 | Median bootstrap 95 percent CIs are deterministic for a bootstrap seed, and CI non-overlap flags regressions. |
| B1-6 | Seeded scenarios include empty host, movement, combat, path query, and `b1.10k-scale-anchor`. |
| B1-7 | B4a, B4b, and Western Front consumers emit B1 JSON and CSV without changing their behavior. |

## Validation

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```
