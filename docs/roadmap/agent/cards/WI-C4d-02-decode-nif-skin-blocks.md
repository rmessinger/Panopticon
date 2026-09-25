# Task Card `WI-C4d-02`: Decode NIF skin blocks

| Field | Value |
|---|---|
| Card ID | `WI-C4d-02` |
| Roadmap phase | [Track C · C4d](../../track-c-external-assets.md): EE2 skinned character animation |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [C4d architecture diagrams](../../deep-dives/c4d-ee2-skinned-animation.md#12-architecture-diagrams) |

## Goal

Parse `NiSkinInstance`, `NiSkinData`, and `NiSkinPartition` at NIF 10.1.0.0 with exact stream
consumption and validated references, replacing the C4a blanket rejection of skinned geometry.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4d-ee2-skinned-animation.md` sections 6 and 10.
- `src/Panopticon.FileFormats.Nif/NifFile.cs:118-160` and `162-330`.
- `src/Panopticon.FileFormats.Nif/Blocks/NifGeometry.cs` and `NifKeyframeController.cs` (block
  record pattern).
- `src/Panopticon.FileFormats.Nif/NifBinaryReader.cs`.
- `test/Panopticon.Simulation.Tests/Data/Nif/NifTestDataBuilder.cs` and `NifSceneGraphTests.cs`.

## Steps

1. Add `NifSkinInstance` (data ref, skeleton-root ref, bone refs), `NifSkinData` (skin
   transform, partition ref, has-vertex-weights, bone list), `NifSkinBoneData` (transform, bound
   center, bound radius, vertex weights as index and weight pairs), and `NifSkinPartition`
   (partitions with every field in section 6). Represent a transform as rotation `NifMatrix33`,
   translation `Vector3`, and scale `float`.
2. Register the three types in `NifFile.ReadBlock`.
3. In `ValidateReferences`, replace the skinned-geometry `NotSupportedException`:
   - the geometry skin ref must be null or a `NifSkinInstance`;
   - the instance data ref must be a `NifSkinData`;
   - the skeleton root and every bone ref must be a `NifNode`;
   - the data partition ref must be null or a `NifSkinPartition`;
   - instance and data bone counts must match.
   Wrong types and count mismatches throw `InvalidDataException`.
4. Extend `NifTestDataBuilder` with writers for the three blocks.
5. Add tests for exact consumption of each block, a partition with and without vertex map,
   weights, strips, triangles, and bone indices, each wrong-reference case, and the bone-count
   mismatch.

## Files likely to change

- `src/Panopticon.FileFormats.Nif/NifFile.cs`
- `src/Panopticon.FileFormats.Nif/Blocks/NifSkinInstance.cs` (new)
- `src/Panopticon.FileFormats.Nif/Blocks/NifSkinData.cs` (new, with `NifSkinBoneData`)
- `src/Panopticon.FileFormats.Nif/Blocks/NifSkinPartition.cs` (new)
- `test/Panopticon.Simulation.Tests/Data/Nif/NifTestDataBuilder.cs`
- `test/Panopticon.Simulation.Tests/Data/Nif/NifSkinTests.cs` (new)

## Acceptance criteria

- [ ] Synthetic streams for all three blocks parse to exact end-of-file.
- [ ] Every wrong-type reference and the bone-count mismatch throw `InvalidDataException`.
- [ ] A skinned shape no longer fails in the parser. The importer still rejects it until
      `WI-C4d-06`, with a message naming C4d.
- [ ] Existing C4a, C4b, and C4c parser tests pass unchanged.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NifSkinTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not convert skins or touch `Panopticon.Simulation` importer code beyond moving the rejection
  message into `NifSceneConverter`.
- Do not interpret partition data.
- Do not commit EE2 asset bytes.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done and `WI-C4d-01` and `WI-C4d-03` are done, change `WI-C4d-06` to `Ready`.
- Append a result line to `.exchange`.
