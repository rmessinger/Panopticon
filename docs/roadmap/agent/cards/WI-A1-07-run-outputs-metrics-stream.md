# Task Card `WI-A1-07`: Emit metrics and events run output from a headless run

| Field | Value |
|---|---|
| Card ID | `WI-A1-07` |
| Roadmap phase | [Track A · A1](../../track-a-foundations.md): Headless simulation host, sub-phase A1-5 |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A1-06` (the host), `WI-A2-03` (seeded randomness, so runs are comparable per the [relaxed-determinism Decision](../../open-questions.md)) |
| Diagrams | [A1 §5](../../deep-dives/a1-headless-host.md#5-architecture-diagrams) |

## Goal

Emit the first A1-5 run output: a metrics and events stream plus the run's terminal `GameOutcome`,
written to disk from a `SimulationHost` run in a form the A5 visualizer can reload. This is the T0
aggregate tier only.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a1-headless-host.md` §2.5, §3 (A1-5 row).
- `docs/roadmap/deep-dives/a5-visualization.md` §5.1: the T0, T1, T2 tier definitions. This card
  implements T0 only.
- `src/OpenSage.Game/SimulationHost.cs`: produced by `WI-A1-06`; the per-tick hook this card feeds.
- `src/OpenSage.Game/Logic/GameLogic.cs`: the logic frame counter and `Random` seed to record.
- The P0 harness assertion and JSON output path under `src/OpenSage.Tools.ReplaySketch.Core/`:
  reuse its serialization conventions rather than inventing a second output format.

## Steps

1. Add a run-output record type holding: run id, seed, scenario identifier, total logic frames,
   wall-clock duration, the terminal `GameOutcome`, and a time series of per-player metric samples.
2. Sample metrics on the host's per-tick hook at a configurable frame interval, defaulting to a
   value that keeps the output bounded for long runs. Do not sample every frame by default.
3. Sample only metrics that already exist on `Player` and `GameLogic`. Do not add new gameplay
   counters in this card.
4. Serialize to JSON using the same conventions as the existing P0 harness output so A5 has one
   reader to write, not two.
5. Add a test that runs the host headlessly, writes the output, reloads it, and asserts the frame
   count, seed, and terminal outcome round-trip.
6. Run `dotnet format src` and confirm `dotnet format src --verify-no-changes`.

## Files likely to change

- `src/OpenSage.Game/` (new run-output types)
- `src/OpenSage.Game.Tests/` (new test file)

## Acceptance criteria

- [ ] A headless run writes a JSON run output containing seed, frame count, terminal `GameOutcome`,
      and a bounded metric time series.
- [ ] The output round-trips through a reload in a test.
- [ ] Output size is bounded by the sampling interval, not linear in per-entity state.
- [ ] No new serialization convention is introduced alongside the P0 harness one.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- T1 hierarchical signature trees and T2 triggered detail capture are out of scope. They need the
  tolerance and bucketing design from A2 and A5-4; do not guess at it.
- Do not implement snapshot or delta recording here.
- Do not add replay export; the existing `.rep` path already covers input recording.
- Do not build any visualization. That is A5-3.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Do not tick A1-5 in [`implementation-checklist.md`](../../implementation-checklist.md): this card
  delivers only the metrics and events portion. Note the partial progress under the item instead.
- Append a result line to repo-root `.exchange`.
- Follow-up cards to create:
  - T1 signature-tree recording, once A2 defines tolerances and A5-4 defines bucketing.
  - T2 triggered detail capture with a bounded ring buffer.
  - State snapshot and delta recording.
