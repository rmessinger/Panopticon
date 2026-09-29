# Task Card `WI-A1-16`: Restore host from checkpoint

| Field | Value |
|---|---|
| Card ID | `WI-A1-16` |
| Roadmap phase | [Track A · A1](../../track-a-foundations.md): A1-6b restore host from checkpoint |
| Status | `Draft` |
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
