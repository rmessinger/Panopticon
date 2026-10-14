# Task Card `WI-B4-01`: Partition counters and hot-path audit

| Field | Value |
|---|---|
| Card ID | `WI-B4-01` |
| Roadmap phase | [B4](../../track-b-scale.md): spatial and pathfinding scaling |
| Status | `Done` |
| Type | `source-edit` |
| Effort | `S` |
| Prerequisites | none (B1 landed) |
| Diagrams | [Partition class and sequence](../../deep-dives/b4-spatial-pathfinding-scaling.md#architecture-diagrams) |

## Goal

Add per-tick partition counters to the B1 record, then audit and record the pre-fix hot path
at full scale. The counters are the yardstick for `WI-B4-02`.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4-spatial-pathfinding-scaling.md` sections "Measured baseline"
  and "Partition hot path".
- `docs/roadmap/implementation/track-m-m1-capacity-probe.md`: probe commands and record
  layout.

## Files

- `src/Panopticon.Simulation/Logic/PartitionCellManager.cs`
- `src/Panopticon.Simulation/Benchmarks` recorder wiring for the probe
- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeRunner.cs`
- `test/Panopticon.Simulation.Tests/` partition tests

## Steps

1. Count per tick in `PartitionCellManager.Update`: dirty objects processed, candidate
   pairs emitted, exact hits. Expose them next to `Navigation.PathRequestCount` style
   statics or a small stats struct the probe samples.
2. Surface the counters in the probe's `BenchmarkRunRecord` statistics the same way
   `path.requestsPerTick` landed (WI-M1-02 precedent).
3. Add an install-free unit test that drives the manager with known objects and asserts
   counter values, including duplicate candidates in the pre-fix shape.
4. Run the M1 full-scale probe headless once with counters on and record the medians in
   the implementation plan.

## Acceptance criteria

- [x] The B1 record for the probe contains `partition.dirtyObjectsPerTick`,
  `partition.candidatePairsPerTick`, and `partition.exactHitsPerTick` statistics.
- [x] An install-free test pins counter semantics.
  - `PartitionCounterTests` (3 tests) pins the pre-fix duplicate-candidate shape for WI-B4-02
    to update.
- [x] A pre-fix full-scale baseline run is recorded in the implementation plan with the
  same profile and seed as M1.
  - Medians: 1040 dirty objects/tick, 850475 candidate pairs/tick, 714747 exact hits/tick;
    partition span 783.9 ms. See the WI-B4-01 baseline section of the implementation plan.
- [x] Standard validation passes (see [guide](README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~Partition"
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
  dotnet run --project src/Panopticon.Tools.TestHarness --no-build -- \
  --headless --built-in-scenario MassBattleCapacityProbe
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No behavior change in pair delivery; counters only.
- The full-scale run is hours of wall time; run it once.

## On completion

- Set Status above to `Done`.
- Record the baseline medians in `docs/roadmap/implementation/track-b-b4-spatial-scaling.md`.
- Reconcile `WI-B4-02` from `Blocked` to `Ready`.
- Append a result line to repo-root `.exchange`.
