# Task Card `WI-A1-11`: State snapshot and delta recording

| Field | Value |
|---|---|
| Card ID | `WI-A1-11` |
| Roadmap phase | [Track A · A1](../../track-a-foundations.md): Headless simulation host, A1-5 remainder |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A1-07` (the run-output framework it extends) |
| Diagrams | [A1 §5](../../deep-dives/a1-headless-host.md#5-architecture-diagrams) |

## Goal

Record periodic world snapshots and per-frame deltas so any frame of a headless run can be
reconstructed for analysis without assuming a re-run reproduces it bit-for-bit
([a1-headless-host.md §2.5](../../deep-dives/a1-headless-host.md#25-result-outputs)).

## Context to read (and only this)

- `docs/roadmap/deep-dives/a1-headless-host.md` §2.5.
- `src/Panopticon.Simulation/RunOutput.cs`: the artifact family this joins.
- `src/Panopticon.Simulation/Logic/GameLogic.cs` and `src/Panopticon.Data.Sav/StatePersister.cs`: the
  existing whole-world serialization to reuse, not duplicate.

## Steps

1. Define the snapshot interval and the delta granularity (per-frame object add/remove/modify)
   sized so a long run stays bounded.
2. Serialize snapshots through the existing save-format state persistence rather than a new
   object walker.
3. Emit deltas as the difference between consecutive sampled states.
4. Add a round-trip test reconstructing an arbitrary sampled frame from snapshot + deltas.

## Files likely to change

- `src/Panopticon.Simulation/` (snapshot/delta recording types)
- `test/Panopticon.Simulation.Tests/` (reconstruction test)

## Acceptance criteria

- [ ] A run writes snapshots and deltas reloadable by test code.
- [ ] Reconstructing a sampled frame matches a directly captured state.
- [ ] Output size is bounded by the snapshot interval.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement visualization or comparison UI (A5).
- The artifact must stand alone without requiring re-simulation.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`.
- Follow-up cards: none new.
