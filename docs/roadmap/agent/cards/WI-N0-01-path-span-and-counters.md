# Task Card `WI-N0-01`: `path` span and request counters by origin

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N0-01` |
| Roadmap phase | [N0](../../track-n-pathfinding-performance.md): Path cost baseline |
| Status | `Done` |
| Type | `source-edit` |
| Effort | `S` |
| Prerequisites | B1, B4, M4 (all Done) |
| Diagrams | [Track N architecture diagrams](../../deep-dives/n-multi-unit-pathfinding.md#8-architecture-diagrams) |

## Goal

Measure logic-thread path search time as a nested B1 `path` span and count requests by origin,
nodes expanded, failed searches, and endpoint adjustments, without changing any path result.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-multi-unit-pathfinding.md` sections 1, 4 (N0), and 8.
- `src/Panopticon.Simulation/Navigation/Navigation.cs`, `Graph.cs`, `GroupMovement.cs`.
- `src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/AIUpdate.cs`: `AppendPathToTargetPoint`.
- `src/Panopticon.Simulation/Logic/Object/Contain/HordeContain.cs:93`.
- `src/Panopticon.Simulation/LogicStepper.cs` and `Game.cs:499-508`: the `ai` span pattern.
- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeRunner.cs`: `path.requestsPerTick` statistics.
- `test/Panopticon.Simulation.Tests/Navigation/NavigationPathRequestCounterTests.cs`, `LogicStepperTests.cs`.

## Steps

1. Add `PathRequestOrigin` (`Unit`, `SquadLeader`, `BlockedFollower`, `Horde`, `Async`) and
   `PathStatistics` (per-origin request counts, `NodesExpanded`, `FailedSearches`,
   `EndpointAdjustments`, worker search ticks) in `src/Panopticon.Simulation/Navigation/`.
2. Add an optional origin parameter to `CalculatePath` (default `Unit`). Pass `SquadLeader` and
   `BlockedFollower` from `GroupMovement.MoveSquad` and `Horde` from `HordeContain`. Async
   worker searches record `Async` and accumulate worker time separately.
3. `Graph.Search` reports nodes expanded through an out parameter or a counter field; no
   behavior change.
4. Accumulate logic-thread `ComputePath` time in `Navigation` and expose `ConsumePathSpan()`.
   `LogicStepper` emits `path` after `ai`; the no-sink branch consumes and discards it, as it
   does for `ai`.
5. Add `path.nodesExpandedPerTick`, `path.failedSearchesPerTick`, and per-origin per-tick
   statistics to the capacity probe's B1 record beside `path.requestsPerTick`.
6. Tests: origin attribution for unit, squad, and blocked-follower requests; nodes expanded and
   failed-search counts on a small synthetic graph; `LogicStepper` emits the `path` span and it
   resets per tick; `PathRequestCount` keeps its current meaning.

## Files likely to change

- `src/Panopticon.Simulation/Navigation/Navigation.cs`, `Graph.cs`, `GroupMovement.cs`, `PathRequestQueue.cs`
- `src/Panopticon.Simulation/Navigation/PathStatistics.cs` (new, with `PathRequestOrigin`)
- `src/Panopticon.Simulation/Logic/Object/Contain/HordeContain.cs`
- `src/Panopticon.Simulation/LogicStepper.cs`
- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeRunner.cs`
- `test/Panopticon.Simulation.Tests/Navigation/NavigationPathRequestCounterTests.cs`, `LogicStepperTests.cs`

## Acceptance criteria

- [ ] B1 records contain a `path` span and the new per-tick path statistics.
- [ ] Every `CalculatePath` call site passes an explicit or default origin; counts by origin sum to `PathRequestCount` for synchronous requests.
- [ ] Waypoint results are unchanged (existing navigation tests pass untouched).
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Navigation|FullyQualifiedName~LogicStepper"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No search, ordering, or path-assignment change.
- No new scenario (that is `WI-N0-02`).
- A determinism-enforcing test failure is a suspected deviation: stop, keep artifacts, report.

## On completion

- Set Status above to `Done` or `Blocked` with reason; set `WI-N0-02` to `Ready`.
- Tick N0-1 in `docs/roadmap/implementation-checklist.md` and update the README Track N progress row.
- Append a result line to `.exchange`.
