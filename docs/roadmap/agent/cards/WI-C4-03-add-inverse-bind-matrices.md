# Task Card `WI-C4-03`: Add inverse-bind matrices

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-C4-03` |
| Roadmap phase | [Track C · C4](../../track-c-external-assets.md): First production external format |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-C1-14` |
| Diagrams | [C4 architecture diagrams](../../deep-dives/c4-gltf-importer.md#11-architecture-diagrams) |

## Goal

Carry inverse-bind matrices through the neutral skeleton and runtime model, while preserving
identity behavior for every existing W3D bone.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4-gltf-importer.md` section 6.
- `src/OpenSage.Game/Content/Importing/ImportedBone.cs`.
- `src/OpenSage.Game/Content/Importing/W3d/W3dModelImporter.cs`.
- `src/OpenSage.Game/Content/Loaders/ImportedModelMaterializer.cs`.
- `src/OpenSage.Game/Graphics/ModelBone.cs`.
- `src/OpenSage.Game/Graphics/ModelBoneHierarchy.cs`.
- `src/OpenSage.Game/Graphics/ModelInstance.cs`: bind-pose initialization and skinning upload.
- `src/OpenSage.Game.Tests/Content/Importing/ImportedAssetTests.cs`.
- `src/OpenSage.Game.Tests/Content/Importing/W3dModelImporterTests.cs`.

## Steps

1. Add immutable `Matrix4x4 InverseBindMatrix` to `ImportedBone` and `ModelBone`.
2. Keep the existing imported-bone construction path as an identity inverse-bind compatibility
   overload, and make W3D conversion use identity explicitly or through that overload.
3. Copy the matrix through `ModelBoneHierarchy` without changing parent ordering or bind TRS.
4. Extract a small internal CPU helper used by `ModelInstance` to fill skinning matrices in
   repository row-vector order:
   `bone.InverseBindMatrix * relativeBoneTransforms[bone.Index]`.
5. Use the helper for the GPU upload and keep the existing bind-pose and non-skinned paths
   unchanged.
6. Add tests for copied non-identity matrices, exact multiplication order, identity W3D output,
   and unchanged W3D imported descriptors.

## Files likely to change

- `src/OpenSage.Game/Content/Importing/ImportedBone.cs`
- `src/OpenSage.Game/Content/Importing/W3d/W3dModelImporter.cs`
- `src/OpenSage.Game/Graphics/ModelBone.cs`
- `src/OpenSage.Game/Graphics/ModelBoneHierarchy.cs`
- `src/OpenSage.Game/Graphics/ModelInstance.cs`
- `src/OpenSage.Game.Tests/Content/Importing/ImportedAssetTests.cs`
- `src/OpenSage.Game.Tests/Content/Importing/W3dModelImporterTests.cs`
- `src/OpenSage.Game.Tests/Graphics/ModelSkinningTests.cs` (new)

## Acceptance criteria

- [ ] Imported and runtime bones retain the exact supplied inverse-bind matrix.
- [ ] Skinning matrices multiply inverse bind before relative transform.
- [ ] The multiplication-order test uses non-commuting matrices.
- [ ] Every W3D-created bone has identity inverse bind and unchanged upload results.
- [ ] Non-skinned models allocate no new resource.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ModelSkinningTests|FullyQualifiedName~ImportedAssetTests|FullyQualifiedName~W3dModelImporterTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add glTF parsing or validate glTF inverse binds.
- Do not change shader matrix layout, transpose behavior, or vertex influence count.
- Do not add scale to the neutral skeleton transform.
- Do not update W3D parity expectations to hide a behavior change.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C4-01` and `WI-C4-02` are done, change `WI-C4-05` to `Ready`.
- Append a result line to `.exchange`.
