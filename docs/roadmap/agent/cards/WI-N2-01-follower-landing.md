# Task Card `WI-N2-01`: Bounded landing for blocked followers

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N2-01` |
| Roadmap phase | [N2](../../track-n-pathfinding-performance.md): Shared group requests |
| Status | `Blocked` (on `WI-N1-03`) |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-N1-02`, `WI-N1-03` |
| Diagrams | [Track N architecture diagrams](../../deep-dives/n-multi-unit-pathfinding.md#8-architecture-diagrams) |

## Goal

Serve a blocked follower with a bounded ring search for a passable landing cell in the
destination's region, so it retraces the leader's path instead of paying a full search.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-multi-unit-pathfinding.md` sections 4 (N2) and 6.
- `src/Panopticon.Simulation/Navigation/GroupMovement.cs`, `NavigationRegions.cs`, `Navigation.cs`.
- `test/Panopticon.Simulation.Tests/Navigation/GroupMovementTests.cs`.
- `src/Panopticon.Tools.TestHarness/MassBattleSquadInvariantSampler.cs`: blocked-follower reporting.

## Steps

1. Add `NavigationOptions` (on `Navigation`) with `MaxLandingRadiusCells`; pick the default
   from the `WI-N0-02` distribution of blocked-offset distances and record the choice.
2. Add `FollowerLanding.TryLand`: rings outward from the offset destination's cell, first
   passable cell in the destination's region wins, ties by fixed ring order.
3. In `MoveSquad`, a blocked follower in the leader's start region lands and retraces the leader
   path, ending on the landing cell. Otherwise it keeps today's full-search fallback.
4. Split `BlockedFollowerCount` into landed and fallback counts; report both in the squad
   sampler and `PathStatistics`.
5. Report impassable retrace points along offset paths in the scenario report (existing
   behavior, not fixed here).
6. Re-run `PathfindingLoad` squad cases and the M4 army-AI probe; record full searches per order
   and the `path` span before and after in "WI-N2 results".

## Files likely to change

- `src/Panopticon.Simulation/Navigation/GroupMovement.cs`, `Navigation.cs`
- `src/Panopticon.Simulation/Navigation/FollowerLanding.cs`, `NavigationOptions.cs` (new)
- `src/Panopticon.Tools.TestHarness/MassBattleSquadInvariantSampler.cs`, `PathfindingLoadRunner.cs`
- `test/Panopticon.Simulation.Tests/Navigation/GroupMovementTests.cs`, `FollowerLandingTests.cs` (new)

## Acceptance criteria

- [ ] Landed followers issue no search; fallback remains for other-region or failed landings.
- [ ] Full searches per squad order are at or under squads plus fallback followers in both runs.
- [ ] M4 invariants hold: unassigned 0, commanderless 0, no stall past the threshold.
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

- Squad-less selections stay per-unit (`WI-N2-02`).
- This changes AI squad movement. A determinism-enforcing test failure is a suspected deviation: stop, keep artifacts, report.

## On completion

- Set Status above to `Done` or `Blocked` with reason; set `WI-N2-02` to `Ready`.
- Tick N2-1 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
