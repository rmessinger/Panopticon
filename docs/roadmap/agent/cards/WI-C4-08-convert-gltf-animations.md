# Task Card `WI-C4-08`: Convert glTF animations

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-C4-08` |
| Roadmap phase | [Track C · C4](../../track-c-external-assets.md): First production external format |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C2-06`, `WI-C4-01`, `WI-C4-02`, `WI-C4-04`, `WI-C4-05` |
| Diagrams | [C4 architecture diagrams](../../deep-dives/c4-gltf-importer.md#11-architecture-diagrams) |

## Goal

Convert one indexed glTF animation into named C2 channels whose values are offsets from the
source document's rest transform and preserve `LINEAR` or `STEP` interpolation.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4-gltf-importer.md` sections 3, 4, and 7.
- `src/OpenSage.Game/Content/Importing/ImportedAnimation.cs`.
- `src/OpenSage.Game/Content/Importing/ImportedAnimationChannel.cs`.
- `src/OpenSage.Game/Content/Importing/ImportedAnimationTarget.cs` (new from `WI-C2-01`).
- `src/OpenSage.Game/Content/Importing/ImportedKeyframe.cs`.
- `src/OpenSage.Game/Content/Loaders/ImportedAnimationMaterializer.cs`.
- `src/OpenSage.Game/Graphics/Animation/ModelAnimation.cs`.
- `src/OpenSage.Game/Content/Importing/Gltf/GltfAssetKey.cs` (new from `WI-C4-01`).
- `src/OpenSage.Game/Content/Importing/Gltf/GltfScenePlan.cs` (new from `WI-C4-05`).

## Steps

1. Use the parsed animation index from `GltfAssetKey`. Return null for an invalid animation key or
   missing root, and throw `InvalidDataException` when the index is outside the loaded document.
2. Use the authored animation name when non-empty. Otherwise use
   `<model-name>.Animation<index>`.
3. Accept channels targeting any imported bone, including unskinned mesh and attach nodes.
   Require every targeted node to belong to the selected-scene node set and have a non-empty
   authored name unique under `OrdinalIgnoreCase`.
4. Validate finite, non-negative, monotonically increasing input times. Normalize finite
   quaternion samples and set duration to the maximum channel time.
5. Convert translation and rotation samples through the C4 basis and compute offsets from the
   source document's rest transform:
   `animatedRotation * inverse(restRotation)` and
   `transform(animatedTranslation - restTranslation, inverse(restRotation))`.
6. Emit named translation X/Y/Z and quaternion channels. Map `LINEAR` and `STEP`; reject
   `CUBICSPLINE`.
7. Omit scale channels only when every sample is identity. Reject non-identity scale, weight
   channels, empty animations, targets outside the selected-scene node set, and unnamed or
   ambiguous targets.
8. Add tests for fallback names, reordered runtime binding, non-identity rest transforms,
   coordinate conversion, step holding, duration, and every unsupported channel form.

## Files likely to change

- `src/OpenSage.Game/Content/Importing/Gltf/GltfAnimationConverter.cs` (new)
- `src/OpenSage.Game/Content/Importing/Gltf/GltfAnimationImporter.cs` (new)
- `src/OpenSage.Game.Tests/Content/Importing/GltfAnimationImporterTests.cs` (new)
- `src/OpenSage.Game.Tests/Graphics/Animation/ModelAnimationTests.cs`

## Acceptance criteria

- [ ] Animation keys select a stable zero-based source index.
- [ ] Named channels bind correctly to a differently ordered runtime hierarchy.
- [ ] A named rigid node can animate an attached unskinned sub-object.
- [ ] Offset times rest reconstructs the authored local translation and rotation at each key.
- [ ] Linear and step interpolation reach bound playback unchanged.
- [ ] Scale, weights, cubic interpolation, invalid times, and invalid targets fail with the
      documented exception category.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~GltfAnimationImporterTests|FullyQualifiedName~ModelAnimationTests|FullyQualifiedName~AnimationInstanceTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not resample, approximate cubic curves, or add scale or weight runtime channels.
- Do not target generated unnamed-node identifiers.
- Do not perform humanoid retargeting or bind-pose compensation across documents.
- Do not share mutable SharpGLTF objects between model and animation imports.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C4-07` and `WI-C4-09` are done, change `WI-C4-10` to `Ready`.
- Append a result line to `.exchange`.
