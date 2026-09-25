# Task Card `WI-C4d-03`: Widen C1 skin influences to four

| Field | Value |
|---|---|
| Card ID | `WI-C4d-03` |
| Roadmap phase | [Track C · C4d](../../track-c-external-assets.md): EE2 skinned character animation |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [C4d architecture diagrams](../../deep-dives/c4d-ee2-skinned-animation.md#12-architecture-diagrams) |

## Goal

Give `ImportedVertex` four bone-index and bone-weight slots, with C1 validation for all four,
without changing any existing importer's output.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4d-ee2-skinned-animation.md` sections 4 and 8.
- `src/Panopticon.Simulation/Content/Importing/ImportedVertex.cs`.
- `src/Panopticon.Simulation/Content/Importing/ImportedMesh.cs:157-180`.
- `src/Panopticon.Simulation/Content/Importing/ImportedModel.cs:60-90`.
- `src/Panopticon.Simulation/Content/Importing/ImportedTangentGenerator.cs:90-140`.
- `grep -rn "new ImportedVertex(" src` for every construction site.

## Steps

1. Add `BoneIndex2`, `BoneIndex3`, `BoneWeight2`, and `BoneWeight3` to `ImportedVertex` as
   optional constructor parameters after `boneWeight1`, defaulting to 0, with the same `[0, 1]`
   weight checks. Update the class summary: four slots, and importers must not truncate beyond
   four.
2. In `ImportedMesh.CopyVertices`, extend the unskinned check to all four slots.
3. In `ImportedMesh.CopyVertices`, reject a vertex whose `BoneWeight2` or `BoneWeight3` is
   nonzero when `SecondaryPosition` or `SecondaryNormal` is non-null and differs from the
   primary value. Throw `ArgumentException` naming the vertex.
4. In `ImportedModel`, validate all four bone indices against the skeleton.
5. In `ImportedTangentGenerator` and every other site that rebuilds a vertex from an existing
   one, copy all four slots.
6. Add tests for the constructor range checks, the unskinned check on slots 2 and 3, the
   secondary-geometry rule, the bone-index range on slots 2 and 3, and tangent generation
   preserving all four slots.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/ImportedVertex.cs`
- `src/Panopticon.Simulation/Content/Importing/ImportedMesh.cs`
- `src/Panopticon.Simulation/Content/Importing/ImportedModel.cs`
- `src/Panopticon.Simulation/Content/Importing/ImportedTangentGenerator.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/ImportedModelTests.cs` or the existing C1 model
  test file

## Acceptance criteria

- [ ] All four slots are exposed, range-checked, and validated.
- [ ] W3D, glTF, and NIF importer outputs are unchanged. Existing importer and parity tests pass
      without edits.
- [ ] Tangent generation round-trips all four slots.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~Content.Importing"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not change the runtime vertex layout or shaders. That is `WI-C4d-04`.
- Do not change the glTF or NIF converters. Those are `WI-C4d-05` and `WI-C4d-06`.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, change `WI-C4d-04` to `Ready`.
- If done and `WI-C4d-01` and `WI-C4d-02` are done, change `WI-C4d-06` to `Ready`.
- Append a result line to `.exchange`.
