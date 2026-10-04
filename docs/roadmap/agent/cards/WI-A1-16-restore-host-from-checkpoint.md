# Task Card `WI-A1-16`: Restore host from checkpoint

| Field | Value |
|---|---|
| Card ID | `WI-A1-16` |
| Roadmap phase | [Track A · A1](../../track-a-foundations.md): A1-6b restore host from checkpoint |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-A1-15` |
| Diagrams | [A1-6 §5.3](../../deep-dives/a1-6-operational-recording.md#53-engine-checkpoint) and [A1-6 §6.1](../../deep-dives/a1-6-operational-recording.md#61-flow) |

## Goal

Restore a `SimulationHost` from a `PNCK` checkpoint without `StartCampaign`, then prove it steps byte-identically to an uninterrupted run.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a1-6-operational-recording.md` §5.3 and §6.1: restore order and reconstruction boot flow.
- `src/Panopticon.Simulation/Data/Sav/SaveFile.cs:78-175`: read-mode `StartCampaign` behavior to bypass.
- `src/Panopticon.Simulation/SimulationHost.cs:68-147`: host construction, injected orders, and stepping.
- `src/Panopticon.Simulation/Recording/EngineCheckpoint.cs`: checkpoint format from `WI-A1-15`.

## Steps

1. Add a restore entry point that boots the same scenario and loads checkpoint engine state without calling `StartCampaign` from `SaveFile.Persist` read mode.
2. Restore random state, host frame, map time, pending injected orders, and domain runtime after the engine section.
3. After restore, compare a fresh domain-state capture with the checkpoint `domain-state` section when present.
4. Run an uninterrupted host and a restored host for at least one frame past the checkpoint.
5. Compare per-object persisted bytes after each stepped frame.

## Files likely to change

- `src/Panopticon.Simulation/Recording/EngineCheckpoint.cs`
- `src/Panopticon.Simulation/Data/Sav/SaveFile.cs` or a narrow restore seam
- `test/Panopticon.Simulation.Tests/Recording/EngineCheckpointRestoreTests.cs`

## Acceptance criteria

- [ ] Restore does not call `StartCampaign` when a host is already booted for the scenario.
- [ ] Per-object persisted bytes match between restored and uninterrupted hosts after at least one frame.
- [ ] Domain-state load check passes before stepping when the domain projects state.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~EngineCheckpointRestore"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

Do not implement the run reconstructor or operational stream. Do not broaden save-game player semantics.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A1-22`.
- List follow-up cards to create here:
  - none

## Result

Added `EngineCheckpointRestorer.Restore(host, checkpoint)` in
`src/Panopticon.Simulation/Recording/EngineCheckpointRestorer.cs`, restoring in order: engine
section via `SaveFile.LoadFromStream` (the booted host is already in-game, so the read-mode
start-campaign and skirmish-restart paths are bypassed by the WI-A1-13 guards; no
`StartCampaign` call), SageRandom state words, host frame/map-time/pending order schedule via
the new internal `SimulationHost.RestoreHostState`, optional domain runtime, and the
domain-state check: when the checkpoint carries a `domain-state` section, a fresh
`CaptureState` after the engine restore is hashed with `OperationalStateHasher` and compared
against the recorded section, throwing `InvalidOperationException` naming the frame on
mismatch. `EngineCheckpoint` gained internal `DecodeRandomSection`, `DecodeHostSection`, and
`DecodeDomainStateSection` decoders.

Tests (`test/Panopticon.Simulation.Tests/Recording/EngineCheckpointRestoreTests.cs`): a
restored RTS host matches an uninterrupted run per-object persisted bytes after 10 frames,
including a pending injected order schedule carried through the restore; a projected wrapper
domain restores with its domain-state check passing and rejects a stale domain-state section
with `InvalidOperationException`.

Finding for WI-A1-24: hosts whose domains create objects from in-memory definitions
(`TransportGraphDomainModel`'s `TransportNodeController`/`TransportVehicle`) cannot be
restored yet, because `ObjectDefinitionLookupTable.GetById` resolves names through the scoped
asset store and those definitions are never registered (each vehicle definition also carries
its route in module data, which per-name registration would not round-trip). Conformance for
such domains needs registered definitions plus domain-scoped runtime restore; deferred to the
reconstruction-conformance card.

Validation: EngineCheckpointRestoreTests 2/2 with the installed game; recording suites 43/43;
`dotnet format src/Panopticon.sln --verify-no-changes` clean;
`scripts/check_brace_newline.py` clean; `git diff --check` clean.

## Revision: storage-first A1-6 (#147)

- The domain-state check compares the persisted section bytes with a fresh capture directly (no hasher) and throws `InvalidDataException`.
- Load now calls `SleepyUpdateList.Remake()` after all objects are read; without it a mid-run checkpoint left the wake-frame heap out of order on the next step.
