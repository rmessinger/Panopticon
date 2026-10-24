# Task Card `WI-N1-01`: Allocation-free search scratch with a path-equality corpus

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N1-01` |
| Roadmap phase | [N1](../../track-n-pathfinding-performance.md): Search core efficiency |
| Status | `Done` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-N0-02` |
| Diagrams | [Track N architecture diagrams](../../deep-dives/n-multi-unit-pathfinding.md#8-architecture-diagrams) |

## Goal

Replace the `HashSet<Node>` visited set and per-call route and waypoint allocations with
reusable scratch, proven by a corpus that returns identical waypoints before and after.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-multi-unit-pathfinding.md` section 4 (N1) and 6.
- `src/Panopticon.Simulation/Navigation/Graph.cs`, `Node.cs`, `Navigation.cs`, `PathOptimizer.cs`.
- `test/Panopticon.Simulation.Tests/Navigation/GraphTest.cs`, `PathOptimizerTest.cs`.
- "WI-N0-02 baseline" in `docs/roadmap/implementation/track-n-pathfinding-performance.md`.

## Steps

1. Corpus first, before any change: generate seeded start/end pairs on the M0 map grid
   (passable, impassable, diagonal-only gaps, unreachable) and store their current waypoint
   lists as a test fixture under `test/Panopticon.Simulation.Tests/Navigation/`.
2. Add a search generation to `Graph` and `Node`; visited is `node.SearchGeneration == current`.
   Remove `_visited`. Keep the priority queue and tie order exactly as today.
3. Let `ComputePath` write into a caller-supplied `List<Vector3>`; the public `CalculatePath`
   keeps its signature and allocates only the returned list. Reuse the route list inside `Graph`.
4. Micro-benchmark search over the corpus before and after: mean time and bytes allocated per
   search.
5. Record results in "WI-N1 results".

## Files likely to change

- `src/Panopticon.Simulation/Navigation/Graph.cs`, `Node.cs`, `Navigation.cs`
- `test/Panopticon.Simulation.Tests/Navigation/PathEqualityCorpusTests.cs` and fixture (new)

## Acceptance criteria

- [ ] The corpus returns identical waypoints after the change.
- [ ] Steady-state allocation per internal search is zero apart from the returned list.
- [ ] Before/after per-search time is recorded.
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

- No heuristic, cost, or neighbour change; no region labels (`WI-N1-02`).
- A corpus mismatch means the change is wrong; do not regenerate the fixture to make it pass.

## On completion

- Set Status above to `Done` or `Blocked` with reason; set `WI-N1-02` to `Ready`.
- Tick N1-1 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
