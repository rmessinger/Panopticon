# Task Card `WI-G12-05`: Terrain normal and roughness shading

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-G12-05` |
| Roadmap phase | [Track G · G12](../../track-g-rendering-clarity.md): Open material packs |
| Status | `Blocked` |
| Type | `implementation` |
| Effort | `M` |
| Prerequisites | `WI-G12-04` |
| Diagrams | [G12 architecture](../../deep-dives/g12-open-material-packs.md#5-architecture-diagrams) |

## Goal

Shade terrain with per-layer normal, roughness, and occlusion from material packs, blended like colour, with GGX specular matching the C3 PBR lighting.

## Context to read (and only this)

- [G12 design](../../deep-dives/g12-open-material-packs.md) section 3.4 and the class diagram.
- `src/Panopticon.Simulation/Assets/Shaders/Terrain.frag`, `Terrain.vert`, `TerrainLighting.h`.
- `src/Panopticon.Simulation/Assets/Shaders/PbrMetallicRoughness.frag` (lighting terms to share).
- `src/Panopticon.Simulation/Graphics/Shaders/TerrainShaderResources.cs`, `Terrain.cs:489-622` (cliff UVs, arrays).

## Steps

1. Create normal (RG) and roughness-occlusion (RG) `Texture2D` arrays parallel to the diffuse array. Layers without maps get neutral values; maps without a pack are a single neutral layer.
2. Bind them in `TerrainShaderResources` and add a material constant `HasSurfaceMaps`.
3. In `Terrain.frag`, when `HasSurfaceMaps`: sample the three layers with the colour UVs and gradients, blend with the colour weights, reconstruct Z, build the tangent frame from the terrain normal and world X/Y UV axes (cliff tiles from the `CliffInfo` axes), apply roughness and occlusion, and add GGX specular from the shared lighting include with metallic 0. Without maps, output matches the `WI-G12-04` path bit for bit.
4. Extract shared GGX terms into an include used by both terrain and `PbrMetallicRoughness.frag` without changing the latter's output.

## Files likely to change

- `src/Panopticon.Simulation/Assets/Shaders/Terrain.frag`, `TerrainLighting.h`, a shared PBR lighting include, `PbrMetallicRoughness.frag`
- `src/Panopticon.Simulation/Graphics/Shaders/TerrainShaderResources.cs`, `Terrain/Terrain.cs`

## Acceptance criteria

- [ ] With no surface maps, `enhanced-materials` and all existing goldens are unchanged.
- [ ] The C3 PBR goldens are unchanged after the include extraction.
- [ ] Synthetic tilted-plane normals light consistently on flat and cliff tiles in a capture.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario VisualRegression --headless
```

## Out of scope / guardrails

- Parallax or height maps, tessellation, and triplanar mapping.
- Model materials (`WI-G12-07`).

## On completion

- Set Status above to `Done` or `Blocked` with reason; set `WI-G12-06` to `Ready`.
- Append a result line to `.exchange`.
