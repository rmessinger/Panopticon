# Task Card `WI-N20-01`: Layered navigation verification

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N20-01` |
| Roadmap phase | [N20](../../track-n-pathfinding-performance.md): Layered navigation verification |
| Status | `Blocked` |
| Type | `investigation` |
| Effort | `M` |
| Prerequisites | `WI-N18-02`, `WI-N18-03`, `WI-N19-02` |
| Diagrams | [Track N layered navigation diagrams](../../deep-dives/n-layered-navigation.md#9-architecture-diagrams) |

## Goal

Every layered target has a verdict over 3 seeds and four option modes, with default-flip recommendations for `LayeredNavigation`, `GroupRouting`, and `UnitSteering`.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-layered-navigation.md` sections 4 and 5 (N20).
- `docs/roadmap/implementation/track-n-pathfinding-performance.md` results sections for N5, N10, N14, and N15.

## Steps

1. Run every N6, N11, and N15 case, the N0 cost cases, and the M4 army-AI probe over 3 seeds with all Track N options on.
2. Repeat with each of `LayeredNavigation`, `GroupRouting`, and `UnitSteering` off in turn.
3. Report each section 4 target with before and after numbers and a verdict; attribute misses to a layer via `MovementProgress.Layer`.
4. Record a default-flip recommendation per option for the operator.

## Files likely to change

- `docs/roadmap/implementation/track-n-pathfinding-performance.md`
- `docs/roadmap/deep-dives/n-layered-navigation.md` (targets revised only with recorded numbers)

## Acceptance criteria

- [ ] Every target has a verdict with numbers.
- [ ] N10 and N14 targets re-verified with layers on.
- [ ] Recommendations recorded; defaults unchanged pending operator decision.

## Validation commands

```sh
dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario NavigationDeadlock --headless
dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario PathfindingLoad --headless
dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario MassBattleCapacityProbe --headless
```

## Out of scope / guardrails

- Flipping defaults (operator decision).
- Determinism-test failures follow the suspected-deviation protocol in `AGENTS.md`.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Tick N20-1 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
