# Task Card `WI-C1-11`: Materialize imported models

| Field | Value |
|---|---|
| Card ID | `WI-C1-11` |
| Roadmap phase | [Track C · C1](../../track-c-external-assets.md): Format-agnostic import abstraction |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-C1-10` |
| Diagrams | [C1 architecture diagrams](../../deep-dives/c1-import-abstraction.md#9-architecture-diagrams) |

## Goal

Add engine-owned model and skeleton materialization from interchange values while leaving active
loader and game-definition wiring unchanged.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c1-import-abstraction.md` sections 3, 4, 6, and 9.
- `src/Panopticon.Simulation/Graphics/Model.cs`, `ModelBoneHierarchy.cs`, `ModelSubObject.cs`,
  `ModelBox.cs`, `ModelMesh.cs`, and `ModelMesh.W3d.cs`.

## Steps

1. Add `ImportedModelMaterializer` under `Content/Loaders/`.
2. Materialize skeletons, boxes, vertices, indices, mesh parts, textures, materials, shader
   resources, sub-objects, and `Model` from interchange values. Validate all references before the
   first GPU allocation.
3. Add a skeleton materialization entry point used later by the hierarchy loader.
4. Preserve all named shader-material properties when populating shader resources.
5. Add synthetic tests that materialize a static model, a skinned model, a box, and each supported
   material-property kind.

## Files likely to change

- `src/Panopticon.Simulation/Content/Loaders/ImportedModelMaterializer.cs` (new)
- `src/Panopticon.Simulation/Graphics/Model*.cs` (format-neutral construction only)
- `test/Panopticon.Simulation.Tests/Content/Importing/ImportedModelMaterializerTests.cs` (new)

## Acceptance criteria

- [ ] Import completes before materialization performs any GPU or asset-store access.
- [ ] Every imported material-property kind reaches the matching shader-resource setter.
- [ ] Existing loaders and game definitions remain unchanged.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ImportedModelMaterializerTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not switch loaders or game definitions; `WI-C1-12` owns the cutover.
- Do not delete the old direct W3D constructors until parity is locked by `WI-C1-13`;
  `WI-C1-14` owns removal.
- Do not change runtime rendering or animation playback.

## On completion

- Set Status above to `Done` or `Blocked`.
- Change `WI-C1-12` to `Ready` once `WI-C1-07` is also `Done`.
- Change `WI-C4a-10` to `Ready`.
- Append a result line to `.exchange`.
