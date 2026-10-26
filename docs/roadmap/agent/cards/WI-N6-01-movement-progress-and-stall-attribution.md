# Task Card `WI-N6-01`: Movement progress and stall attribution

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N6-01` |
| Roadmap phase | [N6](../../track-n-pathfinding-performance.md): Deadlock detection baseline |
| Status | `Ready` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-N5-01` |
| Diagrams | [Track N deadlock diagrams](../../deep-dives/n-navigation-deadlocks.md#8-architecture-diagrams) |

## Goal

Record per-unit movement progress, the blocking object, and a stall reason in the simulation, and report per-unit stalls and wait-for cycles from the TestHarness. No movement changes.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-navigation-deadlocks.md` sections 1, 4 (N6), and 8.
- `src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/AIUpdate.cs` (waypoint advance, async callback).
- `src/Panopticon.Simulation/Logic/Object/Locomotor.cs`, `Logic/PartitionCellManager.cs`, `Logic/GroundFootprint.cs`.
- `src/Panopticon.Tools.TestHarness/MassBattleSquadInvariantSampler.cs`.
- `test/Panopticon.Simulation.Tests/Logic/GroundFootprintTests.cs`.

## Steps

1. Add an `out ObjectId blocker` overload of `PartitionCellManager.GroundFootprintFits` returning the first blocking object; thread it out of `Locomotor.LocoUpdateMoveTowardsPosition`.
2. Add `MovementProgress` and `NavigationStallReason` (deep dive section 8). `AIUpdate` updates them each move step: progress when the distance to `TargetPoints[0]` shrinks by `ProgressEpsilon`; `BlockedByUnit` with blocker ID; `AwaitingPath` while a pending handle exists; `NoRoute` when an order produced an empty route.
3. Add TestHarness `NavigationStallSampler`: per-unit records (ID, definition, reason, blocker, frames without progress, order destination) and wait-for cycles over blocker IDs.
4. Switch `MassBattleSquadInvariantSampler` stall counting to the progress rule (no progress while holding an order, or no route, while enemies remain); add per-unit stall records to the report.
5. Run the M4 army-AI probe (seed 1, 4500 frames) with options off and with `AsyncOrderPaths` on; record stall counts by reason and attribute the units behind the known idle-stall failure in "Navigation deadlocks results" of the plan.

## Files likely to change

- `src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/AIUpdate.cs`, `Locomotor.cs`, `PartitionCellManager.cs`
- `src/Panopticon.Simulation/Navigation/MovementProgress.cs` (new, with `NavigationStallReason`)
- `src/Panopticon.Tools.TestHarness/NavigationStallSampler.cs` (new), `MassBattleSquadInvariantSampler.cs`
- `test/Panopticon.Simulation.Tests/Navigation/MovementProgressTests.cs` (new)

## Acceptance criteria

- [ ] Install-free tests: a head-on pair reports `BlockedByUnit` with each other as blocker and one wait-for cycle; a unit following a moving leader keeps progressing; a failed path reports `NoRoute`.
- [ ] Movement is unchanged: existing navigation and footprint tests pass without edits.
- [ ] The M4 report carries per-unit stall records with reasons; the known stall units are attributed.
- [ ] Baseline numbers are recorded; a rise in the M4 stall count is labelled as the new baseline, not tuned away.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Navigation|FullyQualifiedName~GroundFootprint"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No resolution behavior (N7 to N9).
- Do not change `StallThresholdFrames`.
- A determinism-enforcing test failure is a suspected deviation: stop, keep artifacts, report.

## On completion

- Set Status above to `Done` or `Blocked` with reason; set `WI-N6-02` to `Ready`.
- Tick N6-1 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
