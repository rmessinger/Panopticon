# Task Card `WI-N1-02`: Connected-region labels and unreachable early reject

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N1-02` |
| Roadmap phase | [N1](../../track-n-pathfinding-performance.md): Search core efficiency |
| Status | `Blocked` (on `WI-N1-01`) |
| Type | `source-edit` |
| Effort | `S` |
| Prerequisites | `WI-N1-01` |
| Diagrams | [Track N architecture diagrams](../../deep-dives/n-multi-unit-pathfinding.md#8-architecture-diagrams) |

## Goal

Label connected passable regions so a request between different regions returns its empty
result without flooding the grid.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-multi-unit-pathfinding.md` sections 4 (N1) and 6.
- `src/Panopticon.Simulation/Navigation/Navigation.cs`: `ComputePath`, `GetNextPassablePosition`, `UpdateAreaPassability`.
- `src/Panopticon.Simulation/Navigation/Node.cs`: `GetAdjacentNodes`.
- The `WI-N1-01` corpus test.

## Steps

1. Add `NavigationRegions`: an `int` label per cell, computed by flood fill with the exact
   neighbour rule of `Node.GetAdjacentNodes`.
2. `UpdateAreaPassability` calls `MarkDirty()`; the next `SameRegion` query relabels.
3. In `ComputePath`, after start and end adjustment, return the empty result and count a failed
   search when the regions differ. Keep the existing warning text.
4. Extend the corpus with unreachable pairs created by placing a structure; all corpus entries
   still match.
5. Record full relabel time on the M0 grid and the unreachable case's order-tick span from
   the `PathfindingLoad` scenario, before and after, in "WI-N1 results".

## Files likely to change

- `src/Panopticon.Simulation/Navigation/NavigationRegions.cs` (new)
- `src/Panopticon.Simulation/Navigation/Navigation.cs`
- `test/Panopticon.Simulation.Tests/Navigation/NavigationRegionsTests.cs` (new), corpus tests

## Acceptance criteria

- [ ] Corpus results are identical, including diagonal-only gaps.
- [ ] Unreachable requests do not call `Graph.Search` (asserted by nodes expanded = 0).
- [ ] Labels update after `UpdateAreaPassability`.
- [ ] Relabel time and unreachable-case before/after numbers are recorded.
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

- One passability layer only; per-layer labels wait for E1.
- If relabeling costs more on the M0 grid than the floods it avoids in the baseline, stop and report.

## On completion

- Set Status above to `Done` or `Blocked` with reason; set `WI-N1-03` to `Ready`.
- Tick N1-2 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
