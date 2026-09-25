# Task Card `WI-A5-07`: Version the portable run-bundle manifest

| Field | Value |
|---|---|
| Card ID | `WI-A5-07` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): run artifact foundation |
| Status | `Blocked` |
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

- [ ] Moving the complete run directory preserves all references.
- [ ] Frame zero reconstructs.
- [ ] Incompatible versions and signature configurations fail clearly.
- [ ] Older compatible manifests load with documented defaults.
- [ ] Standard validation passes.

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
