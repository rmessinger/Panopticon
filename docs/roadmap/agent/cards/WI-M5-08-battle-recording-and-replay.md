# Task Card `WI-M5-08`: Battle recording and replay

| Field | Value |
|---|---|
| Card ID | `WI-M5-08` |
| Roadmap phase | [M5](../../track-m-mass-battle.md): 1000 vs 1000 battle |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `M` |
| Prerequisites | `WI-M5-04`; `WI-A5-04` (A5-3 replayer) and `WI-A5-09` (run bundles), both `Blocked` |
| Diagrams | [M5 battle](../../deep-dives/mass-battle-1k.md#m5-battle) |

## Goal

Make a headless battle run write a complete run bundle and scrub it in the A5 results replayer
from start to outcome with all 2000 units shown. This adds the replay mode to the `WI-M5-04`
scenario.

Blocked: A5-3 is not implemented. Recheck `WI-A5-04` and `WI-A5-09` (and the bounded-memory
reader `WI-A5-08`), then confirm the bundle API names below before setting `Ready`.

## Context to read (and only this)

- `docs/roadmap/implementation/track-m-m5-mass-battle.md`: decision 8.
- `src/Panopticon.Simulation/Recording/RunRecording.cs`: `RunRecording.Attach`, `RecordingOptions`.
- The bundle writer from `WI-A5-09` and the replayer load path from `WI-A5-04`.
- `src/Panopticon.Tools.TestHarness/MassBattleRunner.cs`.

## Steps

1. Add a `MassBattleRecordDirectory` knob; when set, attach the A5-9 bundle writer to the
   headless run's `SimulationHost` and write one bundle per run (batch runs write one per seed).
2. Open a full-size bundle in the replayer and scrub start, middle, and outcome frames; record
   load time, resident memory, and scrub latency.
3. Add an Interactive test with a scaled roster: the bundle validates and its final frame shows
   the reported outcome and alive counts.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/MassBattleRunner.cs`
- `src/Panopticon.Tools.TestHarness/TestHarnessConfig.cs`
- `test/Panopticon.Simulation.Tests/Tools/TestHarness/MassBattleScenarioTests.cs`
- `docs/roadmap/implementation/track-m-m5-mass-battle.md`

## Acceptance criteria

- [ ] A finished full-size run scrubs from start to outcome with all 2000 units shown.
- [ ] Resident memory during the scrub stays within the `WI-A5-08` bound; the measured value is
  recorded in the M5 plan.
- [ ] Recording does not change the run's outcome or frame count.
- [ ] Standard validation passes (see [guide](README.md#standard-validation)).

## Validation commands

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" EMPIRE_EARTH_II_PATH="<ee2 install>" \
  dotnet test src --filter "FullyQualifiedName~MassBattleScenario"
dotnet build src
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No replayer or bundle-format work; A5 owns both.
- No `.rep` order-file replay: it cannot reproduce direct spawns.

## On completion

- Set Status above to `Done`.
- Tick M5-1 (all three modes) and M5-3 in the implementation checklist and update the README
  progress row.
- Append a result line to repo-root `.exchange`.
- List any follow-up cards to create here:
