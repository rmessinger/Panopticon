# Task Card `WI-M5-04`: Mass battle scenario

| Field | Value |
|---|---|
| Card ID | `WI-M5-04` |
| Roadmap phase | [M5](../../track-m-mass-battle.md): 1000 vs 1000 battle |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `M` |
| Prerequisites | `WI-M5-01`, `WI-M5-02`, `WI-M5-03` |
| Diagrams | [M5 battle](../../deep-dives/mass-battle-1k.md#m5-battle) |

## Goal

Add the `MassBattle` TestHarness scenario: the roster profile from `WI-M5-02`, army AI on both
sides from `WI-M5-01`, a seeded run to a P3 outcome from `WI-M5-03`, headless through
`SimulationHost` and windowed through `HarnessWindowedFrame`, from one profile.

## Context to read (and only this)

- `docs/roadmap/implementation/track-m-m5-mass-battle.md`: decisions 1 to 3.
- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeRunner.cs`: boot, settle, windowed
  step, report writing, B1 record.
- `src/Panopticon.Tools.ReplaySketch.Core/Services/HeadlessHierarchicalMatchEvaluator.cs`:
  `SimulationHost` plus `RunOutputRecorder` to a decided outcome.
- `src/Panopticon.Simulation/SimulationHost.cs`, `src/Panopticon.Simulation/RunOutput.cs`.
- `src/Panopticon.Tools.TestHarness/BuiltInScenario.cs`, `Program.cs` dispatch,
  `TestHarnessConfig.cs` `MassBattle*` knobs.

## Steps

1. Add `MassBattleRunner`: load the battle profile, boot with hostile teams and the run seed,
   import and spawn through `WI-M5-02`, attach `ArmyAiSides`, select the army-eliminated rule and
   the frame limit, settle, then run.
2. Headless: `SimulationHost.Run(frameLimit, outcome Decided)` with `RunOutputRecorder` and the
   B1 recorder. Windowed: the probe's `HarnessWindowedFrame` loop with the same stop condition.
3. Knobs: `MassBattleProfilePath`, `MassBattleSeed` (default 1), `MassBattleFrameLimit` (default
   54000), `MassBattleReportPath`, `MassBattleScreenshotPath`.
4. Write `MassBattleReport` JSON: profile, seed, mode, frames run, P3 outcome (status, reason,
   winning side id, decided frame), per-side spawned and alive per definition, B1 record path.
5. Add `MassBattle` to `BuiltInScenario`, `Program` dispatch, the catalog descriptors from
   `WI-P0-21`/`WI-P0-22`, and the TestHarness `AGENTS.md` commands.
6. Interactive test with a scaled roster: both seeds 1 and 2 reach `Decided`; same seed twice
   gives an identical report apart from timings.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/MassBattleRunner.cs` (new)
- `src/Panopticon.Tools.TestHarness/MassBattleReport.cs` (new)
- `src/Panopticon.Tools.TestHarness/BuiltInScenario.cs`
- `src/Panopticon.Tools.TestHarness/Program.cs`
- `src/Panopticon.Tools.TestHarness/TestHarnessConfig.cs`
- `src/Panopticon.Tools.TestHarness/AGENTS.md`
- `test/Panopticon.Simulation.Tests/Tools/TestHarness/MassBattleScenarioTests.cs` (new, Interactive)

## Acceptance criteria

- [x] One profile runs headless and windowed with no code change between modes.
- [x] A full-size headless run reaches a P3 `Decided` outcome; the report records it.
- [x] Same seed, same report apart from timings (a mismatch is a suspected determinism
  deviation: stop and report).
- [x] No scripted orders: every unit order comes from the hierarchy.
- [x] Standard validation passes (see [guide](README.md#standard-validation)).

## Validation commands

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" EMPIRE_EARTH_II_PATH="<ee2 install>" \
  dotnet run --project src/Panopticon.Tools.TestHarness -- --headless --built-in-scenario MassBattle
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" EMPIRE_EARTH_II_PATH="<ee2 install>" \
  dotnet test src --filter "FullyQualifiedName~MassBattleScenario"
dotnet build src
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No batches, statistics, or kill accounting (`WI-M5-05..07`).
- No recording or replay (`WI-M5-08`); no spectator (`WI-M5-09`).
- No AI or engine tuning to shorten runs; record the run length.

## On completion

- Set Status above to `Done`.
- Append a result line to repo-root `.exchange`.
- List any follow-up cards to create here:
  - Result: `MassBattleRunner` + `MassBattleReport` (timing-free fields; same seed reproduces
    byte-identical JSON), knobs `MassBattleProfilePath/Seed/FrameLimit/ReportPath/ScreenshotPath`
    (defaults 1 and 54000), `MassBattle` in `BuiltInScenario` + `Program` dispatch + catalog
    descriptor + TestHarness AGENTS.md. Headless steps `SimulationHost` with `RunOutputRecorder`
    (sibling `.run-output.json`) and a B1 benchmark record; windowed steps the probe's
    `HarnessWindowedFrame` loop with the same stop condition. Full-scale headless run (seed 1,
    both installs): Decided/ScoreDecision, sideB (ee2-led) wins at the 54000-frame limit,
    sideA ground annihilated with 20 parked Raptors alive (the authored army-eliminated
    semantics), 53970 frames in 148.7 min wall (165 ms/frame), report allPassed, zero failure
    reasons. Scaled Interactive test: seeds 1 and 2 decide, seed-1 repeat report identical
    after path normalization. Engine fixes the battle exposed (formation offsets can push
    destinations off the nav graph, which is smaller than the heightmap border band):
    `Navigation.GetClosestNode` uses `Graph.TryGetNode`, `IsPassable` is null-safe (off-graph is
    impassable), `ComputePath` null-checks endpoints before dereferencing,
    `GetNextPassablePosition` stops on null nodes; navigation tests 17/17 unchanged.
  - Windowed caveat: the windowed driver shares every setup step with headless (only the step
    driver branch differs) but cannot execute on the agent host: Xvfb presenting is a documented
    not-working path (docs/screenshots.md). Windowed validation is the operator run, as with the
    M1 probe; WI-M5-09/10 own the live measurements.
