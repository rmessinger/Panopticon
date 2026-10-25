# Task Card `WI-N5-01`: Scale verification

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N5-01` |
| Roadmap phase | [N5](../../track-n-pathfinding-performance.md): Scale verification |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-N2-02`, `WI-N3-01` (and `WI-N3-02` if the gate opened), `WI-N4-02` |
| Diagrams | [Track N architecture diagrams](../../deep-dives/n-multi-unit-pathfinding.md#8-architecture-diagrams) |

## Goal

Measure every Track N budget at scale with all Track N options enabled and record a verdict
per budget.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-multi-unit-pathfinding.md` sections 3 and 4 (N5).
- `docs/roadmap/implementation/track-n-pathfinding-performance.md`: all results sections.
- `docs/roadmap/implementation/track-m-m4-army-ai.md`: invariant and engagement reporting.

## Steps

1. Run every `PathfindingLoad` case and the M4 army-AI probe (seed 1, 4500 frames) with
   `GroupSelectionMoves` and `AsyncOrderPaths` on, on the same host as the N0 baseline.
2. Compare against "WI-N0-02 baseline": medians and 95% CIs for the `path` span (amortized and
   order-tick p99), the 1000-unit order-tick span, searches per order, and `tick.wallMs`.
3. Report M4 invariants and engagement checks.
4. Write "WI-N5-01 verification": one row per budget with Met or Missed and, for a miss, the
   measured residual and its cause. Recommend whether the two option defaults should flip; the
   operator decides.

## Files likely to change

- `docs/roadmap/implementation/track-n-pathfinding-performance.md`

## Acceptance criteria

- [ ] Every budget in deep dive section 3 has a recorded verdict with numbers.
- [ ] M4 invariants and engagement checks pass, or the failure is reported and the card stops.
- [ ] The default-flip recommendation is recorded.

## Validation commands

```sh
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
  dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario PathfindingLoad --headless
```

## Out of scope / guardrails

- No source changes; a miss becomes a new card for the operator to schedule.

## On completion

- Set Status above to `Done` or `Blocked` with reason; set N5 `Done` in the track doc.
- Tick N5-1 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
