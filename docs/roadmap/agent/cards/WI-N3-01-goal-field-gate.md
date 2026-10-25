# Task Card `WI-N3-01`: Shared-destination field gate

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N3-01` |
| Roadmap phase | [N3](../../track-n-pathfinding-performance.md): Shared-destination fields |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-N2-02` |
| Diagrams | [Track N architecture diagrams](../../deep-dives/n-multi-unit-pathfinding.md#8-architecture-diagrams) |

## Goal

Decide with numbers whether a shared-destination goal field is needed after N2, and if so,
author the implementation card.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-multi-unit-pathfinding.md` sections 3 and 4 (N3).
- "WI-N2 results" in `docs/roadmap/implementation/track-n-pathfinding-performance.md`.
- `src/Panopticon.Simulation/Navigation/PathStatistics.cs`.
- `docs/roadmap/implementation/track-b-b4-spatial-scaling.md`: "WI-B4-06 hierarchical gate" as the decision format.

## Steps

1. Add a goal histogram to `PathStatistics` (full searches per tick bucketed by goal cell
   neighbourhood); diagnostics only.
2. Run `PathfindingLoad` (all cases, N2 options on) and the M4 army-AI probe.
3. Gate opens only if both hold: some tick has at least `GoalFieldMinRequests` (default 32)
   full searches sharing one goal neighbourhood, and the order-tick `path` span misses budget.
4. Record the gate decision with numbers in "WI-N3-01 gate decision".
5. If open: write `WI-N3-02` (goal field cache per the deep dive) as `Ready` with diagrams
   updated. If closed: set N3 `Done` with "gate closed".

## Files likely to change

- `src/Panopticon.Simulation/Navigation/PathStatistics.cs`
- `docs/roadmap/implementation/track-n-pathfinding-performance.md`
- `docs/roadmap/agent/cards/WI-N3-02-goal-field-cache.md` (only if the gate opens)

## Acceptance criteria

- [ ] Goal histogram and order-tick spans are recorded for every case.
- [ ] The decision states the threshold, the measured maximum, and the budget verdict.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Navigation"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No field implementation in this card.
- No hierarchical layer.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Tick N3-1 in the checklist and update the README progress row; update track doc N3 status per the gate.
- Append a result line to `.exchange`.
