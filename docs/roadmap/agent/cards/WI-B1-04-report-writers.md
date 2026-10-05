# Task Card `WI-B1-04`: Write JSON run records and CSV summaries

| Field | Value |
|---|---|
| Card ID | `WI-B1-04` |
| Roadmap phase | [Track B · B1](../../track-b-scale.md): B1-4 report writers |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B1-02` |
| Diagrams | [B1 diagrams](../../deep-dives/b1-benchmarking.md#architecture-diagrams) |

## Goal

Persist B1 run records in an A2-compatible JSON shape and derive a CSV summary.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b1-benchmarking.md`.
- `docs/roadmap/deep-dives/a1-headless-host.md`.
- `docs/roadmap/deep-dives/a2-reproducibility.md`.
- `src/Panopticon.Simulation/SimulationHost.cs`.
- `src/Panopticon.Simulation/RunOutput.cs`.

## Steps

1. Add the named B1 types from the deep dive.
2. Thread them through `SimulationBenchmarkRunner`.
3. Add install-free tests for the new behavior.
4. Keep JSON and CSV output compatible with earlier B1 cards.

## Files likely to change

- `src/Panopticon.Benchmarks.Simulation/`
- Benchmark tests

## Result

Landed 2026-10-04:

- `BenchmarkReportWriter`: `WriteJson`/`ToJson`/`FromJson` with the A2-compatible camelCase
  shape (authoritative record) and `WriteCsv` deriving one row per metric aggregate with run
  plus environment context columns (scenarioId, seed, frames, wall ms, os, cpu, processors,
  gcMode, metric, value). Basic aggregates only (min/mean/max, totals, finals, counts);
  bootstrap statistics arrive in B1-5.
- `FromJson` validates the scenario id because System.Text.Json accepts `{}` by defaulting
  constructor parameters; parse failures surface as `ArgumentException`.
- `Program` now writes both artifacts through the writer when `--json`/`--csv` are set.
- Proven on `b1.empty-host` (300 frames): JSON round-trip stable; CSV emitted 21 metric rows
  (run wall, tick aggregates, GC finals, per-system mean/max/count).
- Tests: 6 install-free (`BenchmarkReportWriterTests`): round-trip values, stable
  re-serialization, camelCase fields, CSV shape, on-disk writes, rejection of non-record JSON.

## Acceptance criteria

- [x] The new behavior is covered by tests.
- [x] Reports retain scenario id, seed, and measured metric values.
- [x] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~BenchmarkReport"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No optimization and no simulation detail reduction.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
