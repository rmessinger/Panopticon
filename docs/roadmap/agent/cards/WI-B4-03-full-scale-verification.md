# Task Card `WI-B4-03`: Full-scale verification re-run

| Field | Value |
|---|---|
| Card ID | `WI-B4-03` |
| Roadmap phase | [B4](../../track-b-scale.md): spatial and pathfinding scaling |
| Status | `Done` |
| Type | `investigation` |
| Effort | `M` |
| Prerequisites | `WI-B4-02` |
| Diagrams | [Budget](../../deep-dives/b4-spatial-pathfinding-scaling.md#budget) |

## Goal

Re-run the unchanged M1 full-scale probe and show the partition fix holds the budget:
partition span at or under 10 ms median at contact, counters steady, engagement unchanged.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4-spatial-pathfinding-scaling.md` section "Budget".
- `docs/roadmap/implementation/track-m-m1-capacity-probe.md`: run configuration and medians
  to compare against.
- The `WI-B4-01` baseline recorded in the B4 implementation plan.

## Files

- `docs/roadmap/implementation/track-b-b4-spatial-scaling.md` (results)

## Steps

1. Run the M1 probe headless with the same profile, seed, and frame budget.
2. Compare against M1 and the `WI-B4-01` baseline: `system.partition.spanMs` median and
   deciles, `tick.allocBytes`, engagement counts (spawns, losses, sorties), and the new
   partition counters.
3. Record the comparison table and the artifacts location in the implementation plan.
4. If the budget is missed, record the residual cost shape and stop: further partition work
   is a new card, not a quiet retry loop.

## Acceptance criteria

- [x] A full-scale post-fix run exists with `allPassed` true on engagement checks.
  - 2026-10-12 run, 9000/9000 frames, all checks passed; engagement counts identical to the
    baseline run (878/877 alive, 122/123 destroyed, 37 launches, 22 recoveries).
- [x] `system.partition.spanMs` median at contact deciles is at or under 10 ms, or the miss
  and its residual shape are recorded explicitly.
  - Missed and recorded: median 259.4 ms (contact deciles 225-541 ms) against the 10 ms
    budget; residual shape is ~1040 membership rebuilds plus ~261k genuine overlap tests per
    contact tick (~1 microsecond per pair). Next levers named in the plan: cheaper exact
    test, resting-contact throttling, B3 parallelism.
- [x] The implementation plan carries the before and after medians table.
- [x] Standard validation passes (see [guide](README.md#standard-validation)).
  - Full non-interactive suite green apart from the known set at WI-B4-02 commit time; this
    card changes no source, only documentation.

## Validation commands

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
  dotnet run --project src/Panopticon.Tools.TestHarness --no-build -- \
  --headless --built-in-scenario MassBattleCapacityProbe
dotnet test src --filter "Category!=Interactive"
```

## Out of scope / guardrails

- Do not tune the probe, map, or roster to flatter the numbers.
- Determinism-enforcing failures stop the card: preserve artifacts, report, wait.

## On completion

- Set Status above to `Done`.
- Record results in `docs/roadmap/implementation/track-b-b4-spatial-scaling.md`.
- Append a result line to repo-root `.exchange`.
