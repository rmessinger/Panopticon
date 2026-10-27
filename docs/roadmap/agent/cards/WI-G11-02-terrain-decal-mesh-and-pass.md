# Task Card `WI-G11-02`: Terrain decal mesh and render pass

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-G11-02` |
| Roadmap phase | [Track G · G11](../../track-g-rendering-clarity.md): Terrain impact decals |
| Status | `In progress` (code implemented; visual closeout pending) |
| Type | `implementation` |
| Effort | `M` |
| Prerequisites | `WI-G11-01`, `WI-G3-04` |
| Diagrams | [G11 architecture](../../deep-dives/g11-terrain-impact-decals.md#5-architecture-diagrams) |

## Goal

Draw stored decals as an alpha-blended terrain overlay mesh that reproduces the original scorch geometry, UVs, tint, and draw order under `Legacy`.

## Context to read (and only this)

- [G11 design](../../deep-dives/g11-terrain-impact-decals.md) sections 1 (original behaviour) and 3 (mesh and pass), and the sequence diagram.
- `src/Panopticon.Simulation/Terrain/Terrain.cs`, `HeightMap.cs`, `Roads/RoadCollection.cs`.
- `src/Panopticon.Simulation/Graphics/Shaders/RoadShaderResources.cs`, `TerrainShaderResources.cs`.
- `src/Panopticon.Simulation/Assets/Shaders/Road.vert`, `Road.frag`, `Terrain.frag`.

## Steps

1. Add `TerrainDecalMesher.Build`: per decal, height-map vertices inside its square at vertex height plus `HeightMap` vertical scale / 10, triangulated by the terrain flip state, UVs `(cellOffset + 0.5 + d / (2 * radius)) / (cellsPerRow + 1)` with cell spacing 1.5.
2. Add `TerrainDecalRenderer` with a `TerrainDecals` bucket at priority 5. Rebuild the dynamic buffers when the store `Version` changes or `Terrain.OnHeightMapChanged` reports an overlapping region.
3. Add `TerrainDecalShaderResources` and `TerrainDecal.vert/.frag`: alpha blend, depth less-equal read, no depth write, no cull. `Legacy` tint = terrain ambient + half the first terrain diffuse light.
4. Load the atlas by `GetByName("EXScorch01.tga")` through the on-demand loader.
5. Capture a windowed TestHarness frame of scorches on flat and sloped ground with one under a road; share it under a versioned filename per `docs/screenshots.md`.

## Files likely to change

- `src/Panopticon.Simulation/Terrain/Decals/TerrainDecalMesher.cs`, `TerrainDecalRenderer.cs` (new), `Terrain/Terrain.cs`
- `src/Panopticon.Simulation/Graphics/Shaders/TerrainDecalShaderResources.cs` (new)
- `src/Panopticon.Simulation/Assets/Shaders/TerrainDecal.vert`, `TerrainDecal.frag` (new)
- `test/Panopticon.Simulation.Tests/Terrain/TerrainDecalMesherTests.cs` (new)

## Acceptance criteria

- [x] Mesher tests match hand-computed vertex counts, heights, UVs, and flip-state triangles.
- [ ] Roads draw over scorches; scorches follow slopes without z-fighting at default zoom.
- [ ] Existing G3 goldens are unchanged with no decals present.
- [ ] A captured frame is shared.
- [ ] Full standard validation pending group closeout; focused tests pass.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~TerrainDecal"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- `Enhanced` lighting and limits (`WI-G11-04`).
- Do not reuse or resize `RadiusCursorDecals`.
- Presentation only: decals use `GameClient.Random`; a determinism-enforcing test failure is a suspected deviation: stop, keep artifacts, report.

## On completion

- Set Status above to `Done` or `Blocked` with reason; if `WI-G11-03` is `Done`, set `WI-G11-04` to `Ready`.
- Tick G11-2 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
