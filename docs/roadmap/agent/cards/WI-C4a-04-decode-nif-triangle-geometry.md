# Task Card `WI-C4a-04`: Decode NIF triangle geometry

| Field | Value |
|---|---|
| Card ID | `WI-C4a-04` |
| Roadmap phase | [Track C · C4a](../../track-c-external-assets.md): Empire Earth II NIF 10.1 proof of concept |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4a-03` |
| Diagrams | [C4a architecture diagrams](../../deep-dives/ee2-gamebryo-model-storage.md#10-architecture-diagrams) |

## Goal

Decode `NiTriShape` and `NiTriShapeData` into validated CPU geometry for the two target models.

## Context to read (and only this)

- Completed `WI-C4a-01` target descriptors.
- Niftools `nif.xml` definitions for `NiGeometry`, `NiTriBasedGeom`, `NiTriShape`,
  `NiGeometryData`, `NiTriBasedGeomData`, and `NiTriShapeData`.
- NIF scene-graph types and synthetic builder from `WI-C4a-03`.

## Steps

1. Add one immutable parser type per geometry block in the target inheritance chain.
2. Decode vertices, normals, source bounds, UV sets, optional vertex colors, triangles, and consistency
   counts for NIF 10.1.
3. Retain `NiTriShape` data and property references by block index.
4. Validate triangle indices, declared counts, UV dimensions, normal counts, and data-reference
   type. Reject meshes above the runtime's `ushort` vertex/index limits.
5. Reject an enabled target vertex-color stream unless `WI-C4a-01` proved it absent. Do not drop
   colors silently because C1 currently has no vertex-color field.
6. Reject a shape without normals with a contextual `NotSupportedException`; C1 requires one
   normal per imported vertex.
7. Add synthetic valid and malformed geometry tests using no copied game bytes.

## Files likely to change

- `src/OpenSage.FileFormats.Nif/Blocks/NifGeometry.cs` (new)
- `src/OpenSage.FileFormats.Nif/Blocks/NifTriBasedGeometry.cs` (new)
- `src/OpenSage.FileFormats.Nif/Blocks/NifTriShape.cs` (new)
- `src/OpenSage.FileFormats.Nif/Blocks/NifGeometryData.cs` (new)
- `src/OpenSage.FileFormats.Nif/Blocks/NifTriBasedGeometryData.cs` (new)
- `src/OpenSage.FileFormats.Nif/Blocks/NifTriShapeData.cs` (new)
- `src/OpenSage.Game.Tests/Data/Nif/NifGeometryTests.cs` (new)
- `src/OpenSage.Game.Tests/Data/Nif/NifTestDataBuilder.cs`

## Acceptance criteria

- [ ] Synthetic geometry preserves positions, normals, UVs, triangles, and source bounds exactly.
- [ ] Every count and index is checked before indexing or allocation.
- [ ] Unsupported vertex colors and oversized geometry fail instead of being truncated.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NifGeometryTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not map geometry into C1 types.
- Do not decode strips, skinning, morph data, particles, collision, or animation.
- Do not derive normals or tangents when source arrays are absent; reject missing target normals.

## On completion

- Set Status above to `Done` or `Blocked`.
- If `WI-C4a-05` and `WI-C4a-06` are also done and `WI-C1-08` is done, change
  `WI-C4a-07` to `Ready`.
- Append a result line to `.exchange`.
