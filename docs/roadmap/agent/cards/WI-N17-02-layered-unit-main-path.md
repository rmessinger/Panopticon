# Task Card `WI-N17-02`: Layered unit main path behind `LayeredNavigation`

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N17-02` |
| Roadmap phase | [N17](../../track-n-pathfinding-performance.md): Corridor main path |
| Status | `Blocked` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-N17-01`, `WI-N15-01` |
| Diagrams | [Track N layered navigation diagrams](../../deep-dives/n-layered-navigation.md#9-architecture-diagrams) |

## Goal

With `LayeredNavigation` on, unit order paths come from strategic legs plus a corridor main path that extends ahead of the unit and replans only affected legs after passability changes.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-layered-navigation.md` sections 3, 5 (N17), 7, and both sequence diagrams.
- `NavigationPlan.cs`, `UnitNavigation.cs`, `CorridorPlanner.cs`, `StrategicPlanner.cs`, `PathRequestQueue.cs`.

## Steps

1. With the option on, order paths call `StrategicPlanner.Plan` then `CorridorPlanner.Plan` and store legs and main path in `NavigationPlan`.
2. Extension: when the remaining main path is under `ReplanMarginCells` (default 8) and legs remain, extend; with `AsyncOrderPaths` on, submit the extension to the async queue early enough to land before the margin runs out.
3. On `SectorGraph.Version` change: replan strategic legs from the current leg when a dirty sector lies on a remaining leg; replan only the current segment when it lies only in the corridor; otherwise keep the plan.
4. Async snapshots carry the sector graph version so stale completions are discarded.
5. Run `layered-cross-map-8x12`, `layered-detour-wall`, `layered-structure-placed`, and the N0 cost cases with the option on and off.

## Files likely to change

- `UnitNavigation.cs`, `NavigationPlan.cs`, `PathRequestQueue.cs`, `PassabilitySnapshot.cs`, `Navigation.cs`
- `test/Panopticon.Simulation.Tests/Navigation/LayeredMainPathTests.cs` (new)
- `docs/roadmap/implementation/track-n-pathfinding-performance.md` (results)

## Acceptance criteria

- [ ] Option off: golden baselines and `SeededRunTests` unchanged.
- [ ] Option on: 0 seam gaps in layered cases; full-grid nodes expanded per order down at least 80% in the 1000-unit selection case, or the miss recorded.
- [ ] `layered-structure-placed` replans only units whose remaining legs or corridor touch the structure.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Navigation"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario NavigationDeadlock --headless
```

## Out of scope / guardrails

- Group routing (`WI-N18-01`) and steering (`WI-N19-02`).
- Options stay default off; determinism-test failures follow the suspected-deviation protocol in `AGENTS.md`.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-N18-01` to `Ready`.
- Tick N17-2 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
