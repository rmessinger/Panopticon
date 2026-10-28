# Task Card `WI-N19-01`: Reciprocal velocity-obstacle steering core

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N19-01` |
| Roadmap phase | [N19](../../track-n-pathfinding-performance.md): Unit steering |
| Status | `Ready` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | None (pure, no movement change) |
| Diagrams | [Track N layered navigation diagrams](../../deep-dives/n-layered-navigation.md#9-architecture-diagrams) |

## Goal

A pure `UnitSteering.Compute` returns the velocity closest to the preferred one that avoids neighbors and nearby impassable cells within the horizon.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-layered-navigation.md` sections 4, 5 (N19), and 7.
- `src/Panopticon.Simulation/Logic/PartitionCellManager.cs` (`CollectCells`), `src/Panopticon.Simulation/Logic/GroundFootprint.cs` (clearance rule).

## Steps

1. Add `SteeringNeighbor` (position, velocity, radius, reciprocal flag) and `UnitSteering.Compute(self, preferred, neighbors, obstacles, horizon, maxTurn)`.
2. Build ORCA half-planes: reciprocal share 0.5 for moving friendlies, full responsibility for stationary, enemy, and `Queued` units; static half-planes for impassable cells in the horizon window.
3. Solve the 2D linear program; when infeasible, fall back to the least-violation velocity.
4. Clamp the result to the heading change `maxTurn` allows; infantry pass no clamp.
5. Add `SteeringHorizonFrames` (30), `SteeringMaxNeighbors` (8), and `SteeringStallFrames` (45) options.

## Files likely to change

- `src/Panopticon.Simulation/Navigation/UnitSteering.cs`, `SteeringNeighbor.cs` (new)
- `NavigationOptions.cs`
- `test/Panopticon.Simulation.Tests/Navigation/UnitSteeringTests.cs` (new)

## Acceptance criteria

- [ ] Install-free tests: a head-on pair passes without contact in simulated steps; a right-angle crossing passes; a unit beside a wall never steers into it.
- [ ] A stationary neighbor receives no share; results are identical for any neighbor input order.
- [ ] Zero allocation per call.
- [ ] No movement code calls it yet.
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

- Locomotor integration (`WI-N19-02`).
- Options stay default off; determinism-test failures follow the suspected-deviation protocol in `AGENTS.md`.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-N19-02` to `Ready` if `WI-N15-01` and `WI-N10-01` are done.
- Tick N19-1 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
