# Task Card `WI-M5-06`: Seeded batch runner

| Field | Value |
|---|---|
| Card ID | `WI-M5-06` |
| Roadmap phase | [M5](../../track-m-mass-battle.md): 1000 vs 1000 battle |
| Status | `Ready` |
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

- [ ] Every run reaches a P3 outcome or is reported as a failure with its reason.
- [ ] Wins plus draws plus failures equal runs; per-side intervals bracket the win rates.
- [ ] Re-running a seed from the batch reproduces its per-run report apart from timings.
- [ ] Standard validation passes (see [guide](README.md#standard-validation)).

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
