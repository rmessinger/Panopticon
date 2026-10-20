# Task Card `WI-M5-06`: Seeded batch runner

| Field | Value |
|---|---|
| Card ID | `WI-M5-06` |
| Roadmap phase | [M5](../../track-m-mass-battle.md): 1000 vs 1000 battle |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `M` |
| Prerequisites | `WI-M5-05` |
| Diagrams | [M5 battle](../../deep-dives/mass-battle-1k.md#m5-battle) |

## Goal

Run the headless battle over a seed range and aggregate the runs into per-side win rates with
Wilson 95 percent intervals, draws, and summed kills and losses per unit type.

## Context to read (and only this)

- `docs/roadmap/implementation/track-m-m5-mass-battle.md`: decisions 5 and 7.
- `src/Panopticon.Tools.TestHarness/MassBattleRunner.cs` and `MassBattleReport.cs`.
- `src/Panopticon.Tools.ReplaySketch.Core/Services/StatisticalRunHarness.cs`: `StatisticalRunSample`, `StatisticalRunReport`, `MetricStatistics`.
- `src/Panopticon.Tools.TestHarness/TestHarnessConfig.cs`: `MassBattle*` knobs.

## Steps

1. Add a Wilson score interval helper beside `StatisticalRunHarness` (successes, trials,
   confidence level), with tests against published reference values including 0 and n
   successes.
2. Add `MassBattleBatchRunner`: for seeds `MassBattleBatchSeedStart` to
   `MassBattleBatchSeedStart + MassBattleBatchRuns - 1`, run one headless `MassBattleRunner` run
   per seed with a fresh `Game`, keep every per-run report, and continue past a failed run
   (recorded as a failure, not a draw).
3. Aggregate into `MassBattleBatchReport`: runs, wins per side, draws, failures, win rate and
   Wilson interval per side, median frames to outcome, kills and losses per definition summed and
   per run mean, and the `StatisticalRunReport` frame and wall-clock statistics.
4. Add the `MassBattleBatch` scenario entry, knobs, catalog descriptors, and `AGENTS.md`
   command.
5. Interactive test: a 3-seed scaled batch produces a report whose counts reconcile with its
   per-run reports.

## Files likely to change

- `src/Panopticon.Tools.ReplaySketch.Core/Services/WinRateInterval.cs` (new)
- `src/Panopticon.Tools.TestHarness/MassBattleBatchRunner.cs` (new)
- `src/Panopticon.Tools.TestHarness/MassBattleBatchReport.cs` (new)
- `src/Panopticon.Tools.TestHarness/BuiltInScenario.cs`, `Program.cs`, `TestHarnessConfig.cs`, `AGENTS.md`
- `test/Panopticon.Simulation.Tests/Tools/ReplaySketch/WinRateIntervalTests.cs` (new)
- `test/Panopticon.Simulation.Tests/Tools/TestHarness/MassBattleBatchTests.cs` (new, Interactive)

## Acceptance criteria

- [x] Every run reaches a P3 outcome or is reported as a failure with its reason.
- [x] Wins plus draws plus failures equal runs; per-side intervals bracket the win rates.
- [x] Re-running a seed from the batch reproduces its per-run report apart from timings.
- [x] Standard validation passes (see [guide](README.md#standard-validation)).

## Validation commands

```sh
dotnet test src --filter "FullyQualifiedName~WinRateInterval"
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" EMPIRE_EARTH_II_PATH="<ee2 install>" \
  dotnet test src --filter "FullyQualifiedName~MassBattleBatch"
dotnet build src
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No parallel or distributed runs; one process, sequential seeds.
- No mirrored control or acceptance batch (`WI-M5-07`).

## On completion

- Set Status above to `Done`.
- Append a result line to repo-root `.exchange`.
- List any follow-up cards to create here:
  - Result: `WinRateInterval.Wilson` beside `StatisticalRunHarness` (z table for 90/95/99
    percent, exact endpoints at 0 and n successes after a sqrt-rounding epsilon was observed at
    0 wins) with install-free tests against the published 10-trial reference values (7/7).
    `MassBattleBatchRunner` runs consecutive seeds through one `MassBattleRunner` call per seed
    (fresh Game each), keeps every per-run report under `<batch>-runs/seed-<n>.json` (plus its
    benchmark and run-output siblings), continues past failures, and aggregates wins per side,
    draws (no winner), failures, Wilson 95 intervals with draws and failures in the
    denominator, median/mean/variance/min/max frames and wall minutes, and kills/losses summed
    plus per-run means. Knobs `MassBattleBatchSeedStart`/`MassBattleBatchRuns` (default 1/10)/
    `MassBattleBatchReportPath`; `MassBattleBatch` scenario entry, catalog descriptor, AGENTS.md
    section. Scaled 3-seed Interactive test reconciles the aggregate with its per-run reports
    (runs 3, wins+draws+failures == 3, intervals bracket rates, summed tables match, kills total
    preserved); per-seed reproduction is the WI-M5-04 same-seed identity already covered by
    `MassBattleScenarioTests`. Full non-interactive suite 2404 passed / 8 known failures.
