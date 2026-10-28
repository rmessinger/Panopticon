# Task Card `WI-N15-01`: Navigation plan and layer contract

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N15-01` |
| Roadmap phase | [N15](../../track-n-pathfinding-performance.md): Layered navigation contract and baseline |
| Status | `Blocked` |
| Type | `mechanical-refactor` |
| Effort | `M` |
| Prerequisites | `WI-X6-01` (`UnitNavigation` exists) |
| Diagrams | [Track N layered navigation diagrams](../../deep-dives/n-layered-navigation.md#9-architecture-diagrams) |

## Goal

Every write to a unit's route goes through a per-unit `NavigationPlan` that records which layer produced it, with no behavior change.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-layered-navigation.md` sections 1, 3, 5 (terms and N15), and the class diagram.
- `docs/roadmap/implementation/track-x-x6-aiupdate-navigation-extraction.md`: `UnitNavigation` design.
- `src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/UnitNavigation.cs` and every writer of `TargetPoints` it lists.
- `src/Panopticon.Simulation/Navigation/GroupMovement.cs`, `BlockedMovementResolver.cs`, and the N12 splice site if merged.

## Steps

1. Add `NavigationLayer` and `NavigationPlan` (`Goal`, `Legs`, `LegIndex`, `MainPath`, `LocalTarget`, `Version`) owned by `UnitNavigation`.
2. Add `SetMainPath`, `AppendMainPath`, `SpliceLocal`, and `Clear`; make `TargetPoints` writable only through them (internal setter removed or forwarded).
3. Reroute every existing writer: order paths and async completion use `SetMainPath(..., MainPath)`; `GroupMovement` follower paths use `SetMainPath(..., LocalRoute)`; N8 detours and N12 lane points use `SpliceLocal`.
4. Add `MovementProgress.Layer`; set it wherever a stall reason is set, from the layer of the plan part involved.
5. Add `LayeredNavigation`, `GroupRouting`, and `UnitSteering` options (all default off, unused yet).

## Files likely to change

- `src/Panopticon.Simulation/Navigation/NavigationPlan.cs`, `NavigationLayer.cs` (new)
- `UnitNavigation.cs`, `MovementProgress.cs`, `NavigationOptions.cs`, `GroupMovement.cs`, `BlockedMovementResolver.cs`
- `test/Panopticon.Simulation.Tests/Navigation/NavigationPlanTests.cs` (new)

## Acceptance criteria

- [ ] `TargetPoints` has no writer outside `NavigationPlan`; its public type is read-only.
- [ ] Plan unit tests: set, append, splice, and clear keep `TargetPoints` equal to the materialized plan.
- [ ] Golden baselines and `SeededRunTests` unchanged; saves round-trip byte-identical.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Navigation"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Any planner, router, or steering logic (`WI-N16-01` onward).
- No persistence format change; the plan is rebuilt from `TargetPoints` on load.
- Options stay default off; determinism-test failures follow the suspected-deviation protocol in `AGENTS.md`.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-N15-02` to `Ready` if `WI-N6-02` is done, and `WI-N17-02` to `Ready` if `WI-N17-01` is done.
- Tick N15-1 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
