# Task Card `WI-C1-08`: Extract W3D material conversion

| Field | Value |
|---|---|
| Card ID | `WI-C1-08` |
| Roadmap phase | [Track C · C1](../../track-c-external-assets.md): Format-agnostic import abstraction |
| Status | `Blocked` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-C1-05` |
| Diagrams | [C1 architecture diagrams](../../deep-dives/c1-import-abstraction.md#9-architecture-diagrams) |

## Goal

Convert W3D material passes into CPU-only `ImportedMaterial` and `ImportedMeshPart` values while
preserving current shader, texture, render-state, and mesh-part grouping semantics.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c1-import-abstraction.md` sections 2, 4, 5, and 7.
- `docs/roadmap/implementation/track-c-c1-import-abstraction.md` section
  `ModelMesh.W3d.cs conversion responsibilities`.
- `src/OpenSage.Game/Graphics/ModelMesh.W3d.cs:23-67,101-147,208-554`.
- `src/OpenSage.Game/Graphics/ModelMeshPart.cs`.
- `src/OpenSage.Game/Graphics/Shaders/FixedFunctionShaderResources.cs`: engine material inputs
  used by the current conversion.

## Steps

1. Add a W3D-specific converter under `Content/Importing/W3d/` that produces
   `ImportedMaterial` and `ImportedMeshPart` values only.
2. Move shader-material versus fixed-function selection, vertex-material conversion, shading
   configuration, per-face run grouping, cull/depth/blend state, shader-set name normalization,
   texture-name extraction, and every type-tagged named shader-material property into the
   converter.
3. Preserve current errors for mixed materials on one triangle and multi-frame textures, but
   rethrow them with asset and mesh context per the C1 failure contract.
4. Do not create textures, materials, pipelines, buffers, or shader resources in the converter.
5. Add unit tests for shader-material and fixed-function paths, one per-face split, two-sided
   culling, blend state, texture-name output, and every named-property value kind.

## Files likely to change

- `src/OpenSage.Game/Content/Importing/W3d/W3dMaterialConverter.cs` (new)
- `src/OpenSage.Game/Graphics/ModelMesh.W3d.cs` (extraction only; legacy caller remains)
- `src/OpenSage.Game.Tests/Content/Importing/W3dMaterialConverterTests.cs` (new)

## Acceptance criteria

- [ ] The converter output contains no W3D indices or parser types.
- [ ] The converter performs no GPU, `AssetStore`, texture, shader-store, or pipeline access.
- [ ] Current mesh-part grouping, render state, and named shader-material properties are covered
      by focused tests.
- [ ] The legacy model path remains active and behaviorally unchanged.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~W3dMaterialConverterTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add PBR fields or glTF conventions; C3 owns material translation.
- Do not switch model loading to interchange values.
- Do not alter runtime `Material`, `MaterialPass`, or shader implementations.

## On completion

- Set Status above to `Done` or `Blocked`.
- Change `WI-C1-09` to `Ready`.
- If `WI-C4a-04`, `WI-C4a-05`, and `WI-C4a-06` are done, change `WI-C4a-07` to `Ready`.
- Append a result line to `.exchange`.
