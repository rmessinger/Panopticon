# Task Card `WI-N11-01`: Passage finder and cross-section cache

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N11-01` |
| Roadmap phase | [N11](../../track-n-pathfinding-performance.md): Passage detection and baseline |
| Status | `Done` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | None (N5 done) |
| Diagrams | [Track N passage diagrams](../../deep-dives/n-confined-passage-queueing.md#8-architecture-diagrams) |

## Goal

A pure `PassageFinder` returns the narrow passages a route crosses, with throat, width, lane count, direction, and corridor, from the passability grid alone.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-confined-passage-queueing.md` sections 1, 4 (terms and N11), and the class diagram.
- `src/Panopticon.Simulation/Navigation/Graph.cs`, `Node.cs`, `PathOptimizer.cs` (Bresenham walk), `Navigation.cs` (`GetNodeAt`, `PassabilityVersion`).

## Steps

1. Add `PassageSpan` and `CrossSectionCache` (width per cell and direction octant, reset on a new `PassabilityVersion`).
2. Add `PassageFinder.FindSpans(graph, route, pitch, maxWidthCells, cache)`: sample the route once per cell, cast two perpendicular rays capped at `maxWidthCells`, keep samples closed on both sides and narrower than `PassageLaneLimit` pitches, merge samples within `PassageMergeCells`, and pick the narrowest sample as throat. Lanes are `max(1, floor(width / pitch))`.
3. Add `PassageLaneLimit` (3), `MaxPassageWidthCells` (16), and `PassageMergeCells` (2) to `NavigationOptions`.
4. Add a debug counter for casts and cache hits to `PathStatistics`.

## Files likely to change

- `src/Panopticon.Simulation/Navigation/PassageFinder.cs`, `PassageSpan.cs`, `CrossSectionCache.cs` (new), `NavigationOptions.cs`, `PathStatistics.cs`
- `test/Panopticon.Simulation.Tests/Navigation/PassageFinderTests.cs` (new)

## Acceptance criteria

- [ ] Install-free synthetic grids: a straight gap, a diagonal gap, and an L-shaped corridor each return one span with the expected throat and lane count.
- [ ] A route along a one-sided wall or cliff returns no span.
- [ ] Two routes through the same gap at different offsets produce throats inside each other's corridors.
- [ ] A second scan of the same route makes no new casts; a passability change resets the cache.
- [ ] No movement code calls the finder yet; existing tests unchanged.
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

- Queues, holds, and any movement change (`WI-N12-01`, `WI-N12-02`).
- No precomputed map-wide choke graph and no change to `Graph.Search`.

## On completion

- Set Status above to `Done` or `Blocked` with reason; set `WI-N12-01` to `Ready` if `WI-N6-01` is done.
- Tick N11-1 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
