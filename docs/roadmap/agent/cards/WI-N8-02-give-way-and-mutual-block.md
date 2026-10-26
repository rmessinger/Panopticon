# Task Card `WI-N8-02`: Give-way and mutual block yield

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N8-02` |
| Roadmap phase | [N8](../../track-n-pathfinding-performance.md): Blocked-unit resolution |
| Status | `Blocked` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-N8-01` |
| Diagrams | [Track N deadlock diagrams](../../deep-dives/n-navigation-deadlocks.md#8-architecture-diagrams) |

## Goal

Behind `BlockedUnitResolution`, idle or arrived friendly blockers sidestep, and mutual blocks resolve with exactly one unit yielding.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-navigation-deadlocks.md` sections 4 (N8) and 6, and the blocked-unit sequence.
- `AIUpdate.cs` (`_moveOutOfWay1/2`, `_ignoreObstacleId`), `BlockedMovementResolver.cs`.
- `src/Panopticon.Simulation/Logic/AI/AIUpdateStateMachine.cs` (repulsor state, not reused).

## Steps

1. Add `AIUpdate.RequestGiveWay(requester, segment)` using `_moveOutOfWay1/2`; accepted only from a friendly, by an idle, arrived, or order-less blocker, at most once per `BlockedWaitFrames`.
2. Add `GiveWayPlanner.FindSidestep`: the nearest free passable cell off the requester's next segment within the local window; the blocker moves there and resumes idle.
3. Mutual block (each records the other as blocker): the higher `ObjectId` yields; the lower waits.
4. Insert give-way before local repath in `BlockedMovementResolver`.
5. Run `head-on-pair`, `corridor-opposed-20`, `queue-behind-idle`, and the M4 probe with the option on; record settle frames, wait-for cycles, and sidesteps.

## Files likely to change

- `src/Panopticon.Simulation/Navigation/GiveWayPlanner.cs` (new), `BlockedMovementResolver.cs`
- `AIUpdate.cs`
- `test/Panopticon.Simulation.Tests/Navigation/GiveWayTests.cs` (new)

## Acceptance criteria

- [ ] Option off changes nothing.
- [ ] `head-on-pair` settles with one sidestep; wait-for cycles at end are 0 in every case.
- [ ] No cascade: at most one sidestep per blocker per wait window, asserted in tests.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Navigation|FullyQualifiedName~GroundFootprint"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Enemy blockers never give way.
- Repulsor and panic behavior unchanged.
- A determinism-enforcing test failure is a suspected deviation: stop, keep artifacts, report.

## On completion

- Set Status above to `Done` or `Blocked` with reason; if `WI-N7-02` and `WI-N9-02` are `Done`, set `WI-N10-01` to `Ready`.
- Tick N8-2 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
