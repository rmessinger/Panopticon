# Task Card `WI-B1-04`: Write JSON run records and CSV summaries

| Field | Value |
|---|---|
| Card ID | `WI-B1-04` |
| Roadmap phase | [Track B · B1](../../track-b-scale.md): B1-4 report writers |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B1-02` |
| Diagrams | [B1 diagrams](../../deep-dives/b1-benchmarking.md#architecture-diagrams) |

## Goal

Draft: flip to Ready once the prerequisite cards are Done and the named types are reconciled. Persist B1 run records in an A2-compatible JSON shape and derive a CSV summary.

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

## Acceptance criteria

- [ ] The new behavior is covered by tests.
- [ ] Reports retain scenario id, seed, and measured metric values.
- [ ] Standard validation passes.

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
