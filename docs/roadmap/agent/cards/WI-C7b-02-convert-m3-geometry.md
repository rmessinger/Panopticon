# Task Card `WI-C7b-02`: Convert M3 geometry to C1 sub-objects

| Field | Value |
|---|---|
| Card ID | `WI-C7b-02` |
| Roadmap phase | [Track C · C7b](../../track-c-external-assets.md): StarCraft II static M3 models |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C7b-01` |
| Diagrams | [C7a/C7b architecture diagrams](../../deep-dives/sc2-casc-m3-storage.md#14-architecture-diagrams) |

## Goal

Add `M3CoordinateConverter`, `M3GeometryConverter`, and a first `M3ModelImporter` that imports opaque batches as untextured rigid sub-objects in bind pose.

## Context to read (and only this)

- `docs/roadmap/deep-dives/sc2-casc-m3-storage.md` sections 7, 9, 10, and 12.
- `src/Panopticon.Simulation/Content/Importing/IModelImporter.cs`, `ImportedModel.cs`, `ImportedMesh.cs`, `ImportedMeshPart.cs`, `ImportedVertex.cs`, `ImportedTangentGenerator.cs`.
- `src/Panopticon.Simulation/Content/Importing/Nif/NifModelImporter.cs`: importer shape and single-root skeleton.

## Steps

1. Add `M3CoordinateConverter` with `ToEnginePosition` `(x, y, z) -> (-10y, 10x, 10z)`, `ToEngineDirection` (rotation only), and the section 5 table from the C7c deep dive as unit-tested static methods (translation, quaternion, scale swap, matrix conjugation). C7c reuses them.
2. Add `M3GeometryConverter`: per batch, decode the region's vertices (section 7.1), convert positions and normals, keep UV0 and UV1, use region-relative indices, and generate tangents with `ImportedTangentGenerator` on UV0.
3. Add `M3ModelImporter : IModelImporter` (`Id` `m3`): the key is the case-insensitive CASC path. Read through `AssetImportContext.FileSystem`, return null when missing, and build an `ImportedModel` with one root bone and one sub-object per batch. Use a placeholder opaque PBR material with base color factor 1 until `WI-C7b-04`.
4. Validate face indices against the region vertex count (`InvalidDataException`).
5. Tests on `M3TestDataBuilder`: converted positions and normals, UV scaling `i16 / 2048`, one sub-object per batch, and an out-of-range face index.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/M3/M3CoordinateConverter.cs`, `M3GeometryConverter.cs`, `M3ModelImporter.cs` (new)
- `test/Panopticon.Simulation.Tests/Content/Importing/M3CoordinateConverterTests.cs`, `M3ModelImporterTests.cs` (new)

## Acceptance criteria

- [ ] Source `(0, -1, 0)` (forward) maps to engine `(10, 0, 0)`, and `+Z` stays up.
- [ ] Quaternion conjugation and scale swap match matrix conjugation within 1e-5.
- [ ] Synthetic batches import as sub-objects with correct UVs and generated tangents.
- [ ] Out-of-range faces fail with `InvalidDataException`.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No skinning or skeleton. That is C7c.
- No textures or real materials. That is `WI-C7b-03` and `WI-C7b-04`.
- Do not register the importer in `ExternalFormatFactory`.
- Do not commit StarCraft II files, extracted bytes, or captures.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done and `WI-C7b-03` is done, change `WI-C7b-04` to `Ready`.
- Append a result line to `.exchange`.
