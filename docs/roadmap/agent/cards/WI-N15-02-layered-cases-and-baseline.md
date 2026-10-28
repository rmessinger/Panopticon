# Task Card `WI-N15-02`: Layered cases, layer spans, and option-off baseline

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N15-02` |
| Roadmap phase | [N15](../../track-n-pathfinding-performance.md): Layered navigation contract and baseline |
| Status | `Blocked` |
| Type | `test-addition` |
| Effort | `M` |
| Prerequisites | `WI-N15-01`, `WI-N6-02` (`NavigationDeadlock` scenario) |
| Diagrams | [Track N layered navigation diagrams](../../deep-dives/n-layered-navigation.md#9-architecture-diagrams) |

## Goal

Five layered cases and per-layer B1 spans record an option-off baseline that confirms or revises the section 4 targets.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-layered-navigation.md` sections 4 and 5 (N15 case table).
- `src/Panopticon.Tools.TestHarness/NavigationDeadlockRunner.cs` and its case list.
- `src/Panopticon.Tools.TestHarness/PathfindingLoadRunner.cs`, `PathfindingLoadCasePlanner.cs`.
- `src/Panopticon.Tools.TestHarness/RoutingChokeRunner.cs` (wall placement helpers).

## Steps

1. Add cases `layered-cross-map-8x12`, `layered-detour-wall`, `layered-structure-placed`, `steer-crossing-2x10`, and `steer-crowd-60` to `NavigationDeadlock`.
2. Add `layered-cross-map` and `layered-detour-wall` cost cases to `PathfindingLoad`.
3. Record per case: full-grid nodes expanded, route length vs optimal, settle frames, seam gaps (frames with an empty route while the goal is unreached), gate stops, give-way events, and slot lag (median distance to formation offset).
4. Add B1 spans `strategic`, `mainpath`, `route`, and `steer` (zero until their phases land).
5. Run all cases over 3 seeds with options off; write results to the plan's layered results section and revise section 4 targets with numbers where needed.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/NavigationDeadlockRunner.cs`, its case planner and report
- `PathfindingLoadRunner.cs`, `PathfindingLoadCasePlanner.cs`
- `src/Panopticon.Simulation/LogicStepper.cs` (span sink)
- `docs/roadmap/implementation/track-n-pathfinding-performance.md`

## Acceptance criteria

- [ ] Each case runs headless and reports every listed metric.
- [ ] `layered-structure-placed` places the structure on the measured route.
- [ ] Baseline numbers recorded for 3 seeds; targets confirmed or revised with reasons.
- [ ] Movement unchanged: golden baselines and `SeededRunTests` unchanged.
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

- Any behavior change.
- New maps; cases use existing maps and placed structures.
- Options stay default off; determinism-test failures follow the suspected-deviation protocol in `AGENTS.md`.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Tick N15-2.
- Tick N15-2 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
