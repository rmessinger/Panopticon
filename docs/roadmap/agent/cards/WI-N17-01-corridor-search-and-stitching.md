# Task Card `WI-N17-01`: Masked corridor search and segment stitching

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N17-01` |
| Roadmap phase | [N17](../../track-n-pathfinding-performance.md): Corridor main path |
| Status | `Blocked` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-N16-02` |
| Diagrams | [Track N layered navigation diagrams](../../deep-dives/n-layered-navigation.md#9-architecture-diagrams) |

## Goal

A corridor planner produces the reachable main path through the next strategic legs using a masked `Graph.Search`, with near-optimal length on the N1 corpus.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-layered-navigation.md` sections 4 and 5 (N17).
- `src/Panopticon.Simulation/Navigation/Graph.cs` (`Search`), `PathOptimizer.cs`, `StrategicPlanner.cs`.

## Steps

1. Add `Graph.Search(start, end, mask)`: identical A* that skips nodes outside the mask; the unmasked overload calls it with no mask and stays byte-identical.
2. Add `CorridorPlanner.Plan(route, legIndex, lookahead)`: corridor = sectors of the current position and the next `MainPathLookaheadLegs` (default 2) legs; search to the last lookahead transition or the goal; smooth with line-of-sight checks restricted to the mask.
3. Add `Extend`: plan from the current segment end and re-smooth the seam over its last two waypoints.
4. Fallback: on a failed corridor search, replan the strategic route once, then use the full-map search; count `CorridorFallbacks`.
5. Corpus test: stitched full route length vs optimal, and full-grid nodes expanded vs N1 numbers.

## Files likely to change

- `Graph.cs`, `PathOptimizer.cs`, `NavigationOptions.cs`, `PathStatistics.cs`
- `src/Panopticon.Simulation/Navigation/CorridorPlanner.cs` (new)
- `test/Panopticon.Simulation.Tests/Navigation/CorridorPlannerTests.cs` (new)

## Acceptance criteria

- [ ] Unmasked `Search` returns identical waypoints on the N1 corpus.
- [ ] Stitched length ratio median at most 1.05 and p99 at most 1.20, or the miss recorded with numbers.
- [ ] Every stitched route is passable cell to cell.
- [ ] Movement unchanged.
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

- Using the planner for unit orders (`WI-N17-02`).
- Options stay default off; determinism-test failures follow the suspected-deviation protocol in `AGENTS.md`.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-N17-02` to `Ready` if `WI-N15-01` is done.
- Tick N17-1 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
