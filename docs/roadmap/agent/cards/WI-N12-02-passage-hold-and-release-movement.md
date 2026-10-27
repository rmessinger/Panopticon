# Task Card `WI-N12-02`: Passage hold, release, and route splice

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N12-02` |
| Roadmap phase | [N12](../../track-n-pathfinding-performance.md): Passage queues |
| Status | `Blocked` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-N12-01`, `WI-N11-02` |
| Diagrams | [Track N passage diagrams](../../deep-dives/n-confined-passage-queueing.md#8-architecture-diagrams) |

## Goal

With `PassageQueueing` on, ground units scan ahead, hold before a passage in rank order, enter on the shared lane path when released, and resume their own route after the exit.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-confined-passage-queueing.md` sections 3 and 4 (N12) and the join sequence.
- `src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/AIUpdate.cs` waypoint advance and `SetTargetPoint`.
- `src/Panopticon.Simulation/LogicStepper.cs` (drain at `:115`), `PassageCoordinator.cs` (from `WI-N12-01`).

## Steps

1. Call `Navigation.Passages.Update(frame)` from `LogicStepper` after `DrainPathCompletions`, only when the option is on; time it as a `passage` span.
2. Scan each ground unit's lookahead on its staggered frame; squads scan the leader route once per order.
3. In `AIUpdate`, a `Holding` ticket stops the unit at its hold point, faces the entry, and records `Queued`; a released ticket splices the lane points between entry and exit.
4. New orders, death, and exit call `Leave`.
5. Run `passage-column-40`, `passage-converge-2x20`, and `passage-mixed-30` with the option on; record the section 3 targets against the `WI-N11-02` baseline.

## Files likely to change

- `src/Panopticon.Simulation/LogicStepper.cs`, `AIUpdate.cs`, `Navigation/PassageCoordinator.cs`, `PathStatistics.cs`
- `test/Panopticon.Simulation.Tests/Navigation/PassageMovementTests.cs` (new)

## Acceptance criteria

- [ ] Option off: movement unchanged, golden baselines unchanged.
- [ ] Option on, open ground: `crowd-goal-100` opens 0 passages and moves identically.
- [ ] `passage-column-40` and `passage-converge-2x20` settle with 0 mouth jams over 90 frames and 0 wait-for cycles.
- [ ] `passage-column-40` last arrival is recorded against the 0.8 x baseline target.
- [ ] Before/after GIFs of `passage-converge-2x20` are in the screenshot viewer per `docs/screenshots.md`.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Navigation|FullyQualifiedName~GroundFootprint"
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario NavigationDeadlock --headless
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Opposing flow and squad block entry (`WI-N13-01`, `WI-N13-02`).
- No continuous avoidance or push-apart physics.
- A determinism-enforcing test failure is a suspected deviation: stop, keep artifacts, report.

## On completion

- Set Status above to `Done` or `Blocked` with reason; set `WI-N13-01` and `WI-N13-02` to `Ready`.
- Tick N12-2 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
