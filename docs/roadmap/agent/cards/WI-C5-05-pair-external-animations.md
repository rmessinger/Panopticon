# Task Card `WI-C5-05`: Pair external animations

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-C5-05` |
| Roadmap phase | [Track C · C5](../../track-c-external-assets.md): Authoring and validation tooling |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4-08`, `WI-C5-04` |
| Diagrams | [C5 architecture diagrams](../../deep-dives/c5-external-model-tooling.md#12-architecture-diagrams) |

## Goal

Add same-document and cross-document animation pairing plus validated importer options and
preview-only transform, camera, lighting, animation sample, and gated morph controls.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c5-external-model-tooling.md` sections 4, 5, and 7.
- `docs/roadmap/deep-dives/c4-gltf-importer.md` sections 4 and 7.
- `src/Panopticon.Simulation/Content/Importing/ExternalAnimationDescriptor.cs` (new, from `WI-C5-01`).
- `src/Panopticon.Simulation/Content/Importing/Gltf/GltfImportOptions.cs` (new, from `WI-C4-01`).
- `src/Panopticon.Simulation/Content/Importing/Gltf/GltfAnimationImporter.cs` (new, from `WI-C4-08`).
- `src/Panopticon.Tools.ReplaySketch.Core/Services/ExternalModelSession.cs` (new, from `WI-C5-04`).
- `src/Panopticon.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelSnapshot.cs` (new, from
  `WI-C5-04`).

## Steps

1. Add immutable `ExternalModelImportOptions` with positive finite `UnitsPerMeter`, named feature
   gates, and per-target allowed morph-weight ranges. Defaults keep every C6 feature disabled.
2. Add immutable `ExternalModelPreviewOptions` for model yaw, pitch, roll, translation; camera
   orbit, elevation, distance, and field of view; background and lighting preset; selected
   animation; sample time; and gated per-target morph weights.
3. Validate finite numeric values, positive camera and scale values, sample time within the
   selected duration, and morph weights within declared ranges only when the owning gate is
   enabled.
4. Add explicit animation pairing that can select a descriptor from the model catalog or from one
   separately mounted animation document. Preserve each descriptor's stable C4 asset key.
5. Extend `ExternalModelSnapshot` with the optional animation source, selected pairing, importer
   options, and preview options.
6. On animation source replacement, cancel current work, increment generation, dispose the old
   animation mount, inspect the new catalog, and clear an invalid prior selection.
7. On importer option changes, cancel and rerun inspection. On preview-only changes, publish a new
   snapshot without reopening or modifying source data.
8. Extend `ExternalModelReport` with animation pairing, importer options, and preview corrections.
   Reset restores default C4 import scale and default preview values. Reports and requests receive
   every non-default value.
9. Add tests for same-source and other-source selection, duplicate names with stable keys, invalid
   ranges, generation races, importer reload, preview-only updates, feature-gate hiding, and reset.

## Files likely to change

- `src/Panopticon.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelImportOptions.cs` (new)
- `src/Panopticon.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelPreviewOptions.cs` (new)
- `src/Panopticon.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalAnimationPairing.cs` (new)
- `src/Panopticon.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelSnapshot.cs` (new, from
  `WI-C5-04`)
- `src/Panopticon.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelReport.cs` (new, from
  `WI-C5-03`)
- `src/Panopticon.Tools.ReplaySketch.Core/Services/ExternalModelSession.cs` (new, from `WI-C5-04`)
- `test/Panopticon.Tools.ReplaySketch.Tests/ExternalAnimationPairingTests.cs` (new)

## Acceptance criteria

- [ ] Animation selection uses stable descriptor keys and supports model or separate source roots.
- [ ] `UnitsPerMeter` changes rerun inspection; preview transforms and camera changes do not.
- [ ] Axis controls remain post-import preview transforms and do not alter C4 basis conversion.
- [ ] C6 gates default off; unavailable morph controls stay hidden and cannot affect requests.
- [ ] Invalid options fail before inspection or preview execution.
- [ ] Reset returns canonical C4 import and preview defaults without modifying source files.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ExternalAnimationPairingTests|FullyQualifiedName~ExternalModelSessionTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement morph import, humanoid retargeting, source rewriting, or real-time playback.
- Do not guess model scale from bounds or expose an alternate coordinate basis.
- Do not match duplicate animations by display name when stable keys differ.
- Do not let preview-only changes mutate imported arrays or importer options.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C5-02` and `WI-C5-03` are done, change `WI-C5-06` to `Ready`.
- Append a result line to `.exchange`.
