# Task Card `WI-N2-02`: Group moves for squad-less selections, behind an option

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N2-02` |
| Roadmap phase | [N2](../../track-n-pathfinding-performance.md): Shared group requests |
| Status | `Ready` |
| Type | `source-edit` |
| Effort | `S` |
| Prerequisites | `WI-N2-01` |
| Diagrams | [Track N architecture diagrams](../../deep-dives/n-multi-unit-pathfinding.md#8-architecture-diagrams) |

## Goal

With `NavigationOptions.GroupSelectionMoves` on, route squad-less multi-unit ground selections
through `GroupMovement.MoveSquad` per position cluster. Off by default.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-multi-unit-pathfinding.md` section 4 (N2).
- `src/Panopticon.Simulation/Logic/Orders/OrderProcessor.cs`: `MoveUnits`.
- `src/Panopticon.Simulation/Navigation/GroupMovement.cs`, `NavigationOptions.cs`.
- `test/Panopticon.Simulation.Tests/Navigation/GroupMovementTests.cs`.

## Steps

1. Add `GroupSelectionMoves` (default false) and `SelectionClusterCells` to `NavigationOptions`;
   expose both as `PathfindingLoad` harness parameters.
2. In `MoveUnits`, when the option is on, bucket the squad-less ground units by position cell
   (cell size `SelectionClusterCells`, units ordered by object id) and call `MoveSquad` per
   bucket. Aircraft and single units keep `SetTargetPoint`.
3. Tests: option off leaves per-unit behavior and request counts unchanged; option on issues at
   most one leader search per cluster plus fallbacks.
4. Run the `PathfindingLoad` squad-less cases with the option on and off; record the 1000-unit
   order-tick span and searches per order in "WI-N2 results".

## Files likely to change

- `src/Panopticon.Simulation/Logic/Orders/OrderProcessor.cs`
- `src/Panopticon.Simulation/Navigation/NavigationOptions.cs`
- `src/Panopticon.Tools.TestHarness/TestHarnessConfig.cs`, `PathfindingLoadRunner.cs`
- `test/Panopticon.Simulation.Tests/Navigation/GroupMovementTests.cs`

## Acceptance criteria

- [ ] Option off: no behavior or count change; golden and seeded baselines untouched.
- [ ] Option on: searches per order at or under clusters plus fallback followers.
- [ ] 1000-unit order-tick span before/after is recorded against the 33.3 ms budget.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Navigation|FullyQualifiedName~PathfindingLoad"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Do not flip the default; that is an operator decision after N5.
- No formation shaping beyond offsets; no change to replay order encoding.

## On completion

- Set Status above to `Done` or `Blocked` with reason; set N2 `Done` in the track doc; set `WI-N3-01` to `Ready`.
- Tick N2-2 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
