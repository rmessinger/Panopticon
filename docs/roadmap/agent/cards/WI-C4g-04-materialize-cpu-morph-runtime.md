# Task Card `WI-C4g-04`: Materialize CPU morph runtime

| Field | Value |
|---|---|
| Card ID | `WI-C4g-04` |
| Roadmap phase | [Track C · C4g](../../track-c-external-assets.md): EE2 morph animation |
| Status | `Blocked` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-C4g-02` |
| Diagrams | [C4g architecture diagrams](../../deep-dives/c4g-ee2-morph-animation.md#10-architecture-diagrams) |

## Goal

Materialize C6 morph target sets into a gated CPU morph path that updates per-instance vertex
buffers before existing skinning.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4g-ee2-morph-animation.md` sections 6, 7, and 8.
- `docs/roadmap/deep-dives/c6-extended-model-features.md` section 4.
- `src/OpenSage.Game/Graphics/ModelMesh.Imported.cs:20-75`.
- `src/OpenSage.Game/Graphics/ModelMeshInstance.cs`.
- `src/OpenSage.Game/Graphics/ModelInstance.cs:185-251`.
- `src/OpenSage.Game/Assets/Shaders/Mesh.h:5-76`.

## Steps

1. Change Status to `In progress`.
2. Add runtime model types for materialized morph target sets and per-target position deltas.
3. Preserve the existing immutable model vertex buffer for feature-disabled and non-morph meshes.
4. Add a per-`ModelMeshInstance` dynamic vertex buffer for morph-enabled meshes.
5. Add a method to set the current morph weight vector for a primitive.
6. On update or before render, rebuild the dynamic vertex buffer from base vertices plus weighted
   deltas, then let existing skinning continue unchanged.
7. Add tests that compare CPU morphed positions against a small synthetic mesh at weights 0, 0.5,
   and 1.
8. Add a feature-disabled parity test that verifies the existing static buffer path is still used.

## Files likely to change

- `src/OpenSage.Game/Graphics/ModelMesh.Imported.cs`
- `src/OpenSage.Game/Graphics/ModelMesh.cs`
- `src/OpenSage.Game/Graphics/ModelMeshInstance.cs`
- `src/OpenSage.Game/Graphics/ModelInstance.cs`
- `src/OpenSage.Game/Graphics/ModelMorphTargetSet.cs` (new)
- `src/OpenSage.Game.Tests/Graphics/ModelMorphTests.cs` (new)

## Acceptance criteria

- [ ] Feature-disabled meshes allocate the same static vertex path as before.
- [ ] Morph-enabled mesh instances own per-instance dynamic vertex data.
- [ ] CPU morphing applies position deltas before existing skinning.
- [ ] Synthetic runtime tests pass for weights 0, 0.5, and 1.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ModelMorphTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add shader morph accumulation.
- Do not change W3D vertex layout behavior when no morph targets are present.
- Do not change skinning matrix math.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C4g-03` is done, change `WI-C4g-05` to `Ready`.
- Append a result line to `.exchange`.
