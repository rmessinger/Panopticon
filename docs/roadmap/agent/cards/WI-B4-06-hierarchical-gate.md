# Task Card `WI-B4-06`: Hierarchical pathfinding gate

| Field | Value |
|---|---|
| Card ID | `WI-B4-06` |
| Roadmap phase | [B4](../../track-b-scale.md): spatial and pathfinding scaling |
| Status | `Done` |
| Type | `investigation` |
| Effort | `M` |
| Prerequisites | `WI-B4-04`, `WI-B4-05` |
| Diagrams | [Hierarchical gate](../../deep-dives/b4-spatial-pathfinding-scaling.md#hierarchical-pathfinding-gated) |

## Goal

Decide, on measurement, whether a hierarchical or flow-field layer is needed: run the scale
anchor (M1-scale probe with squad movement and concurrent async requests), compare path
latency and tick cost against the budget, and either implement the coarse layer or record
the no-op decision with numbers.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4-spatial-pathfinding-scaling.md` section "Hierarchical
  pathfinding, gated".
- The M1 report's path measurements.

## Files

- `docs/roadmap/implementation/track-b-b4-spatial-scaling.md` (decision)
- `src/Panopticon.Simulation/Navigation/` only if the gate opens

## Steps

1. Author the scale anchor: squads issuing concurrent requests on a large map with the
   probe harness.
2. Measure request latency, queue depth over time, and tick cost.
3. Gate: if the latency budget holds, record the no-op decision with the numbers and close
  the card. If it fails, design the coarse overlay (cluster graph over the tile grid),
  implement, and re-measure.

## Acceptance criteria

- [x] The scale anchor runs and its measurements are recorded.
  - Full-scale anchor run: 14 bursts of 1000 async requests over a 4500-frame engagement,
    14000/14000 applied, 0 cancelled; recorded in
    `docs/roadmap/implementation/track-b-b4-spatial-scaling.md` (WI-B4-06 section) with
    artifacts in `/tmp/opencode/b4-06-anchor/`.
- [x] The gate decision is explicit with numbers either way.
  - No hierarchical layer: median wait 2 frames, p99 35, max 42; 208 of 14000 (1.5 percent)
    missed the 30-frame bound, all first-burst warm-up; backlog returns to zero between
    bursts; contact-window partition span within 21 percent of the WI-B4-03 baseline while
    absorbing the bursts.
- [x] If implemented: seeded determinism suites stay green and the re-measurement shows the
  budget met.
  - N/A: the gate held, no layer implemented.
- [x] Standard validation passes (see [guide](README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No hierarchical layer without a failed budget measurement behind it.
- B4a large-map work stays out.

## On completion

- Set Status above to `Done`.
- Record the decision in the B4 implementation plan and the track B status row.
- Append a result line to repo-root `.exchange`.
