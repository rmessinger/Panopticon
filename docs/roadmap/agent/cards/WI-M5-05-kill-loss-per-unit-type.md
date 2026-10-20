# Task Card `WI-M5-05`: Kills and losses per unit type

| Field | Value |
|---|---|
| Card ID | `WI-M5-05` |
| Roadmap phase | [M5](../../track-m-mass-battle.md): 1000 vs 1000 battle |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `M` |
| Prerequisites | `WI-M5-04` |
| Diagrams | [M5 battle](../../deep-dives/mass-battle-1k.md#m5-battle) |

## Goal

Record, per side and per unit definition, how many units were lost and how many enemy units that
definition killed, and add both tables to the battle report.

## Context to read (and only this)

- `src/Panopticon.Simulation/Logic/GameLogic.cs`: internal `ObjectLifecycleChanged`;
  `GameObjectLifecycleEventArgs.cs` (reason `Died`/`Destroyed`, no killer).
- `src/Panopticon.Simulation/Logic/VictorySystem.cs`: an existing lifecycle subscriber.
- `src/Panopticon.Simulation/Logic/Object/Body/ActiveBody.cs`: private `_lastDamageInfo`
  (`Request.SourceID`).
- `src/Panopticon.Simulation/Logic/Player.cs`: `PlayerScoreManager` (unwritten kill/loss
  dictionaries; leave as is).
- `src/Panopticon.Tools.TestHarness/MassBattleReport.cs` from `WI-M5-04`.

## Steps

1. Expose the last damage source of a dead object read-only (for example
   `GameObject.LastDamageSourceId`), sourced from `ActiveBody`'s last damage info.
2. Add `KillLossTracker` in Simulation: subscribe to `ObjectLifecycleChanged`, count each unit
   death once (`Died`, else `Destroyed`), credit a loss to the victim's owner and definition and a
   kill to the killer's owner and definition when the source still resolves; unresolved killers
   count under `unknown`.
3. Attach the tracker in `MassBattleRunner` after spawn and settle; add `losses` and `kills`
   tables (side, definition, count) to `MassBattleReport`.
4. Mocked-game tests: one kill credited to the shooter's definition, a death with a despawned
   killer counted as `unknown`, structures excluded, each death counted once.

## Files likely to change

- `src/Panopticon.Simulation/Logic/Object/GameObject.cs`
- `src/Panopticon.Simulation/Logic/Object/Body/ActiveBody.cs`
- `src/Panopticon.Simulation/Logic/KillLossTracker.cs` (new)
- `src/Panopticon.Tools.TestHarness/MassBattleRunner.cs`
- `src/Panopticon.Tools.TestHarness/MassBattleReport.cs`
- `test/Panopticon.Simulation.Tests/Logic/KillLossTrackerTests.cs` (new)

## Acceptance criteria

- [x] Per side, losses summed over definitions equal spawned minus alive in the report.
- [x] Kills summed over both sides equal losses summed over both sides, `unknown` included.
- [x] Recording the tables does not change the outcome or frame count for a seed.
- [x] Standard validation passes (see [guide](README.md#standard-validation)).

## Validation commands

```sh
dotnet test src --filter "FullyQualifiedName~KillLossTracker"
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" EMPIRE_EARTH_II_PATH="<ee2 install>" \
  dotnet test src --filter "FullyQualifiedName~MassBattleScenario"
dotnet build src
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No changes to damage, death, or `PlayerScoreManager` behavior.
- No score screen or UI.

## On completion

- Set Status above to `Done`.
- Append a result line to repo-root `.exchange`.
- List any follow-up cards to create here:
  - Result: `GameObject.LastDamageSourceId` from the body's recorded damage info;
    `KillLossTracker` (internal, Logic/) subscribes `ObjectLifecycleChanged`, counts each
    death once (`Died` else `Destroyed`), and restricts losses to the tracked army (the
    spawned roster) of non-structure attackers; kills credit the resolved last damager
    (projectiles hop to their creator), otherwise `unknown`. Runner attaches the tracker after
    spawning with both sides' units; report gains `kills`/`losses` tables. Two overcount
    sources found and fixed by evidence (debug-tagged death and creation stacks): inert
    death-spawned objects (GenericDebris 60, vehicle hulks, tree stumps, projectiles) excluded
    by the attacker-class predicate, and pilot-ejection Rangers (US vehicles' `CreateObjectDie`
    spawns crew that fight and die) excluded by army membership. Mocked tests 4/4 (shooter
    credit, unknown source, projectile-to-creator, structures excluded + once-only); scaled
    CLI battle: sideA 11/11, sideB 3/3, kills == losses == 14; scaled Interactive test asserts
    the same invariants on seeds 1/2 plus the unchanged same-seed report identity; full
    non-interactive suite 2397 passed / 8 known failures unchanged.
