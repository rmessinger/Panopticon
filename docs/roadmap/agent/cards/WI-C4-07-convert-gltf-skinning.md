# Task Card `WI-C4-07`: Convert glTF skinning

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-C4-07` |
| Roadmap phase | [Track C · C4](../../track-c-external-assets.md): First production external format |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4-03`, `WI-C4-05`, `WI-C4-06` |
| Diagrams | [C4 architecture diagrams](../../deep-dives/c4-gltf-importer.md#11-architecture-diagrams) |

## Goal

Convert one selected-scene skin into the existing two-influence vertex layout with validated
joint remapping, inverse binds, and model-space primary and secondary geometry.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4-gltf-importer.md` sections 5 and 6.
- `src/OpenSage.Game/Content/Importing/ImportedBone.cs`.
- `src/OpenSage.Game/Content/Importing/ImportedVertex.cs`.
- `src/OpenSage.Game/Content/Importing/ImportedMesh.cs`.
- `src/OpenSage.Game/Content/Importing/ImportedModel.cs`: skeleton and vertex-index validation.
- `src/OpenSage.Game/Graphics/ModelMesh.Imported.cs`: primary and secondary geometry upload.
- `src/OpenSage.Game/Content/Importing/Gltf/GltfScenePlan.cs` (new from `WI-C4-05`).
- `src/OpenSage.Game/Content/Importing/Gltf/GltfAccessorDecoder.cs` (new from `WI-C4-06`).
- `src/OpenSage.Game/Content/Importing/Gltf/GltfPrimitiveConverter.cs` (new from `WI-C4-06`).

## Steps

1. Require every skinned primitive in the selected scene to reference the same skin. Reject a
   second skin before producing an `ImportedModel`.
2. Require every skin joint to be reachable in the selected-scene node order and remap skin-local
   joint indices to imported bone indices.
3. Decode `JOINTS_0` and `WEIGHTS_0` only as a pair. Reject `JOINTS_1`, `WEIGHTS_1`, negative or
   non-finite weights, out-of-range joints, and a meaningful third or fourth influence.
4. Sort the two meaningful influences deterministically by descending weight, then source slot.
   Normalize a nonzero sum within a documented small tolerance and reject a zero total.
5. Decode one inverse bind per joint when present, or use identity when absent. Convert each with
   `C^-1 * M * C`; require finite and invertible results.
6. Rebuild the imported bone list and `ImportedSkeleton` with converted inverse binds while
   preserving parent-before-child order. Attach skinned primitives to the imported skeleton root
   without baking the skinned mesh-node transform.
7. For two-influence vertices, copy the same converted model-space position and normal into
   primary and secondary geometry. Leave secondary geometry unused for one influence. Recompute
   mesh bounds through `GltfBoundsCalculator` after any skin-specific primary-geometry rewrite.
8. Add tests for identity and non-identity inverse binds, joint remapping, two-influence geometry,
   normalization tolerance, CPU reference deformation, and every rejection above.

## Files likely to change

- `src/OpenSage.Game/Content/Importing/Gltf/GltfSkinConverter.cs` (new)
- `src/OpenSage.Game/Content/Importing/Gltf/GltfPrimitiveConverter.cs` (new from `WI-C4-06`)
- `src/OpenSage.Game/Content/Importing/Gltf/GltfScenePlan.cs` (new from `WI-C4-05`)
- `src/OpenSage.Game.Tests/Content/Importing/GltfSkinConverterTests.cs` (new)

## Acceptance criteria

- [ ] One-influence and two-influence vertices map to valid imported skinning data.
- [ ] Primary and secondary source geometry are identical before their skin matrices apply.
- [ ] Joint indices address full imported bone order, not skin-local order.
- [ ] Converted inverse binds include unit-scaled translation and pass a CPU deformation
      reference.
- [ ] Skinned mesh bounds contain exactly the final primary positions supplied to materialization.
- [ ] Multiple skins, extra meaningful influences, invalid weights, and unreachable joints fail
      explicitly.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~GltfSkinConverterTests|FullyQualifiedName~ModelSkinningTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not raise the runtime influence limit or silently discard an influence.
- Do not bake a skinned mesh-node transform into vertices.
- Do not accept scale on joints or their ancestors.
- Do not add setters or other mutation paths to `ImportedBone`.
- Do not add dual-quaternion skinning, morph targets, or multiple-skin composition.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C4-08` and `WI-C4-09` are done, change `WI-C4-10` to `Ready`.
- Append a result line to `.exchange`.
