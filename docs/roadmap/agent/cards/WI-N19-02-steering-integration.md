# Task Card `WI-N19-02`: Steering integration with N8 fallback

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N19-02` |
| Roadmap phase | [N19](../../track-n-pathfinding-performance.md): Unit steering |
| Status | `Blocked` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-N19-01`, `WI-N15-01`, `WI-N10-01` |
| Diagrams | [Track N layered navigation diagrams](../../deep-dives/n-layered-navigation.md#9-architecture-diagrams) |

## Goal

With `UnitSteering` on, ground units move by steering velocity toward their local target, keep the footprint gate as the overlap guard, and hand stalls to N8.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-layered-navigation.md` sections 3, 5 (N19), and the grouped-order sequence diagram.
- `src/Panopticon.Simulation/Logic/Object/Locomotor.cs` (`LocoUpdateMoveTowardsPosition`, gate), `UnitSteering.cs`, `UnitNavigation.cs`, `BlockedMovementResolver.cs`, `PartitionCellManager.cs`.

## Steps

1. With the option on, collect up to `SteeringMaxNeighbors` neighbors in fixed `ObjectId` order and obstacles in the horizon window, then call `UnitSteering.Compute`.
2. Pass `position + velocity x dt` to the locomotor as the goal for this tick; waypoint popping still uses the route.
3. Speed under 10% of desired for `SteeringStallFrames` sets `BlockedByUnit` with `Layer = Steering` and enables N8 resolution for that unit.
4. Add the `steer` span and per-case jitter (heading changes per second).
5. Run `head-on-pair`, `corridor-opposed-20`, `steer-crossing-2x10`, `steer-crowd-60`, and the M4 probe with the option on and off.

## Files likely to change

- `Locomotor.cs`, `UnitNavigation.cs`, `BlockedMovementResolver.cs`, `LogicStepper.cs`
- `test/Panopticon.Simulation.Tests/Navigation/SteeringIntegrationTests.cs` (new)
- `docs/roadmap/implementation/track-n-pathfinding-performance.md` (results)

## Acceptance criteria

- [ ] Option off: golden baselines and `SeededRunTests` unchanged.
- [ ] Option on: head-on and crossing cases pass with 0 give-way events and 0 gate stops, or the miss recorded.
- [ ] `steer` span median at most 1.0 ms per tick in the M4 probe, or the miss recorded.
- [ ] N10 deadlock targets still met.
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

- Push-apart physics and aircraft.
- Options stay default off; determinism-test failures follow the suspected-deviation protocol in `AGENTS.md`.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-N20-01` to `Ready` if `WI-N18-02` and `WI-N18-03` are done.
- Tick N19-2 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
