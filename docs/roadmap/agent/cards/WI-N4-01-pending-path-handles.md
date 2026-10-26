# Task Card `WI-N4-01`: Pending path handles on `AIUpdate`

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N4-01` |
| Roadmap phase | [N4](../../track-n-pathfinding-performance.md): Off-thread order paths |
| Status | `Ready` |
| Type | `source-edit` |
| Effort | `S` |
| Prerequisites | `WI-N1-03` |
| Diagrams | [Track N architecture diagrams](../../deep-dives/n-multi-unit-pathfinding.md#8-architecture-diagrams) |

## Goal

Give `AIUpdate` a pending async path handle with assign-on-drain and cancel-on-new-order
semantics, without any gameplay caller using it yet.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-multi-unit-pathfinding.md` section 4 (N4).
- `src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/AIUpdate.cs`: `SetTargetPoint`, `AppendPathToTargetPoint`, `ResetMovementIntent`.
- `src/Panopticon.Simulation/Navigation/Navigation.cs`, `PathRequestHandle.cs`, `PathRequestQueue.cs`.
- `test/Panopticon.Simulation.Tests/Navigation/AsyncPathRequestTests.cs`.

## Steps

1. Add `AIUpdate.RequestPathAsync(target)`: cancels any pending handle, submits with this
   object as requester, stores the handle.
2. The drain callback applies the same post-processing as `AppendPathToTargetPoint` (drop the
   start point, land exactly on a passable target, set `Moving`) and clears the handle.
3. `ResetMovementIntent` and every `SetTargetPoint` cancel the pending handle.
4. Tests: completion assigns waypoints; a second request cancels the first; reset cancels;
   destroyed requester is cancelled by `Drain`.

## Files likely to change

- `src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/AIUpdate.cs`
- `test/Panopticon.Simulation.Tests/Navigation/AsyncPathRequestTests.cs`

## Acceptance criteria

- [ ] Assign, replace, reset, and destroy cases are tested.
- [ ] No existing caller changes behavior.
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

- No option or caller switch (`WI-N4-02`).
- Pending handles are not persisted in save files; document that a load drops them.

## On completion

- Set Status above to `Done` or `Blocked` with reason; set `WI-N4-02` to `Ready`.
- Tick N4-1 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
