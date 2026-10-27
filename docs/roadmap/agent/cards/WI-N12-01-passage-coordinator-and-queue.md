# Task Card `WI-N12-01`: Passage coordinator, queue order, and lane path

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N12-01` |
| Roadmap phase | [N12](../../track-n-pathfinding-performance.md): Passage queues |
| Status | `Blocked` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-N11-01`, `WI-N6-01` |
| Diagrams | [Track N passage diagrams](../../deep-dives/n-confined-passage-queueing.md#8-architecture-diagrams) |

## Goal

`PassageCoordinator` forms ad hoc passage queues from scanned spans, builds one shared lane path per passage, and decides unit order, lane, hold, and release, without moving any unit yet.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-confined-passage-queueing.md` section 4 (N12 steps 2 to 7) and the join sequence.
- `src/Panopticon.Simulation/Navigation/PassageFinder.cs`, `PassageSpan.cs` (from `WI-N11-01`), `Navigation.cs`, `NavigationOptions.cs`.
- `MovementProgress` and `NavigationStallReason` (from `WI-N6-01`).

## Steps

1. Add `PassageTicket`, `PassageTicketState`, `PassageLanePath`, `PassageQueue`, and `PassageCoordinator` (owned by `Navigation`, exposed as `Passages`).
2. Join: match a span to an open passage whose corridor holds its throat, same direction, allied owner; otherwise open one. Queue pitch is the largest member pitch.
3. Lane path: centerline from cross-section midpoints, lane offsets by pitch, drop lanes with impassable points.
4. Order: provisional rank by estimated time to entry, ties by `ObjectId`; rank fixed at `HoldRadius`; lane nearest the lateral approach, balanced by count.
5. Release rule per lane and leave rules (exit, death, new order, corridor passability change closes the passage).
6. Add `NavigationStallReason.Queued`, `PassageQueueing` (default off), `HoldRadius` in pitches (3), `PassageLookaheadCells` (24), and `PassageScanIntervalFrames` (15).

## Files likely to change

- `src/Panopticon.Simulation/Navigation/PassageCoordinator.cs`, `PassageQueue.cs`, `PassageLanePath.cs`, `PassageTicket.cs` (new), `Navigation.cs`, `NavigationOptions.cs`
- `MovementProgress` / `NavigationStallReason` source from `WI-N6-01`
- `test/Panopticon.Simulation.Tests/Navigation/PassageQueueTests.cs` (new)

## Acceptance criteria

- [ ] Install-free tests with stub units: ranks are deterministic for equal arrival estimates, fixed ranks never change, lanes balance, and releases respect one-pitch spacing.
- [ ] A passability change inside a corridor closes the passage and clears every ticket.
- [ ] Opposite-direction and enemy spans open separate passages (merged in `WI-N13-01`).
- [ ] Option off: the coordinator is never constructed; existing tests unchanged.
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

- `AIUpdate` hold and route splice (`WI-N12-02`).
- Opposing flow and group entry (`WI-N13-01`, `WI-N13-02`).
- Tickets are not persisted; no save format change.

## On completion

- Set Status above to `Done` or `Blocked` with reason; set `WI-N12-02` to `Ready` if `WI-N11-02` is done.
- Tick N12-1 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
