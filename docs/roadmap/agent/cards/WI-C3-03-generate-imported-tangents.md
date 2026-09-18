# Task Card `WI-C3-03`: Generate imported tangents

| Field | Value |
|---|---|
| Card ID | `WI-C3-03` |
| Roadmap phase | [Track C · C3](../../track-c-external-assets.md): Material and texture mapping |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C3-01` |
| Diagrams | [C3 architecture diagrams](../../deep-dives/c3-material-texture-mapping.md#9-architecture-diagrams) |

## Goal

Generate a deterministic tangent and binormal basis for normal-mapped imported triangle meshes
that provide normals and UVs but omit tangents.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c3-material-texture-mapping.md` section 4.
- `src/OpenSage.Game/Content/Importing/ImportedMesh.cs`.
- `src/OpenSage.Game/Content/Importing/ImportedMeshPart.cs`.
- `src/OpenSage.Game/Content/Importing/ImportedVertex.cs`.
- `src/OpenSage.Game/Graphics/ModelMesh.W3d.cs`: existing tangent conversion only.
- Existing mathematics vector helpers used by mesh import tests.

## Steps

1. Add CPU-only `ImportedTangentGenerator`.
2. Accumulate triangle tangent and bitangent contributions in stable index order using the
   selected UV set.
3. When normal-mapped parts share a vertex index, require their selected UV values for that vertex
   to match. Permit disjoint parts to select different UV sets.
4. Orthonormalize each tangent against its normal and derive a consistent handed binormal.
5. Return a new immutable imported mesh or vertex set; do not mutate caller-owned data.
6. Reject non-triangle indices, incomplete arrays, non-finite inputs, degenerate UV triangles, and
   zero-length results with contextual `NotSupportedException`.
7. Add deterministic, mirrored-UV, shared-vertex, mixed-UV-selection, and malformed-input tests.

## Files likely to change

- `src/OpenSage.Game/Content/Importing/ImportedTangentGenerator.cs` (new)
- `src/OpenSage.Game.Tests/Content/Importing/ImportedTangentGeneratorTests.cs` (new)

## Acceptance criteria

- [ ] Repeated generation produces byte-equivalent tangent and binormal values.
- [ ] Generated bases are finite, normalized, and orthogonal to their normals within tolerance.
- [ ] Mirrored UVs preserve the expected handedness.
- [ ] Conflicting selected UV values on a shared vertex fail explicitly.
- [ ] Degenerate or incomplete inputs fail instead of disabling normal mapping.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ImportedTangentGeneratorTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not alter source normals, positions, indices, or UVs.
- Do not add a second runtime vertex layout.
- Do not generate tangents for meshes without a normal-map binding.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C3-02` and `WI-C3-08` are done, change `WI-C3-09` to `Ready`.
- Append a result line to `.exchange`.
