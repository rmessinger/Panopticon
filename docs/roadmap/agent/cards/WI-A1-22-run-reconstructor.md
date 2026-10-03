# Task Card `WI-A1-22`: Reconstruct operational runs

| Field | Value |
|---|---|
| Card ID | `WI-A1-22` |
| Roadmap phase | [Track A · A1](../../track-a-foundations.md): A1-6f run reconstructor |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A1-16`, `WI-A1-21` |
| Diagrams | [A1-6 §6.1](../../deep-dives/a1-6-operational-recording.md#61-flow) and [A1-6 §6.2](../../deep-dives/a1-6-operational-recording.md#62-verification-failure) |

## Goal

Add `RunReconstructor` to restore from bundle checkpoints, replay inputs, deliver frame sinks, and stop on operational hash divergence.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a1-6-operational-recording.md` §5.4 and §6.1-§6.2: identity and reconstruction flow.
- `src/Panopticon.Simulation/Recording/OperationalStreamReader.cs`: chunk and input records.
- `src/Panopticon.Simulation/Recording/EngineCheckpoint.cs`: restore entry point from `WI-A1-16`.
- `src/Panopticon.Simulation/Recording/OperationalStateHasher.cs`: verification hashes.
- `src/Panopticon.Simulation/Recording/WorldStateSnapshot.cs`: `ExportSlice` artifact compatibility.

## Steps

1. Add `IReconstructionBootstrap` that boots the game, scenario, and domain model for a bundle.
2. Add `RunReconstructor.Open(bundle, bootstrap, options)` that validates identity and refuses mismatch unless `AllowIdentityMismatch` is set.
3. Implement `Reconstruct(fromFrame, toFrame, sink)` using the nearest bundle checkpoint at or before `fromFrame`.
4. Re-inject logged inputs at their recorded dispatch frames.
5. Deliver `WorldStateSummary` and `DomainStateFrame` to sinks from `fromFrame` through `toFrame`.
6. At every operational boundary, hash reconstructed state and compare with recorded `Hashes` records.
7. Return `ReconstructionDivergence` with boundary frame, recorded and reconstructed world hashes, and diverged bucket keys on first mismatch.
8. Add `ExportSlice(from, to, path)` writing a standard A1-5 snapshot and delta artifact for existing readers.

## Files likely to change

- `src/Panopticon.Simulation/Recording/RunReconstructor.cs`
- `src/Panopticon.Simulation/Recording/IReconstructionBootstrap.cs`
- `src/Panopticon.Simulation/Recording/ReconstructionSink.cs`
- `src/Panopticon.Simulation/Recording/ReconstructionDivergence.cs`
- `test/Panopticon.Simulation.Tests/Recording/RunReconstructorTests.cs`

## Acceptance criteria

- [ ] Identity mismatch is refused unless `AllowIdentityMismatch` is set.
- [ ] Reconstruction from a checkpoint delivers the requested frame range to the sink.
- [ ] Recorded and reconstructed operational hashes match for the fixture run.
- [ ] A forced state mismatch returns `ReconstructionDivergence` and does not deliver frames after the last matching boundary.
- [ ] `ExportSlice` output loads through existing A1-5 readers.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~RunReconstructor"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

Do not implement the LRU derived-checkpoint cache. On determinism mismatch, preserve artifacts and report before attempting a fix outside this card.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A1-23`, `WI-A1-24`, and `WI-W3a-31`.
- List follow-up cards to create here:
  - none
