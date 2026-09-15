# Task Card `WI-C1-10`: Compose models through the W3D importer

| Field | Value |
|---|---|
| Card ID | `WI-C1-10` |
| Roadmap phase | [Track C · C1](../../track-c-external-assets.md): Format-agnostic import abstraction |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-C1-09` |
| Diagrams | [C1 architecture diagrams](../../deep-dives/c1-import-abstraction.md#9-architecture-diagrams) |

## Goal

Implement `W3dModelImporter` for model, skeleton, HLOD, sub-object, mesh, and box import while
leaving the existing runtime loader active.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c1-import-abstraction.md` sections 3, 5, 6, and 7.
- `docs/roadmap/implementation/track-c-c1-import-abstraction.md` model stage trace.
- `src/OpenSage.Game/Content/Loaders/OnDemandModelLoader.cs`.
- `src/OpenSage.Game/Content/Loaders/OnDemandModelBoneHierarchyLoader.cs`.
- `src/OpenSage.Game/Graphics/ModelBoneHierarchy.cs`.
- `src/OpenSage.Game/Graphics/ModelSubObject.cs`.
- `src/OpenSage.Game/Graphics/ModelBox.cs`.
- `src/OpenSage.Game/Content/Importing/W3d/W3dMeshConverter.cs`.

## Steps

1. Add `W3dModelImporter : IModelImporter` with the W3D path resolver injected in its constructor.
2. Implement embedded, referenced, and default skeleton import as `ImportedSkeleton`.
3. Implement HLOD first-LOD composition, single-renderable fallback, empty-model validation,
   missing-renderable validation, bone-index validation, `W3dMesh` delegation, and `W3dBox`
   conversion.
4. Implement `ImportSkeleton` for the existing separately cached hierarchy path.
5. Apply the C1 failure contract: missing files return null; malformed indices, missing referenced
   hierarchies, unsupported chunks, and invalid model structure throw contextual exceptions.
6. Add tests covering every branch named in the model audit.

## Files likely to change

- `src/OpenSage.Game/Content/Importing/W3d/W3dModelImporter.cs` (new)
- `src/OpenSage.Game.Tests/Content/Importing/W3dModelImporterTests.cs` (new)

## Acceptance criteria

- [ ] Every audited model and hierarchy branch has a focused test.
- [ ] The importer returns only interchange types and accesses only `AssetImportContext`.
- [ ] Referenced hierarchy misses and invalid bone indices produce contextual
      `InvalidDataException`, not null-reference or index exceptions.
- [ ] The existing runtime loader remains active.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~W3dModelImporterTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not materialize `Model`, `ModelMesh`, materials, textures, or GPU buffers.
- Do not import render-object chunk types unsupported by the current loader.
- Do not add glTF dispatch or extension-based selection.

## On completion

- Set Status above to `Done` or `Blocked`.
- Change `WI-C1-11` to `Ready`.
- Append a result line to `.exchange`.
