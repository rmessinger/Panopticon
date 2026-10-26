# Task Card `WI-N8-01`: Blocked wait and bounded local repath

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N8-01` |
| Roadmap phase | [N8](../../track-n-pathfinding-performance.md): Blocked-unit resolution |
| Status | `Ready` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-N6-02` |
| Diagrams | [Track N deadlock diagrams](../../deep-dives/n-navigation-deadlocks.md#8-architecture-diagrams) |

## Goal

Behind `BlockedUnitResolution`, a unit blocked past `BlockedWaitFrames` by a blocker that is not progressing repaths around it inside a bounded window.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-navigation-deadlocks.md` section 4 (N8) and the blocked-unit sequence.
- `AIUpdate.cs` waypoint advance, `MovementProgress`.
- `src/Panopticon.Simulation/Navigation/Navigation.cs`, `Graph.cs`, `PassabilitySnapshot.cs`, `PathStatistics.cs`.

## Steps

1. Add `BlockedMovementResolver` with the steps wait, local repath, give up (give-way is `WI-N8-02`). A progressing blocker resets the counter.
2. Add `LocalOccupancyWindow` marking stationary units' footprints over the snapshot within `LocalRepathWindowCells` (default 12).
3. Add `Navigation.CalculateLocalPath(start, rejoin, window)`: rejoin is the first route waypoint outside the window; origin `PathRequestOrigin.BlockedRepath`; expansion bounded by the window.
4. After `MaxResolutionAttempts` (default 3), settle as `GoalCrowded` inside `CrowdArrivalRadius`, else keep the route and stay reported.
5. Run `queue-behind-idle`, `corridor-opposed-20`, and the M4 probe with the option on; record settle frames, repaths per tick, and the `path` span.

## Files likely to change

- `src/Panopticon.Simulation/Navigation/BlockedMovementResolver.cs`, `LocalOccupancyWindow.cs` (new), `Navigation.cs`, `NavigationOptions.cs`, `PathStatistics.cs`
- `AIUpdate.cs`
- `test/Panopticon.Simulation.Tests/Navigation/BlockedMovementResolverTests.cs` (new)

## Acceptance criteria

- [ ] Option off changes nothing.
- [ ] An install-free test unit behind a stationary enemy reaches its goal by local repath.
- [ ] Local repath expansion never exceeds (2W+1)^2 cells.
- [ ] N5 path budgets are still met with the option on, recorded.
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

- Give-way and mutual blocks (`WI-N8-02`).
- No continuous avoidance or push-apart physics.
- A determinism-enforcing test failure is a suspected deviation: stop, keep artifacts, report.

## On completion

- Set Status above to `Done` or `Blocked` with reason; set `WI-N8-02` to `Ready`.
- Tick N8-1 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
