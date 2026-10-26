# Task Card `WI-N7-02`: Offset route validation behind an option

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N7-02` |
| Roadmap phase | [N7](../../track-n-pathfinding-performance.md): Path liveness |
| Status | `Blocked` |
| Type | `source-edit` |
| Effort | `S` |
| Prerequisites | `WI-N7-01` |
| Diagrams | [Track N deadlock diagrams](../../deep-dives/n-navigation-deadlocks.md#8-architecture-diagrams) |

## Goal

Stop followers retracing leader offsets across impassable cells, behind `ValidateOffsetRoutes`.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-navigation-deadlocks.md` section 4 (N7).
- `src/Panopticon.Simulation/Navigation/GroupMovement.cs`, `FollowerLanding.cs`, `PassabilitySnapshot.cs`.
- `src/Panopticon.Tools.TestHarness/PathfindingLoadRunner.cs` retrace counter.

## Steps

1. Add `OffsetRouteValidator.Crosses(snapshot, route)` walking each segment cell by cell with the search's diagonal rule.
2. With `ValidateOffsetRoutes` on, a follower whose offset route crosses an impassable cell lands through `FollowerLanding` on the leader route, else falls back to its own search.
3. Count rerouted followers in `PathStatistics`.
4. Re-run `squad-offset-retrace`, the PathfindingLoad squad cases, and the M4 probe; record retrace points and searches per order.

## Files likely to change

- `src/Panopticon.Simulation/Navigation/OffsetRouteValidator.cs` (new), `GroupMovement.cs`, `NavigationOptions.cs`, `PathStatistics.cs`
- `test/Panopticon.Simulation.Tests/Navigation/OffsetRouteValidatorTests.cs` (new)

## Acceptance criteria

- [ ] Option off changes nothing.
- [ ] Option on: `impassableRetracePoints` reads 0 in every case.
- [ ] Full searches per order stay within the N2 bound plus rerouted followers, recorded.
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

- A determinism-enforcing test failure is a suspected deviation: stop, keep artifacts, report.

## On completion

- Set Status above to `Done` or `Blocked` with reason; if `WI-N8-02` and `WI-N9-02` are `Done`, set `WI-N10-01` to `Ready`.
- Tick N7-2 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
