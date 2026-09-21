# Task Card `WI-C4a-07`: Add the NIF model importer

| Field | Value |
|---|---|
| Card ID | `WI-C4a-07` |
| Roadmap phase | [Track C · C4a](../../track-c-external-assets.md): Empire Earth II NIF 10.1 proof of concept |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C1-05`, `WI-C1-08`, `WI-C4a-04`, `WI-C4a-05`, `WI-C4a-06` |
| Diagrams | [C4a architecture diagrams](../../deep-dives/ee2-gamebryo-model-storage.md#10-architecture-diagrams) |

## Goal

Implement `NifModelImporter` as a CPU-only adapter from the selected NIF 10.1 subset to the C1
`ImportedModel` graph.

## Context to read (and only this)

- `docs/roadmap/deep-dives/ee2-gamebryo-model-storage.md` sections 7, 9, and 10.
- `docs/roadmap/deep-dives/c1-import-abstraction.md` sections 2, 3, 4, and 6.
- `src/OpenSage.Game/Content/Importing/ImportedModel.cs` and sibling interchange types.
- `src/OpenSage.Game/Content/Importing/IModelImporter.cs` and `AssetImportContext.cs`.
- NIF parser types completed by `WI-C4a-04..06`.

## Steps

1. Reference `OpenSage.FileFormats.Nif` from `OpenSage.Game` and add
   `Content/Importing/Nif/NifModelImporter`.
2. Resolve `<asset-key>.nifcache` beneath `cache/nifcache/` through
   `AssetImportContext.FileSystem`; return null only when the top-level entry is absent.
3. Walk the selected node graph and accumulate each shape's full source transform. Bake that
   transform and the documented source-to-engine matrix into positions and normals. Reverse
   triangle winding if handedness changes.
4. Create one identity root `ImportedBone`, attach each transformed unskinned `ImportedMesh` to
   that root through `ImportedSubObject`, and preserve the source shape name.
5. Map fixed-function colors, depth, alpha, specular, culling, and source texture names into
   `ImportedMaterial`. Use the texture-property names established by `WI-C1-08` and consumed by
   `WI-C1-11`; set texture-stage count to zero while preserving source names as properties.
6. Bake the selected `NiLODNode` child and omit its other LOD children.
7. Recompute axis-aligned boxes and spheres from transformed positions. Do not copy source bounds
   after transform baking.
8. Translate malformed references/data to contextual `InvalidDataException` and non-finite or
   otherwise unsafe transforms and valid unsupported features to `NotSupportedException`.
9. Add synthetic importer tests for a plain static model and a highest-detail LOD model.

## Files likely to change

- `src/OpenSage.Game/OpenSage.Game.csproj`
- `src/OpenSage.Game/Content/Importing/Nif/NifModelImporter.cs` (new)
- `src/OpenSage.Game/Content/Importing/Nif/NifMaterialConverter.cs` (new)
- `src/OpenSage.Game/Content/Importing/Nif/NifSceneConverter.cs` (new)
- `src/OpenSage.Game/Content/Importing/Nif/NifTransformConverter.cs` (new)
- `src/OpenSage.Game.Tests/Content/Importing/NifModelImporterTests.cs` (new)

## Acceptance criteria

- [ ] Synthetic static and LOD models produce valid deterministic `ImportedModel` graphs.
- [ ] Accumulated static transforms are baked into mesh data and bounds without losing placement.
- [ ] No NIF parser type reaches `OpenSage.Game/Graphics/`.
- [ ] The importer performs no GPU, `AssetStore`, texture decode, or ZIP API work.
- [ ] Missing, malformed, and unsupported inputs follow the C1 failure contract.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NifModelImporterTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add animation, skinning, runtime LOD, `.texcache`, player color, or DDF binding.
- Do not register NIF in shipped game definitions or add an EE2 game definition.
- Do not widen the C1 interchange graph without stopping and updating the C4a decision.

## On completion

- Set Status above to `Done` or `Blocked`.
- If `WI-C4a-08` is also done, change `WI-C4a-09` to `Ready`.
- Append a result line to `.exchange`.
