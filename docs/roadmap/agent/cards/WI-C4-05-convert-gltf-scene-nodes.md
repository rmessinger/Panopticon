# Task Card `WI-C4-05`: Convert glTF scene nodes

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-C4-05` |
| Roadmap phase | [Track C · C4](../../track-c-external-assets.md): First production external format |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4-01`, `WI-C4-02`, `WI-C4-03` |
| Diagrams | [C4 architecture diagrams](../../deep-dives/c4-gltf-importer.md#11-architecture-diagrams) |

## Goal

Convert the selected glTF scene into a deterministic parent-before-child skeleton and rigid
attachment plan with the documented basis, units, scale policy, and recomputed bounds.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4-gltf-importer.md` sections 4 and 5.
- `src/OpenSage.Game/Content/Importing/ImportedBone.cs`.
- `src/OpenSage.Game/Content/Importing/ImportedSkeleton.cs`.
- `src/OpenSage.Game/Content/Importing/ImportedSubObject.cs`.
- `src/OpenSage.Game/Content/Importing/ImportedModel.cs`.
- `src/OpenSage.Game/Content/Importing/ImportedMesh.cs`.
- `src/OpenSage.Game/Content/Importing/Gltf/GltfImportOptions.cs` (new from `WI-C4-01`).
- `src/OpenSage.Game/Content/Importing/Gltf/GltfDocumentLoader.cs` (new from `WI-C4-01`).
- `src/OpenSage.Game.Tests/Content/Importing/ImportedAssetTests.cs`.

## Steps

1. Select the declared default scene. If it is absent, accept exactly one scene and reject zero
   or multiple scenes.
2. Walk selected roots in source order and children depth-first in source order. Insert one
   synthetic root only when the scene has multiple roots.
3. Preserve authored node names. Give unnamed static nodes deterministic reserved names that
   cannot collide with authored names; retain an explicit unnamed state for later animation
   validation.
4. Convert translations and vectors with `(x, y, z)` to `(z, x, y)` and
   `GltfImportOptions.UnitsPerMeter`. Convert matrices with `C^-1 * M * C` using repository
   row-vector conventions.
5. Accept matrix or TRS nodes only when the transform decomposes without shear and has positive
   determinant. Require identity scale for joints and their ancestors.
6. Remove positive uniform scale on unskinned hierarchies from the node plan by propagating it
   into descendant translations. For each attachment, record the remaining geometry transform,
   its inverse-transpose normal matrix, and whether tangent re-orthogonalization is required.
   Accept positive non-uniform scale only on a leaf unskinned mesh node.
7. Produce a rigid attachment record for each unskinned primitive at its mesh-node bone. Provide
   a bounds helper that computes box and sphere from supplied converted positions; card 6 owns
   calling it for emitted meshes.
8. Add focused synthetic-document tests for scene selection, stable order, synthetic root,
   coordinate and unit conversion, rigid placement, scale cases, mirrored and sheared rejection,
   and transformed bounds.

## Files likely to change

- `src/OpenSage.Game/Content/Importing/Gltf/GltfScenePlan.cs` (new)
- `src/OpenSage.Game/Content/Importing/Gltf/GltfSceneConverter.cs` (new)
- `src/OpenSage.Game/Content/Importing/Gltf/GltfTransformConverter.cs` (new)
- `src/OpenSage.Game/Content/Importing/Gltf/GltfBoundsCalculator.cs` (new)
- `src/OpenSage.Game.Tests/Content/Importing/GltfSceneConverterTests.cs` (new)

## Acceptance criteria

- [ ] Imported bones are stable, parent-before-child, and use one synthetic root only when needed.
- [ ] One meter becomes approximately `3.28084` world units by default.
- [ ] Basis conversion makes authored +Z forward become OpenSAGE +X forward without reversing
      winding.
- [ ] Rigid attachment plans retain their mesh-node bone, and the bounds helper returns expected
      transformed bounds for synthetic position sets.
- [ ] Supported scale plans have positive determinants, uniform scale is absorbed into descendant
      translations, and mirrored, sheared, and skinned scale cases fail explicitly.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~GltfSceneConverterTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not decode general accessors, skin weights, animations, or materials.
- Do not guess units from model bounds.
- Do not add scale to `ImportedBone`.
- Do not use a metric conversion helper unless it exists when this card executes; current source
  has no `WorldUnits.ToFeet`.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, change `WI-C4-06` to `Ready`.
- If done and `WI-C4-04` is done, change `WI-C4-08` to `Ready` only after `WI-C4-02` is also done.
- Append a result line to `.exchange`.
