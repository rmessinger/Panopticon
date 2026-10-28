# Task Card `WI-N21-01`: Structure passability coverage

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N21-01` |
| Roadmap phase | [N21](../../track-n-pathfinding-performance.md): Structure passability coverage |
| Status | `Done` |
| Type | `implementation` |
| Effort | `S` |
| Prerequisites | `WI-N8-02`, `WI-N10-01` |
| Diagrams | [Track N deadlock diagrams](../../deep-dives/n-navigation-deadlocks.md#8-architecture-diagrams) |

## Goal

Close the through-structure holes in structure cell marking and in blocked-resolution legs, so no planned or spliced straight-line leg drives through a building footprint.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-navigation-deadlocks.md` sections 4 (N21) and the code-facts list.
- `src/Panopticon.Simulation/Navigation/Navigation.cs` (`UpdateAreaPassability`).
- `src/Panopticon.Simulation/Navigation/GiveWayPlanner.cs`, `BlockedMovementResolver.cs`.

## Steps

1. Mark cells by rectangle overlap between each node's cell square and the collider's axis-aligned bounding area, replacing node-center point containment.
2. Validate the sidestep approach and resume legs and the local-detour approach leg with a position-based `PathOptimizer.IsDirectLinePassable`.
3. Validate the squad assignment legs (offset retrace, reroute first leg, landing tail) in `GroupMovement.AssignSquadMembers`; unclear assignments reroute onto the leader route or fall back to their own search.
4. Update the marking-semantics test helpers and record the shifted `NavigationDeadlock` numbers.

## Files likely to change

- `src/Panopticon.Simulation/Navigation/Navigation.cs`
- `src/Panopticon.Simulation/Navigation/PathOptimizer.cs`
- `src/Panopticon.Simulation/Navigation/GiveWayPlanner.cs`
- `src/Panopticon.Simulation/Navigation/GroupMovement.cs`
- `src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/AIUpdate.cs`
- `src/Panopticon.Simulation/Navigation/BlockedMovementResolver.cs`
- `test/Panopticon.Simulation.Tests/Navigation/*`

## Acceptance criteria

- [x] Every cell overlapping a structure collider is marked impassable (axis-aligned and rotated).
- [x] No sidestep or local-detour leg crosses impassable cells; a unit standing on an impassable cell can still leave.
- [x] With `ValidateOffsetRoutes` on, no squad assignment leg (offset retrace, reroute first leg, landing tail) crosses impassable cells; an impassable formation slot reroutes onto the leader route instead of keeping the offset waypoints.
- [x] The squad-offset-retrace capture shows no tank entering a structure footprint.
- [x] Standard validation passes.

## Validation commands

```sh
dotnet build src -c Release
dotnet test test/Panopticon.Simulation.Tests -c Debug --filter "FullyQualifiedName~Panopticon.Tests.Navigation"
dotnet run --project src/Panopticon.Tools.TestHarness -c Release -- --built-in-scenario NavigationDeadlock --headless --n6-cases squad-offset-retrace --n6-budget 2400 --n6-blocked-resolution true --n6-validate-offsets true
```

## Out of scope / guardrails

- Follower offset retraces stay behind `ValidateOffsetRoutes` (N10 verdict unchanged).
- No locomotion-level structure gate; the grid remains the only structure barrier.
- Residual corridor and crowd deadlocks (capacity, N10 records) are not reopened.

## On completion

- Set Status above to `Done` or `Blocked` with reason; update the Track N table Status for N21.
- Append a result line to `.exchange`.
