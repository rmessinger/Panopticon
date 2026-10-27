# Task Card `WI-N9-01`: Destination slots behind an option

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N9-01` |
| Roadmap phase | [N9](../../track-n-pathfinding-performance.md): Destination crowding |
| Status | `Done` |
| Type | `source-edit` |
| Effort | `S` |
| Prerequisites | `WI-N6-02` |
| Diagrams | [Track N deadlock diagrams](../../deep-dives/n-navigation-deadlocks.md#8-architecture-diagrams) |

## Goal

Behind `DestinationSlots`, give every unit of a squad-less or cluster selection its own goal instead of one shared point.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-navigation-deadlocks.md` section 4 (N9).
- `src/Panopticon.Simulation/Logic/Orders/OrderProcessor.cs` (`MoveUnits`, cluster moves).
- `src/Panopticon.Simulation/Navigation/NavigationRegions.cs`, `GroupMovement.cs`.
- `src/Panopticon.Simulation/GameData.cs` (`MaxCellsAdjustDestination`).

## Steps

1. Add `DestinationSlots.Assign(regions, destination, units, maxCells)`: rings outward by footprint radius, passable cells in the destination's region, deterministic order by distance then `ObjectId`.
2. Use it in `MoveUnits` for squad-less units and cluster members when the option is on; squads keep formation offsets.
3. Consume `GameData.MaxCellsAdjustDestination` as the bound; past it, fall back to the shared point.
4. Run `crowd-goal-100` and the PathfindingLoad squad-less cases; record settle frames and searches per order.

## Files likely to change

- `src/Panopticon.Simulation/Navigation/DestinationSlots.cs` (new), `NavigationOptions.cs`
- `src/Panopticon.Simulation/Logic/Orders/OrderProcessor.cs`
- `test/Panopticon.Simulation.Tests/Navigation/DestinationSlotsTests.cs` (new)

## Acceptance criteria

- [ ] Option off changes nothing.
- [ ] Slots are unique, passable, in-region, and deterministic in tests.
- [ ] Searches per order stay within the N2 bound, recorded.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Navigation|FullyQualifiedName~Orders"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Crowd arrival (`WI-N9-02`).
- A determinism-enforcing test failure is a suspected deviation: stop, keep artifacts, report.

## On completion

- Set Status above to `Done` or `Blocked` with reason; set `WI-N9-02` to `Ready`.
- Tick N9-1 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
