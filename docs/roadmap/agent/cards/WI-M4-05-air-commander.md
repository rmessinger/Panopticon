# Task Card `WI-M4-05`: Air commander

| Field | Value |
|---|---|
| Card ID | `WI-M4-05` |
| Roadmap phase | [M4](../../track-m-mass-battle.md): army-scale AI |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `M` |
| Prerequisites | `WI-M4-03` |
| Diagrams | [M4 army hierarchy](../../deep-dives/mass-battle-1k.md#m4-army-hierarchy) |

## Goal

Aircraft form their own squads under an air commander that assigns strike, patrol, and escort missions and re-tasks aircraft after they return to their airfield to rearm, replacing the M1 scripted sortie loop (M4-1, air half).

## Context to read (and only this)

- `docs/roadmap/implementation/track-m-m4-army-ai.md` (aircraft split decision).
- `docs/roadmap/deep-dives/mass-battle-1k.md` section 6.
- `src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/JetAIUpdate.cs`: jet states; out-of-ammo return to base, parked state.
- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeRunner.cs`: jet parking wiring, re-issue on park, sortie tracking.
- `src/Panopticon.Simulation/Logic/AI/Hierarchy/ICommanderDecisionPolicy.cs`, `CommanderDirective.cs`, `SoldierGoal.cs`.

## Steps

1. Group aircraft into air squads (per WI-M4-01) and route them to an air commander instead of the ground commander.
2. Define three missions as commander goals: strike (attack the general's target or nearest enemy ground squad), patrol (move between authored points over friendly squads), escort (follow a friendly ground squad's centroid). Assign by a documented deterministic rule.
3. Re-task parked aircraft: a squad member in the parked state with a refilled clip receives its mission again while enemies remain; rearm itself stays the airfield's existing park, heal, re-clip cycle.
4. Remove the M1 scripted jet re-issue in army-AI mode; keep it for the plain capacity probe.
5. Report per jet: missions assigned, departures, recoveries; per mission type: count.
6. Tests: install-free mission-assignment test; scaled Interactive run asserting each mission type occurs and at least one jet departs again after recovery.

## Files likely to change

- `src/Panopticon.Simulation/Logic/AI/Hierarchy/` (air commander policy, new file)
- `src/Panopticon.Simulation/Logic/AI/Hierarchy/HierarchicalAIDecisionPolicy.cs`
- `src/Panopticon.Simulation/Logic/AI/Hierarchy/SquadHierarchy.cs`
- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeRunner.cs`
- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeReport.cs`
- `test/Panopticon.Simulation.Tests/Logic/AI/Hierarchy/AirCommanderTests.cs` (new)
- `test/Panopticon.Simulation.Tests/Tools/TestHarness/MassBattleCapacityProbeScenarioTests.cs`

## Acceptance criteria

- [ ] Every aircraft is assigned to an air squad at start and after every casualty.
- [ ] Strike, patrol, and escort missions each occur in the scaled run.
- [ ] Aircraft return to rearm and re-sortie under AI control (recoveries and second departures in the report).
- [ ] Standard validation passes (see [guide](README.md#standard-validation)).

## Validation commands

```sh
dotnet test src --filter "FullyQualifiedName~AirCommander|FullyQualifiedName~Hierarchy"
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
  dotnet test src --filter "FullyQualifiedName~MassBattleCapacityProbe"
dotnet build src
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No new rearm mechanic; the airfield provides it.
- Generals jets only; EE2 aircraft join at M5 through the same squads.

## On completion

- Set Status above to `Done`.
- Append a result line to repo-root `.exchange`.
- List any follow-up cards to create here:
  - none expected.
