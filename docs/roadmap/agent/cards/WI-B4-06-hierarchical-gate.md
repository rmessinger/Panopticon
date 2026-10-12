# Task Card `WI-B4-06`: Hierarchical pathfinding gate

| Field | Value |
|---|---|
| Card ID | `WI-B4-06` |
| Roadmap phase | [B4](../../track-b-scale.md): spatial and pathfinding scaling |
| Status | `Blocked` |
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

- [ ] The scale anchor runs and its measurements are recorded.
- [ ] The gate decision is explicit with numbers either way.
- [ ] If implemented: seeded determinism suites stay green and the re-measurement shows the
  budget met.
- [ ] Standard validation passes (see [guide](README.md#standard-validation)).

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
