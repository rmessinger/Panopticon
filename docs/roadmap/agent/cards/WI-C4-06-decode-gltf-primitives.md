# Task Card `WI-C4-06`: Decode glTF primitives

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-C4-06` |
| Roadmap phase | [Track C · C4](../../track-c-external-assets.md): First production external format |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4-02`, `WI-C4-05` |
| Diagrams | [C4 architecture diagrams](../../deep-dives/c4-gltf-importer.md#11-architecture-diagrams) |

## Goal

Decode supported triangle primitive accessors into engine-owned arrays and split oversized
primitives deterministically into runtime-sized meshes.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4-gltf-importer.md` sections 4, 5, and 9.
- `src/OpenSage.Game/Content/Importing/ImportedVertex.cs`.
- `src/OpenSage.Game/Content/Importing/ImportedMesh.cs`.
- `src/OpenSage.Game/Content/Importing/ImportedMeshPart.cs`.
- `src/OpenSage.Game/Content/Importing/ImportedSubObject.cs`.
- `src/OpenSage.Game/Graphics/ModelMesh.Imported.cs`: runtime vertex and `ushort` index layout.
- `src/OpenSage.Game/Content/Importing/Gltf/GltfScenePlan.cs` (new from `WI-C4-05`).
- `src/OpenSage.Game/Content/Importing/Gltf/GltfTransformConverter.cs` (new from `WI-C4-05`).

## Steps

1. Add an accessor decoder that immediately copies SharpGLTF values into engine-owned arrays.
   Cover tightly packed and interleaved buffer views, normalized integer components, sparse
   overlays, and required accessor offsets without exposing parser memory.
2. Accept only triangle primitives. Require `POSITION` and `NORMAL`; accept optional `TANGENT`,
   `TEXCOORD_0`, and `TEXCOORD_1`. Reject `COLOR_0`, UV sets above 1, and morph targets.
3. Validate matching attribute counts, finite numeric values, legal component and vector types,
   and index ranges before accessing arrays.
4. Decode unsigned byte, unsigned short, and unsigned int indices. Generate sequential indices
   for non-indexed triangles and reject a count not divisible by three.
5. Apply the attachment geometry transform recorded by card 5. Transform positions and tangents
   with the geometry matrix, normals with its inverse transpose, then normalize and
   re-orthogonalize the tangent basis. Derive binormals with preserved tangent handedness and
   preserve UV coordinates.
6. Emit one imported mesh and mesh part per source primitive. Compute each mesh's box and sphere
   from its converted positions through `GltfBoundsCalculator`. When a primitive references more
   than 65,535 distinct vertices, split at triangle boundaries in source order, rebase indices,
   duplicate boundary vertices deterministically, and recompute bounds for each split mesh.
7. Preserve material and attachment identifiers in the internal conversion result for cards 7
   and 9. Use a temporary placeholder material only in focused tests, not in complete importer
   output.
8. Add tests for every accessor layout, indexed and non-indexed triangles, invalid counts and
   ranges, unsupported modes and attributes, and repeatable oversized splitting.

## Files likely to change

- `src/OpenSage.Game/Content/Importing/Gltf/GltfAccessorDecoder.cs` (new)
- `src/OpenSage.Game/Content/Importing/Gltf/GltfPrimitiveConverter.cs` (new)
- `src/OpenSage.Game/Content/Importing/Gltf/GltfMeshSplitter.cs` (new)
- `src/OpenSage.Game.Tests/Content/Importing/GltfPrimitiveConverterTests.cs` (new)

## Acceptance criteria

- [ ] Interleaved, normalized, sparse, indexed, and non-indexed inputs produce expected arrays.
- [ ] All parser-backed values are copied before conversion returns.
- [ ] Unsupported primitive modes and attributes throw `NotSupportedException`.
- [ ] Malformed accessors and indices throw contextual `InvalidDataException` before out-of-range
      access or oversized allocation.
- [ ] Every emitted mesh has at most 65,535 distinct vertices and stable triangle order.
- [ ] Every emitted and split mesh has bounds computed from exactly its own converted vertices.
- [ ] Supported scale cases preserve normalized normals, orthogonal tangents, and tangent
      handedness.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~GltfPrimitiveConverterTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add skin weights, materials, animations, morph targets, compression, or non-triangle
  topology.
- Do not widen runtime indices beyond `ushort`.
- Do not merge primitives with different attributes or materials.
- Do not hand-parse JSON or GLB when SharpGLTF exposes the required validated accessor data.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C4-03` and `WI-C4-05` are done, change `WI-C4-07` to `Ready`.
- If done and `WI-C3-09` and `WI-C3-10` are done, change `WI-C4-09` to `Ready`.
- Append a result line to `.exchange`.
