# Task Card `WI-N4-02`: Async order paths behind an option

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N4-02` |
| Roadmap phase | [N4](../../track-n-pathfinding-performance.md): Off-thread order paths |
| Status | `Ready` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-N4-01`; `WI-N2-01` if landed (shares `NavigationOptions`) |
| Diagrams | [Track N architecture diagrams](../../deep-dives/n-multi-unit-pathfinding.md#8-architecture-diagrams) |

## Goal

With `NavigationOptions.AsyncOrderPaths` on, order-driven unit paths and squad leader paths
compute on the async queue, and the logic-thread `path` span on order ticks drops to submission
cost.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-multi-unit-pathfinding.md` sections 4 (N4) and 6.
- `src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/AIUpdate.cs`, `src/Panopticon.Simulation/Navigation/GroupMovement.cs`, `NavigationOptions.cs`.
- `src/Panopticon.Tools.TestHarness/PathfindingLoadRunner.cs`, `MassBattleCapacityProbeRunner.cs`, `TestHarnessConfig.cs`.
- "WI-N0-02 baseline" and "WI-N2 results" in the Track N plan.

## Steps

1. Add `AsyncOrderPaths` (default false) to `NavigationOptions` and harness parameters for
   `PathfindingLoad` and the capacity probe.
2. With it on, `AppendPathToTargetPoint` for ground units calls `RequestPathAsync`;
   `MoveSquad` submits the leader path and assigns leader and followers in the callback.
   Blocked-follower fallbacks also submit.
3. Report per run: submitted, applied, cancelled, wait frames (median, p99, max), and deadline
   misses against `AsyncPathFrameBound`.
4. Run `PathfindingLoad` and the M4 army-AI probe with the option on and off; record order-tick
   `path` span, `path.workerMs`, wait frames, and M4 invariants in "WI-N4 results".

## Files likely to change

- `src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/AIUpdate.cs`
- `src/Panopticon.Simulation/Navigation/GroupMovement.cs`, `NavigationOptions.cs`
- `src/Panopticon.Tools.TestHarness/TestHarnessConfig.cs`, `PathfindingLoadRunner.cs`, `MassBattleCapacityProbeRunner.cs`
- `test/Panopticon.Simulation.Tests/Navigation/GroupMovementTests.cs`, `AsyncPathRequestTests.cs`

## Acceptance criteria

- [ ] Option off: no behavior change.
- [ ] Option on: logic-thread `path` span on order ticks is submission cost only; worker time is reported separately.
- [ ] Wait frames stay inside the frame bound outside warm-up; M4 stall and squad invariants hold.
- [ ] Before/after numbers are recorded.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Navigation|FullyQualifiedName~PathfindingLoad"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Do not flip the default.
- Aircraft, dozer, supply, and script-specific movement keep synchronous paths.
- Run-to-run variation with the option on is expected; with it off, a determinism-enforcing test failure is a suspected deviation.

## On completion

- Set Status above to `Done` or `Blocked` with reason; set N4 `Done` in the track doc.
- Tick N4-2 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
