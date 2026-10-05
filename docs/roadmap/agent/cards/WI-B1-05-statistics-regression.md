# Task Card `WI-B1-05`: Add median bootstrap confidence intervals and regression comparison

| Field | Value |
|---|---|
| Card ID | `WI-B1-05` |
| Roadmap phase | [Track B · B1](../../track-b-scale.md): B1-5 statistics |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-B1-04` |
| Diagrams | [B1 diagrams](../../deep-dives/b1-benchmarking.md#architecture-diagrams) |

## Goal

Add deterministic bootstrap CIs and CI-overlap regression checks.

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

- `BootstrapStatistics`: seeded median bootstrap with linear-interpolation percentiles; same
  samples + seed always yield the same median and 95 percent CI; single samples degenerate to
  a point interval.
- `BenchmarkStatistics`: per-metric median/CI summary plus `BaselineRunId`/`IsRegression`;
  regression rule is the B1 decision verbatim: intervals that do not overlap differ (medians
  carry the direction).
- Runner computes statistics for `tick.wallMs`, `tick.allocBytes`, and each
  `system.<name>.spanMs` from the run seed; `--baseline <path>` loads a stored record and
  stamps per-metric verdicts (baseline run id from its A1 `RunId`).
- Writer emits `stat.<metric>.median/.ciLow/.ciHigh/.regression` CSV rows; JSON gains the
  `statistics` list; CLI prints "N metric(s) changed beyond CI overlap".
- Proven live: two `b1.empty-host` runs (seed 5), second with `--baseline`: tick.wallMs
  median 3.015 ms CI [2.986, 3.037], tick.allocBytes CI [1.49M, 1.52M], gameLogic CI
  [2.588, 2.640] flagged changed; network CI overlapped and stayed unflagged.
- Tests: 9 install-free (`BootstrapStatisticsTests`: median odd/even, seeded
  reproducibility, ordered bounds, percentile interpolation, FromSamples, CI-overlap rule).

## Acceptance criteria

- [x] The new behavior is covered by tests.
- [x] Reports retain scenario id, seed, and measured metric values.
- [x] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~Bootstrap"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No optimization and no simulation detail reduction.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
