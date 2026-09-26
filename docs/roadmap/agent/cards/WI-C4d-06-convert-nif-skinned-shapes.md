# Task Card `WI-C4d-06`: Convert NIF skinned shapes

| Field | Value |
|---|---|
| Card ID | `WI-C4d-06` |
| Roadmap phase | [Track C · C4d](../../track-c-external-assets.md): EE2 skinned character animation |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4d-01`, `WI-C4d-02`, `WI-C4d-03` |
| Diagrams | [C4d architecture diagrams](../../deep-dives/c4d-ee2-skinned-animation.md#12-architecture-diagrams) |

## Goal

Convert skinned `NiTriShape` geometry into model-space skinned `ImportedMesh` sub-objects with
`NiSkinData` inverse binds and up to four influences, with bind-pose parity.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4d-ee2-skinned-animation.md` sections 4, 7, and 10.
- `src/Panopticon.Simulation/Content/Importing/Nif/NifSceneConverter.cs`.
- `src/Panopticon.Simulation/Content/Importing/Nif/NifTransformConverter.cs`.
- The skin block records from `WI-C4d-02`.
- `src/Panopticon.Simulation/Content/Importing/Gltf/GltfSkinConverter.cs:41-121` for inverse-bind and
  skeleton rebuild precedent.
- `src/Panopticon.Simulation/Graphics/ModelInstance.cs:255-262`.
- `test/Panopticon.Simulation.Tests/Content/Importing/NifModelImporterTests.cs` and
  `test/Panopticon.Simulation.Tests/Data/Nif/NifTestDataBuilder.cs`.

## Steps

1. Add `NifSkinConverter`. `NifSceneConverter` routes a shape with a skin ref to it, after the
   bone walk has finished, with the node-to-bone-index map.
2. Validate per section 7.4 and section 10: model-root skeleton root, runtime-bone skin bones
   (C4f message for scaled nodes), has-vertex-weights, vertex index range, one weight per vertex
   per bone.
3. Build per-vertex influences under section 4: finite weights in `[0, 1]`, drop at or below
   1e-4, at most four, sum within 0.01 of 1, normalize, descending order.
4. Transform positions by `S^-1` then the source-to-engine map, and normals by the inverse
   transpose. Set secondary position and normal equal to primary. Ignore the shape transform.
5. Compute each bone's source inverse bind `B_i * S`, conjugate it to the engine frame, and
   rebuild the skeleton with those inverse binds. A bone shared by shapes with inverse binds that
   differ by more than 1e-4 throws `InvalidDataException`.
6. Attach the skinned mesh to bone 0 with `isSkinned: true`. Compute bounds from model-space
   positions. Keep material, UV, winding, and LOD handling unchanged.
7. Add synthetic tests: a two-bone skin with a posed rest distinct from bind, every section 10
   failure row, a five-influence vertex, a 0.842 weight sum, and bind-pose parity: posing bones
   at the inverse of their inverse binds reproduces imported positions within 1e-4.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/Nif/NifSkinConverter.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/Nif/NifSceneConverter.cs`
- `src/Panopticon.Simulation/Content/Importing/Nif/NifTransformConverter.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/NifSkinConverterTests.cs` (new)
- `test/Panopticon.Simulation.Tests/Data/Nif/NifTestDataBuilder.cs`

## Acceptance criteria

- [x] Synthetic skins import with correct inverse binds, four-slot influences, and bind-pose
      parity within 1e-4.
- [x] Every section 10 skin failure throws the listed exception with shape or node context.
      Parser-level rows (truncation, bad references, wrong block types, bone-count mismatch)
      are covered by the `NifSkinTests` suite from `WI-C4d-02`; converter-level rows are
      covered by `NifSkinConverterTests`.
- [x] No influence above 1e-4 is dropped.
- [x] Unskinned NIF imports are unchanged.
- [x] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NifSkinConverterTests"
dotnet test src --filter "FullyQualifiedName~Nif"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not use `NiSkinPartition` data.
- Do not change the animation converter.
- Do not support non-unit-scale skin bones. Reject them naming C4f.
- Do not commit EE2 asset bytes.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done and `WI-C4d-04` is done, change `WI-C4d-07` to `Ready`.
- Append a result line to `.exchange`.
