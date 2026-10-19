# Task Card `WI-A5-07`: Version the portable run-bundle manifest

| Field | Value |
|---|---|
| Card ID | `WI-A5-07` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): run artifact foundation |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-A5-06` |
| Diagrams | [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) |

## Goal

Complete `RunOutput` as a versioned portable manifest and guarantee a reconstructable frame-zero
state.

## Context to read (and only this)

- `docs/roadmap/implementation/track-a-a5-scenario-visualization.md`: bundle schema.
- `src/Panopticon.Simulation/RunOutput.cs`.
- `src/Panopticon.Simulation/Recording/RunRecording.cs`, `SignatureTree.cs`,
  `WorldStateSnapshot.cs`.
- Existing run-output and tiered-recording tests.

## Steps

1. Add schema/artifact versions, domain and bucketing identity, T1 tolerances/cell settings, and
   relative artifact names.
2. Capture a frame-zero snapshot before the first delta.
3. Resolve references relative to the manifest directory.
4. Define loading defaults for older compatible JSON.
5. Add portability, compatibility, frame-zero, and mismatch tests.

## Files likely to change

- `src/Panopticon.Simulation/RunOutput.cs`
- `src/Panopticon.Simulation/Recording/`
- Existing recording tests

## Acceptance criteria

- [x] Moving the complete run directory preserves all references.
  - `A5ManifestTests.SaveToFile_MakesBundlePortableAcrossMoves`: bundle moved on disk reloads
    with the snapshot artifact resolving beside the new manifest location.
- [x] Frame zero reconstructs.
  - `RunRecording.Attach` captures the frame-zero baseline before the first delta
    (`SnapshotDeltaRecorder.CaptureInitial`); tested at cadence 1000 so only frame zero exists.
- [x] Incompatible versions and signature configurations fail clearly.
  - `FromJson` rejects `manifestSchemaVersion` above the understood version; validation names
    both `RunId`s and both configurations; tested for version, configuration drift, and
    legacy-without-configuration acceptance.
- [x] Older compatible manifests load with documented defaults.
  - Absent version normalizes to 1; absent `T1Configuration` defaults to
    `SignatureTolerances.Default`, `DefaultCellSize`, and `domain` bucket keys.
- [x] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~RunOutput|FullyQualifiedName~TieredRecording"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No ReplaySketch view or event capture.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
