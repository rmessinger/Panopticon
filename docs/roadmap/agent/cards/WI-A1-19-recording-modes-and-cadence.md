# Task Card `WI-A1-19`: Add recording modes and cadence

| Field | Value |
|---|---|
| Card ID | `WI-A1-19` |
| Roadmap phase | [Track A · A1](../../track-a-foundations.md): A1-6d recording modes and cadence |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [A1-6 §4](../../deep-dives/a1-6-operational-recording.md#4-recording-modes-and-cadence) |

## Goal

Add `Full` and `Operational` recording mode contracts while keeping `Full` as the default and preserving byte-identical existing run outputs.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a1-6-operational-recording.md` §4: contracts for modes, cadence, and reconstructable domains.
- `src/Panopticon.Simulation/Recording/RunRecording.cs:15-125` and `:382-398`: current full recorder and attach flow.
- `src/Panopticon.Simulation/RunOutput.cs:62-110`: run output options and JSON defaults.
- `src/Panopticon.Simulation/Simulation/ISimulationDomainModel.cs`: sibling domain seam.

## Steps

1. Add `RecordingMode { Full, Operational }` with `Full` as the default path.
2. Add `RecordingCadence` with `IsOperationalBoundary` and `IsCheckpointBoundary` delegates.
3. Add `RecordingOptions` with nullable default cadence and `RecordT1` as described in §4.
4. Add `IReconstructableDomain` with `Cadence`, `GranularLayers`, `CaptureRollups`, `DrainOperationalEvents`, and `PersistRuntime`.
5. Wire options into `RunRecording.Attach` without changing behavior when options are omitted.
6. Add tests proving omitted options produce byte-identical `Full` output JSON and artifact bytes compared with the pre-card default fixture.

## Files likely to change

- `src/Panopticon.Simulation/Recording/RecordingMode.cs`
- `src/Panopticon.Simulation/Recording/RecordingOptions.cs`
- `src/Panopticon.Simulation/Simulation/IReconstructableDomain.cs`
- `src/Panopticon.Simulation/Recording/RunRecording.cs`
- `test/Panopticon.Simulation.Tests/Recording/RecordingOptionsTests.cs`

## Acceptance criteria

- [ ] `RunRecording.Attach` with no options follows the existing `Full` path.
- [ ] `Full` output JSON and artifact bytes remain byte-identical for the existing fixture.
- [ ] `Operational` mode can be selected but does not yet require an operational stream writer.
- [ ] The default cadence is 1,800 operational frames and 108,000 checkpoint frames when no domain cadence is present.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~RecordingOptions|RunRecording"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

Do not write `operational.bin` or checkpoints in this card. Do not change the semantics of A1-5 snapshot and delta recording in `Full` mode.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A1-20` and `WI-W3a-31`.
- List follow-up cards to create here:
  - none

## Result

Implemented 2026-10-04 on `track-a-a1-6a-foundations` (commit follows `24b13a647`).

- Added `RecordingMode` (`Full`, `Operational`), `RecordingCadence` (validated boundary delegates), and `RecordingOptions` (`Mode`, `BundleDirectory`, `DefaultCadence`, `RecordT1`, static `CreateDefaultCadence()` = 1800 operational / 108000 checkpoint frames at 30 Hz).
- Added `IReconstructableDomain` (`Cadence`, `GranularLayers`, `CaptureRollups`, `DrainOperationalEvents`, `PersistRuntime`) plus a minimal `DomainEvent(Kind, Detail?)` record; no domain implements it yet (W3a-31 later).
- `WorldStateSampler` gained an optional boundary filter; `RunRecording.Attach` gained an optional `RecordingOptions` parameter. Effective cadence = domain cadence, else options default, else the built-in default. Omitted options leave the `Full` path untouched; `Operational` mode samples only at boundaries and skips live T2 (null).
- `RunOutput` unchanged (manifest fields are A1-21 scope).
- Tests (`RecordingOptionsTests`, 4): default cadence values; `Full`-options vs omitted-options JSON byte-identical after normalizing the per-attach `runId` and wall-clock `*Milliseconds` fields; Operational mode samples frames 5/10/15/20 under a %5 cadence with T2 null and no domain cadence; domain cadence wins over the caller default (frames 3/6/9). Host frames start at 1, so frame 0 is never sampled.
- Validation: 26 recording-suite tests unmodified and green, `dotnet format` verify clean, `check_brace_newline` clean, `git diff --check` clean.
