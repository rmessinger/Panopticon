# Task Card `WI-N18-01`: Navigation groups and spine following

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N18-01` |
| Roadmap phase | [N18](../../track-n-pathfinding-performance.md): Group local router |
| Status | `Blocked` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-N17-02` |
| Diagrams | [Track N layered navigation diagrams](../../deep-dives/n-layered-navigation.md#9-architecture-diagrams) |

## Goal

With `GroupRouting` on, squads and selection clusters move as persistent navigation groups whose members track slots along one spine plan, replacing the one-shot offset retrace.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-layered-navigation.md` sections 3 and 5 (N18), and the grouped-order sequence diagram.
- `src/Panopticon.Simulation/Navigation/GroupMovement.cs`, `FollowerLanding.cs`, `src/Panopticon.Simulation/Logic/Orders/OrderProcessor.cs` (`MoveUnits`, `MoveUnitsByPositionCluster`).

## Steps

1. Add `NavigationGroup` and `GroupRouter` on `Navigation`; `GroupRouter.Update(frame)` runs after the async drain and N12's coordinator in `LogicStepper`.
2. `OrderProcessor.MoveUnits` creates a group per squad and per cluster when the option is on; the spine plan comes from the leader or the member nearest the centroid.
3. Slots: offsets in the spine frame from order-time positions; every `RouterIntervalFrames` (default 5, staggered by group id) set each member's `LocalTarget` to the spine point at group progress plus its rotated offset, clamped to a passable cell in the same region via `FollowerLanding`.
4. Cohesion: group speed is the slowest member's speed; a member lagging over `RegroupDistance` (default 6 pitches) slows the spine.
5. Dissolve on arrival, membership under 2, or new orders; arrival uses N9 destination slots.
6. Groups are not persisted; after a load, members keep their plans as ungrouped units.

## Files likely to change

- `src/Panopticon.Simulation/Navigation/NavigationGroup.cs`, `GroupRouter.cs` (new)
- `OrderProcessor.cs`, `LogicStepper.cs`, `NavigationOptions.cs`, `UnitNavigation.cs`
- `test/Panopticon.Simulation.Tests/Navigation/GroupRouterTests.cs` (new)

## Acceptance criteria

- [ ] Option off: nothing constructed; baselines unchanged.
- [ ] Option on: `layered-cross-map-8x12` has 0 member waypoints on impassable cells and median slot lag at most 2 pitches outside passages.
- [ ] M4 squad invariants hold in the army-AI probe.
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

- Width adaptation and passage handoff (`WI-N18-02`); crowd slots (`WI-N18-03`).
- Options stay default off; determinism-test failures follow the suspected-deviation protocol in `AGENTS.md`.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-N18-03` to `Ready`; set `WI-N18-02` to `Ready` if `WI-N14-01` is done.
- Tick N18-1 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
