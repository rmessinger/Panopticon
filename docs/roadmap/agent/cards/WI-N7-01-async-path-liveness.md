# Task Card `WI-N7-01`: Async and squad path liveness

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N7-01` |
| Roadmap phase | [N7](../../track-n-pathfinding-performance.md): Path liveness |
| Status | `Ready` |
| Type | `source-edit` |
| Effort | `S` |
| Prerequisites | `WI-N6-02` |
| Diagrams | [Track N deadlock diagrams](../../deep-dives/n-navigation-deadlocks.md#8-architecture-diagrams) |

## Goal

No order leaves a unit holding with no route: failed, thrown, or stale path requests settle the unit or fall back once.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-navigation-deadlocks.md` section 4 (N7) and the async liveness sequence.
- `src/Panopticon.Simulation/Navigation/PathRequestQueue.cs`, `PathRequestHandle.cs`, `GroupMovement.cs`, `NavigationOptions.cs`.
- `AIUpdate.cs` async request region.
- `test/Panopticon.Simulation.Tests/Navigation/AsyncPathRequestTests.cs`, `GroupMovementTests.cs`.

## Steps

1. Add `PathRequestOutcome` on the handle; a worker exception completes as `Failed` and calls back. Requester-cancelled requests still never call back.
2. Add `AIUpdate.SettleNoRoute`: clear route and moving state, record `NoRoute`. Call it from the async callback on a failed or empty result and from the synchronous path on an empty result.
3. Add `NavigationOptions.PendingPathTimeoutFrames` (default 60): a pending handle older than that cancels and falls back to one synchronous `CalculatePath`.
4. A failed or empty squad-leader result, synchronous or async, settles every member as `NoRoute`.
5. Re-run the `async-no-route` case and the M4 probe with `AsyncOrderPaths` on; record results.

## Files likely to change

- `src/Panopticon.Simulation/Navigation/PathRequestQueue.cs`, `PathRequestHandle.cs`, `GroupMovement.cs`, `NavigationOptions.cs`
- `AIUpdate.cs`
- `test/Panopticon.Simulation.Tests/Navigation/AsyncPathRequestTests.cs`, `GroupMovementTests.cs`

## Acceptance criteria

- [ ] Tests: a worker exception settles the unit; a stale pending request falls back once; a failed leader settles all members; requester-cancelled requests still never apply.
- [ ] `async-no-route` ends with 0 units in `AwaitingPath`, each settled at the drain or inside the watchdog bound.
- [ ] Successful-path behavior is unchanged.
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

- Offset routes (`WI-N7-02`).
- A determinism-enforcing test failure is a suspected deviation: stop, keep artifacts, report.

## On completion

- Set Status above to `Done` or `Blocked` with reason; set `WI-N7-02` to `Ready`.
- Tick N7-1 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
