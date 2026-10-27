# Task Card `WI-N9-02`: Crowd arrival

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N9-02` |
| Roadmap phase | [N9](../../track-n-pathfinding-performance.md): Destination crowding |
| Status | `Done` |
| Type | `source-edit` |
| Effort | `S` |
| Prerequisites | `WI-N9-01` |
| Diagrams | [Track N deadlock diagrams](../../deep-dives/n-navigation-deadlocks.md#8-architecture-diagrams) |

## Goal

A unit on its final segment held off by an arrived friendly near its goal stops and reports `CrowdArrived` instead of holding its route.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-navigation-deadlocks.md` sections 4 (N9) and 6.
- `AIUpdate.cs` arrival check, `MovementProgress`.

## Steps

1. Add `NavigationOptions.CrowdArrivalRadius`, default taken from the `WI-N6-02` crowd distribution and recorded.
2. In the arrival check: on the final segment, blocked for `BlockedWaitFrames` by an arrived friendly whose own goal lies within the radius of this unit's goal, clear the route and report `CrowdArrived`. Active when `BlockedUnitResolution` or `DestinationSlots` is on.
3. Run `crowd-goal-100` with slots off and on; record settle frames and units still holding routes.

## Files likely to change

- `AIUpdate.cs`, `NavigationOptions.cs`
- `test/Panopticon.Simulation.Tests/Navigation/CrowdArrivalTests.cs` (new)

## Acceptance criteria

- [ ] Options off change nothing.
- [ ] `crowd-goal-100` ends with 0 units holding routes in both slot modes.
- [ ] A unit with a reachable free goal is never crowd-arrived in tests.
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

- A determinism-enforcing test failure is a suspected deviation: stop, keep artifacts, report.

## On completion

- Set Status above to `Done` or `Blocked` with reason; if `WI-N7-02` and `WI-N8-02` are `Done`, set `WI-N10-01` to `Ready`.
- Tick N9-2 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
