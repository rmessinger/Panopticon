# Task Card `WI-N16-01`: Sector graph with incremental rebuild

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N16-01` |
| Roadmap phase | [N16](../../track-n-pathfinding-performance.md): Strategic sector graph |
| Status | `Ready` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | None (N1 done; pure, no movement change) |
| Diagrams | [Track N layered navigation diagrams](../../deep-dives/n-layered-navigation.md#9-architecture-diagrams) |

## Goal

A `SectorGraph` of sectors, components, and portals over the passability grid, with exact intra-sector edge costs and rebuilds limited to sectors a structure touches.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-layered-navigation.md` sections 3, 5 (terms and N16), 7, and the class and structure-placed diagrams.
- `src/Panopticon.Simulation/Navigation/Graph.cs`, `Node.cs` (`GetAdjacentNodes`), `NavigationRegions.cs`, `Navigation.cs` (`UpdateAreaPassability`, `PassabilityVersion`).

## Steps

1. Add `SectorGraph`, `SectorComponent`, and `Portal`. Sectors are `SectorSizeCells` squares (default 16, edge sectors smaller).
2. Components: flood fill restricted to the sector with the `GetAdjacentNodes` rule.
3. Portals: maximal runs of passable cell pairs across each shared border, split at `MaxPortalCells` (default 8), with one transition cell per side at the run midpoint; diagonal-only corner links become single-cell portals.
4. Intra-sector edges: exact cost between transitions of one component via a search masked to the sector; cache per sector.
5. `MarkDirty(cellRect)` from `UpdateAreaPassability`; lazy rebuild of dirty sectors and their border portals on the next query; bump `Version`.
6. Add `SectorSizeCells` and `MaxPortalCells` options; add rebuild count and time to `PathStatistics`.

## Files likely to change

- `src/Panopticon.Simulation/Navigation/SectorGraph.cs`, `SectorComponent.cs`, `Portal.cs` (new)
- `Navigation.cs`, `NavigationOptions.cs`, `PathStatistics.cs`
- `test/Panopticon.Simulation.Tests/Navigation/SectorGraphTests.cs` (new)

## Acceptance criteria

- [ ] Install-free synthetic grids: two sectors joined by one gap give one portal; a sector split by a wall gives two components; a diagonal corner link gives a single-cell portal.
- [ ] For every portal pair in one component, edge cost equals a full `Graph.Search` restricted to the sector.
- [ ] Abstract connectivity agrees with `NavigationRegions.SameRegion` on a random corpus of cell pairs.
- [ ] Marking one structure footprint dirty rebuilds only the sectors it overlaps and their neighbors' shared portals; rebuild time recorded for the largest shipped map.
- [ ] No movement code uses the graph yet; existing tests unchanged.
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

- The strategic planner (`WI-N16-02`).
- Per-layer graphs for bridges and water (E1).
- Options stay default off; determinism-test failures follow the suspected-deviation protocol in `AGENTS.md`.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-N16-02` to `Ready`.
- Tick N16-1 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
