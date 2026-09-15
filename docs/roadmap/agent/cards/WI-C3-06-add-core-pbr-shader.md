# Task Card `WI-C3-06`: Add the core PBR shader scaffold

| Field | Value |
|---|---|
| Card ID | `WI-C3-06` |
| Roadmap phase | [Track C · C3](../../track-c-external-assets.md): Material and texture mapping |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C3-01` |
| Diagrams | [C3 architecture diagrams](../../deep-dives/c3-material-texture-mapping.md#9-architecture-diagrams) |

## Goal

Add one engine-owned shader set, resource layout, and base-color path that compile for static and
skinned meshes.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c3-material-texture-mapping.md` sections 5 and 6.
- `src/OpenSage.Game/Graphics/Shaders/FixedFunctionShaderResources.cs`.
- `src/OpenSage.Game/Graphics/Shaders/NormalMappedShaderResources.cs`.
- `src/OpenSage.Game/Graphics/Shaders/MeshShaderResources.cs`.
- `src/OpenSage.Game/Assets/Shaders/NormalMapped.vert` and `NormalMapped.frag`.
- Shared shader headers under `src/OpenSage.Game/Assets/Shaders/`.
- `src/OpenSage.Game/OpenSage.Game.csproj`: shader compilation targets.

## Steps

1. Add `PbrMetallicRoughness.vert` using the existing mesh vertex layout, transforms, skinning
   resources, and UV0/UV1 outputs.
2. Add a fragment shader that renders the base-color factor and optional base-color texture.
3. Decode base-color samples from sRGB and encode final linear output to sRGB while the render
   target remains non-sRGB.
4. Add `PbrMetallicRoughnessShaderResources` with the complete resource layouts and constant types
   needed by the deep-dive payload.
5. Leave metallic-roughness lighting and the remaining texture roles to `WI-C3-07`.
6. Leave material and pipeline caching to `WI-C3-08`.

## Files likely to change

- `src/OpenSage.Game/Assets/Shaders/PbrMetallicRoughness.vert` (new)
- `src/OpenSage.Game/Assets/Shaders/PbrMetallicRoughness.frag` (new)
- `src/OpenSage.Game/Graphics/Shaders/PbrMetallicRoughnessShaderResources.cs` (new)

## Acceptance criteria

- [ ] The shader build succeeds for the new vertex and fragment stages.
- [ ] Every core PBR input and texture role has a reserved resource or constant binding.
- [ ] Static and skinned meshes share one shader family.
- [ ] Base-color factor and texture shader paths compile without glTF-specific decoding.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement metallic-roughness lighting, normal, occlusion, or emissive behavior yet.
- Do not add advanced `KHR_materials_*` semantics or vertex colors.
- Do not change the renderer-wide target color space; G6 owns that migration.
- Do not derive from `ShaderMaterialShaderResources`.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, change `WI-C3-07` to `Ready`.
- Append a result line to `.exchange`.
