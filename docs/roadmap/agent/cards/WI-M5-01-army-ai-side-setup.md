# Task Card `WI-M5-01`: Reusable army AI side setup

| Field | Value |
|---|---|
| Card ID | `WI-M5-01` |
| Roadmap phase | [M5](../../track-m-mass-battle.md): 1000 vs 1000 battle |
| Status | `Draft` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | M4 done (`WI-M4-03..06` merged to `main`) |
| Diagrams | [§9](../../deep-dives/mass-battle-1k.md#9-architecture-diagrams) `†` (M5 placeholder) |

## Goal

Move the per-side army AI wiring out of `MassBattleCapacityProbeRunner` into one reusable
Simulation-side setup, so a scenario attaches a hierarchical general to each side with one call.
The M5-1 battle scenario and later consumers (Track W W2) use this setup instead of copying the
probe's code. The probe keeps its scripted M1 general attack as the baseline mode.

`Draft` because M4 is not on `main` yet: confirm the types below against the merged code and the
M5 diagrams before setting `Ready`.

## Context to read (and only this)

- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeRunner.cs`: army-AI mode wiring
  (`MassBattleCapacityProbeArmyAi`): the `AIDecisionPolicyPayload` with
  `AIDecisionPolicyType.Hierarchical` on each `PlayerSetting.AIPolicy`, side players resolved as
  `Players[2 + i]` (also in `ParkAircraft`), the `DisableAI` loop that skips side players, and
  `BuildReport` casting `AIPlayer.DecisionPolicy` to `HierarchicalAIDecisionPolicy` for air
  mission counts.
- `src/Panopticon.Tools.TestHarness/MassBattleSquadInvariantSampler.cs`: resolves the same
  players and reads the internal `SquadHierarchy` of each side.
- `src/Panopticon.Simulation/Logic/AI/Hierarchy/HierarchicalAIDecisionPolicy.cs`: internal
  `Hierarchy`, `AirHierarchy`, `AirMissionAssignmentCount`, `AirMissionAssignmentCountForUnit`.
- `src/Panopticon.Simulation/Logic/PlayerSettings.cs`: `PlayerSetting` and `AIPolicy`.
- `docs/roadmap/implementation/track-m-m4-army-ai.md`: M4 decisions (probe knob, invariant block,
  air commander).

## Steps

1. Add `ArmyAiSides` under `src/Panopticon.Simulation/Logic/AI/Hierarchy/` with two calls:
   - before game start: apply a hierarchical (or caller-supplied) `AIDecisionPolicyPayload` to
     each side's `PlayerSetting`;
   - after game start: resolve each side's `Player` and `AIPlayer`, call `DisableAI` on every
     non-side player, and return one `ArmyAiSide` per side (side id, `Player`, `AIPlayer`,
     policy).
2. Resolve side players in one place. Prefer a mapping from the `PlayerSetting` to its `Player`;
   if the engine has none, keep the `2 + i` offset behind a named constant in `ArmyAiSides`.
   `ParkAircraft` and the sampler use the resolved players instead of their own offset.
3. Add a public read-only stats view on `HierarchicalAIDecisionPolicy` covering what reports
   and the sampler read today: ground squad count and members, commanderless squads, air
   mission assignment counts per mission, and per-unit mission count. Replace the
   `HierarchicalAIDecisionPolicy` casts and internal `SquadHierarchy` reads in the probe report
   and `MassBattleSquadInvariantSampler` with that view.
4. Switch the probe's army-AI mode to `ArmyAiSides`. Scripted mode (`IssueGeneralAttack`,
   `RefreshOrders`) is unchanged and stays the default.
5. Add install-free tests for `ArmyAiSides` on the mocked game: policy applied per side, non-side
   players disabled, side order preserved, stats view matches the hierarchy.

## Files likely to change

- `src/Panopticon.Simulation/Logic/AI/Hierarchy/ArmyAiSides.cs` (new, with `ArmyAiSide`)
- `src/Panopticon.Simulation/Logic/AI/Hierarchy/HierarchicalAIDecisionPolicy.cs`
- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeRunner.cs`
- `src/Panopticon.Tools.TestHarness/MassBattleSquadInvariantSampler.cs`
- `test/Panopticon.Simulation.Tests/Logic/AI/Hierarchy/ArmyAiSidesTests.cs` (new)

## Acceptance criteria

- [ ] A scenario attaches a hierarchical general to every side with `ArmyAiSides` and no
  per-side policy, player-index, or `DisableAI` code of its own.
- [ ] No code outside `HierarchicalAIDecisionPolicy` casts to it or reads its internal
  hierarchies for reporting.
- [ ] `Players[2 + i]` appears only inside `ArmyAiSides`, or not at all.
- [ ] Army-AI probe report fields other than timings match the pre-refactor run on the same
  seed; scripted-mode reports are unchanged.
- [ ] Standard validation passes (see [guide](README.md#standard-validation)).

## Validation commands

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
  dotnet run --project src/Panopticon.Tools.TestHarness -- \
  --headless --built-in-scenario MassBattleCapacityProbe   # with MassBattleCapacityProbeArmyAi on and off
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
  dotnet test src --filter "FullyQualifiedName~MassBattleCapacityProbe|FullyQualifiedName~ArmyAiSides|FullyQualifiedName~Hierarchy"
dotnet build src
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No change to general, commander, soldier, or air commander decisions.
- No removal of the scripted M1 general attack.
- No M5-1 battle scenario; this card only supplies the setup it uses.
- A report diff on the same seed is a suspected determinism deviation: stop and report it.

## On completion

- Set Status above to `Done`.
- Append a result line to repo-root `.exchange`.
- List any follow-up cards to create here:
