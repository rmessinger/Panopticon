# Task Card `WI-A1-21`: Assemble operational run bundle

| Field | Value |
|---|---|
| Card ID | `WI-A1-21` |
| Roadmap phase | [Track A · A1](../../track-a-foundations.md): A1-6e operational run bundle |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A1-15`, `WI-A1-17`, `WI-A1-20` |
| Diagrams | [A1-6 §5](../../deep-dives/a1-6-operational-recording.md#5-run-bundle) and [A1-6 §5.1](../../deep-dives/a1-6-operational-recording.md#51-manifest-additions) |

## Goal

Wire operational recording into `RunRecording`, write checkpoints at boundary frames, and add nullable manifest fields without changing `Full` output.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a1-6-operational-recording.md` §5, §5.1, and §5.3: bundle layout, manifest additions, and checkpoint timing.
- `src/Panopticon.Simulation/Recording/RunRecording.cs:382-398`: attachment lifecycle.
- `src/Panopticon.Simulation/RunOutput.cs:62-110`: manifest properties and JSON options.
- `src/Panopticon.Simulation/SimulationHost.cs:68-147`: `TickComplete` boundary timing.
- `src/Panopticon.Simulation/Recording/EngineCheckpoint.cs` and `OperationalStreamWriter.cs`: artifacts from prerequisite cards.

## Steps

1. In `Operational` mode, create the bundle layout with `run.json`, `operational.bin`, and `checkpoints/`.
2. Write the frame-zero checkpoint at start and later checkpoints in `TickComplete` of checkpoint-boundary frames after boundary systems ran.
3. Write operational chunks at operational boundaries through `OperationalStreamWriter`.
4. Extend `RunOutput` with nullable `recordingMode`, `identity`, `operationalStreamPath`, and `checkpoints` properties using `JsonIgnoreCondition.WhenWritingNull`.
5. Write the manifest at start and rewrite it at completion so a crashed run keeps a manifest.
6. Record checkpoint frame, relative path, byte size, and XxHash3 file hash.
7. Add tests that `Full` mode output stays byte-identical and `Operational` mode writes the expected layout and manifest fields.

## Files likely to change

- `src/Panopticon.Simulation/Recording/RunRecording.cs`
- `src/Panopticon.Simulation/RunOutput.cs`
- `src/Panopticon.Simulation/Recording/OperationalRecorder.cs`
- `test/Panopticon.Simulation.Tests/Recording/OperationalRunBundleTests.cs`

## Acceptance criteria

- [ ] `Full` mode JSON and existing artifacts stay byte-identical.
- [ ] `Operational` mode writes `operational.bin`, `checkpoints/000000000.ckpt`, and later boundary checkpoints.
- [ ] Manifest checkpoint entries contain relative path, frame, byte size, and XxHash3 hash.
- [ ] Checkpoint writes occur from `TickComplete` of boundary frames.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~OperationalRunBundle|RunRecording"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

Do not implement `RunReconstructor`. Do not make manifest fields non-null in `Full` mode.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A1-22`.
- List follow-up cards to create here:
  - none
