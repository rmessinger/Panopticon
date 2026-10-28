# Task Card `WI-N18-03`: Crowd routing for large selections

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N18-03` |
| Roadmap phase | [N18](../../track-n-pathfinding-performance.md): Group local router |
| Status | `Blocked` |
| Type | `source-edit` |
| Effort | `S` |
| Prerequisites | `WI-N18-01` |
| Diagrams | [Track N layered navigation diagrams](../../deep-dives/n-layered-navigation.md#9-architecture-diagrams) |

## Goal

Selections larger than `CrowdModeThreshold` move as a crowd on hex-packed slots behind the spine head, with stable assignment.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-layered-navigation.md` section 5 (N18, crowd mode).
- `GroupRouter.cs`, `NavigationGroup.cs`, `OrderProcessor.cs` (`MoveUnits`).

## Steps

1. Add `GroupMode` {`Formation`, `Crowd`}; squad-less selections over `CrowdModeThreshold` (default 24) use `Crowd`.
2. Generate hex-packed slots behind the spine head sized by member count and the largest member pitch.
3. Assign greedily by distance with `ObjectId` tie-break; reassign only when width adaptation changes the slot set.
4. Arrival keeps N9 destination slots.

## Files likely to change

- `GroupRouter.cs`, `NavigationGroup.cs`, `NavigationOptions.cs`
- `test/Panopticon.Simulation.Tests/Navigation/GroupRouterTests.cs`

## Acceptance criteria

- [ ] `steer-crowd-60` and `crowd-goal-100` settle with 0 units holding routes.
- [ ] Slot assignment is identical across runs with the same seed.
- [ ] Order tick for the 1000-unit selection stays at or under the N5 value.
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

- Steering (`WI-N19-02`).
- Options stay default off; determinism-test failures follow the suspected-deviation protocol in `AGENTS.md`.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-N20-01` to `Ready` if `WI-N18-02` and `WI-N19-02` are done.
- Tick N18-3 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
