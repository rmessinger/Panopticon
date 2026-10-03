# Task Card `WI-W3a-31`: Add Western Front operational recording adapter

| Field | Value |
|---|---|
| Card ID | `WI-W3a-31` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W0-07`, `WI-W0-09`, `WI-A1-19`, `WI-A1-22`, `WI-W3a-12` |
| Diagrams | [A1-6 §4](../../deep-dives/a1-6-operational-recording.md#4-recording-modes-and-cadence), [W0 §4](../../deep-dives/w0-charter-fixture.md#4-layers-and-state-vector), and [W3a §10](../../deep-dives/w3a-sedan-1870.md#10-scale-and-run-families) |

## Goal

Implement `IReconstructableDomain` for Western Front so Sedan-scale runs record operational rollups and reconstruct tactical intervals from checkpoints.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a1-6-operational-recording.md` §4 and §6: reconstructable domain contract and reconstructor flow.
- `docs/roadmap/deep-dives/w0-charter-fixture.md` §4.4 and §8: tactical records and the Western Front domain adapter.
- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §10: Sedan scale and run families.
- `docs/roadmap/agent/cards/WI-W3a-12-period-ledger-families.md`: the preceding Western Front schema version bump.
- `docs/roadmap/agent/cards/WI-W0-07-western-front-domain-adapter.md`: existing adapter scope.
- `docs/roadmap/agent/cards/WI-W0-09-charter-fixture-evidence.md`: fixture recording evidence.

## Steps

1. Make `WesternFrontDomainModel` implement `IReconstructableDomain` beside `IDomainStateProjection`.
2. Drive `Cadence` from the W0 §7 scheduler boundaries: operational on each 60 s theater crossing (1,800 frames in tactical mode, every host frame in compressed mode) and checkpoint on each 3600 s campaign crossing (108,000 frames in tactical mode, 60 host frames in compressed mode).
3. Declare `tactical` as the granular layer excluded from the operational stream.
4. Exclude `soldier` records from `Operational` mode stream chunks. Keep them in checkpoints and reconstructed frames.
5. Add a `rollup` record kind to the tactical layer in a Western Front schema version bump after `WI-W3a-12`'s version. Fields use roles `None`: per-formation sums of soldier holdings, soldier count, centroid, and cells occupied.
6. Implement `CaptureRollups` to aggregate tactical soldier records by formation with ordinal keys and carry soldier totals for operational-boundary conservation checks.
7. Implement `PersistRuntime` for scheduler queues, mode-change requests, sequence counters, and other runtime state not stored in schema records.
8. Add a W0 charter fixture test that records an operational run, reconstructs the tactical interval from the frame-zero checkpoint, and compares it with `Full` mode live capture of the same seed.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/WesternFrontDomainModel.cs`
- `src/Panopticon.Simulation.WesternFront/WesternFrontStateSchema.cs` or schema owner file
- `src/Panopticon.Simulation.WesternFront/WesternFrontScheduler.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/WesternFrontOperationalRecordingTests.cs`

## Acceptance criteria

- [ ] `WesternFrontDomainModel` exposes `GranularLayers` containing only `tactical`.
- [ ] Operational and checkpoint boundaries coincide with the scheduler's operational and campaign ticks in both clock modes.
- [ ] `rollup` records contain per-formation soldier holdings sums, soldier count, centroid, and cells occupied with conservation role `None`.
- [ ] In `Operational` mode, `soldier` records do not appear in the operational stream and do appear in checkpoints and reconstructions.
- [ ] Rollups carry enough per-formation soldier totals for conservation checks at every operational boundary.
- [ ] `PersistRuntime` restores scheduler queues and sequence counters needed for deterministic continuation.
- [ ] The W0 charter fixture tactical interval reconstructed from the frame-zero checkpoint matches `Full` mode live capture of the same seed.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontOperationalRecording"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

Do not change historical Sedan datasets or run families. Do not add W6 save/load UI. On reconstruction divergence, preserve artifacts and report before changing deterministic behavior.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock W3a operational reference-run evidence cards that depend on A1-6.
- List follow-up cards to create here:
  - none
