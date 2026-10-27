# Task Card `WI-N13-01`: Opposing flow arbitration

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N13-01` |
| Roadmap phase | [N13](../../track-n-pathfinding-performance.md): Opposing flow and group entry |
| Status | `Blocked` |
| Type | `source-edit` |
| Effort | `S` |
| Prerequisites | `WI-N12-02` |
| Diagrams | [Track N passage diagrams](../../deep-dives/n-confined-passage-queueing.md#8-architecture-diagrams) |

## Goal

Opposing allied flows share one passage: split lanes when there are two or more, and alternate bounded grants on a single lane.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-confined-passage-queueing.md` section 4 (N13) and the opposing-flow sequence.
- `src/Panopticon.Simulation/Navigation/PassageCoordinator.cs`, `PassageQueue.cs`.

## Steps

1. Let an opposite-direction span join the matching passage as its second queue.
2. Two or more lanes: each direction takes the lanes on its right.
3. One lane: add `PassageGrant`; switch after the queue empties, `PassageMaxBatch` (8) units enter, or `PassageMaxGrantFrames` (300) pass, once the corridor holds no released unit. First grant to the longer-waiting queue, ties to the lower owning player index.
4. Run `passage-opposed-1lane` and `corridor-opposed-20` with the option on.

## Files likely to change

- `src/Panopticon.Simulation/Navigation/PassageCoordinator.cs`, `PassageQueue.cs`, `PassageGrant.cs` (new), `NavigationOptions.cs`
- `test/Panopticon.Simulation.Tests/Navigation/PassageQueueTests.cs`

## Acceptance criteria

- [ ] Install-free tests: grants alternate, never starve a non-empty queue past the frame bound, and never release into an occupied single lane.
- [ ] `passage-opposed-1lane` and `corridor-opposed-20` settle with 0 wait-for cycles.
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

- Enemy flows stay separate passages; N8 handles them.
- A determinism-enforcing test failure is a suspected deviation: stop, keep artifacts, report.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Tick N13-1 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
