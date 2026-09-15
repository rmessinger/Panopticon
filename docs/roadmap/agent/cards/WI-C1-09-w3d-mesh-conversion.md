# Task Card `WI-C1-09`: Extract W3D mesh conversion

| Field | Value |
|---|---|
| Card ID | `WI-C1-09` |
| Roadmap phase | [Track C · C1](../../track-c-external-assets.md): Format-agnostic import abstraction |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-C1-08` |
| Diagrams | [C1 architecture diagrams](../../deep-dives/c1-import-abstraction.md#9-architecture-diagrams) |

## Goal

Convert W3D mesh geometry, skinning, bounds, and flags into `ImportedMesh`, composing the material
converter without allocating runtime or GPU resources.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c1-import-abstraction.md` sections 2, 4, 5, and 7.
- `docs/roadmap/implementation/track-c-c1-import-abstraction.md` model audit.
- `src/OpenSage.Game/Graphics/ModelMesh.W3d.cs:17-99,149-206`.
- `src/OpenSage.Game/Content/Importing/W3d/W3dMaterialConverter.cs`.
- Interchange types and tests from `WI-C1-04`.

## Steps

1. Add `W3dMeshConverter` under `Content/Importing/W3d/`.
2. Move position, normal, optional secondary geometry, two-bone influence, index, bounds, hidden,
   camera-oriented, skinned, and house-color detection into CPU conversion.
3. Compose `W3dMaterialConverter` for parts and material descriptions.
4. Validate 16-bit index limits, influence ranges, source array lengths, and part ranges before
   returning `ImportedMesh`.
5. Keep the legacy `ModelMesh(W3dMesh, AssetLoadContext)` path active, delegating pure conversion
   helpers where practical without changing GPU construction.
6. Add tests for static and skinned meshes, secondary geometry, flags, bounds, and invalid indices.

## Files likely to change

- `src/OpenSage.Game/Content/Importing/W3d/W3dMeshConverter.cs` (new)
- `src/OpenSage.Game/Graphics/ModelMesh.W3d.cs`
- `src/OpenSage.Game.Tests/Content/Importing/W3dMeshConverterTests.cs` (new)

## Acceptance criteria

- [ ] `W3dMeshConverter` produces complete `ImportedMesh` values for every responsibility in the
      model audit.
- [ ] Conversion performs no GPU or asset-store access.
- [ ] Index overflow and malformed source arrays fail before runtime construction.
- [ ] The legacy W3D runtime path remains active.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~W3dMeshConverterTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not widen the runtime vertex layout or skinning influence count.
- Do not materialize textures, materials, buffers, or models.
- Do not compose HLOD sub-objects; `WI-C1-10` owns model composition.

## On completion

- Set Status above to `Done` or `Blocked`.
- Change `WI-C1-10` to `Ready`.
- Append a result line to `.exchange`.
