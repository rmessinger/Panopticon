# Task Card `WI-C1-04`: Add CPU-only model and animation interchange types

| Field | Value |
|---|---|
| Card ID | `WI-C1-04` |
| Roadmap phase | [Track C · C1](../../track-c-external-assets.md): Format-agnostic import abstraction |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C1-03` |
| Diagrams | [C1 architecture diagrams](../../deep-dives/c1-import-abstraction.md#9-architecture-diagrams) |

## Goal

Add the CPU-only interchange value graph specified by the C1 deep dive, with constructor
validation for structural invariants and no W3D, Veldrid, or `AssetStore` dependency.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c1-import-abstraction.md` sections 2, 4, and 6.
- `src/Panopticon.Simulation/Graphics/ModelBone.cs`: existing bone data shape.
- `src/Panopticon.Simulation/Graphics/ModelMesh.W3d.cs:149-206`: current vertex and index outputs.
- `src/Panopticon.Simulation/Graphics/Animation/AnimationClip.cs` and `Keyframe.cs`: current animation
  channel and value shapes.
- `src/Panopticon.Simulation/Graphics/ModelBox.cs:14-28`: current box bounds data.

## Steps

1. Create `src/Panopticon.Simulation/Content/Importing/` and add the interchange types listed in deep-dive
   section 2, including the type-tagged `ImportedMaterialProperty`, one type per file.
2. Use immutable arrays or read-only collections. Copy caller-owned arrays at the construction
   boundary.
3. Validate parent indices, sub-object bone indices, mesh index bounds, mesh-part ranges,
   animation bone indices, monotonic keyframe times, material-property value kinds, and the
   two-bone skinning limit.
4. Throw `ArgumentException` for invalid programmatic construction. Importers later translate
   malformed file data to `InvalidDataException` with asset context.
5. Add focused unit tests under `test/Panopticon.Simulation.Tests/Content/Importing/` for one valid static
   mesh, one valid skinned mesh and animation, and each invariant in step 3.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/` (new value-type files)
- `test/Panopticon.Simulation.Tests/Content/Importing/ImportedAssetTests.cs` (new)

## Acceptance criteria

- [ ] The complete section 2 value graph exists, one type per file.
- [ ] No file under `Content/Importing/` references `Panopticon.FileFormats.W3d`, Veldrid,
      `AssetStore`, `GraphicsDevice`, or shader-resource types.
- [ ] Invalid indices, ranges, skinning counts, and keyframe ordering are rejected by tests.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ImportedAssetTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add importer interfaces or modify existing loaders.
- Do not add glTF-, PBR-, or retargeting-specific fields.
- Preserve the current two-bone skinning and animation-channel capabilities; widening them is not
  part of C1.

## On completion

- Set Status above to `Done` or `Blocked`.
- Change `WI-C1-05` to `Ready`.
- Append a result line to `.exchange`.
