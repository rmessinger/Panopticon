# Task Card `WI-B1-03`: Record tick, allocation, GC, memory, and system spans

| Field | Value |
|---|---|
| Card ID | `WI-B1-03` |
| Roadmap phase | [Track B · B1](../../track-b-scale.md): B1-3 tick and system metrics |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-B1-02` |
| Diagrams | [B1 diagrams](../../deep-dives/b1-benchmarking.md#architecture-diagrams) |

## Goal

Add bounded metric capture to each measured run: per-tick wall time, allocation deltas, GC counts, sampled memory, per-system spans, and the run environment record.

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

- `TickMetricSample` (wall ms, allocation delta, cumulative GC counts, interval-sampled heap
  and working set), `SystemMetricSample` (frame, system, span ms), `EnvironmentRecord`
  (OS/runtime/GC/arch/CPU/processors/memory/GPU/install/env vars) per the B1-01 schema.
- `BenchmarkMetricRecorder`: subscribes host `TickStarting`/`TickComplete` plus a new
  `SimulationHost.LogicSystemSpan` forwarding `LogicStepper.SystemSpan`; attached after warmup
  so warmup samples are discarded by construction. System spans align to the in-progress frame
  (`host.Frame + 1`) since `AdvanceLogic` runs before the frame counter increments.
- Engine seam: `LogicStepper.LogicTick` measures its four subsystems (gameLogic, network,
  scene3d, partition) with `Stopwatch` timestamp pairs when a span sink is attached; the
  default path is unchanged and allocation-free (no closures in the measured path).
- Proven on `b1.empty-host` (300 frames): 300 tick samples frames 31..330, wall 1.9/2.55/4.3
  ms min/mean/max, 354 MB total allocations, 49 gen0 GCs, heap 120 MB, working set 329 MB;
  per-system mean ms: gameLogic 2.146, scene3d 0.052, network 0.001, partition ~0;
  environment captured (Ubuntu 26.04, Intel Ultra 7 265K, 20 logical, Workstation/Interactive,
  install `cnc_generals`).
- Tests: 7 install-free (`BenchmarkMetricRecorderTests` over the mocked game, incl. span
  alignment and interval control; `BenchmarkMetricEnvironmentTests`).

## Acceptance criteria

- [x] The new behavior is covered by tests.
- [x] Reports retain scenario id, seed, and measured metric values.
- [x] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~BenchmarkMetric"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No optimization and no simulation detail reduction.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
