# Task Card `WI-B1-01`: Audit benchmark hooks and report contract

| Field | Value |
|---|---|
| Card ID | `WI-B1-01` |
| Roadmap phase | [Track B · B1](../../track-b-scale.md): B1-1 benchmark audit |
| Status | `Done` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [B1 diagrams](../../deep-dives/b1-benchmarking.md#architecture-diagrams) |

## Goal

Ground the benchmark harness contract against landed A1/A2 types and fix the initial B1 report schema notes.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b1-benchmarking.md`.
- `docs/roadmap/deep-dives/a1-headless-host.md`.
- `docs/roadmap/deep-dives/a2-reproducibility.md`.
- `src/Panopticon.Simulation/SimulationHost.cs`.
- `src/Panopticon.Simulation/RunOutput.cs`.
- `src/Panopticon.Tools.ReplaySketch.Core/Services/StatisticalRunHarness.cs`.

## Steps

1. Verify the B1 hook table against source.
2. Add missing file:line references.
3. Draft JSON schema fields for run, tick, system, environment, and statistics records.
4. Record follow-up symbol names for B1-2 through B1-5.

## Files likely to change

- `docs/roadmap/deep-dives/b1-benchmarking.md`

## Result

Audit complete 2026-10-04 against `c75c6d975`:

- All 8 hooks re-verified against source; 6 stale file:line ranges corrected (run loop, tick
  callbacks, order injection, run record, recorder, A2 JSON shape).
- Schema field tables added for tick, system, environment, and statistic records; the run-record
  table plus the derived-CSV note already cover A2 compatibility and CSV derivation.
- Type names fixed for B1-2 through B1-5: `SimulationBenchmarkRunner`, `SimulationBenchmarkPlan`,
  `SimulationBenchmarkScenario`, `BenchmarkRunRecord`, `TickMetricSample`, `SystemMetricSample`,
  `EnvironmentRecord`, `BenchmarkStatistics`, `BenchmarkReportWriter`, `BootstrapStatistics`.

## Acceptance criteria

- [x] Every hook has a file:line reference.
- [x] Report fields cover A2 compatibility, CSV derivation, environment capture, and bootstrap CI.
- [x] The diagram renders.

## Validation commands

```sh
mkdir -p docs/roadmap/.validation-b1h
echo '{"args":["--no-sandbox"]}' > docs/roadmap/.validation-b1h/pp.json
npx -y @mermaid-js/mermaid-cli@latest -p docs/roadmap/.validation-b1h/pp.json -i docs/roadmap/deep-dives/b1-benchmarking.md -o docs/roadmap/.validation-b1h/card.svg
```

## Out of scope / guardrails

No code changes and no benchmark execution.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
