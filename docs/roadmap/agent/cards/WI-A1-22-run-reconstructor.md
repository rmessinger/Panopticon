# Task Card `WI-A1-22`: Reconstruct operational runs

| Field | Value |
|---|---|
| Card ID | `WI-A1-22` |
| Roadmap phase | [Track A · A1](../../track-a-foundations.md): A1-6f run reconstructor |
| Status | `Done` |
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

## Result

Implemented `RunReconstructor` (Open/Reconstruct/ExportSlice) with `IReconstructionBootstrap`,
`IReconstructionSink` + `BufferedReconstructionSink`, and `ReconstructionDivergence` carrying the
boundary frame, both world hashes, and diverged bucket keys. Three fidelity findings surfaced by
the conformance tests and fixed here:

- Neutral map structures (bridges, supply docks, oil derricks) are teamless, and their owner was
  not derivable on restore: `GameObject.Persist` now persists the owner index for teamless objects
  (format version 10; version 9 retail saves still load).
- Input replay originally re-injected everything up front; the recorded host-section hashes
  capture the pending schedule as it evolved, so replay must reproduce the injection moment.
  `SimulationHost.OrdersInjected` (injection frame, dispatch frame, orders) now feeds the
  `OperationalInputs` records (which gained `InjectionFrame`), and the reconstructor re-injects
  after the injection frame's boundary verification completes.
- The PNOR string table is file-global: the reconstructor decodes every chunk from the start and
  collects only in-window records, instead of skipping earlier chunks.

Hash verification runs through the shared `OperationalStateHasher.HashHostState` (same code path
as the recorder), so writer and verifier cannot drift. Validation: 6/6 `RunReconstructorTests`
installed (identity refusal, verified delivery with per-object byte equality vs the uninterrupted
run, mid-run checkpoint replay, divergence stops all delivery, A1-5-loadable export slice);
recording suites 63/63 with env; format/brace/diff clean.
