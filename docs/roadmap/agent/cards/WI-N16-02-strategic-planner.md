# Task Card `WI-N16-02`: Strategic planner and route-quality corpus

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N16-02` |
| Roadmap phase | [N16](../../track-n-pathfinding-performance.md): Strategic sector graph |
| Status | `Blocked` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-N16-01` |
| Diagrams | [Track N layered navigation diagrams](../../deep-dives/n-layered-navigation.md#9-architecture-diagrams) |

## Goal

`StrategicPlanner.Plan(start, goal)` returns portal legs over the sector graph, measured for cost and quality against full-grid A* on the N1 corpus.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-layered-navigation.md` sections 4 and 5 (N16).
- `src/Panopticon.Simulation/Navigation/SectorGraph.cs` and its tests.
- The N1 start/end corpus test (`test/Panopticon.Simulation.Tests/Navigation/`, search-equivalence corpus from `WI-N1-01`).

## Steps

1. Add `StrategicRoute` and `StrategicPlanner`.
2. Plan: region check first; same sector and component returns zero legs; otherwise link temporary start and goal nodes to their component's transitions by sector-masked searches, then A* over the abstract graph with an octile heuristic.
3. Reuse scratch collections; no per-query allocation in steady state.
4. Add a corpus test that reports abstract nodes expanded vs full-grid nodes expanded and the length ratio of the portal polyline vs the optimal path.

## Files likely to change

- `src/Panopticon.Simulation/Navigation/StrategicPlanner.cs`, `StrategicRoute.cs` (new)
- `test/Panopticon.Simulation.Tests/Navigation/StrategicPlannerTests.cs` (new)
- `docs/roadmap/implementation/track-n-pathfinding-performance.md` (results)

## Acceptance criteria

- [ ] A route exists exactly when `SameRegion` holds, over the corpus.
- [ ] Abstract nodes expanded at most 5% of full-grid nodes expanded for cross-map pairs, or the miss recorded with numbers.
- [ ] Zero allocation per query in steady state.
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

- Corridor search and any movement change (`WI-N17-01`).
- Options stay default off; determinism-test failures follow the suspected-deviation protocol in `AGENTS.md`.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-N17-01` to `Ready`.
- Tick N16-2 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
