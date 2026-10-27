# Task Card `WI-N14-01`: Passage queueing verification

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N14-01` |
| Roadmap phase | [N14](../../track-n-pathfinding-performance.md): Passage verification |
| Status | `Blocked` |
| Type | `investigation` |
| Effort | `M` |
| Prerequisites | `WI-N13-01`, `WI-N13-02`, `WI-N10-01` |
| Diagrams | [Track N passage diagrams](../../deep-dives/n-confined-passage-queueing.md#8-architecture-diagrams) |

## Goal

Every passage target has a verdict across 3 seeds, measured with and without N8 resolution, and a `PassageQueueing` default-flip recommendation is recorded.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-confined-passage-queueing.md` sections 3 and 4 (N14).
- The passage and deadlock results sections of `docs/roadmap/implementation/track-n-pathfinding-performance.md`.

## Steps

1. Run every passage case, the N6 cases, and the M4 army-AI probe over 3 seeds in three modes: all Track N options on, `PassageQueueing` off, `BlockedUnitResolution` off.
2. Report each section 3 target with before and after numbers, the `passage` span, the N5 path budgets, and M4 invariants.
3. Attribute any miss to a residual cause.
4. Record the default-flip recommendation for `PassageQueueing` and its interaction with `BlockedUnitResolution`.

## Files likely to change

- `docs/roadmap/implementation/track-n-pathfinding-performance.md` (results)

## Acceptance criteria

- [ ] Each target has a verdict per mode and seed.
- [ ] The `passage` span median at M4 scale is recorded against 0.25 ms.
- [ ] A default-flip recommendation is recorded for the operator.

## Validation commands

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario NavigationDeadlock --headless
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario MassBattleCapacityProbe --headless
```

## Out of scope / guardrails

- Flipping the default is an operator decision; this card only recommends.
- A determinism-enforcing test failure is a suspected deviation: stop, keep artifacts, report.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Tick N14-1 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
