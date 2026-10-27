# Task Card `WI-N13-02`: Squad and cluster passage entry

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N13-02` |
| Roadmap phase | [N13](../../track-n-pathfinding-performance.md): Opposing flow and group entry |
| Status | `Blocked` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-N12-02` |
| Diagrams | [Track N passage diagrams](../../deep-dives/n-confined-passage-queueing.md#8-architecture-diagrams) |

## Goal

Squads and selection clusters enter a passage as a contiguous block on lane points and restore their formation offsets after the exit.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-confined-passage-queueing.md` section 4 (N13 group entry).
- `src/Panopticon.Simulation/Navigation/GroupMovement.cs` (`MoveSquad`, `AssignSquadMembers`, `AssignPath`).
- `src/Panopticon.Simulation/Logic/Orders/OrderProcessor.cs` (`MoveUnitsByPositionCluster`), `PassageCoordinator.cs`.

## Steps

1. When the leader route crosses a passage, `MoveSquad` joins all ground members as one block: consecutive ranks in front-to-back order, block ranked by its first member.
2. Members keep offset waypoints up to the entry and after the exit; between them they use lane points.
3. Cluster moves (`GroupSelectionMoves`) join per cluster the same way.
4. Run `passage-squad-12` and the M4 army-AI probe with the option on.

## Files likely to change

- `src/Panopticon.Simulation/Navigation/GroupMovement.cs`, `PassageCoordinator.cs`, `PassageQueue.cs`
- `test/Panopticon.Simulation.Tests/Navigation/GroupMovementTests.cs`

## Acceptance criteria

- [ ] Option off: `GroupMovementTests` unchanged.
- [ ] `passage-squad-12` reports 0 offset waypoints on impassable cells inside the passage and settles.
- [ ] Block members are never interleaved with other units in the same lane.
- [ ] M4 squad invariants hold with the option on, recorded.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Navigation"
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario NavigationDeadlock --headless
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- `SquadHierarchy` is untouched; passage blocks are navigation-transient.
- A determinism-enforcing test failure is a suspected deviation: stop, keep artifacts, report.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Tick N13-2 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
